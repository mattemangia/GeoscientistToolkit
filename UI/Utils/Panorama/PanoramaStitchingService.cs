// GeoscientistToolkit/Business/Panorama/PanoramaStitchingService.cs

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
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
    public DatasetGroup ImageGroup { get; }
    public PanoramaStitchingService Service { get; }
    public Guid Id { get; } = Guid.NewGuid();

    public PanoramaStitchJob(DatasetGroup imageGroup)
    {
        ImageGroup = imageGroup;
        Service = new PanoramaStitchingService(imageGroup.Datasets.Cast<ImageDataset>().ToList());
    }
}

public class PanoramaStitchingService
{
    public PanoramaState State { get; private set; } = PanoramaState.Idle;
    public float Progress { get; private set; }
    public string StatusMessage { get; private set; }
    public ConcurrentQueue<string> Logs { get; } = new();
    public List<PanoramaImage> Images { get; } = new();
    public StitchGraph Graph { get; private set; }
    public List<StitchGroup> StitchGroups => Graph?.FindConnectedComponents() ?? new List<StitchGroup>();
    
    private readonly List<ImageDataset> _datasets;
    private CancellationTokenSource _cancellationTokenSource;

    public PanoramaStitchingService(List<ImageDataset> datasets)
    {
        _datasets = datasets;
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
                // OpenCLService is not a provided class, so its direct initialization is removed.
                // It is assumed that dependent classes like OrbFeatureDetectorCL handle their own setup.
                Log($"Using OpenCL-based feature detection and matching.");

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
        
                // Convert ImageDataset to SKBitmap
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
        Log("Matching features between image pairs...");

        Graph = new StitchGraph(Images);
        var pairs = new List<(PanoramaImage, PanoramaImage)>();
        for (int i = 0; i < Images.Count; i++)
        {
            for (int j = i + 1; j < Images.Count; j++)
            {
                pairs.Add((Images[i], Images[j]));
            }
        }

        int processedCount = 0;
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
                    if (homography.HasValue && inliers.Count >= 10)
                    {
                        Graph.AddEdge(image1, image2, inliers, homography.Value);
                        Log($"Found {inliers.Count} inlier matches between {image1.Dataset.Name} and {image2.Dataset.Name}.");
                    }
                }
            }
            Interlocked.Increment(ref processedCount);
            UpdateProgress((float)processedCount / pairs.Count, $"Matching pairs... ({processedCount}/{pairs.Count})");
        });

        await Task.WhenAll(tasks);
        Log("Feature matching complete.");
    }
    private PanoramaImage ChooseReferenceImage(StitchGroup group)
    {
        // pick the most connected image to reduce perspective skew
        var best = group.Images
            .Select(img => new { Img = img, Deg = (Graph._adj.TryGetValue(img.Id, out var n) ? n.Count : 0) })
            .OrderByDescending(x => x.Deg)
            .ThenBy(x => x.Img.Dataset.Name)
            .First().Img;
        return best;
    }
    private void AnalyzeConnectivity()
    {
        if (Images.Count < 2)
        {
            State = PanoramaState.Failed;
            StatusMessage = "Not enough images to create a panorama.";
            Log("Error: At least two images are required.");
            return;
        }

        var imagesWithNoFeatures = Images.Where(img => img.Features.KeyPoints.Count < 20).ToList();
        var stitchGroups = StitchGroups;

        if (imagesWithNoFeatures.Any() || stitchGroups.Count > 1)
        {
            State = PanoramaState.AwaitingManualInput;
            StatusMessage = "User input required to proceed.";
            Log("Process paused. Please resolve unmatched images or groups.");
        }
        else
        {
            State = PanoramaState.ReadyForPreview;
            StatusMessage = "Ready to generate preview.";
            Log("All images successfully matched into a single group.");
        }
    }

    public void AddManualLinkAndRecompute(PanoramaImage img1, PanoramaImage img2, List<(Vector2 P1, Vector2 P2)> points)
    {
        if (points.Count < 4)
        {
            Log("Manual link failed: At least 4 point pairs are required.");
            Logger.LogWarning("[PanoramaService] Manual link failed: Less than 4 points provided.");
            return;
        }

        var srcPoints = points.Select(p => p.P1).ToArray();
        var dstPoints = points.Select(p => p.P2).ToArray();
        
        var homography = ComputeHomography(srcPoints, dstPoints);

        if (homography.HasValue)
        {
            // For manual links, we pass an empty list of feature matches
            Graph.AddEdge(img1, img2, new List<FeatureMatch>(), homography.Value);
            Log($"Successfully added manual link between {img1.Dataset.Name} and {img2.Dataset.Name}.");
            Log("Re-analyzing connectivity...");
            AnalyzeConnectivity();
        }
        else
        {
            Log($"Failed to compute a valid link between {img1.Dataset.Name} and {img2.Dataset.Name}. Points may be collinear.");
            Logger.LogError("[PanoramaService] Failed to compute homography from manual points.");
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
                Log($"Starting final blend process to output file: {outputPath}");
                
                var components = StitchGroups;
                if (components.Count == 0 || components[0].Images.Count == 0)
                {
                    throw new InvalidOperationException("No connected images to blend.");
                }
                
                var mainGroup = components.OrderByDescending(g => g.Images.Count).First();
                Log($"Blending {mainGroup.Images.Count} images in the main group...");
                
                UpdateProgress(0.1f, "Computing global transformations...");
                token.ThrowIfCancellationRequested();
                
                var globalTransforms = ComputeGlobalTransformations(mainGroup, token);
                
                UpdateProgress(0.2f, "Computing canvas bounds...");
                token.ThrowIfCancellationRequested();
                
                var (canvasWidth, canvasHeight, offsetX, offsetY) = ComputeCanvasBounds(mainGroup.Images, globalTransforms);
                Log($"Canvas size: {canvasWidth}x{canvasHeight}, offset: ({offsetX}, {offsetY})");
                
                UpdateProgress(0.3f, "Warping and blending images...");
                token.ThrowIfCancellationRequested();
                
                var blendedImage = await BlendImagesAsync(mainGroup.Images, globalTransforms, 
                    canvasWidth, canvasHeight, offsetX, offsetY, token);
                
                UpdateProgress(0.9f, "Saving panorama...");
                token.ThrowIfCancellationRequested();
                
                SavePanoramaImage(outputPath, blendedImage, canvasWidth, canvasHeight);
                
                Log($"Successfully saved panorama to {outputPath}");
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
    
    private Dictionary<Guid, Matrix3x3> ComputeGlobalTransformations(StitchGroup group, CancellationToken token)
    {
        var global = new Dictionary<Guid, Matrix3x3>();
        var visited = new HashSet<Guid>();
        if (group.Images.Count == 0) return global;

        // anchor a center-ish image, not always the first
        var reference = ChooseReferenceImage(group);
        global[reference.Id] = Matrix3x3.Identity;

        var q = new Queue<Guid>();
        q.Enqueue(reference.Id);
        visited.Add(reference.Id);

        while (q.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var curId = q.Dequeue();
            var Tcur = global[curId];

            if (!Graph._adj.TryGetValue(curId, out var neighbors)) continue;

            foreach (var (nbrId, _, Hcur_to_nbr) in neighbors)
            {
                if (visited.Contains(nbrId)) continue;

                // We need T_nbr (nbr→world). Edge stores H(cur→nbr).
                // For any point p_i in 'cur' frame and p_j in 'nbr' frame:
                // p_j = Hcur_to_nbr * p_i, and world = Tcur * p_i = Tnbr * p_j
                // => Tnbr * Hcur_to_nbr = Tcur  =>  Tnbr = Tcur * Hcur_to_nbr^{-1}
                if (!Matrix3x3.Invert(Hcur_to_nbr, out var Hnbr_to_cur)) continue;

                // Normalize for numerical stability
                float s = Math.Abs(Hnbr_to_cur.M33) > 1e-8f ? Hnbr_to_cur.M33 : 1f;
                Hnbr_to_cur = new Matrix3x3(
                    Hnbr_to_cur.M11 / s, Hnbr_to_cur.M12 / s, Hnbr_to_cur.M13 / s,
                    Hnbr_to_cur.M21 / s, Hnbr_to_cur.M22 / s, Hnbr_to_cur.M23 / s,
                    Hnbr_to_cur.M31 / s, Hnbr_to_cur.M32 / s, 1f
                );

                global[nbrId] = Tcur * Hnbr_to_cur;
                visited.Add(nbrId);
                q.Enqueue(nbrId);
            }
        }

        return global;
    }
    private (int width, int height, int offsetX, int offsetY) ComputeCanvasBounds(
        List<PanoramaImage> images, Dictionary<Guid, Matrix3x3> transforms)
    {
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        
        foreach (var image in images)
        {
            if (!transforms.TryGetValue(image.Id, out var transform))
                continue;
                
            var corners = new Vector2[]
            {
                new Vector2(0, 0),
                new Vector2(image.Dataset.Width, 0),
                new Vector2(0, image.Dataset.Height),
                new Vector2(image.Dataset.Width, image.Dataset.Height)
            };
            
            foreach (var corner in corners)
            {
                var transformed = TransformPoint(corner, transform);
                minX = Math.Min(minX, transformed.X);
                minY = Math.Min(minY, transformed.Y);
                maxX = Math.Max(maxX, transformed.X);
                maxY = Math.Max(maxY, transformed.Y);
            }
        }
        
        int width = (int)Math.Ceiling(maxX - minX);
        int height = (int)Math.Ceiling(maxY - minY);
        int offsetX = (int)Math.Floor(minX);
        int offsetY = (int)Math.Floor(minY);
        
        return (width, height, offsetX, offsetY);
    }
    
    private Vector2 TransformPoint(Vector2 point, Matrix3x3 matrix)
    {
        float w = matrix.M31 * point.X + matrix.M32 * point.Y + matrix.M33;
        if (Math.Abs(w) < 1e-8f) w = 1.0f;
        
        return new Vector2(
            (matrix.M11 * point.X + matrix.M12 * point.Y + matrix.M13) / w,
            (matrix.M21 * point.X + matrix.M22 * point.Y + matrix.M23) / w
        );
    }
    
    private async Task<byte[]> BlendImagesAsync(
        List<PanoramaImage> images, 
        Dictionary<Guid, Matrix3x3> transforms,
        int canvasWidth, int canvasHeight, 
        int offsetX, int offsetY,
        CancellationToken token)
    {
        long estimatedMemory = (long)canvasWidth * canvasHeight * 24; // 24 bytes per pixel (float accumulators + weight)
        long availableMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        
        if (estimatedMemory > availableMemory * 0.7)
        {
            Log($"Large panorama detected. Using tile-based blending to conserve memory.");
            return await BlendImagesTiledAsync(images, transforms, canvasWidth, canvasHeight, offsetX, offsetY, token);
        }
        
        var canvas = new float[canvasWidth * canvasHeight * 4];
        var weights = new float[canvasWidth * canvasHeight];
        
        int processedImages = 0;
        foreach (var image in images)
        {
            token.ThrowIfCancellationRequested();
            
            if (!transforms.TryGetValue(image.Id, out var transform))
                continue;
                
            Log($"Warping and blending: {image.Dataset.Name}");
            
            var adjustedTransform = new Matrix3x3(
                transform.M11, transform.M12, transform.M13 - offsetX,
                transform.M21, transform.M22, transform.M23 - offsetY,
                transform.M31, transform.M32, transform.M33
            );
            
            await Task.Run(() => WarpAndBlendImage(
                image.Dataset, adjustedTransform, 
                canvas, weights, canvasWidth, canvasHeight), token);
            
            processedImages++;
            UpdateProgress(0.3f + 0.6f * processedImages / images.Count, 
                $"Blending image {processedImages}/{images.Count}");
        }
        
        var result = new byte[canvasWidth * canvasHeight * 4];
        Parallel.For(0, canvasWidth * canvasHeight, i =>
        {
            if (weights[i] > 1e-6f)
            {
                int baseIdx = i * 4;
                result[baseIdx]     = ClampToByte(canvas[baseIdx]     / weights[i]);
                result[baseIdx + 1] = ClampToByte(canvas[baseIdx + 1] / weights[i]);
                result[baseIdx + 2] = ClampToByte(canvas[baseIdx + 2] / weights[i]);
                result[baseIdx + 3] = 255;
            }
        });
        
        return result;
    }
    
    private async Task<byte[]> BlendImagesTiledAsync(
        List<PanoramaImage> images, 
        Dictionary<Guid, Matrix3x3> transforms,
        int canvasWidth, int canvasHeight, 
        int offsetX, int offsetY,
        CancellationToken token)
    {
        const int tileSize = 2048;
        int tilesX = (int)Math.Ceiling((double)canvasWidth / tileSize);
        int tilesY = (int)Math.Ceiling((double)canvasHeight / tileSize);
        
        Log($"Processing panorama in {tilesX}x{tilesY} tiles ({tileSize}x{tileSize} each)");
        
        var result = new byte[canvasWidth * canvasHeight * 4];
        int totalTiles = tilesX * tilesY;
        int processedTiles = 0;
        
        for (int ty = 0; ty < tilesY; ty++)
        {
            for (int tx = 0; tx < tilesX; tx++)
            {
                token.ThrowIfCancellationRequested();
                
                int tileX = tx * tileSize;
                int tileY = ty * tileSize;
                int tileW = Math.Min(tileSize, canvasWidth - tileX);
                int tileH = Math.Min(tileSize, canvasHeight - tileY);
                
                var tileCanvas = new float[tileW * tileH * 4];
                var tileWeights = new float[tileW * tileH];
                
                foreach (var image in images)
                {
                    if (!transforms.TryGetValue(image.Id, out var transform))
                        continue;
                    
                    var adjustedTransform = new Matrix3x3(
                        transform.M11, transform.M12, transform.M13 - offsetX,
                        transform.M21, transform.M22, transform.M23 - offsetY,
                        transform.M31, transform.M32, transform.M33
                    );
                    
                    await Task.Run(() => WarpAndBlendImageTile(
                        image.Dataset, adjustedTransform,
                        tileCanvas, tileWeights, tileW, tileH, tileX, tileY), token);
                }
                
                for (int y = 0; y < tileH; y++)
                {
                    for (int x = 0; x < tileW; x++)
                    {
                        int tileIdx = y * tileW + x;
                        if (tileWeights[tileIdx] > 1e-6f)
                        {
                            int canvasIdx = ((tileY + y) * canvasWidth + (tileX + x)) * 4;
                            result[canvasIdx]     = ClampToByte(tileCanvas[tileIdx * 4]     / tileWeights[tileIdx]);
                            result[canvasIdx + 1] = ClampToByte(tileCanvas[tileIdx * 4 + 1] / tileWeights[tileIdx]);
                            result[canvasIdx + 2] = ClampToByte(tileCanvas[tileIdx * 4 + 2] / tileWeights[tileIdx]);
                            result[canvasIdx + 3] = 255;
                        }
                    }
                }
                
                processedTiles++;
                UpdateProgress(0.3f + 0.6f * processedTiles / totalTiles,
                    $"Processing tile {processedTiles}/{totalTiles}");
                
                if (processedTiles % 10 == 0)
                {
                    GC.Collect();
                }
            }
        }
        
        return result;
    }
    
    private void WarpAndBlendImageTile(
        ImageDataset dataset, Matrix3x3 transform,
        float[] tileCanvas, float[] tileWeights, int tileWidth, int tileHeight,
        int tileOffsetX, int tileOffsetY)
    {
        if (dataset.ImageData == null) dataset.Load();
            
        var imageData = dataset.ImageData;
        var imgWidth = dataset.Width;
        var imgHeight = dataset.Height;
        
        if (!Matrix3x3.Invert(transform, out var invMatrix)) return;
        
        var distanceMap = ComputeDistanceToEdge(imgWidth, imgHeight);
        
        Parallel.For(0, tileHeight, y =>
        {
            for (int x = 0; x < tileWidth; x++)
            {
                int canvasX = tileOffsetX + x;
                int canvasY = tileOffsetY + y;
                
                var srcPt = TransformPoint(new Vector2(canvasX, canvasY), invMatrix);
                
                if (srcPt.X < 0 || srcPt.X >= imgWidth - 1 || 
                    srcPt.Y < 0 || srcPt.Y >= imgHeight - 1)
                    continue;
                
                int x0 = (int)srcPt.X;
                int y0 = (int)srcPt.Y;
                float fx = srcPt.X - x0;
                float fy = srcPt.Y - y0;
                
                int idx00 = (y0 * imgWidth + x0) * 4;
                float r = BilinearInterp(imageData[idx00], imageData[idx00 + 4], imageData[idx00 + imgWidth * 4], imageData[idx00 + imgWidth * 4 + 4], fx, fy);
                float g = BilinearInterp(imageData[idx00 + 1], imageData[idx00 + 5], imageData[idx00 + imgWidth * 4 + 1], imageData[idx00 + imgWidth * 4 + 5], fx, fy);
                float b = BilinearInterp(imageData[idx00 + 2], imageData[idx00 + 6], imageData[idx00 + imgWidth * 4 + 2], imageData[idx00 + imgWidth * 4 + 6], fx, fy);
                
                float distWeight = BilinearInterp(distanceMap[y0 * imgWidth + x0], distanceMap[y0 * imgWidth + x0 + 1], distanceMap[(y0 + 1) * imgWidth + x0], distanceMap[(y0 + 1) * imgWidth + x0 + 1], fx, fy);
                
                float weight = Math.Min(1.0f, distWeight / 50.0f);
                
                int tileIdx = (y * tileWidth + x);
                int tileBaseIdx = tileIdx * 4;
                
                // Note: Direct accumulation isn't thread-safe on float arrays without locks.
                // Using Interlocked.Add would be safer but is not available for floats.
                // A lock is the simplest safe approach here.
                lock (tileCanvas)
                {
                    tileCanvas[tileBaseIdx] += r * weight;
                    tileCanvas[tileBaseIdx + 1] += g * weight;
                    tileCanvas[tileBaseIdx + 2] += b * weight;
                    tileWeights[tileIdx] += weight;
                }
            }
        });
    }
    
    private void WarpAndBlendImage(
        ImageDataset dataset, Matrix3x3 transform,
        float[] canvas, float[] weights, int canvasWidth, int canvasHeight)
    {
        if (dataset.ImageData == null) dataset.Load();
            
        var imageData = dataset.ImageData;
        var imgWidth = dataset.Width;
        var imgHeight = dataset.Height;
        
        if (!Matrix3x3.Invert(transform, out var invMatrix)) return;

        var distanceMap = ComputeDistanceToEdge(imgWidth, imgHeight);
        
        Parallel.For(0, canvasHeight, y =>
        {
            for (int x = 0; x < canvasWidth; x++)
            {
                var srcPt = TransformPoint(new Vector2(x, y), invMatrix);
                
                if (srcPt.X < 0 || srcPt.X >= imgWidth - 1 || 
                    srcPt.Y < 0 || srcPt.Y >= imgHeight - 1)
                    continue;
                
                int x0 = (int)srcPt.X;
                int y0 = (int)srcPt.Y;
                float fx = srcPt.X - x0;
                float fy = srcPt.Y - y0;
                
                int idx00 = (y0 * imgWidth + x0) * 4;
                int idx10 = (y0 * imgWidth + (x0 + 1)) * 4;
                int idx01 = ((y0 + 1) * imgWidth + x0) * 4;
                int idx11 = ((y0 + 1) * imgWidth + (x0 + 1)) * 4;
                
                float r = BilinearInterp(imageData[idx00], imageData[idx10], imageData[idx01], imageData[idx11], fx, fy);
                float g = BilinearInterp(imageData[idx00 + 1], imageData[idx10 + 1], imageData[idx01 + 1], imageData[idx11 + 1], fx, fy);
                float b = BilinearInterp(imageData[idx00 + 2], imageData[idx10 + 2], imageData[idx01 + 2], imageData[idx11 + 2], fx, fy);
                
                float distWeight = BilinearInterp(distanceMap[y0 * imgWidth + x0], distanceMap[y0 * imgWidth + x0 + 1], distanceMap[(y0 + 1) * imgWidth + x0], distanceMap[(y0 + 1) * imgWidth + x0 + 1], fx, fy);
                
                float weight = Math.Min(1.0f, distWeight / 50.0f);
                
                int canvasIdx = y * canvasWidth + x;
                int canvasBaseIdx = canvasIdx * 4;
                lock (canvas)
                {
                    canvas[canvasBaseIdx] += r * weight;
                    canvas[canvasBaseIdx + 1] += g * weight;
                    canvas[canvasBaseIdx + 2] += b * weight;
                    weights[canvasIdx] += weight;
                }
            }
        });
    }

    private float[] ComputeDistanceToEdge(int width, int height)
    {
        var distanceMap = new float[width * height];
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int distToLeft = x;
                int distToRight = width - 1 - x;
                int distToTop = y;
                int distToBottom = height - 1 - y;
                
                float minDist = Math.Min(Math.Min(distToLeft, distToRight), 
                                        Math.Min(distToTop, distToBottom));
                distanceMap[y * width + x] = minDist;
            }
        });
        return distanceMap;
    }
    
    private static float BilinearInterp(float v00, float v10, float v01, float v11, float fx, float fy)
    {
        return v00 * (1 - fx) * (1 - fy) + v10 * fx * (1 - fy) + v01 * (1 - fx) * fy + v11 * fx * fy;
    }
    
    private static byte ClampToByte(float value)
    {
        if (value < 0) return 0;
        if (value > 255) return 255;
        return (byte)value;
    }
    
    private void SavePanoramaImage(string outputPath, byte[] imageData, int width, int height)
    {
        var ext = Path.GetExtension(outputPath).ToLowerInvariant();
        if (ext == ".tif" || ext == ".tiff")
        {
            // The BitMiracle.LibTiff dependency is not provided, so this block is commented out.
            // If you have the library, you can uncomment it.
            // SaveAsTiff(outputPath, imageData, width, height);
            Log("TIFF saving is currently disabled. Saving as PNG instead.");
            SaveAsSkiaImage(Path.ChangeExtension(outputPath, ".png"), imageData, width, height);
        }
        else
        {
            SaveAsSkiaImage(outputPath, imageData, width, height);
        }
    }
    
    /*
    private void SaveAsTiff(string path, byte[] rgba, int width, int height)
    {
        using var tiff = BitMiracle.LibTiff.Classic.Tiff.Open(path, "w");
        if (tiff == null) throw new IOException($"Could not create TIFF file: {path}");
        tiff.SetField(BitMiracle.LibTiff.Classic.TiffTag.IMAGEWIDTH, width);
        tiff.SetField(BitMiracle.LibTiff.Classic.TiffTag.IMAGELENGTH, height);
        // ... (rest of the TIFF settings)
    }
    */
    
    private void SaveAsSkiaImage(string path, byte[] rgba, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        System.Runtime.InteropServices.Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        
        using var image = SKImage.FromBitmap(bitmap);
        var format = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => SKEncodedImageFormat.Png,
            ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
            ".bmp" => SKEncodedImageFormat.Bmp,
            _ => SKEncodedImageFormat.Png
        };
        
        using var stream = File.Create(path);
        image.Encode(format, 95).SaveTo(stream);
    }

    public void Cancel()
    {
        _cancellationTokenSource?.Cancel();
        Log("Cancellation requested.");
    }

    private void UpdateProgress(float progress, string message)
    {
        Progress = progress;
        StatusMessage = message;
    }

    public void Log(string message)
    {
        var logMsg = $"[{DateTime.Now:HH:mm:ss}] {message}";
        Logs.Enqueue(logMsg);
        Logger.Log(logMsg);
    }
    
    public long EstimateMemoryRequirement()
    {
        if (Images.Count == 0) return 0;
        long avgImagePixels = (long)Images.Average(img => (double)img.Dataset.Width * img.Dataset.Height);
        long estimatedCanvasPixels = avgImagePixels * Images.Count / 2; // Rough estimate
        long totalMemory = estimatedCanvasPixels * 24; // float accumulators
        totalMemory += Images.Sum(img => (long)img.Dataset.Width * img.Dataset.Height * 4); // Loaded image data
        return (long)(totalMemory * 1.2); // 20% overhead
    }
    
    public string GetMemoryRequirementString()
    {
        long bytes = EstimateMemoryRequirement();
        if (bytes < 1024) return $"{bytes} bytes";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
    
    #region Private Homography Calculation for Manual Linking
    private static Matrix3x3? ComputeHomography(Vector2[] src, Vector2[] dst)
    {
        if (src.Length < 4) return null;
        
        int n = src.Length;
        var A = new double[2 * n, 8];
        var b = new double[2 * n];
        
        for (int i = 0; i < n; i++)
        {
            float x = src[i].X, y = src[i].Y;
            float u = dst[i].X, v = dst[i].Y;
            
            A[2 * i, 0] = -x; A[2 * i, 1] = -y; A[2 * i, 2] = -1;
            A[2 * i, 3] = 0; A[2 * i, 4] = 0; A[2 * i, 5] = 0;
            A[2 * i, 6] = u * x; A[2 * i, 7] = u * y;
            b[2 * i] = -u;
            
            A[2 * i + 1, 0] = 0; A[2 * i + 1, 1] = 0; A[2 * i + 1, 2] = 0;
            A[2 * i + 1, 3] = -x; A[2 * i + 1, 4] = -y; A[2 * i + 1, 5] = -1;
            A[2 * i + 1, 6] = v * x; A[2 * i + 1, 7] = v * y;
            b[2 * i + 1] = -v;
        }
        
        if (!SolveLeastSquaresDouble(A, b, 8, out var h))
            return null;
        
        return new Matrix3x3(
            (float)h[0], (float)h[1], (float)h[2],
            (float)h[3], (float)h[4], (float)h[5],
            (float)h[6], (float)h[7], 1.0f
        );
    }
    
    private static bool SolveLeastSquaresDouble(double[,] A, double[] b, int numParams, out double[] x)
    {
        int m = b.Length;
        int n = numParams;
        x = new double[n];
        
        var ATA = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                double sum = 0;
                for (int k = 0; k < m; k++)
                    sum += A[k, i] * A[k, j];
                ATA[i, j] = sum;
            }
        }
        
        var ATb = new double[n];
        for (int i = 0; i < n; i++)
        {
            double sum = 0;
            for (int k = 0; k < m; k++)
                sum += A[k, i] * b[k];
            ATb[i] = sum;
        }
        
        return SolveLinearSystemDouble(ATA, ATb, out x);
    }
    
    private static bool SolveLinearSystemDouble(double[,] A, double[] b, out double[] x)
    {
        int n = b.Length;
        x = new double[n];
        const double epsilon = 1e-10;

        for (int p = 0; p < n; p++)
        {
            int max = p;
            for (int i = p + 1; i < n; i++)
                if (Math.Abs(A[i, p]) > Math.Abs(A[max, p])) max = i;
            
            for (int i = 0; i < n; i++) (A[p, i], A[max, i]) = (A[max, i], A[p, i]);
            (b[p], b[max]) = (b[max], b[p]);
            
            if (Math.Abs(A[p, p]) <= epsilon) return false;

            for (int i = p + 1; i < n; i++)
            {
                double alpha = A[i, p] / A[p, p];
                b[i] -= alpha * b[p];
                for (int j = p; j < n; j++) A[i, j] -= alpha * A[p, j];
            }
        }

        for (int i = n - 1; i >= 0; i--)
        {
            double sum = 0.0;
            for (int j = i + 1; j < n; j++) sum += A[i, j] * x[j];
            x[i] = (b[i] - sum) / A[i, i];
        }
        return true;
    }
    #endregion
}