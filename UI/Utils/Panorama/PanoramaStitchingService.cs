// GeoscientistToolkit/Business/Panorama/PanoramaStitchingService.cs

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Util;

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
                UpdateProgress(0, "Initializing OpenCL...");
                OpenCLService.Initialize();
                Log($"Using OpenCL device...");

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
                image.Features = await detector.DetectAsync(image.Dataset, token);
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
                if (matches.Count > 20)
                {
                    var (homography, inliers) = FeatureMatcherCL.FindHomographyRANSAC(
                        matches, image1.Features.KeyPoints, image2.Features.KeyPoints);
                    if (inliers.Count > 15)
                    {
                        Graph.AddEdge(image1, image2, inliers, homography);
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
            // The matches list can be empty as it's not used for manual links, only the homography matters.
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
        var token = _cancellationTokenSource.Token;
        return Task.Run(async () =>
        {
            try
            {
                State = PanoramaState.Blending;
                Log($"Starting final blend process to output file: {outputPath}");
                
                // Get the largest connected component
                var components = StitchGroups;
                if (components.Count == 0 || components[0].Images.Count == 0)
                {
                    throw new InvalidOperationException("No connected images to blend.");
                }
                
                var mainGroup = components.OrderByDescending(g => g.Images.Count).First();
                Log($"Blending {mainGroup.Images.Count} images in the main group...");
                
                UpdateProgress(0.1f, "Computing global transformations...");
                token.ThrowIfCancellationRequested();
                
                // Build global transformations from the stitch graph
                var globalTransforms = ComputeGlobalTransformations(mainGroup, token);
                
                UpdateProgress(0.2f, "Computing canvas bounds...");
                token.ThrowIfCancellationRequested();
                
                // Compute output canvas dimensions
                var (canvasWidth, canvasHeight, offsetX, offsetY) = ComputeCanvasBounds(mainGroup.Images, globalTransforms);
                Log($"Canvas size: {canvasWidth}x{canvasHeight}, offset: ({offsetX}, {offsetY})");
                
                UpdateProgress(0.3f, "Warping and blending images...");
                token.ThrowIfCancellationRequested();
                
                // Perform multi-band blending
                var blendedImage = await BlendImagesAsync(mainGroup.Images, globalTransforms, 
                    canvasWidth, canvasHeight, offsetX, offsetY, token);
                
                UpdateProgress(0.9f, "Saving panorama...");
                token.ThrowIfCancellationRequested();
                
                // Save the final panorama
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
    
    private Dictionary<Guid, Matrix3x2> ComputeGlobalTransformations(StitchGroup group, CancellationToken token)
    {
        var globalTransforms = new Dictionary<Guid, Matrix3x2>();
        var visited = new HashSet<Guid>();
        
        // Choose reference image (first image in the group)
        var referenceImage = group.Images[0];
        globalTransforms[referenceImage.Id] = Matrix3x2.Identity;
        
        // BFS to compute global transformations
        var queue = new Queue<Guid>();
        queue.Enqueue(referenceImage.Id);
        visited.Add(referenceImage.Id);
        
        while (queue.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var currentId = queue.Dequeue();
            var currentTransform = globalTransforms[currentId];
            
            if (Graph._adj.TryGetValue(currentId, out var neighbors))
            {
                foreach (var (neighborId, _, relativeHomography) in neighbors)
                {
                    if (!visited.Contains(neighborId))
                    {
                        visited.Add(neighborId);
                        queue.Enqueue(neighborId);
                        
                        // Compose transformations: global = current * relative
                        globalTransforms[neighborId] = MultiplyAffine(currentTransform, relativeHomography);
                    }
                }
            }
        }
        
        return globalTransforms;
    }
    
    private static Matrix3x2 MultiplyAffine(Matrix3x2 a, Matrix3x2 b)
    {
        return new Matrix3x2(
            a.M11 * b.M11 + a.M12 * b.M21,
            a.M11 * b.M12 + a.M12 * b.M22,
            a.M21 * b.M11 + a.M22 * b.M21,
            a.M21 * b.M12 + a.M22 * b.M22,
            a.M31 * b.M11 + a.M32 * b.M21 + b.M31,
            a.M31 * b.M12 + a.M32 * b.M22 + b.M32
        );
    }
    
    private (int width, int height, int offsetX, int offsetY) ComputeCanvasBounds(
        List<PanoramaImage> images, Dictionary<Guid, Matrix3x2> transforms)
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
                var transformed = Vector2.Transform(corner, transform);
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
    
    private async Task<byte[]> BlendImagesAsync(
        List<PanoramaImage> images, 
        Dictionary<Guid, Matrix3x2> transforms,
        int canvasWidth, int canvasHeight, 
        int offsetX, int offsetY,
        CancellationToken token)
    {
        long estimatedMemory = (long)canvasWidth * canvasHeight * 24; // 24 bytes per pixel
        long availableMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        
        // If estimated memory is more than 70% of available memory, use tile-based approach
        if (estimatedMemory > availableMemory * 0.7)
        {
            Log($"Large panorama detected. Using tile-based blending to conserve memory.");
            return await BlendImagesTiledAsync(images, transforms, canvasWidth, canvasHeight, offsetX, offsetY, token);
        }
        
        // Use feathered alpha blending for seamless transitions
        var canvas = new float[canvasWidth * canvasHeight * 4];
        var weights = new float[canvasWidth * canvasHeight];
        
        int processedImages = 0;
        foreach (var image in images)
        {
            token.ThrowIfCancellationRequested();
            
            if (!transforms.TryGetValue(image.Id, out var transform))
                continue;
                
            Log($"Warping and blending: {image.Dataset.Name}");
            
            // Adjust transform for canvas offset
            var adjustedTransform = new Matrix3x2(
                transform.M11, transform.M12,
                transform.M21, transform.M22,
                transform.M31 - offsetX, transform.M32 - offsetY
            );
            
            // Warp and blend this image
            await Task.Run(() => WarpAndBlendImage(
                image.Dataset, adjustedTransform, 
                canvas, weights, canvasWidth, canvasHeight), token);
            
            processedImages++;
            UpdateProgress(0.3f + 0.6f * processedImages / images.Count, 
                $"Blending image {processedImages}/{images.Count}");
        }
        
        // Normalize by weights and convert to bytes
        var result = new byte[canvasWidth * canvasHeight * 4];
        Parallel.For(0, canvasWidth * canvasHeight, i =>
        {
            if (weights[i] > 0)
            {
                int baseIdx = i * 4;
                result[baseIdx] = ClampToByte(canvas[baseIdx] / weights[i]);
                result[baseIdx + 1] = ClampToByte(canvas[baseIdx + 1] / weights[i]);
                result[baseIdx + 2] = ClampToByte(canvas[baseIdx + 2] / weights[i]);
                result[baseIdx + 3] = 255;
            }
        });
        
        return result;
    }
    
    private async Task<byte[]> BlendImagesTiledAsync(
        List<PanoramaImage> images, 
        Dictionary<Guid, Matrix3x2> transforms,
        int canvasWidth, int canvasHeight, 
        int offsetX, int offsetY,
        CancellationToken token)
    {
        // Process panorama in tiles to reduce memory footprint
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
                
                // Process this tile
                var tileCanvas = new float[tileW * tileH * 4];
                var tileWeights = new float[tileW * tileH];
                
                foreach (var image in images)
                {
                    if (!transforms.TryGetValue(image.Id, out var transform))
                        continue;
                    
                    var adjustedTransform = new Matrix3x2(
                        transform.M11, transform.M12,
                        transform.M21, transform.M22,
                        transform.M31 - offsetX, transform.M32 - offsetY
                    );
                    
                    await Task.Run(() => WarpAndBlendImageTile(
                        image.Dataset, adjustedTransform,
                        tileCanvas, tileWeights, tileW, tileH, tileX, tileY), token);
                }
                
                // Normalize and copy tile to result
                for (int y = 0; y < tileH; y++)
                {
                    for (int x = 0; x < tileW; x++)
                    {
                        int tileIdx = y * tileW + x;
                        int canvasIdx = ((tileY + y) * canvasWidth + (tileX + x)) * 4;
                        
                        if (tileWeights[tileIdx] > 0)
                        {
                            result[canvasIdx] = ClampToByte(tileCanvas[tileIdx * 4] / tileWeights[tileIdx]);
                            result[canvasIdx + 1] = ClampToByte(tileCanvas[tileIdx * 4 + 1] / tileWeights[tileIdx]);
                            result[canvasIdx + 2] = ClampToByte(tileCanvas[tileIdx * 4 + 2] / tileWeights[tileIdx]);
                            result[canvasIdx + 3] = 255;
                        }
                    }
                }
                
                processedTiles++;
                UpdateProgress(0.3f + 0.6f * processedTiles / totalTiles,
                    $"Processing tile {processedTiles}/{totalTiles}");
                
                // Force garbage collection after each tile to keep memory usage low
                if (processedTiles % 10 == 0)
                {
                    GC.Collect();
                }
            }
        }
        
        return result;
    }
    
    private void WarpAndBlendImageTile(
        ImageDataset dataset, Matrix3x2 transform,
        float[] tileCanvas, float[] tileWeights, int tileWidth, int tileHeight,
        int tileOffsetX, int tileOffsetY)
    {
        if (dataset.ImageData == null)
            dataset.Load();
            
        var imageData = dataset.ImageData;
        var imgWidth = dataset.Width;
        var imgHeight = dataset.Height;
        
        if (!Matrix3x2.Invert(transform, out var invTransform))
            return;
        
        var distanceMap = ComputeDistanceToEdge(imgWidth, imgHeight);
        
        Parallel.For(0, tileHeight, y =>
        {
            for (int x = 0; x < tileWidth; x++)
            {
                // Convert tile coordinates to canvas coordinates
                int canvasX = tileOffsetX + x;
                int canvasY = tileOffsetY + y;
                
                var srcPt = Vector2.Transform(new Vector2(canvasX, canvasY), invTransform);
                
                if (srcPt.X < 0 || srcPt.X >= imgWidth - 1 || 
                    srcPt.Y < 0 || srcPt.Y >= imgHeight - 1)
                    continue;
                
                int x0 = (int)srcPt.X;
                int y0 = (int)srcPt.Y;
                int x1 = x0 + 1;
                int y1 = y0 + 1;
                
                float fx = srcPt.X - x0;
                float fy = srcPt.Y - y0;
                
                int idx00 = (y0 * imgWidth + x0) * 4;
                int idx10 = (y0 * imgWidth + x1) * 4;
                int idx01 = (y1 * imgWidth + x0) * 4;
                int idx11 = (y1 * imgWidth + x1) * 4;
                
                float r = BilinearInterp(
                    imageData[idx00], imageData[idx10], 
                    imageData[idx01], imageData[idx11], fx, fy);
                float g = BilinearInterp(
                    imageData[idx00 + 1], imageData[idx10 + 1], 
                    imageData[idx01 + 1], imageData[idx11 + 1], fx, fy);
                float b = BilinearInterp(
                    imageData[idx00 + 2], imageData[idx10 + 2], 
                    imageData[idx01 + 2], imageData[idx11 + 2], fx, fy);
                
                float distWeight = BilinearInterp(
                    distanceMap[y0 * imgWidth + x0], distanceMap[y0 * imgWidth + x1],
                    distanceMap[y1 * imgWidth + x0], distanceMap[y1 * imgWidth + x1], fx, fy);
                
                float weight = Math.Min(1.0f, distWeight / 50.0f);
                
                int tileIdx = (y * tileWidth + x) * 4;
                lock (tileCanvas)
                {
                    tileCanvas[tileIdx] += r * weight;
                    tileCanvas[tileIdx + 1] += g * weight;
                    tileCanvas[tileIdx + 2] += b * weight;
                    tileWeights[y * tileWidth + x] += weight;
                }
            }
        });
    }
    
    private void WarpAndBlendImage(
        ImageDataset dataset, Matrix3x2 transform,
        float[] canvas, float[] weights, int canvasWidth, int canvasHeight)
    {
        if (dataset.ImageData == null)
            dataset.Load();
            
        var imageData = dataset.ImageData;
        var imgWidth = dataset.Width;
        var imgHeight = dataset.Height;
        
        // Compute inverse transform for backward warping
        if (!Matrix3x2.Invert(transform, out var invTransform))
            return;
        
        // Create distance transform for feathering (distance to image border)
        var distanceMap = ComputeDistanceToEdge(imgWidth, imgHeight);
        
        // Warp image with bilinear interpolation
        Parallel.For(0, canvasHeight, y =>
        {
            for (int x = 0; x < canvasWidth; x++)
            {
                // Transform canvas point to source image coordinates
                var srcPt = Vector2.Transform(new Vector2(x, y), invTransform);
                
                // Check bounds
                if (srcPt.X < 0 || srcPt.X >= imgWidth - 1 || 
                    srcPt.Y < 0 || srcPt.Y >= imgHeight - 1)
                    continue;
                
                // Bilinear interpolation
                int x0 = (int)srcPt.X;
                int y0 = (int)srcPt.Y;
                int x1 = x0 + 1;
                int y1 = y0 + 1;
                
                float fx = srcPt.X - x0;
                float fy = srcPt.Y - y0;
                
                int idx00 = (y0 * imgWidth + x0) * 4;
                int idx10 = (y0 * imgWidth + x1) * 4;
                int idx01 = (y1 * imgWidth + x0) * 4;
                int idx11 = (y1 * imgWidth + x1) * 4;
                
                // Interpolate RGB values
                float r = BilinearInterp(
                    imageData[idx00], imageData[idx10], 
                    imageData[idx01], imageData[idx11], fx, fy);
                float g = BilinearInterp(
                    imageData[idx00 + 1], imageData[idx10 + 1], 
                    imageData[idx01 + 1], imageData[idx11 + 1], fx, fy);
                float b = BilinearInterp(
                    imageData[idx00 + 2], imageData[idx10 + 2], 
                    imageData[idx01 + 2], imageData[idx11 + 2], fx, fy);
                
                // Get distance weight for feathering
                float distWeight = BilinearInterp(
                    distanceMap[y0 * imgWidth + x0], distanceMap[y0 * imgWidth + x1],
                    distanceMap[y1 * imgWidth + x0], distanceMap[y1 * imgWidth + x1], fx, fy);
                
                // Apply feathering weight (smooth falloff near edges)
                float weight = Math.Min(1.0f, distWeight / 50.0f);
                
                // Accumulate weighted color
                int canvasIdx = (y * canvasWidth + x) * 4;
                lock (canvas)
                {
                    canvas[canvasIdx] += r * weight;
                    canvas[canvasIdx + 1] += g * weight;
                    canvas[canvasIdx + 2] += b * weight;
                    weights[y * canvasWidth + x] += weight;
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
        float v0 = v00 * (1 - fx) + v10 * fx;
        float v1 = v01 * (1 - fx) + v11 * fx;
        return v0 * (1 - fy) + v1 * fy;
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
            SaveAsTiff(outputPath, imageData, width, height);
        }
        else
        {
            SaveAsSkiaImage(outputPath, imageData, width, height);
        }
    }
    
    private void SaveAsTiff(string path, byte[] rgba, int width, int height)
    {
        using var tiff = BitMiracle.LibTiff.Classic.Tiff.Open(path, "w");
        if (tiff == null)
            throw new IOException($"Could not create TIFF file: {path}");
            
        tiff.SetField(BitMiracle.LibTiff.Classic.TiffTag.IMAGEWIDTH, width);
        tiff.SetField(BitMiracle.LibTiff.Classic.TiffTag.IMAGELENGTH, height);
        tiff.SetField(BitMiracle.LibTiff.Classic.TiffTag.SAMPLESPERPIXEL, 4);
        tiff.SetField(BitMiracle.LibTiff.Classic.TiffTag.BITSPERSAMPLE, 8);
        tiff.SetField(BitMiracle.LibTiff.Classic.TiffTag.ORIENTATION, 
            BitMiracle.LibTiff.Classic.Orientation.TOPLEFT);
        tiff.SetField(BitMiracle.LibTiff.Classic.TiffTag.PLANARCONFIG, 
            BitMiracle.LibTiff.Classic.PlanarConfig.CONTIG);
        tiff.SetField(BitMiracle.LibTiff.Classic.TiffTag.PHOTOMETRIC, 
            BitMiracle.LibTiff.Classic.Photometric.RGB);
        tiff.SetField(BitMiracle.LibTiff.Classic.TiffTag.COMPRESSION, 
            BitMiracle.LibTiff.Classic.Compression.LZW);
        
        int rowBytes = width * 4;
        var scanline = new byte[rowBytes];
        
        for (int y = 0; y < height; y++)
        {
            Buffer.BlockCopy(rgba, y * rowBytes, scanline, 0, rowBytes);
            tiff.WriteScanline(scanline, y);
        }
    }
    
    private void SaveAsSkiaImage(string path, byte[] rgba, int width, int height)
    {
        var info = new SkiaSharp.SKImageInfo(width, height, 
            SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Unpremul);
            
        using var bitmap = new SkiaSharp.SKBitmap(info);
        System.Runtime.InteropServices.Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        
        var format = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => SkiaSharp.SKEncodedImageFormat.Png,
            ".jpg" or ".jpeg" => SkiaSharp.SKEncodedImageFormat.Jpeg,
            ".bmp" => SkiaSharp.SKEncodedImageFormat.Bmp,
            _ => SkiaSharp.SKEncodedImageFormat.Png
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
        // Estimate memory needed for blending
        long totalMemory = 0;
        
        // Compute approximate canvas size
        if (Images.Count == 0) return 0;
        
        // Estimate canvas as 2x the average image size (conservative estimate)
        long avgImageSize = (long)Images.Average(img => (long)img.Dataset.Width * img.Dataset.Height);
        long estimatedCanvasSize = avgImageSize * 2;
        
        // Canvas needs: RGBA bytes (4 bytes) + float accumulator (16 bytes) + weight (4 bytes) = 24 bytes per pixel
        totalMemory += estimatedCanvasSize * 24;
        
        // Each loaded image: RGBA (4 bytes per pixel)
        totalMemory += Images.Sum(img => (long)img.Dataset.Width * img.Dataset.Height * 4);
        
        // Add 20% overhead for temporary buffers
        totalMemory = (long)(totalMemory * 1.2);
        
        return totalMemory;
    }
    
    public string GetMemoryRequirementString()
    {
        long bytes = EstimateMemoryRequirement();
        if (bytes < 1024) return $"{bytes} bytes";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
    
    #region Homography Calculation
    // These methods are duplicated from FeatureMatcherCL to keep this service self-contained
    // for handling manual points without creating a dependency on the matcher's internal implementation.
    private static Matrix3x2? ComputeHomography(Vector2[] src, Vector2[] dst)
    {
        if (src.Length < 3) return null;
        
        // Use all points for least-squares solution when we have more than 3
        if (src.Length > 3)
        {
            return ComputeHomographyLeastSquares(src, dst);
        }

        float sx1 = src[0].X, sy1 = src[0].Y;
        float sx2 = src[1].X, sy2 = src[1].Y;
        float sx3 = src[2].X, sy3 = src[2].Y;
        float dx1 = dst[0].X, dy1 = dst[0].Y;
        float dx2 = dst[1].X, dy2 = dst[1].Y;
        float dx3 = dst[2].X, dy3 = dst[2].Y;

        var A = new float[6, 6] {
            { sx1, sy1, 1, 0,   0,   0 },
            { 0,   0,   0, sx1, sy1, 1 },
            { sx2, sy2, 1, 0,   0,   0 },
            { 0,   0,   0, sx2, sy2, 1 },
            { sx3, sy3, 1, 0,   0,   0 },
            { 0,   0,   0, sx3, sy3, 1 }
        };

        var b = new float[6] { dx1, dy1, dx2, dy2, dx3, dy3 };

        if (!SolveLinearSystem(A, b, out var x))
        {
            return null;
        }
        
        return new Matrix3x2(x[0], x[3], x[1], x[4], x[2], x[5]);
    }
    
    private static Matrix3x2? ComputeHomographyLeastSquares(Vector2[] src, Vector2[] dst)
    {
        int n = src.Length;
        var A = new float[n * 2, 6];
        var b = new float[n * 2];
        
        for (int i = 0; i < n; i++)
        {
            float sx = src[i].X, sy = src[i].Y;
            float dx = dst[i].X, dy = dst[i].Y;
            
            // Row for x-coordinate
            A[i * 2, 0] = sx;
            A[i * 2, 1] = sy;
            A[i * 2, 2] = 1;
            A[i * 2, 3] = 0;
            A[i * 2, 4] = 0;
            A[i * 2, 5] = 0;
            b[i * 2] = dx;
            
            // Row for y-coordinate
            A[i * 2 + 1, 0] = 0;
            A[i * 2 + 1, 1] = 0;
            A[i * 2 + 1, 2] = 0;
            A[i * 2 + 1, 3] = sx;
            A[i * 2 + 1, 4] = sy;
            A[i * 2 + 1, 5] = 1;
            b[i * 2 + 1] = dy;
        }
        
        if (!SolveLeastSquares(A, b, 6, out var x))
            return null;
            
        return new Matrix3x2(x[0], x[3], x[1], x[4], x[2], x[5]);
    }
    
    private static bool SolveLeastSquares(float[,] A, float[] b, int numParams, out float[] x)
    {
        // Solve using normal equations: A^T * A * x = A^T * b
        int m = b.Length; // number of equations
        int n = numParams; // number of unknowns
        
        x = new float[n];
        
        // Compute A^T * A
        var ATA = new float[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                float sum = 0;
                for (int k = 0; k < m; k++)
                    sum += A[k, i] * A[k, j];
                ATA[i, j] = sum;
            }
        }
        
        // Compute A^T * b
        var ATb = new float[n];
        for (int i = 0; i < n; i++)
        {
            float sum = 0;
            for (int k = 0; k < m; k++)
                sum += A[k, i] * b[k];
            ATb[i] = sum;
        }
        
        // Solve the system ATA * x = ATb
        return SolveLinearSystem(ATA, ATb, out x);
    }

    private static bool SolveLinearSystem(float[,] A, float[] b, out float[] x)
    {
        int n = b.Length;
        x = new float[n];
        const float epsilon = 1e-10f;

        for (int p = 0; p < n; p++)
        {
            int max = p;
            for (int i = p + 1; i < n; i++)
            {
                if (Math.Abs(A[i, p]) > Math.Abs(A[max, p])) max = i;
            }
            for (int i = 0; i < n; i++) { (A[p, i], A[max, i]) = (A[max, i], A[p, i]); }
            (b[p], b[max]) = (b[max], b[p]);
            
            if (Math.Abs(A[p, p]) <= epsilon) return false;

            for (int i = p + 1; i < n; i++)
            {
                float alpha = A[i, p] / A[p, p];
                b[i] -= alpha * b[p];
                for (int j = p; j < n; j++) A[i, j] -= alpha * A[p, j];
            }
        }

        for (int i = n - 1; i >= 0; i--)
        {
            float sum = 0.0f;
            for (int j = i + 1; j < n; j++) sum += A[i, j] * x[j];
            x[i] = (b[i] - sum) / A[i, i];
        }
        return true;
    }
    #endregion
}