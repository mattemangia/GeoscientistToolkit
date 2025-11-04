// GeoscientistToolkit/Business/Panorama/PanoramaStitchingService.cs

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using GeoscientistToolkit.Business.Photogrammetry.Math;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Util;
using SkiaSharp;

namespace GeoscientistToolkit.Business.Panorama;

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

public class PanoramaStitchingService
{
    public enum PanoramaProjection
    {
        Planar = 0,
        Cylindrical = 1,
        Spherical = 2
    }

    private readonly List<ImageDataset> _datasets;
    private CancellationTokenSource _cancellationTokenSource;

    public PanoramaStitchingService(List<ImageDataset> datasets)
    {
        _datasets = datasets;
    }

    public ProjectionSettings Projection { get; } = new();

    public PanoramaState State { get; private set; } = PanoramaState.Idle;
    public float Progress { get; private set; }
    public string StatusMessage { get; private set; }
    public ConcurrentQueue<string> Logs { get; } = new();
    public List<PanoramaImage> Images { get; } = new();
    public StitchGraph Graph { get; private set; }
    public List<StitchGroup> StitchGroups => Graph?.FindConnectedComponents() ?? new List<StitchGroup>();

    private static float EstimateFocalPx(int w, int h)
    {
        return 1.1f * Math.Max(w, h);
    }
    
    private Matrix3x3 GetCameraIntrinsics(PanoramaImage image)
    {
        var w = image.Dataset.Width;
        var h = image.Dataset.Height;
        var f = Projection.FocalPx > 0 ? Projection.FocalPx : EstimateFocalPx(w, h);
        var cx = w * 0.5f;
        var cy = h * 0.5f;
        return new Matrix3x3(f, 0, cx, 0, f, cy, 0, 0, 1);
    }
    
    public Task StartProcessingAsync()
    {
        _cancellationTokenSource = new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;

        return Task.Run(async () =>
        {
            try
            {
                Log("Starting panorama stitching process...");
                State = PanoramaState.Initializing;
                UpdateProgress(0, "Initializing...");
                
                Log("Using OpenCL-based feature detection and matching.");

                Images.Clear();
                foreach (var ds in _datasets)
                {
                    ds.Load();
                    if (ds.ImageData == null)
                    {
                        Log($"Warning: Could not load image data for {ds.Name}. Skipping.");
                        continue;
                    }
                    Images.Add(new PanoramaImage(ds));
                }

                token.ThrowIfCancellationRequested();
                await DetectFeaturesAsync(token);
                token.ThrowIfCancellationRequested();
                await MatchFeaturesAsync(token);
                token.ThrowIfCancellationRequested();
                AnalyzeConnectivity();
            }
            catch (OperationCanceledException)
            {
                State = PanoramaState.Failed;
                Log("Process was canceled by the user.");
            }
            catch (Exception ex)
            {
                State = PanoramaState.Failed;
                StatusMessage = "An error occurred.";
                Log($"FATAL ERROR: {ex.Message}\n{ex.StackTrace}");
                Logger.LogError($"[PanoramaService] {ex.Message}");
            }
        }, token);
    }

    private async Task DetectFeaturesAsync(CancellationToken token)
    {
        State = PanoramaState.DetectingFeatures;
        Log("Detecting ORB features in all images...");

        using var detector = new OrbFeatureDetectorCL();
        var totalImages = Images.Count;
        var processedCount = 0;

        var tasks = Images.Select(async image =>
        {
            try
            {
                Log($"Detecting features in {image.Dataset.Name}...");
                using var bitmap = SKBitmap.Decode(image.Dataset.FilePath);
                if (bitmap == null)
                {
                    Log($"Failed to load image: {image.Dataset.Name}");
                    return;
                }
                image.Features = await detector.DetectFeaturesAsync(bitmap, token);
                Log($"Found {image.Features.KeyPoints.Count} keypoints in {image.Dataset.Name}.");
            }
            catch (Exception ex)
            {
                Log($"Failed to detect features in {image.Dataset.Name}: {ex.Message}");
            }
            finally
            {
                Interlocked.Increment(ref processedCount);
                UpdateProgress((float)processedCount / totalImages, $"Detecting features... ({processedCount}/{totalImages})");
            }
        });
        await Task.WhenAll(tasks);
        Log("Feature detection complete.");
    }

