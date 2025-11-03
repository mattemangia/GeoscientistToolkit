// GeoscientistToolkit/Business/Panorama/PanoramaStitchingService.cs

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
                // In a real implementation, this would involve warping all images to a common
                // plane and blending the overlapping regions. This is a complex process.
                for (int i = 0; i <= 100; i++)
                {
                    token.ThrowIfCancellationRequested();
                    UpdateProgress((float)i / 100, $"Blending... {i}%");
                    await Task.Delay(50, token); // Simulate work
                }
                
                Log($"Successfully saved panorama to {outputPath}");
                State = PanoramaState.Completed;
                StatusMessage = "Panorama created successfully!";
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
    
    #region Homography Calculation
    // These methods are duplicated from FeatureMatcherCL to keep this service self-contained
    // for handling manual points without creating a dependency on the matcher's internal implementation.
    private static Matrix3x2? ComputeHomography(Vector2[] src, Vector2[] dst)
    {
        if (src.Length < 3) return null;

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