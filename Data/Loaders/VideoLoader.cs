// GeoscientistToolkit/Data/Loaders/VideoLoader.cs

using GeoscientistToolkit.Data.Media;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Loaders;

/// <summary>
/// Loader for video files (MP4, AVI, MOV, etc.)
/// </summary>
public class VideoLoader : IDataLoader
{
    public string VideoPath { get; set; } = "";
    public bool GenerateThumbnail { get; set; } = true;

    public string Name => "Video File";
    public string Description => "Import video files (MP4, AVI, MOV, MKV, etc.)";

    public bool CanImport => !string.IsNullOrEmpty(VideoPath) && File.Exists(VideoPath);

    public string ValidationMessage
    {
        get
        {
            if (string.IsNullOrEmpty(VideoPath))
                return "Please select a video file";
            if (!File.Exists(VideoPath))
                return "Selected file does not exist";

            // Check if it's a supported video extension
            var extension = Path.GetExtension(VideoPath).ToLowerInvariant();
            var supportedExtensions = new[] { ".mp4", ".avi", ".mov", ".mkv", ".webm", ".flv", ".wmv", ".m4v" };

            if (!supportedExtensions.Contains(extension))
                return $"Unsupported video format: {extension}";

            return null;
        }
    }

    public async Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progressReporter)
    {
        return await Task.Run(async () =>
        {
            try
            {
                progressReporter?.Report((0.1f, "Loading video file..."));

                var fileName = Path.GetFileName(VideoPath);
                var dataset = new VideoDataset(Path.GetFileNameWithoutExtension(fileName), VideoPath);

                progressReporter?.Report((0.3f, "Analyzing video properties..."));

                // Load video metadata (this calls FFProbe internally)
                dataset.Load();

                if (dataset.IsMissing)
                {
                    throw new Exception("Failed to load video metadata");
                }

                progressReporter?.Report((0.6f, "Extracting metadata..."));

                // Populate dataset metadata with file information
                var fileInfo = new FileInfo(VideoPath);
                dataset.DatasetMetadata.SampleName = Path.GetFileNameWithoutExtension(fileName);
                dataset.DatasetMetadata.CollectionDate = fileInfo.CreationTime;
                dataset.DatasetMetadata.Notes = $"Video: {dataset.Width}x{dataset.Height}, {dataset.DurationSeconds:F2}s @ {dataset.FrameRate:F2}fps";

                if (GenerateThumbnail)
                {
                    progressReporter?.Report((0.8f, "Generating thumbnail..."));
                    try
                    {
                        await dataset.GenerateThumbnailAsync();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning($"[VideoLoader] Could not generate thumbnail: {ex.Message}");
                    }
                }

                progressReporter?.Report((1.0f, "Video imported successfully!"));

                Logger.Log($"[VideoLoader] Imported video: {dataset.Width}x{dataset.Height}, " +
                          $"{dataset.DurationSeconds:F2}s, {dataset.Codec}, " +
                          $"{(fileInfo.Length / 1024.0 / 1024.0):F2} MB");

                return dataset;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[VideoLoader] Error importing video: {ex}");
                throw new Exception($"Failed to import video: {ex.Message}", ex);
            }
        });
    }

    public void Reset()
    {
        VideoPath = "";
        GenerateThumbnail = true;
    }

    /// <summary>
    /// Get video info without creating a full dataset (for preview)
    /// </summary>
    public VideoInfo GetVideoInfo()
    {
        if (!CanImport) return null;

        try
        {
            var tempDataset = new VideoDataset("temp", VideoPath);
            tempDataset.Load();

            if (tempDataset.IsMissing)
                return null;

            return new VideoInfo
            {
                Width = tempDataset.Width,
                Height = tempDataset.Height,
                DurationSeconds = tempDataset.DurationSeconds,
                FrameRate = tempDataset.FrameRate,
                Codec = tempDataset.Codec,
                Format = tempDataset.Format,
                FileSize = new FileInfo(VideoPath).Length,
                FileName = Path.GetFileName(VideoPath),
                HasAudio = tempDataset.HasAudioTrack
            };
        }
        catch (Exception ex)
        {
            Logger.LogError($"[VideoLoader] Error reading video info: {ex.Message}");
            return null;
        }
    }

    public class VideoInfo
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public double DurationSeconds { get; set; }
        public double FrameRate { get; set; }
        public string Codec { get; set; }
        public string Format { get; set; }
        public long FileSize { get; set; }
        public string FileName { get; set; }
        public bool HasAudio { get; set; }
    }
}
