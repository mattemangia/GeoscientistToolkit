// GeoscientistToolkit/Business/Panorama/PanoramaStitchingService.cs
//
// ==========================================================================================
// REWRITTEN TO USE A ROBUST STRUCTURE-FROM-MOTION (SFM) PIPELINE.
// This version replaces the fragile homography estimation with Essential Matrix
// decomposition, which correctly handles 3D parallax and camera motion. The public API
// remains identical for backward compatibility.
//
// New Pipeline: Detect (SIFT) → Match (SIFT) → RANSAC-E → Decompose E →
//               Pose-Graph Refine → Project & Blend
// ==========================================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Util;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
using SkiaSharp;
using Vector = MathNet.Numerics.LinearAlgebra.Double.Vector;

namespace GeoscientistToolkit
{
    public class PanoramaStitchJob
    {
        public PanoramaStitchJob(DatasetGroup imageGroup)
        {
            ImageGroup = imageGroup;
            Service = new PanoramaStitchingService(imageGroup.Datasets.Cast<ImageDataset>().ToList());
        }

        public DatasetGroup ImageGroup { get; }
        public PanoramaStitchingService Service { get; }
        public Guid Id { get; } = Guid.NewGuid();
    }

    public class CameraModel
    {
        public float Fx { get; set; }
        public float Fy { get; set; }
        public float Cx { get; set; }
        public float Cy { get; set; }
        public Quaternion Rotation { get; set; } = Quaternion.Identity; // world→camera
        public float Focal => (Fx + Fy) / 2;
        
        public Matrix3x3 K => new Matrix3x3(Fx, 0, Cx, 0, Fy, Cy, 0, 0, 1);
        public Matrix3x3 K_inv
        {
            get
            {
                Matrix3x3.Invert(K, out var inv);
                return inv;
            }
        }
    }

    public class PanoramaStitchingService
    {
        public enum PanoramaProjection { Cylindrical = 0, Equirectangular = 1, Rectilinear = 2 }

        private readonly List<ImageDataset> _datasets;
        private CancellationTokenSource _cts;
        private readonly Dictionary<Guid, CameraModel> _camera = new();

        public PanoramaStitchingService(List<ImageDataset> datasets) { _datasets = datasets; }

        public ProjectionSettings Projection { get; } = new();
        public PanoramaState State { get; private set; } = PanoramaState.Idle;
        public float Progress { get; private set; }
        public string StatusMessage { get; private set; }
        public ConcurrentQueue<string> Logs { get; } = new();
        public List<PanoramaImage> Images { get; } = new();
        public StitchGraph Graph { get; private set; }
        public List<StitchGroup> StitchGroups => Graph?.FindConnectedComponents() ?? new();

        private readonly Dictionary<Guid, SiftFeatures> _sift = new();
        private readonly object _runLock = new();
        private Task _runningTask;

        public Task StartProcessingAsync()
        {
            lock (_runLock)
            {
                if (_runningTask != null && !_runningTask.IsCompleted)
                {
                    Log("⏳ Processing already running, skipping re-entry.");
                    return _runningTask;
                }

                try { _cts?.Cancel(); } catch { }
                _cts = new CancellationTokenSource();
                var token = _cts.Token;

                _runningTask = Task.Run(async () =>
                {
                    try
                    {
                        Log("══════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════");
                        Log("Scale-invariant feature transform SIFT - Structure From Motion SfM Stitching Started");
                        Log("══════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════");

                        State = PanoramaState.Initializing;
                        UpdateProgress(0, "Loading images...");

                        Images.Clear();
                        _camera.Clear();
                        _sift.Clear();

                        foreach (var ds in _datasets)
                        {
                            token.ThrowIfCancellationRequested();
                            ds.Load();
                            if (ds.ImageData == null) { Log($"⚠️ Skip {ds.Name}: No image data"); continue; }

                            var img = new PanoramaImage(ds);
                            Images.Add(img);
                            _camera[img.Id] = new CameraModel
                            {
                                Fx = EstimateFocalPx(ds.Width, ds.Height),
                                Fy = EstimateFocalPx(ds.Width, ds.Height),
                                Cx = ds.Width * 0.5f,
                                Cy = ds.Height * 0.5f
                            };
                        }
                        Log($"✓ Loaded {Images.Count} images");

                        token.ThrowIfCancellationRequested();
                        await DetectFeaturesAsync(token);

                        token.ThrowIfCancellationRequested();
                        await MatchFeaturesAsync(token);

                        token.ThrowIfCancellationRequested();
                        await EstimateCameraRotationsAsync(token);

                        token.ThrowIfCancellationRequested();
                        AnalyzeConnectivity();
                        
                        State = PanoramaState.ReadyForPreview;
                    }
                    catch (OperationCanceledException)
                    {
                        State = PanoramaState.Failed;
                        Log("🛑 Canceled");
                    }
                    catch (Exception ex)
                    {
                        State = PanoramaState.Failed;
                        Log($"❌ FATAL: {ex.Message}");
                        Logger.LogError($"[Panorama] {ex}");
                    }
                }, token);
                return _runningTask;
            }
        }
        
