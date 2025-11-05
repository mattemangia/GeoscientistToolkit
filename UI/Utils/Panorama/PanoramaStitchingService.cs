// GeoscientistToolkit/Business/Panorama/PanoramaStitchingService.cs
//
// ==========================================================================================
// FIXED VERSION: Corrects transformation direction and image ordering
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
        public Quaternion Rotation { get; set; } = Quaternion.Identity; // Camera-to-world rotation
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
        public StitchGraph Graph { get; private set; }
        public List<StitchGroup> StitchGroups => Graph?.FindConnectedComponents() ?? new();

        private readonly List<PanoramaImage> _images = new();
        private readonly Dictionary<Guid, SiftFeatures> _sift = new();
        private readonly object _runLock = new();
        private readonly object _dataLock = new();
        private Task _runningTask;
        
        public List<PanoramaImage> GetImages() { lock (_dataLock) { return _images.ToList(); } }

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

                        lock (_dataLock)
                        {
                            _images.Clear();
                            _camera.Clear();
                            _sift.Clear();

                            foreach (var ds in _datasets)
                            {
                                token.ThrowIfCancellationRequested();
                                ds.Load();
                                if (ds.ImageData == null) { Log($"⚠️ Skip {ds.Name}: No image data"); continue; }

                                var img = new PanoramaImage(ds);
                                _images.Add(img);
                                _camera[img.Id] = new CameraModel
                                {
                                    Fx = EstimateFocalPx(ds.Width, ds.Height),
                                    Fy = EstimateFocalPx(ds.Width, ds.Height),
                                    Cx = ds.Width * 0.5f,
                                    Cy = ds.Height * 0.5f
                                };
                            }
                        }
                        Log($"✓ Loaded {GetImages().Count} images");

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
            var images = GetImages();

            using var detector = new SiftFeatureDetectorSIMD { EnableDiagnostics = true };
            var tasks = images.Select(async image =>
            {
                try
                {
                    var feats = await detector.DetectAsync(image.Dataset, token);
                    lock(_dataLock) { _sift[image.Id] = feats; }
                    Log($" ✓ {image.Dataset.Name}: {feats.KeyPoints.Count} SIFT");
                }
                catch (Exception ex) { Log($" ✗ {image.Dataset.Name}: {ex.Message}"); }
                finally { Interlocked.Increment(ref count); UpdateProgress((float)count / images.Count, $"Features ({count}/{images.Count})"); }
            });

            await Task.WhenAll(tasks);
            Log("✓ Feature detection complete\n");
        }
        
        private async Task MatchFeaturesAsync(CancellationToken token)
        {
            State = PanoramaState.MatchingFeatures;
            Log("→ Matching pairs via Essential Matrix (SfM)...");
            var images = GetImages();
            Graph = new StitchGraph(images);

            var pairs = new List<(PanoramaImage, PanoramaImage)>();
            for (int i = 0; i < images.Count; i++)
                for (int j = i + 1; j < images.Count; j++)
                    pairs.Add((images[i], images[j]));

            Log($" Total pairs to check: {pairs.Count}\n");

            int processed = 0;
            using var matcher = new SIFTFeatureMatcherSIMD { EnableDiagnostics = true };

            var tasks = pairs.Select(async pair =>
            {
                token.ThrowIfCancellationRequested();
                var (img1, img2) = pair;
                
                CameraModel cam1;
                CameraModel cam2;
                SiftFeatures f1;
                SiftFeatures f2;
                lock (_dataLock)
                {
                    cam1 = _camera[img1.Id];
                    cam2 = _camera[img2.Id];
                    f1 = _sift[img1.Id];
                    f2 = _sift[img2.Id];
                }

                try
                {
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
            Log("→ Optimizing camera rotations (Bundle Adjustment)...");
            var groups = StitchGroups;
            if (groups.Count == 0) { Log("No connected images to process."); return; }

            var mainGroup = groups.OrderByDescending(g => g.Images.Count).First();
            Log($" Main group: {mainGroup.Images.Count} images");
            
            // Find the most central image as anchor
            var centralImage = mainGroup.Images
                .OrderByDescending(img => Graph._adj.ContainsKey(img.Id) ? Graph._adj[img.Id].Count : 0)
                .FirstOrDefault();

            if (centralImage == null)
            {
                Log("⚠️ Could not determine a central image.");
                State = PanoramaState.Failed;
                return;
            }
            
            Log($"✓ Anchor: '{centralImage.Dataset.Name}'");
            
            // Set anchor to identity
            lock(_dataLock) 
            { 
                _camera[centralImage.Id].Rotation = Quaternion.Identity;
            }
            
            // Propagate rotations using breadth-first search
            var visited = new HashSet<Guid> { centralImage.Id };
            var queue = new Queue<PanoramaImage>();
            queue.Enqueue(centralImage);
            
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!Graph._adj.TryGetValue(current.Id, out var neighbors)) continue;
                
                foreach (var (neighborId, _, H) in neighbors)
                {
                    if (visited.Contains(neighborId)) continue;
                    
                    var neighbor = mainGroup.Images.FirstOrDefault(i => i.Id == neighborId);
                    if (neighbor == null) continue;
                    
                    lock (_dataLock)
                    {
                        var cam1 = _camera[current.Id];
                        var cam2 = _camera[neighborId];
                        
                        // Extract rotation from homography (from cam1 to cam2 coordinate system)
                        var R = cam2.K_inv * H * cam1.K;
                        var q_rel = MatrixToQuaternion(R);
                        
                        // FIX: The relative rotation transforms from cam1's frame to cam2's frame
                        // So: cam2.Rotation = cam1.Rotation * Inverse(q_rel)
                        // Because q_rel goes from cam1 to cam2, but we want the world-to-camera transform
                        cam2.Rotation = Quaternion.Normalize(cam1.Rotation * Quaternion.Inverse(q_rel));
                    }
                    
                    visited.Add(neighborId);
                    queue.Enqueue(neighbor);
                }
            }
            
            // Refinement iterations
            for (int iter = 0; iter < 5; iter++)
            {
                await Task.Yield();
                token.ThrowIfCancellationRequested();
                
                Log($" → Refinement iteration {iter + 1}...");

                foreach (var img in mainGroup.Images)
                {
                    if (img.Id == centralImage.Id) continue; // Don't move anchor
                    if (!Graph._adj.TryGetValue(img.Id, out var neighbors)) continue;
                    
                    Quaternion avgRotation = new Quaternion(0, 0, 0, 0);
                    float totalWeight = 0;
                    
                    foreach (var (neighborId, _, H) in neighbors)
                    {
                        lock (_dataLock)
                        {
                            var cam1 = _camera[img.Id];
                            var cam2 = _camera[neighborId];
                            
                            var R = cam2.K_inv * H * cam1.K;
                            var q_rel = MatrixToQuaternion(R);
                            
                            // Expected rotation for current camera based on neighbor
                            var expected = Quaternion.Normalize(cam2.Rotation * q_rel);
                            
                            // Weighted average using SLERP
                            if (totalWeight == 0)
                                avgRotation = expected;
                            else
                                avgRotation = Quaternion.Slerp(avgRotation, expected, 1.0f / (totalWeight + 1));
                            
                            totalWeight += 1;
                        }
                    }
                    
                    if (totalWeight > 0)
                    {
                        lock (_dataLock)
                        {
                            var cam = _camera[img.Id];
                            cam.Rotation = Quaternion.Slerp(cam.Rotation, avgRotation, 0.5f);
                        }
                    }
                }
            }
            
            Log("✓ Rotation optimization complete\n");
        }

        private (Matrix3x3? R, Vector3? t, List<FeatureMatch> inliers) FindPoseRANSAC(
            List<FeatureMatch> matches, List<KeyPoint> k1, List<KeyPoint> k2,
            CameraModel cam1, CameraModel cam2, int maxIters, float reprojThreshPx)
        {
            if (matches.Count < 8) return (null, null, new List<FeatureMatch>());

            float avgFocal = (cam1.Focal + cam2.Focal) * 0.5f;
            float reprojThreshNorm = reprojThreshPx / avgFocal;
            float reprojThreshNormSq = reprojThreshNorm * reprojThreshNorm;

            var normPts1 = matches.Select(m => NormalizePoint(k1[m.QueryIndex], cam1)).ToList();
            var normPts2 = matches.Select(m => NormalizePoint(k2[m.TrainIndex], cam2)).ToList();

            var rng = new Random(0); // Deterministic
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
                    if (error < reprojThreshNormSq)
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

            return (p2t_E_p1 * p2t_E_p1) / (mag1 + mag2 + 1e-10f);
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
        
        public async Task StartBlendingAsync(string path, int outputWidth = 4096)
        {
            if (State != PanoramaState.ReadyForPreview && State != PanoramaState.Completed)
            {
                Log("⚠️ Not ready for blending");
                return;
            }

            State = PanoramaState.Blending;
            Log($"→ Blending to: {path} at {outputWidth}px width...");
            var groups = StitchGroups;
            if (groups.Count == 0) { State = PanoramaState.Failed; return; }
            var mainGroup = groups.OrderByDescending(g => g.Images.Count).First();

            // Calculate actual panorama bounds
            float minLon = float.MaxValue, maxLon = float.MinValue;
            float minLat = float.MaxValue, maxLat = float.MinValue;
            
            lock (_dataLock)
            {
                foreach (var img in mainGroup.Images)
                {
                    var cam = _camera[img.Id];
                    
                    // Sample image corners and edges
                    for (int sx = 0; sx <= 10; sx++)
                    {
                        for (int sy = 0; sy <= 10; sy++)
                        {
                            float px = (sx / 10.0f) * img.Dataset.Width;
                            float py = (sy / 10.0f) * img.Dataset.Height;
                            
                            // Convert pixel to camera ray
                            var cameraRay = new Vector3((px - cam.Cx) / cam.Fx, (py - cam.Cy) / cam.Fy, 1);
                            cameraRay = Vector3.Normalize(cameraRay);
                            
                            // Transform camera ray to world space using inverse rotation
                            var worldRay = Vector3.Transform(cameraRay, Quaternion.Inverse(cam.Rotation));
                            
                            // Convert to spherical coordinates
                            float lon = MathF.Atan2(worldRay.X, worldRay.Z);
                            float lat = MathF.Asin(Math.Clamp(worldRay.Y, -1f, 1f));
                            
                            minLon = Math.Min(minLon, lon);
                            maxLon = Math.Max(maxLon, lon);
                            minLat = Math.Min(minLat, lat);
                            maxLat = Math.Max(maxLat, lat);
                        }
                    }
                }
            }
            
            // Add padding
            float lonPadding = (maxLon - minLon) * 0.05f;
            float latPadding = (maxLat - minLat) * 0.05f;
            minLon -= lonPadding;
            maxLon += lonPadding;
            minLat -= latPadding;
            maxLat += latPadding;
            
            // Constrain to valid ranges
            minLon = Math.Max(minLon, -MathF.PI);
            maxLon = Math.Min(maxLon, MathF.PI);
            minLat = Math.Max(minLat, -MathF.PI / 2);
            maxLat = Math.Min(maxLat, MathF.PI / 2);
            
            float lonRange = maxLon - minLon;
            float latRange = maxLat - minLat;
            
            int outW = outputWidth;
            int outH = (int)(outputWidth * latRange / lonRange);
            
            Log($"Panorama spans: {lonRange * 180 / MathF.PI:F1}° × {latRange * 180 / MathF.PI:F1}°");
            Log($"Output size: {outW} × {outH}");

            // First pass: Create label map (which image owns each pixel)
            Log("→ Computing optimal seams...");
            byte[] labelMap = new byte[outW * outH];
            float[] distanceMap = new float[outW * outH];
            
            // Initialize with max distance
            for (int i = 0; i < distanceMap.Length; i++)
            {
                distanceMap[i] = float.MaxValue;
            }

            await Task.Run(() =>
            {
                var groupImages = mainGroup.Images.ToList();
                
                // Find best source image for each pixel (based on distance to image center)
                Parallel.For(0, outH, y =>
                {
                    for (int x = 0; x < outW; x++)
                    {
                        // Map pixel to panorama space
                        float lon = minLon + (x / (float)(outW - 1)) * lonRange;
                        float lat = minLat + (y / (float)(outH - 1)) * latRange;

                        // Convert to 3D world direction
                        var worldRay = new Vector3(
                            MathF.Cos(lat) * MathF.Sin(lon),
                            MathF.Sin(lat),
                            MathF.Cos(lat) * MathF.Cos(lon)
                        );

                        int pixelIdx = y * outW + x;
                        byte bestImageIdx = 255;
                        float bestDistance = float.MaxValue;

                        for (byte imgIdx = 0; imgIdx < groupImages.Count; imgIdx++)
                        {
                            var img = groupImages[imgIdx];
                            var cam = _camera[img.Id];
                            
                            // Transform world ray to camera space
                            var cameraRay = Vector3.Transform(worldRay, cam.Rotation);
                            
                            if (cameraRay.Z <= 0) continue; // Behind camera

                            // Project to image plane
                            float u = cameraRay.X / cameraRay.Z;
                            float v = cameraRay.Y / cameraRay.Z;
                            
                            float pixelX = cam.Fx * u + cam.Cx;
                            float pixelY = cam.Fy * v + cam.Cy;

                            // Check bounds
                            if (pixelX >= 0 && pixelX < img.Dataset.Width - 1 &&
                                pixelY >= 0 && pixelY < img.Dataset.Height - 1)
                            {
                                // Calculate distance from image center (normalized)
                                float dx = (pixelX - cam.Cx) / cam.Cx;
                                float dy = (pixelY - cam.Cy) / cam.Cy;
                                float distance = dx * dx + dy * dy;
                                
                                // Prefer pixels closer to image center
                                if (distance < bestDistance)
                                {
                                    bestDistance = distance;
                                    bestImageIdx = imgIdx;
                                }
                            }
                        }

                        if (bestImageIdx != 255)
                        {
                            labelMap[pixelIdx] = bestImageIdx;
                            distanceMap[pixelIdx] = bestDistance;
                        }
                    }
                });
            });

            Log("→ Applying seam blending...");
            
            // Second pass: Create feathered transitions at seams
            byte[] blended = new byte[outW * outH * 4];
            int processedRows = 0;

            await Task.Run(() =>
            {
                var groupImages = mainGroup.Images.ToList();
                
                Parallel.For(0, outH, y =>
                {
                    for (int x = 0; x < outW; x++)
                    {
                        int pixelIdx = y * outW + x;
                        byte primaryImageIdx = labelMap[pixelIdx];
                        
                        if (primaryImageIdx == 255)
                        {
                            // No image covers this pixel
                            continue;
                        }

                        // Map pixel to panorama space
                        float lon = minLon + (x / (float)(outW - 1)) * lonRange;
                        float lat = minLat + (y / (float)(outH - 1)) * latRange;

                        var worldRay = new Vector3(
                            MathF.Cos(lat) * MathF.Sin(lon),
                            MathF.Sin(lat),
                            MathF.Cos(lat) * MathF.Cos(lon)
                        );

                        // Check if we're near a seam (different neighbors)
                        bool nearSeam = false;
                        int seamDistance = int.MaxValue;
                        
                        // Check neighboring pixels for different labels
                        int checkRadius = 30; // Feather width in pixels
                        for (int dy = -checkRadius; dy <= checkRadius && !nearSeam; dy++)
                        {
                            for (int dx = -checkRadius; dx <= checkRadius && !nearSeam; dx++)
                            {
                                int nx = x + dx;
                                int ny = y + dy;
                                
                                if (nx >= 0 && nx < outW && ny >= 0 && ny < outH)
                                {
                                    int neighborIdx = ny * outW + nx;
                                    if (labelMap[neighborIdx] != 255 && labelMap[neighborIdx] != primaryImageIdx)
                                    {
                                        int dist = Math.Abs(dx) + Math.Abs(dy);
                                        if (dist < seamDistance)
                                        {
                                            seamDistance = dist;
                                            nearSeam = true;
                                        }
                                    }
                                }
                            }
                        }

                        Vector4 finalColor = Vector4.Zero;
                        
                        if (!nearSeam || seamDistance > checkRadius / 2)
                        {
                            // Far from seam - use single image
                            var img = groupImages[primaryImageIdx];
                            var cam = _camera[img.Id];
                            
                            var cameraRay = Vector3.Transform(worldRay, cam.Rotation);
                            
                            if (cameraRay.Z > 0)
                            {
                                float u = cameraRay.X / cameraRay.Z;
                                float v = cameraRay.Y / cameraRay.Z;
                                float pixelX = cam.Fx * u + cam.Cx;
                                float pixelY = cam.Fy * v + cam.Cy;
                                
                                if (pixelX >= 0 && pixelX < img.Dataset.Width - 1 &&
                                    pixelY >= 0 && pixelY < img.Dataset.Height - 1)
                                {
                                    var color = SampleBilinear(img.Dataset.ImageData, img.Dataset.Width, img.Dataset.Height, pixelX, pixelY);
                                    finalColor = new Vector4(color.R, color.G, color.B, 255);
                                }
                            }
                        }
                        else
                        {
                            // Near seam - blend multiple images with Gaussian weights
                            float totalWeight = 0;
                            
                            // Collect all contributing images at this point
                            for (byte imgIdx = 0; imgIdx < groupImages.Count; imgIdx++)
                            {
                                var img = groupImages[imgIdx];
                                var cam = _camera[img.Id];
                                
                                var cameraRay = Vector3.Transform(worldRay, cam.Rotation);
                                
                                if (cameraRay.Z <= 0) continue;

                                float u = cameraRay.X / cameraRay.Z;
                                float v = cameraRay.Y / cameraRay.Z;
                                float pixelX = cam.Fx * u + cam.Cx;
                                float pixelY = cam.Fy * v + cam.Cy;

                                if (pixelX >= 0 && pixelX < img.Dataset.Width - 1 &&
                                    pixelY >= 0 && pixelY < img.Dataset.Height - 1)
                                {
                                    var color = SampleBilinear(img.Dataset.ImageData, img.Dataset.Width, img.Dataset.Height, pixelX, pixelY);
                                    
                                    // Weight based on distance from seam and whether this is the primary image
                                    float weight = 1.0f;
                                    
                                    if (imgIdx == primaryImageIdx)
                                    {
                                        // Primary image - fade out near seam
                                        weight = (float)seamDistance / checkRadius;
                                    }
                                    else
                                    {
                                        // Secondary image - fade in near seam
                                        weight = 1.0f - (float)seamDistance / checkRadius;
                                    }
                                    
                                    // Apply Gaussian falloff for smoother blend
                                    weight = MathF.Exp(-2.0f * (1.0f - weight) * (1.0f - weight));
                                    
                                    finalColor += new Vector4(color.R, color.G, color.B, color.A) * weight;
                                    totalWeight += weight;
                                }
                            }
                            
                            if (totalWeight > 0)
                            {
                                finalColor /= totalWeight;
                            }
                        }

                        int idx = pixelIdx * 4;
                        blended[idx]     = (byte)Math.Clamp(finalColor.X, 0, 255);
                        blended[idx + 1] = (byte)Math.Clamp(finalColor.Y, 0, 255);
                        blended[idx + 2] = (byte)Math.Clamp(finalColor.Z, 0, 255);
                        blended[idx + 3] = (byte)Math.Clamp(finalColor.W, 0, 255);
                    }

                    int currentProgress = Interlocked.Increment(ref processedRows);
                    if (currentProgress % 8 == 0)
                    {
                        UpdateProgress((float)currentProgress / outH, $"Blending ({currentProgress}/{outH})");
                    }
                });
            });

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
        private (byte R, byte G, byte B, byte A) SampleBilinear(byte[] imageData, int width, int height, float u, float v)
        {
            int x = (int)u;
            int y = (int)v;
            float u_ratio = u - x;
            float v_ratio = v - y;
            float u_opposite = 1 - u_ratio;
            float v_opposite = 1 - v_ratio;

            int i00 = (y * width + x) * 4;
            int i10 = i00 + 4;
            int i01 = ((y + 1) * width + x) * 4;
            int i11 = i01 + 4;

            i10 = Math.Min(i10, imageData.Length - 4);
            i01 = Math.Min(i01, imageData.Length - 4);
            i11 = Math.Min(i11, imageData.Length - 4);

            float r = (imageData[i00] * u_opposite + imageData[i10] * u_ratio) * v_opposite +
                      (imageData[i01] * u_opposite + imageData[i11] * u_ratio) * v_ratio;
            float g = (imageData[i00 + 1] * u_opposite + imageData[i10 + 1] * u_ratio) * v_opposite +
                      (imageData[i01 + 1] * u_opposite + imageData[i11 + 1] * u_ratio) * v_ratio;
            float b = (imageData[i00 + 2] * u_opposite + imageData[i10 + 2] * u_ratio) * v_opposite +
                      (imageData[i01 + 2] * u_opposite + imageData[i11 + 2] * u_ratio) * v_ratio;
            
            return ((byte)r, (byte)g, (byte)b, 255);
        }

        public void Cancel() => _cts?.Cancel();
        private void UpdateProgress(float p, string m) { Progress = p; StatusMessage = m; }
        public void Log(string msg) { var m = $"[{DateTime.Now:HH:mm:ss}] {msg}"; Logs.Enqueue(m); Logger.Log(m); }

        public bool TryBuildPreviewLayout(out List<(PanoramaImage, Vector2[])> quads, out (float, float, float, float) bounds)
        {
            quads = new();
            bounds = default;

            lock (_dataLock)
            {
                var mg = StitchGroups.OrderByDescending(g => g.Images.Count).FirstOrDefault();
                if (mg?.Images.Count == 0 || mg == null) return false;

                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;

                foreach (var img in mg.Images)
                {
                    if (!_camera.TryGetValue(img.Id, out var cam)) continue;

                    var pts = new Vector2[4];
                    int[] xs = { 0, img.Dataset.Width, img.Dataset.Width, 0 };
                    int[] ys = { 0, 0, img.Dataset.Height, img.Dataset.Height };

                    for (int i = 0; i < 4; i++)
                    {
                        // Convert pixel to camera ray
                        var cameraRay = new Vector3((xs[i] - cam.Cx) / cam.Fx, (ys[i] - cam.Cy) / cam.Fy, 1);
                        cameraRay = Vector3.Normalize(cameraRay);
                        
                        // Transform to world space
                        var worldRay = Vector3.Transform(cameraRay, Quaternion.Inverse(cam.Rotation));
                        
                        // Cylindrical projection for preview
                        float u = cam.Focal * MathF.Atan2(worldRay.X, worldRay.Z);
                        float v = cam.Focal * (worldRay.Y / MathF.Sqrt(worldRay.X * worldRay.X + worldRay.Z * worldRay.Z));

                        pts[i] = new Vector2(u, v);
                        minX = Math.Min(minX, u);
                        maxX = Math.Max(maxX, u);
                        minY = Math.Min(minY, v);
                        maxY = Math.Max(maxY, v);
                    }
                    quads.Add((img, pts));
                }
                bounds = (minX, minY, maxX, maxY);
                return true;
            }
        }

        public class ProjectionSettings
        {
            public bool AutoCrop = true;
            public int ExtraPaddingPx = 10;
            public float FocalPx = 0f;
            public PanoramaProjection Type = PanoramaProjection.Equirectangular;
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
            // Assume ~50° horizontal field of view as default
            return 1.2f * Math.Max(width, height);
        }
    }
}