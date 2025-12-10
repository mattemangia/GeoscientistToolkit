// GeoscientistToolkit/Data/Nerf/NerfImageFrame.cs

using System.Numerics;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Nerf;

/// <summary>
/// Represents a single image frame with camera pose for NeRF training.
/// </summary>
public class NerfImageFrame : IDisposable
{
    public NerfImageFrame()
    {
        Id = Guid.NewGuid();
    }

    // Unique identifier
    public Guid Id { get; set; }

    // Source information
    public string SourcePath { get; set; }
    public int FrameIndex { get; set; }
    public double TimestampSeconds { get; set; }

    // Image data
    public byte[] ImageData { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Channels { get; set; } = 3;

    // Thumbnail for preview
    public byte[] ThumbnailData { get; set; }
    public int ThumbnailWidth { get; set; }
    public int ThumbnailHeight { get; set; }

    // Camera intrinsics
    public float FocalLengthX { get; set; }
    public float FocalLengthY { get; set; }
    public float PrincipalPointX { get; set; }
    public float PrincipalPointY { get; set; }

    // Camera extrinsics (camera-to-world transform)
    public Matrix4x4 CameraToWorld { get; set; } = Matrix4x4.Identity;

    // Camera position (extracted from CameraToWorld)
    public Vector3 CameraPosition
    {
        get => new Vector3(CameraToWorld.M41, CameraToWorld.M42, CameraToWorld.M43);
        set
        {
            var mat = CameraToWorld;
            mat.M41 = value.X;
            mat.M42 = value.Y;
            mat.M43 = value.Z;
            CameraToWorld = mat;
        }
    }

    // Camera look direction
    public Vector3 LookDirection => -new Vector3(CameraToWorld.M31, CameraToWorld.M32, CameraToWorld.M33);

    // Camera up vector
    public Vector3 UpDirection => new Vector3(CameraToWorld.M21, CameraToWorld.M22, CameraToWorld.M23);

    // Pose estimation status
    public PoseEstimationStatus PoseStatus { get; set; } = PoseEstimationStatus.NotEstimated;
    public float PoseConfidence { get; set; } = 0f;

    // Feature points detected in this frame
    public List<FeaturePoint> FeaturePoints { get; set; } = new();

    // For use in training
    public bool IsEnabled { get; set; } = true;
    public bool IsKeyframe { get; set; } = false;

    public void Dispose()
    {
        ImageData = null;
        ThumbnailData = null;
        FeaturePoints.Clear();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Load image frame from file
    /// </summary>
    public static async Task<NerfImageFrame> LoadFromFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Logger.LogError($"Image file not found: {filePath}");
            return null;
        }

        try
        {
            var frame = new NerfImageFrame
            {
                SourcePath = filePath
            };

            // Load image using ImageLoader
            var imageInfo = await Task.Run(() => ImageLoader.LoadImage(filePath));
            if (imageInfo == null)
            {
                Logger.LogError($"Failed to load image: {filePath}");
                return null;
            }

            frame.ImageData = imageInfo.Data;
            frame.Width = imageInfo.Width;
            frame.Height = imageInfo.Height;
            frame.Channels = imageInfo.Data.Length / (imageInfo.Width * imageInfo.Height);

            // Generate thumbnail
            frame.GenerateThumbnail(256);

            // Initialize camera intrinsics with reasonable defaults
            // Assuming a typical camera with ~60 degree FOV
            var focalLength = frame.Width / (2f * (float)Math.Tan(Math.PI / 6));
            frame.FocalLengthX = focalLength;
            frame.FocalLengthY = focalLength;
            frame.PrincipalPointX = frame.Width / 2f;
            frame.PrincipalPointY = frame.Height / 2f;

            return frame;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error loading image frame: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Create frame from raw image data
    /// </summary>
    public static NerfImageFrame FromImageData(byte[] data, int width, int height, int channels = 3)
    {
        var frame = new NerfImageFrame
        {
            ImageData = data,
            Width = width,
            Height = height,
            Channels = channels
        };

        // Initialize camera intrinsics
        var focalLength = width / (2f * (float)Math.Tan(Math.PI / 6));
        frame.FocalLengthX = focalLength;
        frame.FocalLengthY = focalLength;
        frame.PrincipalPointX = width / 2f;
        frame.PrincipalPointY = height / 2f;

        frame.GenerateThumbnail(256);

        return frame;
    }

    /// <summary>
    /// Generate a thumbnail for preview
    /// </summary>
    public void GenerateThumbnail(int maxSize = 256)
    {
        if (ImageData == null || Width == 0 || Height == 0)
            return;

        // Calculate thumbnail dimensions
        float aspectRatio = (float)Width / Height;
        if (Width > Height)
        {
            ThumbnailWidth = Math.Min(Width, maxSize);
            ThumbnailHeight = (int)(ThumbnailWidth / aspectRatio);
        }
        else
        {
            ThumbnailHeight = Math.Min(Height, maxSize);
            ThumbnailWidth = (int)(ThumbnailHeight * aspectRatio);
        }

        // Simple bilinear downsampling
        ThumbnailData = DownsampleImage(ImageData, Width, Height, Channels, ThumbnailWidth, ThumbnailHeight);
    }

    private static byte[] DownsampleImage(byte[] source, int srcW, int srcH, int channels, int dstW, int dstH)
    {
        var result = new byte[dstW * dstH * channels];
        float xRatio = (float)srcW / dstW;
        float yRatio = (float)srcH / dstH;

        for (int y = 0; y < dstH; y++)
        {
            for (int x = 0; x < dstW; x++)
            {
                int srcX = (int)(x * xRatio);
                int srcY = (int)(y * yRatio);
                srcX = Math.Min(srcX, srcW - 1);
                srcY = Math.Min(srcY, srcH - 1);

                int srcIdx = (srcY * srcW + srcX) * channels;
                int dstIdx = (y * dstW + x) * channels;

                for (int c = 0; c < channels; c++)
                {
                    result[dstIdx + c] = source[srcIdx + c];
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Set camera pose from rotation and translation
    /// </summary>
    public void SetPose(Matrix4x4 rotation, Vector3 translation)
    {
        CameraToWorld = new Matrix4x4(
            rotation.M11, rotation.M12, rotation.M13, 0,
            rotation.M21, rotation.M22, rotation.M23, 0,
            rotation.M31, rotation.M32, rotation.M33, 0,
            translation.X, translation.Y, translation.Z, 1
        );
        PoseStatus = PoseEstimationStatus.Estimated;
    }

    /// <summary>
    /// Set camera pose using look-at parameters
    /// </summary>
    public void SetPoseLookAt(Vector3 position, Vector3 target, Vector3 up)
    {
        var forward = Vector3.Normalize(target - position);
        var right = Vector3.Normalize(Vector3.Cross(up, forward));
        var realUp = Vector3.Cross(forward, right);

        CameraToWorld = new Matrix4x4(
            right.X, right.Y, right.Z, 0,
            realUp.X, realUp.Y, realUp.Z, 0,
            -forward.X, -forward.Y, -forward.Z, 0,
            position.X, position.Y, position.Z, 1
        );
        PoseStatus = PoseEstimationStatus.Estimated;
    }

    /// <summary>
    /// Get intrinsic camera matrix (3x3)
    /// </summary>
    public Matrix4x4 GetIntrinsicMatrix()
    {
        return new Matrix4x4(
            FocalLengthX, 0, PrincipalPointX, 0,
            0, FocalLengthY, PrincipalPointY, 0,
            0, 0, 1, 0,
            0, 0, 0, 1
        );
    }

    public NerfImageFrameDTO ToDTO()
    {
        return new NerfImageFrameDTO
        {
            Id = Id,
            SourcePath = SourcePath,
            FrameIndex = FrameIndex,
            TimestampSeconds = TimestampSeconds,
            Width = Width,
            Height = Height,
            Channels = Channels,
            FocalLengthX = FocalLengthX,
            FocalLengthY = FocalLengthY,
            PrincipalPointX = PrincipalPointX,
            PrincipalPointY = PrincipalPointY,
            CameraToWorld = CameraToWorld,
            PoseStatus = PoseStatus.ToString(),
            PoseConfidence = PoseConfidence,
            IsEnabled = IsEnabled,
            IsKeyframe = IsKeyframe
        };
    }

    public static NerfImageFrame FromDTO(NerfImageFrameDTO dto)
    {
        var frame = new NerfImageFrame
        {
            Id = dto.Id,
            SourcePath = dto.SourcePath,
            FrameIndex = dto.FrameIndex,
            TimestampSeconds = dto.TimestampSeconds,
            Width = dto.Width,
            Height = dto.Height,
            Channels = dto.Channels,
            FocalLengthX = dto.FocalLengthX,
            FocalLengthY = dto.FocalLengthY,
            PrincipalPointX = dto.PrincipalPointX,
            PrincipalPointY = dto.PrincipalPointY,
            CameraToWorld = dto.CameraToWorld,
            PoseConfidence = dto.PoseConfidence,
            IsEnabled = dto.IsEnabled,
            IsKeyframe = dto.IsKeyframe
        };

        if (Enum.TryParse<PoseEstimationStatus>(dto.PoseStatus, out var status))
        {
            frame.PoseStatus = status;
        }

        return frame;
    }
}

/// <summary>
/// Status of camera pose estimation for a frame
/// </summary>
public enum PoseEstimationStatus
{
    NotEstimated,
    Estimating,
    Estimated,
    Failed,
    ManuallySet
}

/// <summary>
/// Feature point detected in an image
/// </summary>
public struct FeaturePoint
{
    public Vector2 Position { get; set; }
    public float Scale { get; set; }
    public float Angle { get; set; }
    public float Response { get; set; }
    public int OctaveLayer { get; set; }
    public byte[] Descriptor { get; set; }

    // 3D position after triangulation (if available)
    public Vector3? WorldPosition { get; set; }
    public int? TrackId { get; set; }
}