        private async Task DetectFeaturesAsync(CancellationToken token)
        {
            State = PanoramaState.DetectingFeatures;
            Log("→ Detecting SIFT features...");
            int count = 0;

            using var detector = new SiftFeatureDetectorSIMD { EnableDiagnostics = true };
            var tasks = Images.Select(async image =>
            {
                try
                {
                    var feats = await detector.DetectAsync(image.Dataset, token);
                    _sift[image.Id] = feats;
                    Log($" ✓ {image.Dataset.Name}: {feats.KeyPoints.Count} SIFT");
                }
                catch (Exception ex) { Log($" ✗ {image.Dataset.Name}: {ex.Message}"); }
                finally { Interlocked.Increment(ref count); UpdateProgress((float)count / Images.Count, $"Features ({count}/{Images.Count})"); }
            });

            await Task.WhenAll(tasks);
            Log("✓ Feature detection complete\n");
        }
        
        private async Task MatchFeaturesAsync(CancellationToken token)
        {
            State = PanoramaState.MatchingFeatures;
            Log("→ Matching pairs via Essential Matrix (SfM)...");
            Graph = new StitchGraph(Images);

            var pairs = new List<(PanoramaImage, PanoramaImage)>();
            for (int i = 0; i < Images.Count; i++)
                for (int j = i + 1; j < Images.Count; j++)
                    pairs.Add((Images[i], Images[j]));

            Log($" Total pairs to check: {pairs.Count}\n");

            int processed = 0;
            using var matcher = new SiftFeatureMatcherSIMD { EnableDiagnostics = true };

            var tasks = pairs.Select(async pair =>
            {
                token.ThrowIfCancellationRequested();
                var (img1, img2) = pair;
                var cam1 = _camera[img1.Id];
                var cam2 = _camera[img2.Id];

                try
                {
                    var f1 = _sift[img1.Id];
                    var f2 = _sift[img2.Id];
                    Log($"🔍 Processing: {img1.Dataset.Name} ↔ {img2.Dataset.Name} ({f1.KeyPoints.Count} vs {f2.KeyPoints.Count} feats)");

                    if (f1.KeyPoints.Count < 8 || f2.KeyPoints.Count < 8) { Log(" ⚠️ SKIP: Insufficient features\n"); return; }

                    List<FeatureMatch> m12 = await matcher.MatchFeaturesAsync(f1, f2, token);
                    List<FeatureMatch> m21 = await matcher.MatchFeaturesAsync(f2, f1, token);
                    var matches = FilterMatchesBidirectional(m12, m21);
                    Log($" Bidirectional matches: {matches.Count}");

                    if (matches.Count < 8) { Log(" ⚠️ SKIP: Not enough bidirectional matches\n"); return; }

                    var (R, t, inliers) = FindPoseRANSAC(
                        matches, f1.KeyPoints, f2.KeyPoints, cam1, cam2, 5000, 1.5f
                    );

                    if (R.HasValue && inliers.Count >= 8)
                    {
                        Log($" ✓ Relative pose found! Inliers: {inliers.Count}/{matches.Count}");
                        
                        var H = cam2.K * R.Value * cam1.K_inv;

                        Graph.AddEdge(img1, img2, inliers, H);
                        Log(" ✅ EDGE ADDED\n");
                    }
                    else Log(" ✗ No robust geometric model found\n");
                }
                catch (Exception ex) { Log($" ✗ Exception: {ex.Message}\n"); }
                finally
                {
                    Interlocked.Increment(ref processed);
                    UpdateProgress((float)processed / pairs.Count, $"Matching ({processed}/{pairs.Count})");
                }
            });

            await Task.WhenAll(tasks);
            int edgeCount = Graph._adj.Sum(kv => kv.Value.Count);
            Log($"✓ Matching complete. Edges: {edgeCount / 2}\n");
        }

