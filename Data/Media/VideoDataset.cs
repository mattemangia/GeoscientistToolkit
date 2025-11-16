// GeoscientistToolkit/Data/Media/VideoDataset.cs

using FFMpegCore;
using FFMpegCore.Enums;
using GeoscientistToolkit.Settings;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Media;

/// <summary>
/// Dataset for handling video files (MP4, AVI, MOV, etc.)
/// Uses FFMpegCore for open-source video processing
/// </summary>
public class VideoDataset : Dataset, IDisposable, ISerializableDataset
{
    public VideoDataset(string name, string filePath) : base(name, filePath)
    {
        Type = DatasetType.Video;
    }

    // Video properties
    public int Width { get; set; }
    public int Height { get; set; }
    public double DurationSeconds { get; set; }
    public double FrameRate { get; set; }
    public int TotalFrames { get; set; }
    public string Codec { get; set; }
    public string Format { get; set; }
    public long BitRate { get; set; }

    // Metadata from video file
    public Dictionary<string, string> VideoMetadata { get; set; } = new();

    // Thumbnail data (optional - first frame as thumbnail)
    public byte[] ThumbnailData { get; set; }
    public int ThumbnailWidth { get; set; }
    public int ThumbnailHeight { get; set; }

    // Video analysis
    public bool HasAudioTrack { get; set; }
    public int AudioChannels { get; set; }
    public int AudioSampleRate { get; set; }
    public string AudioCodec { get; set; }

    // Memory management - we don't keep full video in memory
    private IMediaAnalysis _mediaInfo;

    public void Dispose()
    {
        Unload();
        GC.SuppressFinalize(this);
    }

    public object ToSerializableObject()
    {
        return new VideoDatasetDTO
        {
            TypeName = nameof(VideoDataset),
            Name = Name,
            FilePath = FilePath,
            Metadata = this.DatasetMetadata != null ? new DatasetMetadataDTO
            {
                SampleName = this.DatasetMetadata.SampleName,
                LocationName = this.DatasetMetadata.LocationName,
                Latitude = this.DatasetMetadata.Latitude,
                Longitude = this.DatasetMetadata.Longitude,
                CoordinatesX = this.DatasetMetadata.Coordinates?.X,
                CoordinatesY = this.DatasetMetadata.Coordinates?.Y,
                Depth = this.DatasetMetadata.Depth,
                SizeX = this.DatasetMetadata.Size?.X,
                SizeY = this.DatasetMetadata.Size?.Y,
                SizeZ = this.DatasetMetadata.Size?.Z,
                SizeUnit = this.DatasetMetadata.SizeUnit,
                CollectionDate = this.DatasetMetadata.CollectionDate,
                Collector = this.DatasetMetadata.Collector,
                Notes = this.DatasetMetadata.Notes,
                CustomFields = this.DatasetMetadata.CustomFields
            } : new DatasetMetadataDTO(),
            Width = Width,
            Height = Height,
            DurationSeconds = DurationSeconds,
            FrameRate = FrameRate,
            TotalFrames = TotalFrames,
            Codec = Codec,
            Format = Format,
            BitRate = BitRate,
            VideoMetadata = VideoMetadata,
            HasAudioTrack = HasAudioTrack,
            AudioChannels = AudioChannels,
            AudioSampleRate = AudioSampleRate,
            AudioCodec = AudioCodec
        };
    }

    public override long GetSizeInBytes()
    {
        if (File.Exists(FilePath))
            return new FileInfo(FilePath).Length;
        return 0;
    }

    public override void Load()
    {
        if (_mediaInfo != null) return;

        try
        {
            if (!File.Exists(FilePath))
            {
                IsMissing = true;
                Logger.LogWarning($"Video file not found: {FilePath}");
                return;
            }

            // Analyze video using FFMpegCore
            _mediaInfo = FFProbe.Analyse(FilePath);

            if (_mediaInfo != null)
            {
                Width = _mediaInfo.PrimaryVideoStream?.Width ?? 0;
                Height = _mediaInfo.PrimaryVideoStream?.Height ?? 0;
                DurationSeconds = _mediaInfo.Duration.TotalSeconds;
                FrameRate = _mediaInfo.PrimaryVideoStream?.FrameRate ?? 0;
                TotalFrames = (int)(DurationSeconds * FrameRate);
                Codec = _mediaInfo.PrimaryVideoStream?.CodecName ?? "unknown";
                Format = _mediaInfo.Format.FormatName;
                BitRate = (long)_mediaInfo.Format.BitRate;

                // Audio information
                HasAudioTrack = _mediaInfo.PrimaryAudioStream != null;
                if (HasAudioTrack)
                {
                    AudioChannels = _mediaInfo.PrimaryAudioStream.Channels;
                    AudioSampleRate = _mediaInfo.PrimaryAudioStream.SampleRateHz;
                    AudioCodec = _mediaInfo.PrimaryAudioStream.CodecName;
                }

                Logger.Log($"Loaded video: {Width}x{Height}, {DurationSeconds:F2}s, {FrameRate:F2} fps");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to load video {FilePath}: {ex.Message}");
            IsMissing = true;
        }
    }

    public override void Unload()
    {
        if (SettingsManager.Instance.Settings.Performance.EnableLazyLoading)
        {
            _mediaInfo = null;
            ThumbnailData = null;
            GC.Collect();
        }
    }

    /// <summary>
    /// Extract a frame at the specified time (in seconds)
    /// Returns RGBA byte array
    /// </summary>
    public async Task<byte[]> ExtractFrameAsync(double timeSeconds, int width = 0, int height = 0)
    {
        if (!File.Exists(FilePath))
            return null;

        try
        {
            var targetWidth = width > 0 ? width : Width;
            var targetHeight = height > 0 ? height : Height;

            // Create temporary output path
            var tempPath = Path.Combine(Path.GetTempPath(), $"frame_{Guid.NewGuid()}.png");

            // Extract frame using FFMpegCore
            var snapshot = await FFMpeg.SnapshotAsync(
                FilePath,
                tempPath,
                new System.Drawing.Size(targetWidth, targetHeight),
                TimeSpan.FromSeconds(timeSeconds)
            );

            if (File.Exists(tempPath))
            {
                // Load the image and convert to RGBA
                var imageInfo = ImageLoader.LoadImage(tempPath);
                File.Delete(tempPath);
                return imageInfo?.Data;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to extract frame: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Generate thumbnail from first frame
    /// </summary>
    public async Task GenerateThumbnailAsync(int maxWidth = 320, int maxHeight = 240)
    {
        var aspectRatio = (double)Width / Height;
        ThumbnailWidth = maxWidth;
        ThumbnailHeight = (int)(maxWidth / aspectRatio);

        if (ThumbnailHeight > maxHeight)
        {
            ThumbnailHeight = maxHeight;
            ThumbnailWidth = (int)(maxHeight * aspectRatio);
        }

        ThumbnailData = await ExtractFrameAsync(0, ThumbnailWidth, ThumbnailHeight);
    }
}