    private async Task MatchFeaturesAsync(CancellationToken token)
    {
        State = PanoramaState.MatchingFeatures;
        Log("Matching features and estimating rotations...");

        Graph = new StitchGraph(Images);
        var pairs = new List<(PanoramaImage, PanoramaImage)>();
        for (var i = 0; i < Images.Count; i++)
        for (var j = i + 1; j < Images.Count; j++)
            pairs.Add((Images[i], Images[j]));

        var processedCount = 0;
        using var matcher = new FeatureMatcherCL();

        var tasks = pairs.Select(async pair =>
        {
            var (image1, image2) = pair;
            if (image1.Features.KeyPoints.Count > 20 && image2.Features.KeyPoints.Count > 20)
            {
                var matches = await matcher.MatchFeaturesAsync(image1.Features, image2.Features, token);
                if (matches.Count >= 8)
                {
                    var (homography, inliers) = FeatureMatcherCL.FindHomographyRANSAC(
                        matches, image1.Features.KeyPoints, image2.Features.KeyPoints);
                    
                    Log($"Raw matches {image1.Dataset.Name} ↔ {image2.Dataset.Name}: {matches.Count}, inliers: {inliers.Count}");
                    if (homography.HasValue && inliers.Count >= 8)
                    {
                        var K1 = GetCameraIntrinsics(image1);
                        Matrix3x3.Invert(K1, out var K1_inv);
                        var K2 = GetCameraIntrinsics(image2);
                        
                        var R = K1_inv * homography.Value * K2;

                        Vector3 c1 = Vector3.Normalize(new Vector3(R.M11, R.M21, R.M31));
                        Vector3 c2 = Vector3.Normalize(new Vector3(R.M12, R.M22, R.M32));
                        Vector3 c3 = Vector3.Cross(c1, c2);
                        var pureRotation = new Matrix3x3(c1.X, c2.X, c3.X, c1.Y, c2.Y, c3.Y, c1.Z, c2.Z, c3.Z);

                        Graph.AddEdge(image1, image2, inliers, pureRotation);
                        Log($"Estimated stable rotation for {image1.Dataset.Name} ↔ {image2.Dataset.Name}.");
                    }
                }
            }
            Interlocked.Increment(ref processedCount);
            UpdateProgress((float)processedCount / pairs.Count, $"Matching pairs... ({processedCount}/{pairs.Count})");
        });

        await Task.WhenAll(tasks);
        Log("Feature matching complete.");
    }
    
    private void AnalyzeConnectivity()
    {
        const int maxBridgeAttempts = 5; 
        for (int attempt = 0; attempt < maxBridgeAttempts; attempt++)
        {
            if (Images.Count < 2)
            {
                State = PanoramaState.Failed;
                StatusMessage = "Not enough images to create a panorama.";
                Log("Error: At least two images are required.");
                return;
            }

            var stitchGroups = StitchGroups;
            if (stitchGroups.Count <= 1)
            {
                State = PanoramaState.ReadyForPreview;
                StatusMessage = "Ready to generate preview.";
                Log($"Connectivity established. Found {stitchGroups.Count} group(s).");
                return;
            }

            Log($"Attempt {attempt + 1}: Detected {stitchGroups.Count} disconnected groups. Trying to find the best bridge...");
            bool bridged = FindAndAddBestBridge(CancellationToken.None);

            if (!bridged)
            {
                State = PanoramaState.AwaitingManualInput;
                StatusMessage = "User input required to proceed.";
                Log($"Bridging failed. Found {stitchGroups.Count} disconnected groups. Please create manual links.");
                return;
            }
        }
        
        Log("Max bridge attempts reached. There may still be disconnected groups.");
        State = PanoramaState.AwaitingManualInput;
    }