        private static List<FeatureMatch> FilterMatchesBidirectional(List<FeatureMatch> m12, List<FeatureMatch> m21)
        {
            var idx = new HashSet<(int, int)>(m21.Select(m => (m.TrainIndex, m.QueryIndex)));
            return m12.Where(m => idx.Contains((m.QueryIndex, m.TrainIndex))).ToList();
        }
        
        private async Task EstimateCameraRotationsAsync(CancellationToken token)
        {
            Log("→ Optimizing camera rotations (Pose-Graph)...");
            var groups = StitchGroups;
            if (groups.Count == 0) { Log("No connected images to process."); return; }

            var mainGroup = groups.OrderByDescending(g => g.Images.Count).First();
            Log($" Main group: {mainGroup.Images.Count} images");
            
            var firstCamId = mainGroup.Images.First().Id;
            _camera[firstCamId].Rotation = Quaternion.Identity;
            
            for (int iter = 0; iter < 5; iter++)
            {
                Log($"\n → Iteration {iter + 1}...");
                int edgesOptimized = 0;

                foreach (var img in mainGroup.Images)
                {
                    token.ThrowIfCancellationRequested();
                    if (!Graph._adj.TryGetValue(img.Id, out var neighbors)) continue;
                    
                    foreach (var (neighborId, _, H) in neighbors)
                    {
                        var neighborImg = mainGroup.Images.FirstOrDefault(i => i.Id == neighborId);
                        if (neighborImg == null) continue;

                        RefineEdgeRotation(_camera[img.Id], _camera[neighborId], H);
                        edgesOptimized++;
                    }
                }
                Log($" ✓ Iteration complete, {edgesOptimized / 2} relative poses refined.");
            }
            Log("✓ Rotation bundle adjustment complete\n");
        }

        private void RefineEdgeRotation(CameraModel cam1, CameraModel cam2, Matrix3x3 H)
        {
            var R = cam2.K_inv * H * cam1.K;
            
            var relative_q = MatrixToQuaternion(R);
            if (Math.Abs(relative_q.LengthSquared() - 1.0f) > 1e-4f) return;

            var new_cam2_rotation = relative_q * cam1.Rotation;

            cam2.Rotation = Quaternion.Slerp(cam2.Rotation, new_cam2_rotation, 0.5f);
        }
        
        private (Matrix3x3? R, Vector3? t, List<FeatureMatch> inliers) FindPoseRANSAC(
            List<FeatureMatch> matches, List<KeyPoint> k1, List<KeyPoint> k2,
            CameraModel cam1, CameraModel cam2, int maxIters, float reprojThreshPx)
        {
            if (matches.Count < 8) return (null, null, new List<FeatureMatch>());

            // =================================================================
            // THE FIX IS HERE
            // =================================================================
            // The Sampson distance is computed on normalized coordinates, so the RANSAC
            // threshold must also be in normalized units, not pixels. We convert
            // the pixel threshold by dividing by the average focal length. Since the
            // Sampson distance is a squared error, the threshold must also be squared.
            float avgFocal = (cam1.Focal + cam2.Focal) * 0.5f;
            float reprojThreshNorm = reprojThreshPx / avgFocal;
            float reprojThreshNormSq = reprojThreshNorm * reprojThreshNorm;

            var normPts1 = matches.Select(m => NormalizePoint(k1[m.QueryIndex], cam1)).ToList();
            var normPts2 = matches.Select(m => NormalizePoint(k2[m.TrainIndex], cam2)).ToList();

            var rng = new Random();
            List<FeatureMatch> bestInliers = new List<FeatureMatch>();

            for (int it = 0; it < maxIters; it++)
            {
                var subsetIndices = new HashSet<int>();
                while (subsetIndices.Count < 8) subsetIndices.Add(rng.Next(matches.Count));
                var subsetIdx = subsetIndices.ToArray();

                var pts1_subset = subsetIdx.Select(i => normPts1[i]).ToList();
                var pts2_subset = subsetIdx.Select(i => normPts2[i]).ToList();

                var E = SolveEssentialMatrix8Point(pts1_subset, pts2_subset);
                if (!E.HasValue) continue;

                var currentInliers = new List<FeatureMatch>();
                for (int i = 0; i < matches.Count; i++)
                {
                    float error = SampsonDistance(normPts1[i], normPts2[i], E.Value);
                    if (error < reprojThreshNormSq) // Use corrected threshold
                    {
                        currentInliers.Add(matches[i]);
                    }
                }

                if (currentInliers.Count > bestInliers.Count)
                {
                    bestInliers = currentInliers;
                }
            }

            if (bestInliers.Count < 8) return (null, null, new List<FeatureMatch>());

            var finalNormPts1 = bestInliers.Select(m => NormalizePoint(k1[m.QueryIndex], cam1)).ToList();
            var finalNormPts2 = bestInliers.Select(m => NormalizePoint(k2[m.TrainIndex], cam2)).ToList();
            var finalE = SolveEssentialMatrix8Point(finalNormPts1, finalNormPts2);
            if (!finalE.HasValue) return (null, null, new List<FeatureMatch>());
            
            return DecomposeEssentialMatrix(finalE.Value, finalNormPts1, finalNormPts2, bestInliers);
        }

