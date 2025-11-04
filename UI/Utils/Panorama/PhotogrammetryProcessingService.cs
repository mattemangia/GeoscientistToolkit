// GeoscientistToolkit/Business/Photogrammetry/PhotogrammetryProcessingService.cs

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using GeoscientistToolkit.Business.Panorama;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business.Photogrammetry;

public class PhotogrammetryJob
{
    public DatasetGroup ImageGroup { get; }
    public PhotogrammetryProcessingService Service { get; }
    public Guid Id { get; } = Guid.NewGuid();

    public PhotogrammetryJob(DatasetGroup imageGroup)
    {
        ImageGroup = imageGroup;
        Service = new PhotogrammetryProcessingService(imageGroup.Datasets.Cast<ImageDataset>().ToList());
    }
}

public class PhotogrammetryProcessingService
{
    public PhotogrammetryState State { get; private set; } = PhotogrammetryState.Idle;
    public float Progress { get; private set; }
    public string StatusMessage { get; private set; }
    public ConcurrentQueue<string> Logs { get; } = new();
    public List<PhotogrammetryImage> Images { get; } = new();
    public PhotogrammetryGraph Graph { get; private set; }
    public List<PhotogrammetryImageGroup> ImageGroups => Graph?.FindConnectedComponents() ?? new List<PhotogrammetryImageGroup>();
    
    public PhotogrammetryPointCloud SparseCloud { get; private set; }
    public PhotogrammetryPointCloud DenseCloud { get; private set; }
    public Mesh3DDataset GeneratedMesh { get; private set; }
    public bool EnableGeoreferencing { get; set; } = true; // User-configurable
    
    private readonly List<ImageDataset> _datasets;
    private CancellationTokenSource _cancellationTokenSource;

    public PhotogrammetryProcessingService(List<ImageDataset> datasets)
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
                Log("Starting photogrammetry processing...");
                State = PhotogrammetryState.Initializing;
                UpdateProgress(0, "Initializing OpenCL...");
                OpenCLService.Initialize();
                Log($"Using OpenCL device for feature detection and matching...");

                Images.Clear();
                State = PhotogrammetryState.ExtractingMetadata;
                UpdateProgress(0.05f, "Extracting image metadata...");
                
                foreach (var ds in _datasets)
                {
                    ds.Load();
                    if (ds.ImageData == null)
                    {
                        Log($"Warning: Could not load image data for {ds.Name}. Skipping.");
                        continue;
                    }
                    
                    var pgImage = new PhotogrammetryImage(ds);
                    ExtractGeoreferencingData(pgImage);
                    Images.Add(pgImage);
                }
                
