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

    public class PanoramaStitchingService:IDisposable
    {
        private bool _disposed;
        public enum PanoramaProjection { Cylindrical = 0, Equirectangular = 1, Rectilinear = 2 }

        private readonly List<ImageDataset> _datasets;
        private readonly object _ctsLock = new();
        private CancellationTokenSource _cts;
        private readonly Dictionary<Guid, CameraModel> _camera = new();

        public PanoramaStitchingService(List<ImageDataset> datasets) { _datasets = datasets; }

        public ProjectionSettings Projection { get; } = new();

        private readonly object _stateLock = new();
        private PanoramaState _state = PanoramaState.Idle;
        private float _progress;
        private string _statusMessage;

        public PanoramaState State
        {
            get { lock (_stateLock) { return _state; } }
            private set { lock (_stateLock) { _state = value; } }
        }

        public float Progress
        {
            get { lock (_stateLock) { return _progress; } }
            private set { lock (_stateLock) { _progress = value; } }
        }

        public string StatusMessage
        {
            get { lock (_stateLock) { return _statusMessage; } }
            private set { lock (_stateLock) { _statusMessage = value; } }
        }

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

                lock (_ctsLock)
                {
                    try { _cts?.Cancel(); } catch { }
                    _cts?.Dispose();
                    _cts = new CancellationTokenSource();
                }
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
                        Log(" ✓ EDGE ADDED\n");
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
    // ─────────────────────────────────────────────────────────────────────────────
    // PANORAMA BLENDING — FAST SOFT-SEAM WITH SIMD COLOR ACCUMULATION
    // Replaces the slow neighbor-scan near-seam logic and hard transitions.
    // Complexity: O(#pixels × #images) but with very low constant factors due to:
    //  - trig LUTs per row/column,
    //  - top-K (2–3) contributor selection per pixel,
    //  - edge/angle aware soft weights (no window scans),
    //  - Vector4 math for RGBA accumulation.
    // ─────────────────────────────────────────────────────────────────────────────

    if (State != PanoramaState.ReadyForPreview && State != PanoramaState.Completed)
    {
        Log("⚠️ Not ready for blending"); return;
    }

    State = PanoramaState.Blending;
    Log($"→ Blending to: {path} at {outputWidth}px width...");

    var groups = StitchGroups;
    if (groups.Count == 0) { State = PanoramaState.Failed; return; }
    var mainGroup = groups.OrderByDescending(g => g.Images.Count).First();

    // 1) Compute panorama angular bounds from current cameras (same as before but tighter & faster)
    float minLon = float.MaxValue, maxLon = float.MinValue;
    float minLat = float.MaxValue, maxLat = float.MinValue;

    lock (_dataLock)
    {
        foreach (var img in mainGroup.Images)
        {
            var cam = _camera[img.Id];

            // Sample a light grid on the image to estimate spherical coverage
            const int SX = 10, SY = 10;
            for (int iy = 0; iy <= SY; iy++)
            {
                float py = (iy / (float)SY) * img.Dataset.Height;
                float ny = (py - cam.Cy) / cam.Fy;

                for (int ix = 0; ix <= SX; ix++)
                {
                    float px = (ix / (float)SX) * img.Dataset.Width;
                    float nx = (px - cam.Cx) / cam.Fx;

                    var rayCam = Vector3.Normalize(new Vector3(nx, ny, 1f));
                    var rayWorld = Vector3.Transform(rayCam, Quaternion.Inverse(cam.Rotation));

                    float lon = MathF.Atan2(rayWorld.X, rayWorld.Z);
                    float lat = MathF.Asin(Math.Clamp(rayWorld.Y, -1f, 1f));
                    if (lon < minLon) minLon = lon;
                    if (lon > maxLon) maxLon = lon;
                    if (lat < minLat) minLat = lat;
                    if (lat > maxLat) maxLat = lat;
                }
            }
        }
    }

    // Padding (5%) and clamping
    float lonPadding = (maxLon - minLon) * 0.05f;
    float latPadding = (maxLat - minLat) * 0.05f;
    minLon = Math.Max(-MathF.PI,  minLon - lonPadding);
    maxLon = Math.Min( MathF.PI,  maxLon + lonPadding);
    minLat = Math.Max(-MathF.PI/2, minLat - latPadding);
    maxLat = Math.Min( MathF.PI/2, maxLat + latPadding);

    float lonRange = MathF.Max(1e-5f, maxLon - minLon);
    float latRange = MathF.Max(1e-5f, maxLat - minLat);

    int outW = outputWidth;
    int outH = Math.Max(1, (int)(outputWidth * latRange / lonRange));
    Log($"Panorama spans: {lonRange * 180 / MathF.PI:F1}° × {latRange * 180 / MathF.PI:F1}°");
    Log($"Output size: {outW} × {outH}");

    // 2) Precompute trig LUTs: lon per column, lat per row
    var lonLUT = new float[outW];
    var sinLon = new float[outW];
    var cosLon = new float[outW];
    for (int x = 0; x < outW; x++)
    {
        float lon = minLon + (x / (float)(outW - 1)) * lonRange;
        lonLUT[x] = lon;
        sinLon[x] = MathF.Sin(lon);
        cosLon[x] = MathF.Cos(lon);
    }

    var latLUT = new float[outH];
    var sinLat = new float[outH];
    var cosLat = new float[outH];
    for (int y = 0; y < outH; y++)
    {
        float lat = minLat + (y / (float)(outH - 1)) * latRange;
        latLUT[y] = lat;
        sinLat[y] = MathF.Sin(lat);
        cosLat[y] = MathF.Cos(lat);
    }

    // 3) Snapshot image + camera data locally to avoid locks inside the hot path
    List<(ImageDataset ds, CameraModel cam)> imgs;
    lock (_dataLock)
    {
        imgs = mainGroup.Images.Select(i => (i.Dataset, _camera[i.Id])).ToList();
    }

    // 4) Output buffer
    var blended = new byte[outW * outH * 4];

    // 5) Fast soft-seam parameters (tune to taste)
    const int TOPK = 3;             // up to 3 images contribute per pixel
    const float TAU  = 0.08f;       // softness for softmin over score margins
    const float EDGE_FEATHER = 24f; // pixels (in source image) for edge falloff
    const float ANGLE_EXP    = 2.0f;// stronger down-weight at oblique angles
    const float EPS = 1e-7f;

    int rowsDone = 0;

    await Task.Run(() =>
    {
        // PARALLEL ROWS
        Parallel.For(0, outH, y =>
        {
            float sLat = sinLat[y];
            float cLat = cosLat[y];

            for (int x = 0; x < outW; x++)
            {
                // Equirectangular direction in WORLD space
                var worldRay = new Vector3(
                    cLat * sinLon[x],
                    sLat,
                    cLat * cosLon[x]
                );

                // Find top-K contributors (best score = lowest)
                // Score = center preference + slight angle penalty
                //   centerPref ~ (Δx/cx)^2 + (Δy/cy)^2
                //   angleWeight ~ prefer cameraRay.Z close to 1
                Span<int>   topIdx = stackalloc int[TOPK]   { -1, -1, -1 };
                Span<float> topSc  = stackalloc float[TOPK] { float.MaxValue, float.MaxValue, float.MaxValue };
                Span<float> topU   = stackalloc float[TOPK];
                Span<float> topV   = stackalloc float[TOPK];

                for (int i = 0; i < imgs.Count; i++)
                {
                    var (ds, cam) = imgs[i];
                    // Transform world ray to camera space
                    var camRay = Vector3.Transform(worldRay, cam.Rotation);
                    if (camRay.Z <= EPS) continue; // behind camera

                    float u = cam.Fx * (camRay.X / camRay.Z) + cam.Cx;
                    float v = cam.Fy * (camRay.Y / camRay.Z) + cam.Cy;

                    // Inside bounds (minus 1 for bilinear)
                    if (u < 0f || v < 0f || u >= ds.Width - 1 || v >= ds.Height - 1) continue;

                    // Center preference in normalized coords
                    float nx = (u - cam.Cx) / Math.Max(1f, cam.Cx);
                    float ny = (v - cam.Cy) / Math.Max(1f, cam.Cy);
                    float centerPref = nx * nx + ny * ny; // smaller better

                    // Angle preference (prefer facing pixels)
                    float cosTheta = MathF.Max(0f, camRay.Z / camRay.Length()); // ∈ [0..1]
                    float anglePenalty = (1f - cosTheta); // 0 at frontal, 1 at grazing

                    // Total score: main term centerPref + mild angle penalty
                    float score = centerPref + 0.15f * anglePenalty;

                    // Insert into top-K (simple small-K insertion)
                    int pos = -1;
                    if (score < topSc[TOPK-1]) pos = TOPK-1;
                    if (pos >= 0)
                    {
                        // bubble up
                        for (int k = TOPK - 1; k > 0 && score < topSc[k - 1]; k--)
                        {
                            topSc[k]  = topSc[k - 1];
                            topIdx[k] = topIdx[k - 1];
                            topU[k]   = topU[k - 1];
                            topV[k]   = topV[k - 1];
                        }
                        int ins = 0;
                        while (ins < TOPK && score >= topSc[ins]) ins++;
                        if (ins >= TOPK) ins = TOPK - 1;

                        topSc[ins]  = score;
                        topIdx[ins] = i;
                        topU[ins]   = u;
                        topV[ins]   = v;
                    }
                }

                // If nothing covers this pixel, leave transparent
                if (topIdx[0] < 0)
                    continue;

                // Compute weights via softmin on score margins + edge & angle falloffs
                // We normalize weights so they sum to 1.
                float sMin = topSc[0];
                Span<float> w = stackalloc float[TOPK] { 0f, 0f, 0f };
                float wSum = 0f;

                for (int k = 0; k < TOPK; k++)
                {
                    int i = topIdx[k];
                    if (i < 0) continue;

                    var (ds, cam) = imgs[i];

                    // Edge falloff: distance to nearest image edge (in pixels)
                    float du = MathF.Min(topU[k], ds.Width  - 1 - topU[k]);
                    float dv = MathF.Min(topV[k], ds.Height - 1 - topV[k]);
                    float dEdge = MathF.Max(0f, MathF.Min(du, dv));
                    // Smoothstep edge factor in [0..1]
                    float t = MathF.Max(0f, MathF.Min(1f, dEdge / EDGE_FEATHER));
                    float edgeFactor = t * t * (3f - 2f * t);

                    // Angle factor (prefer frontal)
                    var camRayForAngle = Vector3.Transform(worldRay, imgs[i].cam.Rotation);
                    float cosTheta = MathF.Max(0f, camRayForAngle.Z / camRayForAngle.Length());
                    float angleFactor = MathF.Pow(cosTheta, ANGLE_EXP);

                    // Softmin over score margin
                    float margin = (topSc[k] - sMin);
                    float soft = MathF.Exp(-margin / MathF.Max(EPS, TAU));

                    float wk = soft * edgeFactor * angleFactor;
                    w[k] = wk;
                    wSum += wk;
                }

                if (wSum <= EPS)
                {
                    // Fallback to the best-only
                    var (ds, _) = imgs[topIdx[0]];
                    var c0 = SampleBilinearVec4(ds.ImageData, ds.Width, ds.Height, topU[0], topV[0]);
                    WritePixel(blended, outW, x, y, c0);
                    continue;
                }

                // Normalize weights
                float invWSum = 1f / wSum;
                for (int k = 0; k < TOPK; k++) w[k] *= invWSum;

                // Accumulate colors with Vector4 math (SIMD-friendly)
                Vector4 acc = Vector4.Zero;
                for (int k = 0; k < TOPK; k++)
                {
                    int i = topIdx[k];
                    if (i < 0 || w[k] <= 0f) continue;
                    var (ds, _) = imgs[i];
                    var c = SampleBilinearVec4(ds.ImageData, ds.Width, ds.Height, topU[k], topV[k]);
                    acc += c * w[k];
                }

                WritePixel(blended, outW, x, y, acc);
            }

            int r = Interlocked.Increment(ref rowsDone);
            if ((r & 0xF) == 0) // every 16 rows
            {
                UpdateProgress(r / (float)outH, $"Blending ({r}/{outH})");
            }
        });
    });

    Log("✓ Blending complete");

    // 6) Save to disk (safe path using SKImage.FromPixels)
    var info = new SKImageInfo(outW, outH, SKColorType.Rgba8888, SKAlphaType.Unpremul);
    using var skData = SKData.CreateCopy(blended);
    using var skImg  = SKImage.FromPixels(info, skData);
    using var encoded = skImg.Encode(SKEncodedImageFormat.Png, 95);
    using var f = File.Create(path);
    encoded.SaveTo(f);

    State = PanoramaState.Completed;
    Log($"✓ Saved: {path}");

    // ──────────────────────────────── Local helpers ───────────────────────────────
    static Vector4 SampleBilinearVec4(byte[] img, int w, int h, float u, float v)
    {
        int x = (int)u;
        int y = (int)v;

        float fu = u - x;
        float fv = v - y;
        float omfu = 1f - fu;
        float omfv = 1f - fv;

        int i00 = (y * w + x) * 4;
        int i10 = i00 + 4;
        int i01 = i00 + w * 4;
        int i11 = i01 + 4;

        // Clamp the linear indices (saves bounds branches)
        if (i10 > img.Length - 4) i10 = img.Length - 4;
        if (i01 > img.Length - 4) i01 = img.Length - 4;
        if (i11 > img.Length - 4) i11 = img.Length - 4;

        // Bilinear mix per channel (kept in [0..255] domain to avoid extra conversions)
        float w00 = omfu * omfv;
        float w10 = fu   * omfv;
        float w01 = omfu * fv;
        float w11 = fu   * fv;

        float r = img[i00]     * w00 + img[i10]     * w10 + img[i01]     * w01 + img[i11]     * w11;
        float g = img[i00 + 1] * w00 + img[i10 + 1] * w10 + img[i01 + 1] * w01 + img[i11 + 1] * w11;
        float b = img[i00 + 2] * w00 + img[i10 + 2] * w10 + img[i01 + 2] * w01 + img[i11 + 2] * w11;
        return new Vector4(r, g, b, 255f);
    }

    static void WritePixel(byte[] dst, int strideW, int x, int y, in Vector4 c)
    {
        int idx = (y * strideW + x) * 4;
        dst[idx + 0] = (byte)Math.Clamp(c.X + 0.5f, 0f, 255f);
        dst[idx + 1] = (byte)Math.Clamp(c.Y + 0.5f, 0f, 255f);
        dst[idx + 2] = (byte)Math.Clamp(c.Z + 0.5f, 0f, 255f);
        dst[idx + 3] = 255;
    }
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

        public void Cancel()
        {
            lock (_ctsLock)
            {
                _cts?.Cancel();
            }
        }
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
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // 1) Stop any running pipeline
            lock (_ctsLock)
            {
                try { _cts?.Cancel(); } catch { /* ignore */ }
            }

            // 2) Give the current run a brief chance to observe cancellation
            try { _runningTask?.Wait(250); } catch { /* ignore */ }

            // 3) Free caches and big graphs
            try { ClearCaches(); } catch { /* ignore */ }

            // 4) Dispose CTS
            lock (_ctsLock)
            {
                try { _cts?.Dispose(); } catch { /* ignore */ }
                _cts = null;
            }
        }
        public static void ForceGcCompaction()
        {
            try
            {
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
                    System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            }
            catch { /* ignore */ }
        }
        public void ClearCaches()
        {
            lock (_dataLock)
            {
                // SIFT feature blobs (descriptors) are usually the biggest hog
                _sift?.Clear();

                // The per-image staging list
                _images?.Clear();

                // Camera models computed for each image
                _camera?.Clear();

                // The graph (edges + transforms)
                Graph = null;

                // Empty the queue of old log messages to prevent a memory leak
                while (Logs.TryDequeue(out _)) { }
            }
        }
        /// <summary>
        /// Refines the panorama by computing a new relative rotation from manual points and re-optimizing the global rotations.
        /// </summary>
        public Task RefineWithManualPointsAsync(PanoramaImage img1, PanoramaImage img2, List<(Vector2 P1, Vector2 P2)> manualPairs)
        {
            lock (_runLock)
            {
                if (_runningTask != null && !_runningTask.IsCompleted)
                {
                    Log("⏳ Processing already running, skipping refinement.");
                    return _runningTask;
                }

                if (manualPairs.Count < 8)
                {
                    Log($"⚠️ Refinement requires at least 8 point pairs, but got {manualPairs.Count}.");
                    return Task.CompletedTask;
                }

                lock (_ctsLock)
                {
                    _cts?.Dispose();
                    _cts = new CancellationTokenSource();
                }
                var token = _cts.Token;

                _runningTask = Task.Run(async () =>
                {
                    try
                    {
                        State = PanoramaState.MatchingFeatures;
                        Log("════════════════════════════════════════════════════════════");
                        Log($"→ Refining with {manualPairs.Count} manual points for {img1.Dataset.Name} ↔ {img2.Dataset.Name}");

                        // 1. Get camera models
                        CameraModel cam1, cam2;
                        lock (_dataLock)
                        {
                            cam1 = _camera[img1.Id];
                            cam2 = _camera[img2.Id];
                        }

                        // 2. Create mock KeyPoints and FeatureMatches from the manual pairs
                        var keypoints1 = manualPairs.Select(p => new KeyPoint { X = p.P1.X, Y = p.P1.Y }).ToList();
                        var keypoints2 = manualPairs.Select(p => new KeyPoint { X = p.P2.X, Y = p.P2.Y }).ToList();
                        var matches = Enumerable.Range(0, manualPairs.Count)
                            .Select(i => new FeatureMatch { QueryIndex = i, TrainIndex = i })
                            .ToList();

                        // 3. Normalize points and solve for Essential Matrix directly (no RANSAC needed)
                        var normPts1 = matches.Select(m => NormalizePoint(keypoints1[m.QueryIndex], cam1)).ToList();
                        var normPts2 = matches.Select(m => NormalizePoint(keypoints2[m.TrainIndex], cam2)).ToList();
                        var E = SolveEssentialMatrix8Point(normPts1, normPts2);

                        if (!E.HasValue)
                        {
                            throw new InvalidOperationException("Could not solve Essential Matrix from the provided manual points.");
                        }

                        // 4. Decompose E to get the refined relative rotation R
                        var (R, t, inliers) = DecomposeEssentialMatrix(E.Value, normPts1, normPts2, matches);

                        if (!R.HasValue)
                        {
                            throw new InvalidOperationException("Could not decompose Essential Matrix to find a valid rotation.");
                        }

                        Log($"✓ Refined relative rotation found from {inliers.Count} manual points.");

                        // 5. Create a new "pseudo-homography" from the refined rotation
                        var H_refined = cam2.K * R.Value * cam1.K_inv;

                        // 6. Update the graph with this high-confidence link
                        Graph.UpdateEdge(img1, img2, inliers, H_refined);
                        Log("✓ Stitch graph updated with high-confidence manual link.");

                        // 7. Re-run the global rotation optimization
                        token.ThrowIfCancellationRequested();
                        await EstimateCameraRotationsAsync(token);

                        State = PanoramaState.ReadyForPreview;
                        Log("✓ Refinement complete. Panorama is ready for preview.");
                        Log("════════════════════════════════════════════════════════════");
                    }
                    catch (OperationCanceledException)
                    {
                        State = PanoramaState.Failed;
                        Log("🛑 Refinement Canceled");
                    }
                    catch (Exception ex)
                    {
                        State = PanoramaState.Failed;
                        Log($"❌ REFINEMENT FATAL: {ex.Message}");
                        Logger.LogError($"[Panorama Refine] {ex}");
                    }
                }, token);
                return _runningTask;
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