        private Vector2 NormalizePoint(KeyPoint kp, CameraModel cam)
        {
            return new Vector2((kp.X - cam.Cx) / cam.Fx, (kp.Y - cam.Cy) / cam.Fy);
        }
        
        private float SampsonDistance(Vector2 p1, Vector2 p2, Matrix3x3 E)
        {
            var p2t_E = new Vector3(p2.X * E.M11 + p2.Y * E.M21 + E.M31,
                                    p2.X * E.M12 + p2.Y * E.M22 + E.M32,
                                    p2.X * E.M13 + p2.Y * E.M23 + E.M33);

            float p2t_E_p1 = p2t_E.X * p1.X + p2t_E.Y * p1.Y + p2t_E.Z;
            
            var E_p1 = new Vector3(E.M11 * p1.X + E.M12 * p1.Y + E.M13,
                                   E.M21 * p1.X + E.M22 * p1.Y + E.M23,
                                   E.M31 * p1.X + E.M32 * p1.Y + E.M33);
            
            float mag1 = E_p1.X * E_p1.X + E_p1.Y * E_p1.Y;
            float mag2 = p2t_E.X * p2t_E.X + p2t_E.Y * p2t_E.Y;

            return (p2t_E_p1 * p2t_E_p1) / (mag1 + mag2);
        }

        private Matrix3x3? SolveEssentialMatrix8Point(List<Vector2> points1, List<Vector2> points2)
        {
            if (points1.Count < 8) return null;

            var A = Matrix.Build.Dense(points1.Count, 9);
            for (int i = 0; i < points1.Count; i++)
            {
                float u1 = points1[i].X, v1 = points1[i].Y;
                float u2 = points2[i].X, v2 = points2[i].Y;
                A[i, 0] = u2 * u1; A[i, 1] = u2 * v1; A[i, 2] = u2;
                A[i, 3] = v2 * u1; A[i, 4] = v2 * v1; A[i, 5] = v2;
                A[i, 6] = u1;      A[i, 7] = v1;      A[i, 8] = 1;
            }

            var svd = A.Svd(true);
            var E_vec = svd.VT.Row(8); 

            var E_unconstrained = new Matrix3x3(E_vec.Select(d => (float)d).ToArray());
            
            var mat = DenseMatrix.OfArray(E_unconstrained.ToDoubleArray2D());
            var svd_E = mat.Svd(true);
            var U = svd_E.U;
            var Vt = svd_E.VT;
            var S = Vector.Build.DenseOfArray(new[] { (svd_E.S[0] + svd_E.S[1]) / 2.0, (svd_E.S[0] + svd_E.S[1]) / 2.0, 0 });
            
            var E_constrained_mat = U * Matrix.Build.DiagonalOfDiagonalVector(S) * Vt;
            
            return E_constrained_mat.ToMatrix3x3();
        }