    private bool FindAndAddBestBridge(CancellationToken token)
    {
        var groups = StitchGroups;
        if (groups.Count <= 1) return false;

        using var matcher = new FeatureMatcherCL();

        var bestBridge = new
        {
            ImgA = (PanoramaImage)null,
            ImgB = (PanoramaImage)null,
            Inliers = (List<FeatureMatch>)null,
            Homography = (Matrix3x3?)null,
            Score = -1
        };

        for (int i = 0; i < groups.Count; i++)
        {
            for (int j = i + 1; j < groups.Count; j++)
            {
                foreach (var imgA in groups[i].Images)
                {
                    foreach (var imgB in groups[j].Images)
                    {
                        token.ThrowIfCancellationRequested();

                        var matches = matcher.MatchFeaturesAsync(imgA.Features, imgB.Features, token).GetAwaiter().GetResult();
                        if (matches.Count < 5) continue;

                        var (homography, inliers) = FeatureMatcherCL.FindHomographyRANSAC(
                            matches, imgA.Features.KeyPoints, imgB.Features.KeyPoints,
                            maxIters: 2000, reprojThresh: 10.0f);

                        if (homography.HasValue && inliers.Count > bestBridge.Score)
                        {
                            bestBridge = new
                            {
                                ImgA = imgA,
                                ImgB = imgB,
                                Inliers = inliers,
                                Homography = homography,
                                Score = inliers.Count
                            };
                        }
                    }
                }
            }
        }

        if (bestBridge.Score >= 5)
        {
            var K1_inv = GetCameraIntrinsics(bestBridge.ImgA);
            Matrix3x3.Invert(K1_inv, out K1_inv);
            var K2 = GetCameraIntrinsics(bestBridge.ImgB);
            var R = K1_inv * bestBridge.Homography.Value * K2;

            Vector3 c1 = Vector3.Normalize(new Vector3(R.M11, R.M21, R.M31));
            Vector3 c2 = Vector3.Normalize(new Vector3(R.M12, R.M22, R.M32));
            Vector3 c3 = Vector3.Cross(c1, c2);
            var pureRotation = new Matrix3x3(c1.X, c2.X, c3.X, c1.Y, c2.Y, c3.Y, c1.Z, c2.Z, c3.Z);

            Graph.AddEdge(bestBridge.ImgA, bestBridge.ImgB, bestBridge.Inliers, pureRotation);
            Log($"Found best bridge: {bestBridge.ImgA.Dataset.Name} ↔ {bestBridge.ImgB.Dataset.Name} (inliers: {bestBridge.Score}). Graph updated.");
            return true;
        }

        return false;
    }


    public void AddManualLinkAndRecompute(PanoramaImage img1, PanoramaImage img2, List<(Vector2 P1, Vector2 P2)> points)
    {
        if (points.Count < 4)
        {
            Log("Manual link failed: At least 4 point pairs are required.");
            return;
        }

        var src = points.Select(p => (p.P1.X, p.P1.Y)).ToList();
        var dst = points.Select(p => (p.P2.X, p.P2.Y)).ToList();

        if (FeatureMatcherCL.SolveHomographyLinearNormalized(src, dst, out var homography))
        {
            var K1 = GetCameraIntrinsics(img1);
            Matrix3x3.Invert(K1, out var K1_inv);
            var K2 = GetCameraIntrinsics(img2);
            var R = K1_inv * homography * K2;

            Vector3 c1 = Vector3.Normalize(new Vector3(R.M11, R.M21, R.M31));
            Vector3 c2 = Vector3.Normalize(new Vector3(R.M12, R.M22, R.M32));
            Vector3 c3 = Vector3.Cross(c1, c2);
            var pureRotation = new Matrix3x3(c1.X, c2.X, c3.X, c1.Y, c2.Y, c3.Y, c1.Z, c2.Z, c3.Z);
            
            Graph.AddEdge(img1, img2, new List<FeatureMatch>(), pureRotation);
            Log($"Successfully added manual link between {img1.Dataset.Name} and {img2.Dataset.Name}.");
            Log("Re-analyzing connectivity...");
            AnalyzeConnectivity();
        }
        else
        {
            Log($"Failed to compute a valid link between {img1.Dataset.Name} and {img2.Dataset.Name}.");
        }
    }

