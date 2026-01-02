// GeoscientistToolkit/Data/Nerf/NerfImageCollection.cs

using System.Numerics;
using FFMpegCore;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Nerf;

/// <summary>
/// Collection of image frames for NeRF training.
/// Manages camera poses, feature extraction, and pose estimation.
/// </summary>
public class NerfImageCollection : IDisposable
{
    private readonly List<NerfImageFrame> _frames = new();
    private readonly object _lock = new();

    // Collection properties
    public string Name { get; set; } = "Untitled";
    public int FrameCount => _frames.Count;
    public IReadOnlyList<NerfImageFrame> Frames => _frames.AsReadOnly();

    // Computed scene bounds
    public Vector3 SceneCenter { get; private set; } = Vector3.Zero;
    public float SceneRadius { get; private set; } = 1.0f;
    public Vector3 BoundingBoxMin { get; private set; } = -Vector3.One;
    public Vector3 BoundingBoxMax { get; private set; } = Vector3.One;

    // Reconstruction status
    public PoseReconstructionStatus ReconstructionStatus { get; set; } = PoseReconstructionStatus.NotStarted;
    public float ReconstructionProgress { get; set; } = 0f;

    // Sparse point cloud from SfM
    public List<SparsePoint3D> SparsePointCloud { get; set; } = new();