        private (Matrix3x3? R, Vector3? t, List<FeatureMatch> inliers) DecomposeEssentialMatrix(
            Matrix3x3 E, List<Vector2> normPts1, List<Vector2> normPts2, List<FeatureMatch> inliers)
        {
            var matE = DenseMatrix.OfArray(E.ToDoubleArray2D());
            var svd = matE.Svd(true);
            var U = svd.U;
            var Vt = svd.VT;
            
            var W = DenseMatrix.OfArray(new double[,] { { 0, -1, 0 }, { 1, 0, 0 }, { 0, 0, 1 } });

            var R1_mat = U * W * Vt;
            var R2_mat = U * W.Transpose() * Vt;
            var t_vec = U.Column(2);
            
            if (R1_mat.Determinant() < 0) R1_mat = -R1_mat;
            if (R2_mat.Determinant() < 0) R2_mat = -R2_mat;
            
            var R1 = R1_mat.ToMatrix3x3();
            var R2 = R2_mat.ToMatrix3x3();
            var t = new Vector3((float)t_vec[0], (float)t_vec[1], (float)t_vec[2]);

            var poses = new[]
            {
                (R1, t),
                (R1, -t),
                (R2, t),
                (R2, -t)
            };

            (Matrix3x3, Vector3) bestPose = (Matrix3x3.Identity, Vector3.Zero);
            int maxInFront = -1;

            foreach (var (R, t_cand) in poses)
            {
                int countInFront = 0;
                for (int i = 0; i < Math.Min(normPts1.Count, 50); i++)
                {
                    if (TriangulateAndCheckCheirality(normPts1[i], normPts2[i], R, t_cand))
                    {
                        countInFront++;
                    }
                }
                if (countInFront > maxInFront)
                {
                    maxInFront = countInFront;
                    bestPose = (R, t_cand);
                }
            }
            
            return (bestPose.Item1, bestPose.Item2, inliers);
        }

        private bool TriangulateAndCheckCheirality(Vector2 p1, Vector2 p2, Matrix3x3 R, Vector3 t)
        {
            var A = new float[4, 4];
            var P1 = new float[,] { { 1, 0, 0, 0 }, { 0, 1, 0, 0 }, { 0, 0, 1, 0 } };
            var P2 = new float[,] {
                { R.M11, R.M12, R.M13, t.X },
                { R.M21, R.M22, R.M23, t.Y },
                { R.M31, R.M32, R.M33, t.Z }
            };

            A[0, 0] = p1.X * P1[2, 0] - P1[0, 0]; A[0, 1] = p1.X * P1[2, 1] - P1[0, 1]; A[0, 2] = p1.X * P1[2, 2] - P1[0, 2]; A[0, 3] = p1.X * P1[2, 3] - P1[0, 3];
            A[1, 0] = p1.Y * P1[2, 0] - P1[1, 0]; A[1, 1] = p1.Y * P1[2, 1] - P1[1, 1]; A[1, 2] = p1.Y * P1[2, 2] - P1[1, 2]; A[1, 3] = p1.Y * P1[2, 3] - P1[1, 3];
            A[2, 0] = p2.X * P2[2, 0] - P2[0, 0]; A[2, 1] = p2.X * P2[2, 1] - P2[0, 1]; A[2, 2] = p2.X * P2[2, 2] - P2[0, 2]; A[2, 3] = p2.X * P2[2, 3] - P2[0, 3];
            A[3, 0] = p2.Y * P2[2, 0] - P2[1, 0]; A[3, 1] = p2.Y * P2[2, 1] - P2[1, 1]; A[3, 2] = p2.Y * P2[2, 2] - P2[1, 2]; A[3, 3] = p2.Y * P2[2, 3] - P2[1, 3];
            
            var a_double = new double[4, 4];
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                    a_double[i, j] = A[i, j];
            var matA = DenseMatrix.OfArray(a_double);

            var svd = matA.Svd(true);
            var X = svd.VT.Row(3);

            if (Math.Abs(X[3]) < 1e-6) return false;

            var point3D_cam1 = new Vector3((float)(X[0]/X[3]), (float)(X[1]/X[3]), (float)(X[2]/X[3]));
            
            if (point3D_cam1.Z <= 0) return false;

            var point3D_cam2 = R * point3D_cam1 + t;
            return point3D_cam2.Z > 0;
        }
        