    public void RemoveImage(PanoramaImage imageToRemove)
    {
        if (imageToRemove == null) return;
        Images.Remove(imageToRemove);
        Graph?.RemoveNode(imageToRemove.Id);
        Log($"Discarded image: {imageToRemove.Dataset.Name}");
        Log("Re-analyzing connectivity after image removal...");
        AnalyzeConnectivity();
    }

    public Task StartBlendingAsync(string outputPath)
    {
        _cancellationTokenSource ??= new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;
        return Task.Run(async () =>
        {
            try
            {
                State = PanoramaState.Blending;
                var sw = Stopwatch.StartNew();
                Log($"Starting final blend process to output file: {outputPath}");

                var mainGroup = StitchGroups.OrderByDescending(g => g.Images.Count).First();
                Log($"Blending {mainGroup.Images.Count} images...");

                UpdateProgress(0.05f, "Computing global transformations...");
                token.ThrowIfCancellationRequested();
                ComputeGlobalRotations(mainGroup, token);

                UpdateProgress(0.15f, "Computing canvas bounds...");
                token.ThrowIfCancellationRequested();
                var (canvasWidth, canvasHeight, offsetX, offsetY) = ComputeCanvasBounds(mainGroup.Images);
                Log($"Canvas size: {canvasWidth}x{canvasHeight}, offset: ({offsetX}, {offsetY})");

                UpdateProgress(0.3f, "Warping and blending images...");
                token.ThrowIfCancellationRequested();
                var blendedImage = await BlendImagesAsync(mainGroup.Images, canvasWidth, canvasHeight, offsetX, offsetY, token);

                UpdateProgress(0.9f, "Saving panorama...");
                token.ThrowIfCancellationRequested();
                SavePanoramaImage(outputPath, blendedImage, canvasWidth, canvasHeight);

                sw.Stop();
                // Note: The success log message is now inside the robust SavePanoramaImage method.
                State = PanoramaState.Completed;
                StatusMessage = "Panorama created successfully!";
                UpdateProgress(1.0f, "Complete!");
            }
            catch (OperationCanceledException)
            {
                State = PanoramaState.Failed;
                Log("Blending process was canceled by the user.");
            }
            catch (Exception ex)
            {
                State = PanoramaState.Failed;
                StatusMessage = "Blending failed.";
                Log($"FATAL BLENDING ERROR: {ex.Message}\n{ex.StackTrace}");
                Logger.LogError($"[PanoramaService Blending] {ex.Message}");
            }
        }, token);
    }
    