    public void Dispose()
    {
        Clear();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Add a frame to the collection
    /// </summary>
    public void AddFrame(NerfImageFrame frame)
    {
        if (frame == null) return;

        lock (_lock)
        {
            frame.FrameIndex = _frames.Count;
            _frames.Add(frame);
        }
    }

    /// <summary>
    /// Remove a frame from the collection
    /// </summary>
    public void RemoveFrame(NerfImageFrame frame)
    {
        lock (_lock)
        {
            _frames.Remove(frame);
            frame.Dispose();

            // Re-index frames
            for (int i = 0; i < _frames.Count; i++)
            {
                _frames[i].FrameIndex = i;
            }
        }
    }

    /// <summary>
    /// Clear all frames
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            foreach (var frame in _frames)
            {
                frame.Dispose();
            }
            _frames.Clear();
            SparsePointCloud.Clear();
        }
    }

    /// <summary>
    /// Get frame by index
    /// </summary>
    public NerfImageFrame GetFrame(int index)
    {
        lock (_lock)
        {
            if (index >= 0 && index < _frames.Count)
                return _frames[index];
            return null;
        }
    }

    /// <summary>
    /// Get total size of all images in bytes
    /// </summary>
    public long GetTotalSizeBytes()
    {
        lock (_lock)
        {
            return _frames.Sum(f => (long)(f.ImageData?.Length ?? 0));
        }
    }

    /// <summary>
    /// Get enabled frames for training
    /// </summary>
    public IEnumerable<NerfImageFrame> GetEnabledFrames()
    {
        lock (_lock)
        {
            return _frames.Where(f => f.IsEnabled).ToList();
        }
    }

    /// <summary>
    /// Get frames with estimated poses
    /// </summary>
    public IEnumerable<NerfImageFrame> GetFramesWithPoses()
    {
        lock (_lock)
        {
            return _frames.Where(f => f.PoseStatus == PoseEstimationStatus.Estimated ||
                                      f.PoseStatus == PoseEstimationStatus.ManuallySet).ToList();
        }
    }

    /// <summary>
    /// Extract keyframes from a video file
    /// </summary>
    public async Task ExtractKeyframesFromVideoAsync(string videoPath, int keyframeInterval = 15, IProgress<float> progress = null)
    {
        if (!File.Exists(videoPath))
        {
            Logger.LogError($"Video file not found: {videoPath}");
            return;
        }

        try
        {
            // Get video info
            var mediaInfo = await FFProbe.AnalyseAsync(videoPath);
            var totalFrames = (int)(mediaInfo.Duration.TotalSeconds * mediaInfo.PrimaryVideoStream.FrameRate);
            var width = mediaInfo.PrimaryVideoStream.Width;
            var height = mediaInfo.PrimaryVideoStream.Height;
            var frameRate = mediaInfo.PrimaryVideoStream.FrameRate;

            Logger.Log($"Video info: {width}x{height}, {totalFrames} frames, {frameRate} fps");

            int extractedCount = 0;
            for (int frameIndex = 0; frameIndex < totalFrames; frameIndex += keyframeInterval)
            {
                try
                {
                    var timeSeconds = frameIndex / frameRate;
                    var tempPath = Path.Combine(Path.GetTempPath(), $"nerf_frame_{Guid.NewGuid()}.png");

                    // Extract frame
                    var snapshot = await FFMpeg.SnapshotAsync(
                        videoPath,
                        tempPath,
                        new System.Drawing.Size(width, height),
                        TimeSpan.FromSeconds(timeSeconds)
                    );

                    if (File.Exists(tempPath))
                    {
                        var frame = await NerfImageFrame.LoadFromFileAsync(tempPath);
                        if (frame != null)
                        {
                            frame.FrameIndex = extractedCount;
                            frame.TimestampSeconds = timeSeconds;
                            frame.IsKeyframe = true;
                            AddFrame(frame);
                            extractedCount++;
                        }

                        File.Delete(tempPath);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Failed to extract frame at index {frameIndex}: {ex.Message}");
                }

                progress?.Report((float)frameIndex / totalFrames);
            }

            Logger.Log($"Extracted {extractedCount} keyframes from video");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error extracting keyframes: {ex.Message}");
        }
    }

    /// <summary>
    /// Compute scene bounds from camera positions
    /// </summary>
    public void ComputeSceneBounds()
    {
        var posedFrames = GetFramesWithPoses().ToList();
        if (posedFrames.Count == 0)
        {
            SceneCenter = Vector3.Zero;
            SceneRadius = 1.0f;
            BoundingBoxMin = -Vector3.One;
            BoundingBoxMax = Vector3.One;
            return;
        }

        // Compute bounding box of camera positions
        var positions = posedFrames.Select(f => f.CameraPosition).ToList();
        BoundingBoxMin = new Vector3(
            positions.Min(p => p.X),
            positions.Min(p => p.Y),
            positions.Min(p => p.Z)
        );
        BoundingBoxMax = new Vector3(
            positions.Max(p => p.X),
            positions.Max(p => p.Y),
            positions.Max(p => p.Z)
        );

        // Include sparse points if available
        if (SparsePointCloud.Count > 0)
        {
            BoundingBoxMin = new Vector3(
                Math.Min(BoundingBoxMin.X, SparsePointCloud.Min(p => p.Position.X)),
                Math.Min(BoundingBoxMin.Y, SparsePointCloud.Min(p => p.Position.Y)),
                Math.Min(BoundingBoxMin.Z, SparsePointCloud.Min(p => p.Position.Z))
            );
            BoundingBoxMax = new Vector3(
                Math.Max(BoundingBoxMax.X, SparsePointCloud.Max(p => p.Position.X)),
                Math.Max(BoundingBoxMax.Y, SparsePointCloud.Max(p => p.Position.Y)),
                Math.Max(BoundingBoxMax.Z, SparsePointCloud.Max(p => p.Position.Z))
            );
        }

        SceneCenter = (BoundingBoxMin + BoundingBoxMax) * 0.5f;
        SceneRadius = Vector3.Distance(BoundingBoxMin, BoundingBoxMax) * 0.5f;

        // Ensure minimum radius
        SceneRadius = Math.Max(SceneRadius, 0.1f);
    }

    /// <summary>
    /// Generate synthetic camera poses arranged in a circle around the scene center
    /// (Useful for testing or when no COLMAP data is available)
    /// </summary>
    public void GenerateCircularPoses(float radius = 2.0f, float height = 0.5f)
    {
        lock (_lock)
        {
            int frameCount = _frames.Count;
            if (frameCount == 0) return;

            for (int i = 0; i < frameCount; i++)
            {
                float angle = 2 * MathF.PI * i / frameCount;
                var position = new Vector3(
                    radius * MathF.Cos(angle),
                    height,
                    radius * MathF.Sin(angle)
                );

                _frames[i].SetPoseLookAt(position, Vector3.Zero, Vector3.UnitY);
                _frames[i].PoseStatus = PoseEstimationStatus.Estimated;
                _frames[i].PoseConfidence = 1.0f;
            }

            ComputeSceneBounds();
        }
    }

    /// <summary>
    /// Generate synthetic camera poses on a hemisphere looking at center
    /// </summary>
    public void GenerateHemispherePoses(float radius = 2.0f, int rings = 3)
    {
        lock (_lock)
        {
            int frameCount = _frames.Count;
            if (frameCount == 0) return;

            int framesPerRing = frameCount / rings;
            int frameIndex = 0;

            for (int ring = 0; ring < rings && frameIndex < frameCount; ring++)
            {
                float phi = MathF.PI * 0.5f * (ring + 1) / (rings + 1); // Elevation angle
                int framesInThisRing = (ring == rings - 1) ? frameCount - frameIndex : framesPerRing;

                for (int i = 0; i < framesInThisRing && frameIndex < frameCount; i++)
                {
                    float theta = 2 * MathF.PI * i / framesInThisRing; // Azimuth angle

                    var position = new Vector3(
                        radius * MathF.Cos(phi) * MathF.Cos(theta),
                        radius * MathF.Sin(phi),
                        radius * MathF.Cos(phi) * MathF.Sin(theta)
                    );

                    _frames[frameIndex].SetPoseLookAt(position, Vector3.Zero, Vector3.UnitY);
                    _frames[frameIndex].PoseStatus = PoseEstimationStatus.Estimated;
                    _frames[frameIndex].PoseConfidence = 1.0f;
                    frameIndex++;
                }
            }

            ComputeSceneBounds();
        }
    }

    /// <summary>
    /// Import camera poses from COLMAP sparse reconstruction
    /// </summary>
    public async Task ImportColmapPosesAsync(string colmapPath, IProgress<float> progress = null)
    {
        ReconstructionStatus = PoseReconstructionStatus.Loading;

        try
        {
            // Look for images.txt or images.bin
            var imagesTextPath = Path.Combine(colmapPath, "images.txt");
            var imagesBinPath = Path.Combine(colmapPath, "images.bin");
            var camerasTextPath = Path.Combine(colmapPath, "cameras.txt");
            var points3DPath = Path.Combine(colmapPath, "points3D.txt");

            if (File.Exists(imagesTextPath))
            {
                await ImportColmapImagesTextAsync(imagesTextPath, progress);
            }
            else if (File.Exists(imagesBinPath))
            {
                await ImportColmapImagesBinaryAsync(imagesBinPath, progress);
            }

            // Import cameras
            if (File.Exists(camerasTextPath))
            {
                await ImportColmapCamerasTextAsync(camerasTextPath);
            }

            // Import sparse points
            if (File.Exists(points3DPath))
            {
                await ImportColmapPoints3DAsync(points3DPath);
            }

            ComputeSceneBounds();
            ReconstructionStatus = PoseReconstructionStatus.Completed;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to import COLMAP poses: {ex.Message}");
            ReconstructionStatus = PoseReconstructionStatus.Failed;
        }
    }

    private async Task ImportColmapImagesTextAsync(string path, IProgress<float> progress = null)
    {
        var lines = await File.ReadAllLinesAsync(path);
        var imageLines = lines.Where(l => !l.StartsWith("#") && !string.IsNullOrWhiteSpace(l)).ToList();

        // Images.txt has pairs of lines: first line has pose info, second has points2D
        for (int i = 0; i < imageLines.Count; i += 2)
        {
            var parts = imageLines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 10) continue;

            // Parse: IMAGE_ID, QW, QX, QY, QZ, TX, TY, TZ, CAMERA_ID, NAME
            var imageName = parts[9];

            // Find matching frame
            var frame = _frames.FirstOrDefault(f =>
                Path.GetFileName(f.SourcePath)?.Equals(imageName, StringComparison.OrdinalIgnoreCase) == true);

            if (frame == null) continue;

            // Parse quaternion (w, x, y, z)
            float qw = float.Parse(parts[1]);
            float qx = float.Parse(parts[2]);
            float qy = float.Parse(parts[3]);
            float qz = float.Parse(parts[4]);

            // Parse translation
            float tx = float.Parse(parts[5]);
            float ty = float.Parse(parts[6]);
            float tz = float.Parse(parts[7]);

            // Convert quaternion to rotation matrix
            var rotation = Matrix4x4.CreateFromQuaternion(new Quaternion(qx, qy, qz, qw));

            // COLMAP stores world-to-camera, we need camera-to-world
            Matrix4x4.Invert(rotation, out var invRotation);
            var cameraPosition = -Vector3.Transform(new Vector3(tx, ty, tz), invRotation);

            frame.CameraToWorld = new Matrix4x4(
                invRotation.M11, invRotation.M12, invRotation.M13, 0,
                invRotation.M21, invRotation.M22, invRotation.M23, 0,
                invRotation.M31, invRotation.M32, invRotation.M33, 0,
                cameraPosition.X, cameraPosition.Y, cameraPosition.Z, 1
            );

            frame.PoseStatus = PoseEstimationStatus.Estimated;
            frame.PoseConfidence = 1.0f;

            progress?.Report((float)(i / 2 + 1) / (imageLines.Count / 2));
        }

        Logger.Log($"Imported {_frames.Count(f => f.PoseStatus == PoseEstimationStatus.Estimated)} camera poses from COLMAP");
    }

    private async Task ImportColmapCamerasTextAsync(string path)
    {
        var lines = await File.ReadAllLinesAsync(path);
        var cameraLines = lines.Where(l => !l.StartsWith("#") && !string.IsNullOrWhiteSpace(l)).ToList();

        foreach (var line in cameraLines)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5) continue;

            // CAMERA_ID, MODEL, WIDTH, HEIGHT, PARAMS[]
            var model = parts[1];
            var width = int.Parse(parts[2]);
            var height = int.Parse(parts[3]);

            float fx = 0, fy = 0, cx = 0, cy = 0;

            switch (model.ToUpper())
            {
                case "SIMPLE_PINHOLE":
                    fx = fy = float.Parse(parts[4]);
                    cx = float.Parse(parts[5]);
                    cy = float.Parse(parts[6]);
                    break;

                case "PINHOLE":
                    fx = float.Parse(parts[4]);
                    fy = float.Parse(parts[5]);
                    cx = float.Parse(parts[6]);
                    cy = float.Parse(parts[7]);
                    break;

                case "SIMPLE_RADIAL":
                case "RADIAL":
                    fx = fy = float.Parse(parts[4]);
                    cx = float.Parse(parts[5]);
                    cy = float.Parse(parts[6]);
                    break;

                default:
                    // Use first params as focal length
                    if (parts.Length > 4)
                        fx = fy = float.Parse(parts[4]);
                    cx = width / 2f;
                    cy = height / 2f;
                    break;
            }

            // Apply to all frames (assuming single camera)
            foreach (var frame in _frames)
            {
                frame.FocalLengthX = fx;
                frame.FocalLengthY = fy;
                frame.PrincipalPointX = cx;
                frame.PrincipalPointY = cy;
            }
        }
    }

    private async Task ImportColmapImagesBinaryAsync(string path, IProgress<float> progress = null)
    {
        await Task.Run(() =>
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(fs);

            var imageCount = reader.ReadUInt64();
            for (ulong i = 0; i < imageCount; i++)
            {
                var imageId = reader.ReadInt32();
                var qw = reader.ReadDouble();
                var qx = reader.ReadDouble();
                var qy = reader.ReadDouble();
                var qz = reader.ReadDouble();
                var tx = reader.ReadDouble();
                var ty = reader.ReadDouble();
                var tz = reader.ReadDouble();
                var cameraId = reader.ReadInt32();

                var nameChars = new List<byte>();
                byte b;
                while ((b = reader.ReadByte()) != 0)
                    nameChars.Add(b);
                var imageName = System.Text.Encoding.UTF8.GetString(nameChars.ToArray());

                var numPoints2D = reader.ReadUInt64();
                for (ulong p = 0; p < numPoints2D; p++)
                {
                    reader.ReadDouble(); // x
                    reader.ReadDouble(); // y
                    reader.ReadInt64();  // point3D_id
                }

                var frame = _frames.FirstOrDefault(f =>
                    string.Equals(Path.GetFileName(f.SourcePath), imageName, StringComparison.OrdinalIgnoreCase));

                if (frame != null)
                {
                    var rotation = new Quaternion((float)qx, (float)qy, (float)qz, (float)qw);
                    var translation = new Vector3((float)tx, (float)ty, (float)tz);
                    frame.SetPose(Matrix4x4.CreateFromQuaternion(rotation), translation);
                    frame.PoseStatus = PoseEstimationStatus.Estimated;
                    frame.PoseConfidence = 1.0f;
                }

                progress?.Report((float)(i + 1) / imageCount);
            }
        });
    }

    private async Task ImportColmapPoints3DAsync(string path)
    {
        var lines = await File.ReadAllLinesAsync(path);
        SparsePointCloud.Clear();

        foreach (var line in lines.Where(l => !l.StartsWith("#") && !string.IsNullOrWhiteSpace(l)))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 7) continue;

            // POINT3D_ID, X, Y, Z, R, G, B, ERROR, TRACK[]
            var point = new SparsePoint3D
            {
                Position = new Vector3(
                    float.Parse(parts[1]),
                    float.Parse(parts[2]),
                    float.Parse(parts[3])
                ),
                Color = new Vector3(
                    float.Parse(parts[4]) / 255f,
                    float.Parse(parts[5]) / 255f,
                    float.Parse(parts[6]) / 255f
                ),
                Error = parts.Length > 7 ? float.Parse(parts[7]) : 0
            };

            SparsePointCloud.Add(point);
        }

        Logger.Log($"Imported {SparsePointCloud.Count} sparse 3D points from COLMAP");
    }

    /// <summary>
    /// Normalize scene to fit within a unit sphere
    /// </summary>
    public void NormalizeScene()
    {
        ComputeSceneBounds();

        if (SceneRadius <= 0) return;

        float scale = 1.0f / SceneRadius;

        lock (_lock)
        {
            foreach (var frame in _frames)
            {
                var pos = frame.CameraPosition;
                frame.CameraPosition = (pos - SceneCenter) * scale;
            }

            for (int i = 0; i < SparsePointCloud.Count; i++)
            {
                var point = SparsePointCloud[i];
                point.Position = (point.Position - SceneCenter) * scale;
                SparsePointCloud[i] = point;
            }
        }

        // Recompute bounds after normalization
        ComputeSceneBounds();
    }
}

/// <summary>
/// Status of Structure-from-Motion reconstruction
/// </summary>
public enum PoseReconstructionStatus
{
    NotStarted,
    Loading,
    FeatureExtraction,
    FeatureMatching,
    PoseEstimation,
    BundleAdjustment,
    Completed,
    Failed
}

/// <summary>
/// Sparse 3D point from SfM reconstruction
/// </summary>
public struct SparsePoint3D
{
    public Vector3 Position { get; set; }
    public Vector3 Color { get; set; }
    public float Error { get; set; }
    public List<int> ObservingFrames { get; set; }
}