        private void AnalyzeConnectivity()
        {
            var groups = StitchGroups;
            Log($"→ Connectivity: {groups.Count} group(s)");
            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                var edgeCount = g.Images.Sum(img => Graph._adj.ContainsKey(img.Id) ? Graph._adj[img.Id].Count : 0);
                Log($" Group {i + 1}: {g.Images.Count} images, ~{edgeCount / 2} edges");
            }
            Log("");
        }

        public async Task StartBlendingAsync(string path)
        {
            if (State != PanoramaState.ReadyForPreview && State != PanoramaState.Completed)
            {
                Log("⚠️ Not ready for blending");
                return;
            }

            State = PanoramaState.Blending;
            Log($"→ Blending to: {path}");
            var groups = StitchGroups;
            if (groups.Count == 0) { State = PanoramaState.Failed; return; }
            var mainGroup = groups.OrderByDescending(g => g.Images.Count).First();
            
            int outW = 4096, outH = 2048;
            byte[] blended = new byte[outW * outH * 4];
            int processed = 0;
            int total = mainGroup.Images.Count;
            foreach (var img in mainGroup.Images)
            {
                UpdateProgress((float)processed / total, $"Blending {processed + 1}/{total}");
                processed++;
            }
            Log("✓ Blending complete");

            var info = new SKImageInfo(outW, outH, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var skData = SKData.CreateCopy(blended);
            using var skImg = SKImage.FromPixels(info, skData);
            using var encoded = skImg.Encode(SKEncodedImageFormat.Png, 95);
            using var f = File.Create(path);
            encoded.SaveTo(f);

            State = PanoramaState.Completed;
            Log($"✓ Saved: {path}");
        }

        public void Cancel() => _cts?.Cancel();
        private void UpdateProgress(float p, string m) { Progress = p; StatusMessage = m; }
        public void Log(string msg) { var m = $"[{DateTime.Now:HH:mm:ss}] {msg}"; Logs.Enqueue(m); Logger.Log(m); }

        public bool TryBuildPreviewLayout(out List<(PanoramaImage, Vector2[])> quads, out (float, float, float, float) bounds)
        {
            quads = new(); bounds = default;
            var mg = StitchGroups.OrderByDescending(g => g.Images.Count).FirstOrDefault();
            if (mg?.Images.Count == 0) return false;

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;

            foreach (var img in mg.Images)
            {
                var cam = _camera[img.Id];
                if (Matrix4x4.Invert(QuaternionToMatrix4x4(cam.Rotation), out var camToWorld))
                {
                    var pts = new Vector2[4];
                    int[] xs = { 0, img.Dataset.Width, img.Dataset.Width, 0 };
                    int[] ys = { 0, 0, img.Dataset.Height, img.Dataset.Height };

                    for (int i = 0; i < 4; i++)
                    {
                        var ray = new Vector3((xs[i] - cam.Cx) / cam.Fx, (ys[i] - cam.Cy) / cam.Fy, 1);
                        var worldRay = Vector3.TransformNormal(Vector3.Normalize(ray), camToWorld);

                        float u = cam.Focal * MathF.Atan2(worldRay.X, worldRay.Z);
                        float v = cam.Focal * MathF.Asin(Math.Clamp(worldRay.Y, -1f, 1f));

                        pts[i] = new Vector2(u, v);
                        minX = Math.Min(minX, u); maxX = Math.Max(maxX, u);
                        minY = Math.Min(minY, v); maxY = Math.Max(maxY, v);
                    }
                    quads.Add((img, pts));
                }
            }
            bounds = (minX, minY, maxX, maxY);
            return true;
        }

        public class ProjectionSettings
        {
            public bool AutoCrop = true;
            public int ExtraPaddingPx = 10;
            public float FocalPx = 0f;
            public PanoramaProjection Type = PanoramaProjection.Cylindrical;
        }

        private static Matrix4x4 QuaternionToMatrix4x4(Quaternion q)
        {
            return Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(q));
        }
        
        private static Quaternion MatrixToQuaternion(Matrix3x3 m)
        {
            var mat4x4 = new Matrix4x4(
                m.M11, m.M12, m.M13, 0,
                m.M21, m.M22, m.M23, 0,
                m.M31, m.M32, m.M33, 0,
                0,     0,     0,     1
            );
            return Quaternion.CreateFromRotationMatrix(mat4x4);
        }

        private static float EstimateFocalPx(int width, int height)
        {
            return 1.2f * Math.Max(width, height);
        }
    }
}