    private void ComputeGlobalRotations(StitchGroup group, CancellationToken token)
    {
        Log("Performing global alignment (bundle adjustment)...");
        if (group.Images.Count == 0) return;
        
        foreach (var img in group.Images)
        {
            img.GlobalRotation = Matrix3x3.Identity;
        }

        var referenceImage = group.Images.OrderByDescending(img => Graph._adj.ContainsKey(img.Id) ? Graph._adj[img.Id].Count : 0).First();
        
        var q = new Queue<PanoramaImage>();
        var visited = new HashSet<Guid>();

        referenceImage.GlobalRotation = Matrix3x3.Identity;
        q.Enqueue(referenceImage);
        visited.Add(referenceImage.Id);

        while (q.Count > 0)
        {
            var current = q.Dequeue();
            if (!Graph._adj.ContainsKey(current.Id)) continue;
            foreach (var (neighborId, _, relRot) in Graph._adj[current.Id])
            {
                if (!visited.Contains(neighborId))
                {
                    visited.Add(neighborId);
                    var neighbor = Graph.GetImageById(neighborId);
                    neighbor.GlobalRotation = current.GlobalRotation * relRot;
                    q.Enqueue(neighbor);
                }
            }
        }
        
        Log("Refining global rotations...");
        for (int iter = 0; iter < 10; iter++)
        {
            token.ThrowIfCancellationRequested();
            bool changed = false;
            foreach (var img in group.Images)
            {
                if (!Graph._adj.ContainsKey(img.Id) || Graph._adj[img.Id].Count == 0) continue;

                var R_avg = new Matrix4x4();
                foreach (var (neighborId, _, relRot) in Graph._adj[img.Id])
                {
                    var neighbor = Graph.GetImageById(neighborId);
                    var R_est = neighbor.GlobalRotation * Matrix3x3.Transpose(relRot);
                    R_avg += ToMatrix4x4(R_est);
                }
                
                var avgM = R_avg * (1.0f / Graph._adj[img.Id].Count);
                Matrix4x4.Decompose(avgM, out _, out var avgQ, out _);
                var newRot = ToMatrix3x3(Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(avgQ)));

                var diff = ToMatrix4x4(img.GlobalRotation) - ToMatrix4x4(newRot);
                float diffNorm = (diff.M11*diff.M11 + diff.M12*diff.M12 + diff.M13*diff.M13 +
                                  diff.M21*diff.M21 + diff.M22*diff.M22 + diff.M23*diff.M23 +
                                  diff.M31*diff.M31 + diff.M32*diff.M32 + diff.M33*diff.M33);

                if (diffNorm > 1e-6f)
                {
                    changed = true;
                }
                img.GlobalRotation = newRot;
            }
            if (!changed)
            {
                Log($"Global alignment converged after {iter + 1} iterations.");
                break;
            }
        }
        Log("Global alignment complete.");
    }

    private (int width, int height, int offsetX, int offsetY) ComputeCanvasBounds(List<PanoramaImage> images)
    {
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        foreach (var image in images)
        {
            var K = GetCameraIntrinsics(image);
            var corners = new[] {
                new Vector2(0, 0), new Vector2(image.Dataset.Width, 0),
                new Vector2(image.Dataset.Width, image.Dataset.Height), new Vector2(0, image.Dataset.Height)
            };
            foreach(var corner in corners)
            {
                var pWorld = TransformPoint(corner, K, image.GlobalRotation);
                minX = Math.Min(minX, pWorld.X); minY = Math.Min(minY, pWorld.Y);
                maxX = Math.Max(maxX, pWorld.X); maxY = Math.Max(maxY, pWorld.Y);
            }
        }

        if (minX > maxX || minY > maxY) return (0, 0, 0, 0);

        minX -= Projection.ExtraPaddingPx; minY -= Projection.ExtraPaddingPx;
        maxX += Projection.ExtraPaddingPx; maxY += Projection.ExtraPaddingPx;
        var width = (int)Math.Ceiling(maxX - minX);
        var height = (int)Math.Ceiling(maxY - minY);
        return (width, height, (int)Math.Floor(minX), (int)Math.Floor(minY));
    }

    public async Task<byte[]> BlendImagesAsync(List<PanoramaImage> images, 
        int canvasWidth, int canvasHeight, int offsetX, int offsetY, CancellationToken token)
    {
        var canvas = new float[canvasWidth * canvasHeight * 4]; // R, G, B, Weight
        var processedImages = 0;
        
        foreach (var image in images)
        {
            token.ThrowIfCancellationRequested();
            Log($"Warping and blending: {image.Dataset.Name}");

            await Task.Run(() =>
            {
                var K = GetCameraIntrinsics(image);
                var R_inv = Matrix3x3.Transpose(image.GlobalRotation);

                var srcData = image.Dataset.ImageData;
                int srcWidth = image.Dataset.Width;
                int srcHeight = image.Dataset.Height;

                var distanceMap = ComputeDistanceToEdge(srcWidth, srcHeight);
                float maxDist = Math.Min(srcWidth, srcHeight) / 2.0f;

                Parallel.For(0, canvasHeight, y =>
                {
                    for (var x = 0; x < canvasWidth; x++)
                    {
                        var canvasPt = new Vector2(x + offsetX, y + offsetY);
                        var srcPt = InverseTransformPoint(canvasPt, K, R_inv, srcWidth, srcHeight);

                        if (srcPt.X < 0 || srcPt.X >= srcWidth - 1 || srcPt.Y < 0 || srcPt.Y >= srcHeight - 1) continue;
                        
                        var (r, g, b) = BilinearSample(srcData, srcWidth, srcPt.X, srcPt.Y);
                        var distWeight = BilinearInterp(distanceMap, srcWidth, srcPt.X, srcPt.Y);
                        var weight = Math.Min(1.0f, distWeight / (maxDist * 0.5f));
                        
                        int canvasIdx = (y * canvasWidth + x) * 4;
                        lock (canvas)
                        {
                            canvas[canvasIdx] += r * weight;
                            canvas[canvasIdx + 1] += g * weight;
                            canvas[canvasIdx + 2] += b * weight;
                            canvas[canvasIdx + 3] += weight;
                        }
                    }
                });
            }, token);

            processedImages++;
            UpdateProgress(0.3f + 0.6f * processedImages / images.Count, $"Blending image {processedImages}/{images.Count}");
        }

        var result = new byte[canvasWidth * canvasHeight * 4];
        Parallel.For(0, canvasWidth * canvasHeight, i =>
        {
            var baseIdx = i * 4;
            float weight = canvas[baseIdx + 3];
            if (weight > 1e-6f)
            {
                result[baseIdx]     = (byte)Math.Max(0, Math.Min(255, canvas[baseIdx] / weight));
                result[baseIdx + 1] = (byte)Math.Max(0, Math.Min(255, canvas[baseIdx + 1] / weight));
                result[baseIdx + 2] = (byte)Math.Max(0, Math.Min(255, canvas[baseIdx + 2] / weight));
                result[baseIdx + 3] = 255;
            }
        });

        if (Projection.AutoCrop) AutoCropToContent(ref result, ref canvasWidth, ref canvasHeight);
        return result;
    }

    private (byte r, byte g, byte b) BilinearSample(byte[] imageData, int w, float u, float v)
    {
        int x = (int)u; int y = (int)v;
        float u_ratio = u - x; float v_ratio = v - y;
        float u_opposite = 1 - u_ratio; float v_opposite = 1 - v_ratio;
        
        int idx1 = (y * w + x) * 4; int idx2 = (y * w + (x+1)) * 4;
        int idx3 = ((y+1) * w + x) * 4; int idx4 = ((y+1) * w + (x+1)) * 4;

        byte r = (byte)((imageData[idx1] * u_opposite + imageData[idx2] * u_ratio) * v_opposite + (imageData[idx3] * u_opposite + imageData[idx4] * u_ratio) * v_ratio);
        byte g = (byte)((imageData[idx1+1] * u_opposite + imageData[idx2+1] * u_ratio) * v_opposite + (imageData[idx3+1] * u_opposite + imageData[idx4+1] * u_ratio) * v_ratio);
        byte b = (byte)((imageData[idx1+2] * u_opposite + imageData[idx2+2] * u_ratio) * v_opposite + (imageData[idx3+2] * u_opposite + imageData[idx4+2] * u_ratio) * v_ratio);
        return (r,g,b);
    }
    
    private float BilinearInterp(float[] data, int w, float u, float v)
    {
        int x = (int)u; int y = (int)v;
        float u_ratio = u - x; float v_ratio = v - y;
        float u_opposite = 1 - u_ratio; float v_opposite = 1 - v_ratio;
        
        int idx1 = y * w + x; int idx2 = y * w + (x+1);
        int idx3 = (y+1) * w + x; int idx4 = (y+1) * w + (x+1);
        
        return (data[idx1] * u_opposite + data[idx2] * u_ratio) * v_opposite + (data[idx3] * u_opposite + data[idx4] * u_ratio) * v_ratio;
    }

    private float[] ComputeDistanceToEdge(int width, int height)
    {
        var distanceMap = new float[width * height];
        Parallel.For(0, height, y => {
            for (var x = 0; x < width; x++) {
                distanceMap[y * width + x] = Math.Min(Math.Min(x, width - 1 - x), Math.Min(y, height - 1 - y));
            }
        });
        return distanceMap;
    }
    
    private Vector2 TransformPoint(Vector2 point, Matrix3x3 K, Matrix3x3 R)
    {
        Matrix3x3.Invert(K, out var K_inv);
        var ray = K_inv * new Vector3(point.X, point.Y, 1);
        Matrix3x3.Invert(R, out var R_inv);
        var worldRay = R_inv * ray;

        float f = K.M11;
        switch(Projection.Type)
        {
            case PanoramaProjection.Spherical:
                float lon = MathF.Atan2(worldRay.X, worldRay.Z);
                float lat = MathF.Asin(worldRay.Y / worldRay.Length());
                return new Vector2(f * lon, f * lat);
            case PanoramaProjection.Cylindrical:
                float theta = MathF.Atan2(worldRay.X, worldRay.Z);
                float h = worldRay.Y / MathF.Sqrt(worldRay.X * worldRay.X + worldRay.Z * worldRay.Z);
                return new Vector2(f * theta, f * h);
            default: // Planar
                var T = K * R_inv * K_inv;
                var p_h = T * new Vector3(point.X, point.Y, 1);
                return new Vector2(p_h.X / p_h.Z, p_h.Y / p_h.Z);
        }
    }
    
    private Vector2 InverseTransformPoint(Vector2 canvasPt, Matrix3x3 K, Matrix3x3 R_inv, int imgWidth, int imgHeight)
    {
        float f = K.M11;
        float cx = K.M13;
        float cy = K.M23;
        Vector3 worldRay;

        switch (Projection.Type)
        {
            case PanoramaProjection.Spherical:
                float lon = canvasPt.X / f;
                float lat = canvasPt.Y / f;
                worldRay = new Vector3(MathF.Sin(lon) * MathF.Cos(lat), MathF.Sin(lat), MathF.Cos(lon) * MathF.Cos(lat));
                break;
            case PanoramaProjection.Cylindrical:
                float theta = canvasPt.X / f;
                float h = canvasPt.Y / f;
                worldRay = new Vector3(MathF.Sin(theta), h, MathF.Cos(theta));
                worldRay = Vector3.Normalize(worldRay);
                break;
            default: // Planar
                 Matrix3x3.Invert(K * R_inv, out var T_inv);
                 Matrix3x3.Invert(K, out var K_inv);
                 T_inv *= K_inv;
                 var p_h = T_inv * new Vector3(canvasPt.X, canvasPt.Y, 1);
                 return new Vector2(p_h.X / p_h.Z, p_h.Y / p_h.Z);
        }
        
        var camRay = R_inv * worldRay;
        var p = new Vector3(camRay.X / camRay.Z, camRay.Y / camRay.Z, 1);
        
        float x = f * p.X + cx;
        float y = f * p.Y + cy;
        return new Vector2(x, y);
    }
    
    private void AutoCropToContent(ref byte[] rgba, ref int width, ref int height)
    {
        int minX = width, minY = height, maxX = -1, maxY = -1;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            if (rgba[(y * width + x) * 4 + 3] > 0)
            {
                minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
        }
        if (maxX < minX || maxY < minY) return;

        var newW = maxX - minX + 1;
        var newH = maxY - minY + 1;
        if (newW == width && newH == height) return;

        var outImg = new byte[newW * newH * 4];
        for (var y = 0; y < newH; y++)
            Buffer.BlockCopy(rgba, ((minY + y) * width + minX) * 4, outImg, y * newW * 4, newW * 4);
        rgba = outImg;
        width = newW;
        height = newH;
    }

    // *** FIX: Rewritten to use a "save-to-temp-then-move" strategy to prevent crashes ***
    private void SavePanoramaImage(string outputPath, byte[] imageData, int width, int height)
    {
        string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + Path.GetExtension(outputPath));
        
        try
        {
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var bitmap = new SKBitmap();
        
            var handle = GCHandle.Alloc(imageData, GCHandleType.Pinned);
            try
            {
                var rowBytes = width * 4;
                bitmap.InstallPixels(info, handle.AddrOfPinnedObject(), rowBytes, (address, context) => handle.Free(), null);

                using var image = SKImage.FromBitmap(bitmap);
                var format = Path.GetExtension(outputPath).ToLowerInvariant() switch
                {
                    ".png" => SKEncodedImageFormat.Png,
                    ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
                    ".bmp" => SKEncodedImageFormat.Bmp,
                    _ => SKEncodedImageFormat.Png
                };
            
                using (var stream = File.Create(tempPath))
                {
                    image.Encode(format, 95).SaveTo(stream);
                }
            }
            finally
            {
                // This block is crucial. Even if InstallPixels or Encode fails, we must ensure the GCHandle is eventually freed.
                // The release delegate in InstallPixels handles the success case. This handles the failure case.
                if (handle.IsAllocated)
                {
                    handle.Free();
                }
            }

            // Atomically move the temporary file to the final destination, overwriting if it exists.
            File.Move(tempPath, outputPath, true);
            Log($"Successfully saved panorama to {outputPath}");
        }
        catch (Exception ex)
        {
            Log($"ERROR: Failed to save image. Reason: {ex.Message}");
            // Clean up the temporary file if the process failed.
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
            throw; // Re-throw the exception so the calling method knows about the failure.
        }
    }


    public void Cancel() { _cancellationTokenSource?.Cancel(); Log("Cancellation requested."); }
    private void UpdateProgress(float progress, string message) { Progress = progress; StatusMessage = message; }
    public void Log(string message) { var logMsg = $"[{DateTime.Now:HH:mm:ss}] {message}"; Logs.Enqueue(logMsg); Logger.Log(logMsg); }

    public bool TryBuildPreviewLayout(out List<(PanoramaImage Image, Vector2[] Quad)> quads, out (float MinX, float MinY, float MaxX, float MaxY) bounds)
    {
        quads = new List<(PanoramaImage, Vector2[])>();
        bounds = default;
        try
        {
            var mainGroup = StitchGroups.OrderByDescending(g => g.Images.Count).FirstOrDefault();
            if (mainGroup == null || mainGroup.Images.Count == 0) return false;

            ComputeGlobalRotations(mainGroup, CancellationToken.None);

            float minX=float.MaxValue, minY=float.MaxValue, maxX=float.MinValue, maxY=float.MinValue;
            foreach (var img in mainGroup.Images)
            {
                var K = GetCameraIntrinsics(img);
                var corners = new[] { new Vector2(0,0), new Vector2(img.Dataset.Width,0), new Vector2(img.Dataset.Width,img.Dataset.Height), new Vector2(0,img.Dataset.Height) };
                var quad = corners.Select(c => TransformPoint(c, K, img.GlobalRotation)).ToArray();
                quads.Add((img, quad));
                foreach(var p in quad) { minX=Math.Min(minX,p.X); minY=Math.Min(minY,p.Y); maxX=Math.Max(maxX,p.X); maxY=Math.Max(maxY,p.Y); }
            }
            if (quads.Count == 0) return false;
            bounds = (minX, minY, maxX, maxY);
            return true;
        }
        catch (Exception ex) { Log($"Error building preview: {ex.Message}"); return false; }
    }

    private static Matrix4x4 ToMatrix4x4(Matrix3x3 m) => new Matrix4x4(m.M11,m.M12,m.M13,0, m.M21,m.M22,m.M23,0, m.M31,m.M32,m.M33,0, 0,0,0,1);
    private static Matrix3x3 ToMatrix3x3(Matrix4x4 m) => new Matrix3x3(m.M11,m.M12,m.M13, m.M21,m.M22,m.M23, m.M31,m.M32,m.M33);
    public class ProjectionSettings { public bool AutoCrop = true; public int ExtraPaddingPx = 10; public float FocalPx = 0f; public PanoramaProjection Type = PanoramaProjection.Spherical; }
}