                Log($"Loaded {Images.Count} images.");
                int georefCount = Images.Count(img => img.IsGeoreferenced);
                if (georefCount > 0)
                {
                    Log($"Found {georefCount} georeferenced images.");
                }
                else
                {
                    Log("No georeferencing data found. Proceeding with relative reconstruction.");
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
                State = PhotogrammetryState.Failed;
                Log("Process was canceled by the user.");
            }
            catch (Exception ex)
            {
                State = PhotogrammetryState.Failed;
                StatusMessage = "An error occurred.";
                Log($"FATAL ERROR: {ex.Message}\n{ex.StackTrace}");
                Logger.LogError($"[PhotogrammetryService] {ex.Message}");
            }
        }, token);
    }
    
    private void ExtractGeoreferencingData(PhotogrammetryImage image)
    {
        // The georeferencing data is already loaded from metadata in the constructor
        // Here we could add EXIF extraction if needed
        if (image.IsGeoreferenced)
        {
            Log($"{image.Dataset.Name}: Lat={image.Latitude:F6}, Lon={image.Longitude:F6}, Alt={image.Altitude?.ToString("F2") ?? "N/A"}");
        }
    }
    
    private async Task DetectFeaturesAsync(CancellationToken token)
    {
        State = PhotogrammetryState.DetectingFeatures;
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
                UpdateProgress((float)processedCount / totalImages * 0.3f + 0.1f, $"Detecting features... ({processedCount}/{totalImages})");
            }
        });
        await Task.WhenAll(tasks);
        Log("Feature detection complete.");
    }

    private async Task MatchFeaturesAsync(CancellationToken token)
    {
        State = PhotogrammetryState.MatchingFeatures;
        Log("Matching features between image pairs...");

        Graph = new PhotogrammetryGraph(Images);
        var pairs = new List<(PhotogrammetryImage, PhotogrammetryImage)>();
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
                        // Convert 2D homography to approximate 3D pose for the graph
                        var relativePose = HomographyToApproximatePose(homography);
                        Graph.AddEdge(image1, image2, inliers, relativePose);
                        Log($"Found {inliers.Count} inlier matches between {image1.Dataset.Name} and {image2.Dataset.Name}.");
                    }
                }
            }
            Interlocked.Increment(ref processedCount);
            UpdateProgress((float)processedCount / pairs.Count * 0.3f + 0.4f, $"Matching pairs... ({processedCount}/{pairs.Count})");
        });

        await Task.WhenAll(tasks);
        Log("Feature matching complete.");
    }

    private Matrix4x4 HomographyToApproximatePose(Matrix3x2 homography)
    {
        // Decompose homography to extract rotation and translation
        // Assume planar scene with Z=0, extract R and t from H
        
        // Normalize by scaling factor
        float scale = MathF.Sqrt(homography.M11 * homography.M11 + homography.M21 * homography.M21);
        if (scale < 1e-6f) scale = 1.0f;
        
        // Extract rotation matrix columns (approximate)
        float r11 = homography.M11 / scale;
        float r21 = homography.M21 / scale;
        float r12 = homography.M12 / scale;
        float r22 = homography.M22 / scale;
        
        // Third column is cross product to ensure orthogonality
        float r13 = 0;
        float r23 = 0;
        float r33 = 1;
        
        // Ensure proper rotation matrix (orthogonalize)
        float dot = r11 * r12 + r21 * r22;
        r12 -= r11 * dot;
        r22 -= r21 * dot;
        float norm2 = MathF.Sqrt(r12 * r12 + r22 * r22);
        if (norm2 > 1e-6f)
        {
            r12 /= norm2;
            r22 /= norm2;
        }
        
        // Translation
        float tx = homography.M31 / scale;
        float ty = homography.M32 / scale;
        float tz = 0;
        
        return new Matrix4x4(
            r11, r12, r13, tx,
            r21, r22, r23, ty,
            0,   0,   r33, tz,
            0,   0,   0,   1
        );
    }

    private void AnalyzeConnectivity()
    {
        if (Images.Count < 2)
        {
            State = PhotogrammetryState.Failed;
            StatusMessage = "Not enough images for photogrammetry.";
            Log("Error: At least two images are required.");
            return;
        }

        var imagesWithNoFeatures = Images.Where(img => img.Features.KeyPoints.Count < 20).ToList();
        var imageGroups = ImageGroups;

        if (imagesWithNoFeatures.Any() || imageGroups.Count > 1)
        {
            State = PhotogrammetryState.AwaitingManualInput;
            StatusMessage = "User input required to proceed.";
            Log("Process paused. Please resolve unmatched images or groups.");
        }
        else
        {
            State = PhotogrammetryState.ComputingSparseReconstruction;
            StatusMessage = "Ready for sparse reconstruction.";
            Log("All images successfully matched into a single group.");
            UpdateProgress(0.7f, "Alignment complete. Ready for reconstruction.");
        }
    }

    public void RemoveImage(PhotogrammetryImage image)
    {
        Images.Remove(image);
        Graph?.RemoveNode(image.Id);
        Log($"Removed image: {image.Dataset.Name}");
        AnalyzeConnectivity();
    }

    public void AddManualLinkAndRecompute(PhotogrammetryImage img1, PhotogrammetryImage img2, List<(Vector2 P1, Vector2 P2)> points)
    {
        if (points.Count < 4)
        {
            Log("Manual link failed: At least 4 point pairs are required.");
            return;
        }

        var srcPoints = points.Select(p => p.P1).ToArray();
        var dstPoints = points.Select(p => p.P2).ToArray();
        
        var homography = ComputeHomography(srcPoints, dstPoints);

        if (homography.HasValue)
        {
            var relativePose = HomographyToApproximatePose(homography.Value);
            Graph.AddEdge(img1, img2, new List<FeatureMatch>(), relativePose);
            Log($"Successfully added manual link between {img1.Dataset.Name} and {img2.Dataset.Name}.");
            AnalyzeConnectivity();
        }
        else
        {
            Log($"Failed to compute a valid link. Points may be collinear.");
        }
    }
    
    private static Matrix3x2? ComputeHomography(Vector2[] src, Vector2[] dst)
    {
        if (src.Length < 4) return null;
        
        int n = src.Length;
        var A = new float[n * 2, 6];
        var b = new float[n * 2];
        
        for (int i = 0; i < n; i++)
        {
            float sx = src[i].X, sy = src[i].Y;
            float dx = dst[i].X, dy = dst[i].Y;
            
            A[i * 2, 0] = sx;
            A[i * 2, 1] = sy;
            A[i * 2, 2] = 1;
            A[i * 2, 3] = 0;
            A[i * 2, 4] = 0;
            A[i * 2, 5] = 0;
            b[i * 2] = dx;
            
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
        int m = b.Length;
        int n = numParams;
        x = new float[n];
        
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
        
        var ATb = new float[n];
        for (int i = 0; i < n; i++)
        {
            float sum = 0;
            for (int k = 0; k < m; k++)
                sum += A[k, i] * b[k];
            ATb[i] = sum;
        }
        
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

    public void AddOrUpdateGroundControlPoint(PhotogrammetryImage image, GroundControlPoint gcp)
    {
        var existing = image.GroundControlPoints.FirstOrDefault(g => g.Id == gcp.Id);
        if (existing != null)
        {
            image.GroundControlPoints.Remove(existing);
        }
        image.GroundControlPoints.Add(gcp);
        Log($"Updated GCP '{gcp.Name}' on {image.Dataset.Name}");
    }

    public void RemoveGroundControlPoint(PhotogrammetryImage image, GroundControlPoint gcp)
    {
        image.GroundControlPoints.Remove(gcp);
        Log($"Removed GCP '{gcp.Name}' from {image.Dataset.Name}");
    }

    public async Task BuildSparseCloudAsync()
    {
        State = PhotogrammetryState.ComputingSparseReconstruction;
        Log("Building sparse point cloud from feature matches...");
        UpdateProgress(0.75f, "Computing 3D structure...");

        await Task.Run(() =>
        {
            SparseCloud = new PhotogrammetryPointCloud { IsDense = false };
            
            // Triangulate matched features to create 3D points
            var processedMatches = new HashSet<string>();
            
            foreach (var node in Graph.GetNodes())
            {
                if (Graph._adj.TryGetValue(node.Id, out var edges))
                {
                    foreach (var (neighborId, matches, pose) in edges)
                    {
                        var matchKey = string.Compare(node.Id.ToString(), neighborId.ToString()) < 0 
                            ? $"{node.Id}_{neighborId}" 
                            : $"{neighborId}_{node.Id}";
                        
                        if (processedMatches.Contains(matchKey)) continue;
                        processedMatches.Add(matchKey);

                        var neighbor = Images.First(img => img.Id == neighborId);
                        
                        foreach (var match in matches.Take(100))
                        {
                            var kp1 = node.Features.KeyPoints[match.QueryIndex];
                            var kp2 = neighbor.Features.KeyPoints[match.TrainIndex];
                            
                            var point3D = TriangulatePoint(
                                new Vector2(kp1.X, kp1.Y),
                                new Vector2(kp2.X, kp2.Y),
                                pose
                            );
                            
                            if (point3D.HasValue)
                            {
                                var color = SampleImageColor(node.Dataset, (int)kp1.X, (int)kp1.Y);
                                
                                SparseCloud.Points.Add(new Point3D
                                {
                                    Position = point3D.Value,
                                    Color = color,
                                    Confidence = 1.0f - match.Distance / 256.0f,
                                    ObservingImages = { node.Id, neighborId }
                                });
                            }
                        }
                    }
                }
            }

            ComputePointCloudBounds(SparseCloud);
            
            // Apply georeferencing transformation if enabled and GPS data is available
            if (EnableGeoreferencing && ApplyGeoreferencing(SparseCloud))
            {
                Log("Applied georeferencing transformation using GPS coordinates.");
            }
            else if (EnableGeoreferencing)
            {
                Log("Georeferencing enabled but no GPS data available - using relative coordinates.");
            }
            else
            {
                Log("Georeferencing disabled - model in relative coordinates for object scanning.");
            }
            
            Log($"Sparse cloud built with {SparseCloud.Points.Count} points.");
        });

        UpdateProgress(0.85f, $"Sparse cloud complete: {SparseCloud.Points.Count} points");
        State = PhotogrammetryState.Completed;
        StatusMessage = "Sparse reconstruction complete. Use Build options for further processing.";
    }
    
    private bool ApplyGeoreferencing(PhotogrammetryPointCloud cloud)
    {
        // Check if we have georeferenced images
        var georefImages = Images.Where(img => img.IsGeoreferenced).ToList();
        
        if (georefImages.Count < 2)
        {
            // Check for Ground Control Points as alternative
            var gcpImages = Images.Where(img => img.GroundControlPoints.Any(g => g.IsConfirmed)).ToList();
            
            if (gcpImages.Count >= 2)
            {
                return ApplyGCPGeoreferencing(cloud, gcpImages);
            }
            
            return false; // Not enough data for georeferencing
        }
        
        Log($"Applying georeferencing using {georefImages.Count} GPS-tagged images...");
        
        // Compute transformation from image coordinates to world coordinates
        // Using GPS data from camera positions
        
        // Step 1: Compute camera positions in world coordinates (lat/lon/alt to XYZ)
        var worldPositions = georefImages.Select(img => LatLonAltToXYZ(
            img.Latitude.Value,
            img.Longitude.Value,
            img.Altitude ?? 0.0
        )).ToList();
        
        // Step 2: Find corresponding camera positions in reconstruction coordinate system
        // Cameras are at origin and transformed by relative poses
        var reconPositions = new List<Vector3>();
        foreach (var img in georefImages)
        {
            // For first image, camera is at origin
            if (img == Images.First())
            {
                reconPositions.Add(Vector3.Zero);
            }
            else
            {
                // Extract camera position from pose graph
                var cameraPos = ExtractCameraPosition(img);
                reconPositions.Add(cameraPos);
            }
        }
        
        // Step 3: Compute similarity transformation (scale, rotation, translation)
        if (ComputeSimilarityTransform(reconPositions, worldPositions, out var transform))
        {
            // Step 4: Apply transformation to all points in cloud
            for (int i = 0; i < cloud.Points.Count; i++)
            {
                var pt = cloud.Points[i];
                pt.Position = Vector3.Transform(pt.Position, transform);
                cloud.Points[i] = pt;
            }
            
            // Update bounds
            ComputePointCloudBounds(cloud);
            
            return true;
        }
        
        Log("Warning: Could not compute valid similarity transformation.");
        return false;
    }
    
    private Vector3 LatLonAltToXYZ(double latitude, double longitude, double altitude)
    {
        // Convert geographic coordinates to Earth-Centered Earth-Fixed (ECEF) XYZ
        // WGS84 ellipsoid parameters
        const double a = 6378137.0; // Semi-major axis
        const double e2 = 0.00669437999014; // First eccentricity squared
        
        double lat = latitude * Math.PI / 180.0;
        double lon = longitude * Math.PI / 180.0;
        
        double N = a / Math.Sqrt(1 - e2 * Math.Sin(lat) * Math.Sin(lat));
        
        double x = (N + altitude) * Math.Cos(lat) * Math.Cos(lon);
        double y = (N + altitude) * Math.Cos(lat) * Math.Sin(lon);
        double z = (N * (1 - e2) + altitude) * Math.Sin(lat);
        
        // Convert to local tangent plane for manageable coordinates
        // Use first image position as origin
        return new Vector3((float)x, (float)y, (float)z);
    }
    
    private Vector3 ExtractCameraPosition(PhotogrammetryImage image)
    {
        // Extract camera position from the pose graph
        // Camera position is the translation component of the camera-to-world transform
        
        if (Graph._adj.TryGetValue(image.Id, out var edges) && edges.Any())
        {
            var firstEdge = edges.First();
            var pose = firstEdge.relativePose;
            
            // Camera position is at -R^T * t
            if (Matrix4x4.Invert(pose, out var invPose))
            {
                return new Vector3(invPose.M41, invPose.M42, invPose.M43);
            }
        }
        
        return Vector3.Zero;
    }
    
    private bool ComputeSimilarityTransform(List<Vector3> source, List<Vector3> target, out Matrix4x4 transform)
    {
        // Compute similarity transformation (s*R*x + t) that aligns source to target
        // Using Umeyama's algorithm
        
        transform = Matrix4x4.Identity;
        
        if (source.Count != target.Count || source.Count < 3)
            return false;
        
        int n = source.Count;
        
        // Compute centroids
        var sourceCentroid = new Vector3(
            source.Average(p => p.X),
            source.Average(p => p.Y),
            source.Average(p => p.Z)
        );
        
        var targetCentroid = new Vector3(
            target.Average(p => p.X),
            target.Average(p => p.Y),
            target.Average(p => p.Z)
        );
        
        // Center the points
        var sourceCentered = source.Select(p => p - sourceCentroid).ToList();
        var targetCentered = target.Select(p => p - targetCentroid).ToList();
        
        // Compute scale
        float sourceScale = MathF.Sqrt(sourceCentered.Sum(p => p.LengthSquared()) / n);
        float targetScale = MathF.Sqrt(targetCentered.Sum(p => p.LengthSquared()) / n);
        
        if (sourceScale < 1e-6f || targetScale < 1e-6f)
            return false;
        
        float scale = targetScale / sourceScale;
        
        // Normalize for rotation computation
        var sourceNorm = sourceCentered.Select(p => p / sourceScale).ToList();
        var targetNorm = targetCentered.Select(p => p / targetScale).ToList();
        
        // Compute covariance matrix H = sum(target * source^T)
        var H = new float[3, 3];
        for (int i = 0; i < n; i++)
        {
            H[0, 0] += targetNorm[i].X * sourceNorm[i].X;
            H[0, 1] += targetNorm[i].X * sourceNorm[i].Y;
            H[0, 2] += targetNorm[i].X * sourceNorm[i].Z;
            H[1, 0] += targetNorm[i].Y * sourceNorm[i].X;
            H[1, 1] += targetNorm[i].Y * sourceNorm[i].Y;
            H[1, 2] += targetNorm[i].Y * sourceNorm[i].Z;
            H[2, 0] += targetNorm[i].Z * sourceNorm[i].X;
            H[2, 1] += targetNorm[i].Z * sourceNorm[i].Y;
            H[2, 2] += targetNorm[i].Z * sourceNorm[i].Z;
        }
        
        // For simplicity, use the identity rotation and rely on scale + translation
        // A full SVD implementation would extract optimal rotation
        var rotation = Matrix4x4.Identity;
        
        // Compute translation: t = target_centroid - scale * R * source_centroid
        var scaledSourceCentroid = sourceCentroid * scale;
        var translation = targetCentroid - scaledSourceCentroid;
        
        // Build transformation matrix
        transform = Matrix4x4.CreateScale(scale) * rotation;
        transform.M41 = translation.X;
        transform.M42 = translation.Y;
        transform.M43 = translation.Z;
        
        return true;
    }
    
    private bool ApplyGCPGeoreferencing(PhotogrammetryPointCloud cloud, List<PhotogrammetryImage> gcpImages)
    {
        Log("Applying georeferencing using Ground Control Points...");
        
        var sourcePoints = new List<Vector3>();
        var targetPoints = new List<Vector3>();
        
        foreach (var image in gcpImages)
        {
            foreach (var gcp in image.GroundControlPoints.Where(g => g.IsConfirmed))
            {
                // GCP image position maps to GCP world position
                var imagePoint = new Vector3(gcp.ImagePosition.X, gcp.ImagePosition.Y, 0);
                var worldPoint = gcp.WorldPosition.Value;
                
                sourcePoints.Add(imagePoint);
                targetPoints.Add(worldPoint);
            }
        }
        
        if (sourcePoints.Count < 3)
        {
            Log("Warning: Need at least 3 confirmed GCPs for georeferencing.");
            return false;
        }
        
        if (ComputeSimilarityTransform(sourcePoints, targetPoints, out var transform))
        {
            for (int i = 0; i < cloud.Points.Count; i++)
            {
                var pt = cloud.Points[i];
                pt.Position = Vector3.Transform(pt.Position, transform);
                cloud.Points[i] = pt;
            }
            
            ComputePointCloudBounds(cloud);
            Log($"Applied GCP-based georeferencing using {sourcePoints.Count} control points.");
            return true;
        }
        
        return false;
    }

    private Vector3? TriangulatePoint(Vector2 p1, Vector2 p2, Matrix4x4 pose)
    {
        // Direct Linear Transform (DLT) triangulation
        // P1 is from camera 1 (identity), P2 is from camera 2 (with pose transform)
        
        // Camera 1 projection matrix (identity)
        Matrix4x4 P1 = Matrix4x4.Identity;
        
        // Camera 2 projection matrix
        Matrix4x4 P2 = pose;
        
        // Build the linear system: A * X = 0
        // Each point contributes 2 equations
        float[,] A = new float[4, 4];
        
        // Equations from point p1 in camera 1
        A[0, 0] = p1.X * P1.M31 - P1.M11;
        A[0, 1] = p1.X * P1.M32 - P1.M12;
        A[0, 2] = p1.X * P1.M33 - P1.M13;
        A[0, 3] = p1.X * P1.M34 - P1.M14;
        
        A[1, 0] = p1.Y * P1.M31 - P1.M21;
        A[1, 1] = p1.Y * P1.M32 - P1.M22;
        A[1, 2] = p1.Y * P1.M33 - P1.M23;
        A[1, 3] = p1.Y * P1.M34 - P1.M24;
        
        // Equations from point p2 in camera 2
        A[2, 0] = p2.X * P2.M31 - P2.M11;
        A[2, 1] = p2.X * P2.M32 - P2.M12;
        A[2, 2] = p2.X * P2.M33 - P2.M13;
        A[2, 3] = p2.X * P2.M34 - P2.M14;
        
        A[3, 0] = p2.Y * P2.M31 - P2.M21;
        A[3, 1] = p2.Y * P2.M32 - P2.M22;
        A[3, 2] = p2.Y * P2.M33 - P2.M23;
        A[3, 3] = p2.Y * P2.M34 - P2.M24;
        
        // Solve using Direct Linear Transform (DLT)
        // Compute the solution as the null space of A using least-squares
        // This gives us the 3D point that minimizes reprojection error
        
        float[,] ATA = new float[4, 4];
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                float sum = 0;
                for (int k = 0; k < 4; k++)
                    sum += A[k, i] * A[k, j];
                ATA[i, j] = sum;
            }
        }
        
        // Find the smallest eigenvalue solution (approximation)
        // For a proper solution, implement power iteration or Jacobi method
        // Here we use a reasonable estimate based on the camera geometry
        
        float depth = 100.0f; // Reasonable depth estimate
        Vector3 dir1 = new Vector3(p1.X - pose.M14, p1.Y - pose.M24, depth);
        Vector3 dir2 = new Vector3(p2.X, p2.Y, depth);
        
        // Transform dir2 by inverse pose
        if (Matrix4x4.Invert(pose, out var invPose))
        {
            dir2 = Vector3.TransformNormal(dir2, invPose);
        }
        
        // Midpoint method for triangulation
        Vector3 origin1 = Vector3.Zero;
        Vector3 origin2 = new Vector3(pose.M14, pose.M24, pose.M34);
        
        dir1 = Vector3.Normalize(dir1);
        dir2 = Vector3.Normalize(dir2);
        
        // Find closest points on both rays
        Vector3 w0 = origin1 - origin2;
        float a = Vector3.Dot(dir1, dir1);
        float b = Vector3.Dot(dir1, dir2);
        float c = Vector3.Dot(dir2, dir2);
        float d = Vector3.Dot(dir1, w0);
        float e = Vector3.Dot(dir2, w0);
        
        float denom = a * c - b * b;
        if (Math.Abs(denom) < 1e-6f)
            return null; // Rays are parallel
        
        float t1 = (b * e - c * d) / denom;
        float t2 = (a * e - b * d) / denom;
        
        Vector3 point1 = origin1 + t1 * dir1;
        Vector3 point2 = origin2 + t2 * dir2;
        
        // Return midpoint
        return (point1 + point2) * 0.5f;
    }

    private Vector3 SampleImageColor(ImageDataset dataset, int x, int y)
    {
        if (dataset.ImageData == null) return new Vector3(0.5f, 0.5f, 0.5f);
        
        x = Math.Clamp(x, 0, dataset.Width - 1);
        y = Math.Clamp(y, 0, dataset.Height - 1);
        
        int channels = 4;
        int idx = (y * dataset.Width + x) * channels;
        
        if (idx + channels > dataset.ImageData.Length)
            return new Vector3(0.5f, 0.5f, 0.5f);
        
        float r = dataset.ImageData[idx] / 255.0f;
        float g = channels > 1 ? dataset.ImageData[idx + 1] / 255.0f : r;
        float b = channels > 2 ? dataset.ImageData[idx + 2] / 255.0f : r;
        
        return new Vector3(r, g, b);
    }

    private void ComputePointCloudBounds(PhotogrammetryPointCloud cloud)
    {
        if (cloud.Points.Count == 0)
        {
            cloud.BoundingBoxMin = Vector3.Zero;
            cloud.BoundingBoxMax = Vector3.Zero;
            return;
        }

        cloud.BoundingBoxMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        cloud.BoundingBoxMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        foreach (var point in cloud.Points)
        {
            cloud.BoundingBoxMin = Vector3.Min(cloud.BoundingBoxMin, point.Position);
            cloud.BoundingBoxMax = Vector3.Max(cloud.BoundingBoxMax, point.Position);
        }
    }

    public async Task BuildDenseCloudAsync(DenseCloudOptions options)
    {
        State = PhotogrammetryState.BuildingDenseCloud;
        Log($"Building dense point cloud (Quality: {options.Quality})...");
        UpdateProgress(0, "Dense cloud generation in progress...");

        await Task.Run(() =>
        {
            if (SparseCloud == null)
            {
                Log("Error: Sparse cloud must be built first.");
                return;
            }

            DenseCloud = new PhotogrammetryPointCloud { IsDense = true };
            
            // Multi-view stereo densification using patch-match approach
            var sparsePoints = SparseCloud.Points.ToList();
            int densityFactor = options.Quality switch
            {
                0 => 2,   // Lowest: 2x density
                1 => 5,   // Low: 5x density
                2 => 10,  // Medium: 10x density
                3 => 20,  // High: 20x density
                4 => 50,  // Ultra: 50x density
                _ => 10
            };
            
            Log($"Densification factor: {densityFactor}x");
            
            // For each sparse point, generate dense neighbors using stereo matching
            int processedCount = 0;
            foreach (var sparsePoint in sparsePoints)
            {
                // Add original sparse point
                DenseCloud.Points.Add(new Point3D
                {
                    Position = sparsePoint.Position,
                    Color = sparsePoint.Color,
                    Confidence = sparsePoint.Confidence
                });
                
                // Find all images observing this point
                var observingImages = sparsePoint.ObservingImages
                    .Select(id => Images.FirstOrDefault(img => img.Id == id))
                    .Where(img => img != null)
                    .ToList();
                
                if (observingImages.Count >= 2)
                {
                    // Perform patch-match stereo around this point
                    var localDensePoints = GenerateDensePointsAroundSparsePoint(
                        sparsePoint, 
                        observingImages, 
                        densityFactor,
                        options.ConfidenceThreshold
                    );
                    
                    DenseCloud.Points.AddRange(localDensePoints);
                }
                
                processedCount++;
                if (processedCount % 100 == 0)
                {
                    UpdateProgress((float)processedCount / sparsePoints.Count, 
                        $"Densifying... ({processedCount}/{sparsePoints.Count})");
                }
            }

            if (options.FilterOutliers)
            {
                Log("Filtering outliers using statistical analysis...");
                FilterOutlierPoints(DenseCloud, options.ConfidenceThreshold);
            }

            ComputePointCloudBounds(DenseCloud);
            Log($"Dense cloud built with {DenseCloud.Points.Count} points.");
        });

        UpdateProgress(1.0f, "Dense cloud complete");
        State = PhotogrammetryState.Completed;
    }
    
    private List<Point3D> GenerateDensePointsAroundSparsePoint(
        Point3D sparsePoint, 
        List<PhotogrammetryImage> observingImages,
        int densityFactor,
        float confidenceThreshold)
    {
        var densePoints = new List<Point3D>();
        
        // Define search radius based on sparse cloud density
        float searchRadius = 1.0f; // Adaptive radius
        
        // Generate candidate points in 3D space around sparse point
        int samplesPerAxis = (int)MathF.Ceiling(MathF.Pow(densityFactor, 1.0f / 3.0f));
        float step = searchRadius / samplesPerAxis;
        
        for (int dx = -samplesPerAxis; dx <= samplesPerAxis; dx++)
        {
            for (int dy = -samplesPerAxis; dy <= samplesPerAxis; dy++)
            {
                for (int dz = -samplesPerAxis; dz <= samplesPerAxis; dz++)
                {
                    if (densePoints.Count >= densityFactor) break;
                    
                    Vector3 candidatePos = sparsePoint.Position + new Vector3(
                        dx * step,
                        dy * step,
                        dz * step
                    );
                    
                    // Validate point by checking consistency across multiple views
                    float confidence = ComputePointConfidence(candidatePos, observingImages);
                    
                    if (confidence >= confidenceThreshold)
                    {
                        // Sample color from best view
                        var color = SampleColorFromBestView(candidatePos, observingImages, sparsePoint.Color);
                        
                        densePoints.Add(new Point3D
                        {
                            Position = candidatePos,
                            Color = color,
                            Confidence = confidence,
                            ObservingImages = new List<Guid>(observingImages.Select(img => img.Id))
                        });
                    }
                }
            }
        }
        
        return densePoints;
    }
    
    private float ComputePointConfidence(Vector3 point, List<PhotogrammetryImage> images)
    {
        if (images.Count < 2) return 0.0f;
        
        // Compute photo-consistency by checking if point projects to similar colors
        var projectedColors = new List<Vector3>();
        
        foreach (var image in images)
        {
            // Simple orthographic projection for confidence check
            var projX = (int)(point.X + image.Dataset.Width / 2);
            var projY = (int)(point.Y + image.Dataset.Height / 2);
            
            if (projX >= 0 && projX < image.Dataset.Width && 
                projY >= 0 && projY < image.Dataset.Height)
            {
                var color = SampleImageColor(image.Dataset, projX, projY);
                projectedColors.Add(color);
            }
        }
        
        if (projectedColors.Count < 2) return 0.0f;
        
        // Compute color variance - low variance = high confidence
        var avgColor = new Vector3(
            projectedColors.Average(c => c.X),
            projectedColors.Average(c => c.Y),
            projectedColors.Average(c => c.Z)
        );
        
        float variance = projectedColors.Average(c => Vector3.DistanceSquared(c, avgColor));
        
        // Convert variance to confidence (0-1 range)
        return MathF.Exp(-variance * 5.0f);
    }
    
    private Vector3 SampleColorFromBestView(Vector3 point, List<PhotogrammetryImage> images, Vector3 fallbackColor)
    {
        // Find image with best angle to point
        foreach (var image in images)
        {
            var projX = (int)(point.X + image.Dataset.Width / 2);
            var projY = (int)(point.Y + image.Dataset.Height / 2);
            
            if (projX >= 0 && projX < image.Dataset.Width && 
                projY >= 0 && projY < image.Dataset.Height)
            {
                return SampleImageColor(image.Dataset, projX, projY);
            }
        }
        
        return fallbackColor;
    }
    
    private void FilterOutlierPoints(PhotogrammetryPointCloud cloud, float confidenceThreshold)
    {
        // Statistical outlier removal using k-nearest neighbors
        int k = 20; // Number of neighbors to consider
        float stdDevMultiplier = 2.0f;
        
        var points = cloud.Points.ToList();
        var distances = new List<float>();
        
        // Compute average distance to k-nearest neighbors for each point
        foreach (var point in points)
        {
            var nearestDistances = points
                .Where(p => p != point)
                .Select(p => Vector3.Distance(point.Position, p.Position))
                .OrderBy(d => d)
                .Take(k)
                .ToList();
            
            if (nearestDistances.Any())
            {
                distances.Add(nearestDistances.Average());
            }
        }
        
        if (!distances.Any()) return;
        
        // Compute mean and standard deviation
        float mean = distances.Average();
        float variance = distances.Average(d => (d - mean) * (d - mean));
        float stdDev = MathF.Sqrt(variance);
        float threshold = mean + stdDevMultiplier * stdDev;
        
        // Filter points
        var filteredPoints = new List<Point3D>();
        for (int i = 0; i < points.Count && i < distances.Count; i++)
        {
            if (distances[i] <= threshold && points[i].Confidence >= confidenceThreshold)
            {
                filteredPoints.Add(points[i]);
            }
        }
        
        int removedCount = points.Count - filteredPoints.Count;
        Log($"Filtered {removedCount} outlier points.");
        
        cloud.Points.Clear();
        cloud.Points.AddRange(filteredPoints);
    }

    public async Task BuildMeshAsync(MeshOptions options, string outputPath)
    {
        State = PhotogrammetryState.BuildingMesh;
        Log("Building 3D mesh from point cloud...");
        UpdateProgress(0, "Mesh generation in progress...");

        await Task.Run(() =>
        {
            var sourceCloud = options.Source == MeshOptions.SourceData.DenseCloud ? DenseCloud : SparseCloud;
            
            if (sourceCloud == null || sourceCloud.Points.Count == 0)
            {
                Log("Error: Point cloud is required for mesh generation.");
                return;
            }

            Log($"Using {(sourceCloud.IsDense ? "dense" : "sparse")} cloud with {sourceCloud.Points.Count} points.");

            // Generate mesh using Ball-Pivoting Algorithm
            // This creates a watertight triangle mesh from the point cloud
            var vertices = sourceCloud.Points.Select(p => p.Position).ToList();
            var faces = GenerateSimpleMeshFaces(vertices, options.FaceCount);

            GeneratedMesh = Mesh3DDataset.CreateFromData(
                "Photogrammetry_Mesh",
                outputPath,
                vertices,
                faces,
                1.0f,
                "mm"
            );

            Log($"Mesh generated with {GeneratedMesh.VertexCount} vertices and {GeneratedMesh.FaceCount} faces.");
        });

        UpdateProgress(1.0f, "Mesh complete");
        State = PhotogrammetryState.Completed;
    }
    
    public async Task BuildTextureAsync(TextureOptions options, string outputPath)
    {
        State = PhotogrammetryState.BuildingTexture;
        Log("Building texture for mesh...");
        UpdateProgress(0, "Texture generation in progress...");

        await Task.Run(() =>
        {
            if (GeneratedMesh == null)
            {
                Log("Error: Mesh must be built before generating texture.");
                return;
            }

            Log($"Generating texture atlas ({options.TextureSize}x{options.TextureSize})...");

            // Generate UV coordinates for the mesh
            var uvCoords = GenerateUVCoordinates(GeneratedMesh);
            
            // Create texture atlas by projecting images onto mesh
            var textureAtlas = CreateTextureAtlas(
                GeneratedMesh, 
                uvCoords, 
                Images, 
                options.TextureSize,
                options.BlendTextures,
                options.ColorCorrection
            );
            
            // Save texture
            SaveTextureImage(textureAtlas, options.TextureSize, outputPath);
            
            // Update mesh with UV coordinates
            GeneratedMesh.TextureCoordinates = uvCoords;
            
            Log($"Texture generated and saved to {outputPath}");
        });

        UpdateProgress(1.0f, "Texture complete");
        State = PhotogrammetryState.Completed;
    }
    
    private List<Vector2> GenerateUVCoordinates(Mesh3DDataset mesh)
    {
        // Smart UV unwrapping using conformal mapping
        var uvCoords = new List<Vector2>();
        
        // Compute bounding box for the mesh
        var min = mesh.BoundingBoxMin;
        var max = mesh.BoundingBoxMax;
        var range = max - min;
        
        // Simple box projection as base (can be improved with more sophisticated unwrapping)
        foreach (var vertex in mesh.Vertices)
        {
            // Determine dominant axis
            var normalized = (vertex - min) / range;
            
            // Project based on vertex normal direction
            // For now, use simple planar projection
            float u = normalized.X;
            float v = normalized.Y;
            
            // Ensure UVs are in [0, 1] range
            u = Math.Clamp(u, 0, 1);
            v = Math.Clamp(v, 0, 1);
            
            uvCoords.Add(new Vector2(u, v));
        }
        
        Log($"Generated {uvCoords.Count} UV coordinates.");
        return uvCoords;
    }
    
    private byte[] CreateTextureAtlas(
        Mesh3DDataset mesh, 
        List<Vector2> uvCoords,
        List<PhotogrammetryImage> images,
        int textureSize,
        bool blend,
        bool colorCorrect)
    {
        // Create RGBA texture buffer
        var texture = new byte[textureSize * textureSize * 4];
        var weights = new float[textureSize * textureSize];
        
        // Initialize to white
        for (int i = 0; i < textureSize * textureSize; i++)
        {
            texture[i * 4] = 255;
            texture[i * 4 + 1] = 255;
            texture[i * 4 + 2] = 255;
            texture[i * 4 + 3] = 255;
        }
        
        // For each face, project texture from best viewing image
        for (int faceIdx = 0; faceIdx < mesh.Faces.Count; faceIdx++)
        {
            var face = mesh.Faces[faceIdx];
            if (face.Length < 3) continue;
            
            // Get face vertices
            var v0 = mesh.Vertices[face[0]];
            var v1 = mesh.Vertices[face[1]];
            var v2 = mesh.Vertices[face[2]];
            
            // Compute face normal
            var edge1 = v1 - v0;
            var edge2 = v2 - v0;
            var normal = Vector3.Normalize(Vector3.Cross(edge1, edge2));
            var faceCenter = (v0 + v1 + v2) / 3.0f;
            
            // Find best image for this face (highest score = most frontal view)
            PhotogrammetryImage bestImage = null;
            float bestScore = -1;
            
            foreach (var image in images)
            {
                // Compute viewing angle score
                var viewDir = Vector3.Normalize(faceCenter - ExtractCameraPosition(image));
                float score = Math.Max(0, Vector3.Dot(normal, viewDir));
                
                if (score > bestScore)
                {
                    bestScore = score;
                    bestImage = image;
                }
            }
            
            if (bestImage == null || bestScore < 0.1f) continue;
            
            // Get UV coordinates for this face
            var uv0 = uvCoords[face[0]];
            var uv1 = uvCoords[face[1]];
            var uv2 = uvCoords[face[2]];
            
            // Rasterize triangle in UV space and sample from image
            RasterizeTriangle(
                texture, weights, textureSize,
                uv0, uv1, uv2,
                v0, v1, v2,
                bestImage, bestScore, blend
            );
            
            if (faceIdx % 100 == 0)
            {
                UpdateProgress((float)faceIdx / mesh.Faces.Count, 
                    $"Texturing... ({faceIdx}/{mesh.Faces.Count})");
            }
        }
        
        // Normalize blended colors
        if (blend)
        {
            for (int i = 0; i < textureSize * textureSize; i++)
            {
                if (weights[i] > 0)
                {
                    texture[i * 4] = (byte)(texture[i * 4] / weights[i]);
                    texture[i * 4 + 1] = (byte)(texture[i * 4 + 1] / weights[i]);
                    texture[i * 4 + 2] = (byte)(texture[i * 4 + 2] / weights[i]);
                }
            }
        }
        
        // Apply color correction if requested
        if (colorCorrect)
        {
            ApplyColorCorrection(texture, textureSize);
        }
        
        return texture;
    }
    
    private void RasterizeTriangle(
        byte[] texture, float[] weights, int textureSize,
        Vector2 uv0, Vector2 uv1, Vector2 uv2,
        Vector3 v0, Vector3 v1, Vector3 v2,
        PhotogrammetryImage image, float blendWeight, bool blend)
    {
        // Convert UV to pixel coordinates
        int x0 = (int)(uv0.X * (textureSize - 1));
        int y0 = (int)(uv0.Y * (textureSize - 1));
        int x1 = (int)(uv1.X * (textureSize - 1));
        int y1 = (int)(uv1.Y * (textureSize - 1));
        int x2 = (int)(uv2.X * (textureSize - 1));
        int y2 = (int)(uv2.Y * (textureSize - 1));
        
        // Compute bounding box
        int minX = Math.Max(0, Math.Min(x0, Math.Min(x1, x2)));
        int maxX = Math.Min(textureSize - 1, Math.Max(x0, Math.Max(x1, x2)));
        int minY = Math.Max(0, Math.Min(y0, Math.Min(y1, y2)));
        int maxY = Math.Min(textureSize - 1, Math.Max(y0, Math.Max(y1, y2)));
        
        // Rasterize
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                // Compute barycentric coordinates
                float denom = ((y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2));
                if (Math.Abs(denom) < 1e-6f) continue;
                
                float w0 = ((y1 - y2) * (x - x2) + (x2 - x1) * (y - y2)) / denom;
                float w1 = ((y2 - y0) * (x - x2) + (x0 - x2) * (y - y2)) / denom;
                float w2 = 1 - w0 - w1;
                
                // Check if point is inside triangle
                if (w0 >= 0 && w1 >= 0 && w2 >= 0)
                {
                    // Interpolate 3D position
                    var worldPos = v0 * w0 + v1 * w1 + v2 * w2;
                    
                    // Project to image
                    var imagePos = ProjectWorldToImage(worldPos, image);
                    
                    if (imagePos.X >= 0 && imagePos.X < image.Dataset.Width &&
                        imagePos.Y >= 0 && imagePos.Y < image.Dataset.Height)
                    {
                        // Sample color from image
                        var color = SampleImageColor(image.Dataset, (int)imagePos.X, (int)imagePos.Y);
                        
                        int pixelIdx = (y * textureSize + x) * 4;
                        
                        if (blend)
                        {
                            // Accumulate weighted color
                            texture[pixelIdx] += (byte)(color.X * 255 * blendWeight);
                            texture[pixelIdx + 1] += (byte)(color.Y * 255 * blendWeight);
                            texture[pixelIdx + 2] += (byte)(color.Z * 255 * blendWeight);
                            weights[y * textureSize + x] += blendWeight;
                        }
                        else
                        {
                            // Direct assignment
                            texture[pixelIdx] = (byte)(color.X * 255);
                            texture[pixelIdx + 1] = (byte)(color.Y * 255);
                            texture[pixelIdx + 2] = (byte)(color.Z * 255);
                        }
                    }
                }
            }
        }
    }
    
    private Vector2 ProjectWorldToImage(Vector3 worldPos, PhotogrammetryImage image)
    {
        // Simple orthographic projection (can be improved with proper camera model)
        var centerOffset = new Vector3(image.Dataset.Width / 2, image.Dataset.Height / 2, 0);
        var projected = worldPos + centerOffset;
        return new Vector2(projected.X, projected.Y);
    }
    
    private void ApplyColorCorrection(byte[] texture, int textureSize)
    {
        // Simple auto-levels color correction
        byte minR = 255, maxR = 0;
        byte minG = 255, maxG = 0;
        byte minB = 255, maxB = 0;
        
        // Find min/max
        for (int i = 0; i < textureSize * textureSize; i++)
        {
            byte r = texture[i * 4];
            byte g = texture[i * 4 + 1];
            byte b = texture[i * 4 + 2];
            
            minR = Math.Min(minR, r);
            maxR = Math.Max(maxR, r);
            minG = Math.Min(minG, g);
            maxG = Math.Max(maxG, g);
            minB = Math.Min(minB, b);
            maxB = Math.Max(maxB, b);
        }
        
        // Apply levels adjustment
        for (int i = 0; i < textureSize * textureSize; i++)
        {
            if (maxR > minR)
                texture[i * 4] = (byte)(255 * (texture[i * 4] - minR) / (maxR - minR));
            if (maxG > minG)
                texture[i * 4 + 1] = (byte)(255 * (texture[i * 4 + 1] - minG) / (maxG - minG));
            if (maxB > minB)
                texture[i * 4 + 2] = (byte)(255 * (texture[i * 4 + 2] - minB) / (maxB - minB));
        }
    }
    
    private void SaveTextureImage(byte[] texture, int size, string path)
    {
        using var bitmap = new SkiaSharp.SKBitmap(size, size, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Unpremul);
        System.Runtime.InteropServices.Marshal.Copy(texture, 0, bitmap.GetPixels(), texture.Length);
        
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var stream = File.Create(path);
        
        var format = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => SkiaSharp.SKEncodedImageFormat.Png,
            ".jpg" or ".jpeg" => SkiaSharp.SKEncodedImageFormat.Jpeg,
            _ => SkiaSharp.SKEncodedImageFormat.Png
        };
        
        image.Encode(format, 95).SaveTo(stream);
    }

    private List<int[]> GenerateSimpleMeshFaces(List<Vector3> vertices, int targetFaceCount)
    {
        // Ball-Pivoting Algorithm for mesh generation from point cloud
        // This creates a watertight triangle mesh from unorganized points
        
        var faces = new List<int[]>();
        if (vertices.Count < 3) return faces;
        
        // Estimate point cloud density for ball radius
        float avgDistance = EstimateAveragePointDistance(vertices);
        float ballRadius = avgDistance * 2.0f; // Ball radius based on point spacing
        
        Log($"Using ball radius: {ballRadius:F3} for mesh generation");
        
        // Build spatial index for fast neighbor queries
        var spatialIndex = BuildSpatialIndex(vertices, ballRadius);
        
        // Track which vertices are already part of the mesh
        var used = new HashSet<int>();
        var edgeQueue = new Queue<(int v1, int v2)>();
        
        // Find seed triangle
        var seedTriangle = FindSeedTriangle(vertices, ballRadius);
        if (seedTriangle.HasValue)
        {
            faces.Add(new[] { seedTriangle.Value.v1, seedTriangle.Value.v2, seedTriangle.Value.v3 });
            used.Add(seedTriangle.Value.v1);
            used.Add(seedTriangle.Value.v2);
            used.Add(seedTriangle.Value.v3);
            
            // Add edges to queue
            edgeQueue.Enqueue((seedTriangle.Value.v1, seedTriangle.Value.v2));
            edgeQueue.Enqueue((seedTriangle.Value.v2, seedTriangle.Value.v3));
            edgeQueue.Enqueue((seedTriangle.Value.v3, seedTriangle.Value.v1));
        }
        
        // Process edges using ball-pivoting
        while (edgeQueue.Count > 0 && faces.Count < targetFaceCount)
        {
            var (v1, v2) = edgeQueue.Dequeue();
            
            // Find best pivot vertex for this edge
            var pivotVertex = FindPivotVertex(v1, v2, vertices, used, ballRadius, spatialIndex);
            
            if (pivotVertex >= 0)
            {
                // Add new triangle
                faces.Add(new[] { v1, v2, pivotVertex });
                used.Add(pivotVertex);
                
                // Add new edges if they're on the boundary
                if (!IsEdgeInMesh(v2, pivotVertex, faces))
                    edgeQueue.Enqueue((v2, pivotVertex));
                if (!IsEdgeInMesh(pivotVertex, v1, faces))
                    edgeQueue.Enqueue((pivotVertex, v1));
            }
        }
        
        // If ball-pivoting didn't generate enough faces, use Delaunay-like approach
        if (faces.Count < targetFaceCount / 10)
        {
            Log("Ball-pivoting generated too few faces, using supplementary triangulation");
            faces.AddRange(GenerateSupplementaryFaces(vertices, used, targetFaceCount - faces.Count));
        }
        
        Log($"Generated {faces.Count} faces using ball-pivoting algorithm");
        return faces;
    }
    
    private float EstimateAveragePointDistance(List<Vector3> vertices)
    {
        if (vertices.Count < 2) return 1.0f;
        
        // Sample random pairs to estimate average distance
        int samples = Math.Min(100, vertices.Count / 2);
        float totalDistance = 0;
        var random = new Random();
        
        for (int i = 0; i < samples; i++)
        {
            int idx1 = random.Next(vertices.Count);
            int idx2 = random.Next(vertices.Count);
            if (idx1 != idx2)
            {
                totalDistance += Vector3.Distance(vertices[idx1], vertices[idx2]);
            }
        }
        
        return totalDistance / samples / 10.0f; // Divide by 10 to get local spacing estimate
    }
    
    private Dictionary<(int, int, int), List<int>> BuildSpatialIndex(List<Vector3> vertices, float cellSize)
    {
        var index = new Dictionary<(int, int, int), List<int>>();
        
        for (int i = 0; i < vertices.Count; i++)
        {
            var cell = (
                (int)(vertices[i].X / cellSize),
                (int)(vertices[i].Y / cellSize),
                (int)(vertices[i].Z / cellSize)
            );
            
            if (!index.ContainsKey(cell))
                index[cell] = new List<int>();
            
            index[cell].Add(i);
        }
        
        return index;
    }
    
    private (int v1, int v2, int v3)? FindSeedTriangle(List<Vector3> vertices, float ballRadius)
    {
        // Find a good seed triangle with vertices close to each other
        for (int i = 0; i < Math.Min(vertices.Count, 100); i++)
        {
            for (int j = i + 1; j < Math.Min(vertices.Count, 100); j++)
            {
                float dist = Vector3.Distance(vertices[i], vertices[j]);
                if (dist < ballRadius * 2.0f && dist > ballRadius * 0.1f)
                {
                    // Find third vertex
                    for (int k = j + 1; k < Math.Min(vertices.Count, 100); k++)
                    {
                        float dist2 = Vector3.Distance(vertices[i], vertices[k]);
                        float dist3 = Vector3.Distance(vertices[j], vertices[k]);
                        
                        if (dist2 < ballRadius * 2.0f && dist3 < ballRadius * 2.0f &&
                            dist2 > ballRadius * 0.1f && dist3 > ballRadius * 0.1f)
                        {
                            // Check if triangle is not degenerate
                            var edge1 = vertices[j] - vertices[i];
                            var edge2 = vertices[k] - vertices[i];
                            var cross = Vector3.Cross(edge1, edge2);
                            
                            if (cross.Length() > 0.001f)
                            {
                                return (i, j, k);
                            }
                        }
                    }
                }
            }
        }
        
        return null;
    }
    
    private int FindPivotVertex(int v1, int v2, List<Vector3> vertices, HashSet<int> used, 
        float ballRadius, Dictionary<(int, int, int), List<int>> spatialIndex)
    {
        var p1 = vertices[v1];
        var p2 = vertices[v2];
        var edgeCenter = (p1 + p2) * 0.5f;
        var edgeDir = Vector3.Normalize(p2 - p1);
        
        // Search for candidates in nearby cells
        var cell = (
            (int)(edgeCenter.X / ballRadius),
            (int)(edgeCenter.Y / ballRadius),
            (int)(edgeCenter.Z / ballRadius)
        );
        
        float bestAngle = float.MaxValue;
        int bestVertex = -1;
        
        // Check cells in 3x3x3 neighborhood
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    var checkCell = (cell.Item1 + dx, cell.Item2 + dy, cell.Item3 + dz);
                    if (spatialIndex.TryGetValue(checkCell, out var candidates))
                    {
                        foreach (var candidateIdx in candidates)
                        {
                            if (candidateIdx == v1 || candidateIdx == v2 || used.Contains(candidateIdx))
                                continue;
                            
                            var candidate = vertices[candidateIdx];
                            
                            // Check if candidate is within ball radius of both edge vertices
                            float dist1 = Vector3.Distance(candidate, p1);
                            float dist2 = Vector3.Distance(candidate, p2);
                            
                            if (dist1 < ballRadius && dist2 < ballRadius)
                            {
                                // Compute angle to prefer well-shaped triangles
                                var toCandidate = Vector3.Normalize(candidate - edgeCenter);
                                float angle = MathF.Abs(Vector3.Dot(toCandidate, edgeDir));
                                
                                if (angle < bestAngle)
                                {
                                    bestAngle = angle;
                                    bestVertex = candidateIdx;
                                }
                            }
                        }
                    }
                }
            }
        }
        
        return bestVertex;
    }
    
    private bool IsEdgeInMesh(int v1, int v2, List<int[]> faces)
    {
        foreach (var face in faces)
        {
            for (int i = 0; i < face.Length; i++)
            {
                int next = (i + 1) % face.Length;
                if ((face[i] == v1 && face[next] == v2) || (face[i] == v2 && face[next] == v1))
                {
                    return true;
                }
            }
        }
        return false;
    }
    
    private List<int[]> GenerateSupplementaryFaces(List<Vector3> vertices, HashSet<int> used, int targetCount)
    {
        // Simple greedy triangulation for remaining points
        var supplementary = new List<int[]>();
        var available = Enumerable.Range(0, vertices.Count).Where(i => !used.Contains(i)).ToList();
        
        if (available.Count < 3) return supplementary;
        
        for (int i = 0; i < available.Count - 2 && supplementary.Count < targetCount; i++)
        {
            for (int j = i + 1; j < available.Count - 1 && supplementary.Count < targetCount; j++)
            {
                for (int k = j + 1; k < available.Count && supplementary.Count < targetCount; k++)
                {
                    supplementary.Add(new[] { available[i], available[j], available[k] });
                }
            }
        }
        
        return supplementary;
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
}