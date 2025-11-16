// GeoscientistToolkit/Data/Loaders/AudioLoader.cs

using GeoscientistToolkit.Data.Media;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Loaders;

/// <summary>
/// Loader for audio files (WAV, MP3, OGG, etc.)
/// </summary>
public class AudioLoader : IDataLoader
{
    public string AudioPath { get; set; } = "";
    public bool GenerateWaveform { get; set; } = true;

    public string Name => "Audio File";
    public string Description => "Import audio files (WAV, MP3, OGG, FLAC, etc.)";

    public bool CanImport => !string.IsNullOrEmpty(AudioPath) && File.Exists(AudioPath);

    public string ValidationMessage
    {
        get
        {
            if (string.IsNullOrEmpty(AudioPath))
                return "Please select an audio file";
            if (!File.Exists(AudioPath))
                return "Selected file does not exist";

            // Check if it's a supported audio extension
            var extension = Path.GetExtension(AudioPath).ToLowerInvariant();
            var supportedExtensions = new[] { ".wav", ".mp3", ".ogg", ".flac", ".m4a", ".aac", ".wma" };

            if (!supportedExtensions.Contains(extension))
                return $"Unsupported audio format: {extension}";

            return null;
        }
    }

    public async Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progressReporter)
    {
        return await Task.Run(() =>
        {
            try
            {
                progressReporter?.Report((0.1f, "Loading audio file..."));

                var fileName = Path.GetFileName(AudioPath);
                var dataset = new AudioDataset(Path.GetFileNameWithoutExtension(fileName), AudioPath);

                progressReporter?.Report((0.3f, "Analyzing audio properties..."));

                // Load audio metadata and generate waveform
                dataset.Load();

                if (dataset.IsMissing)
                {
                    throw new Exception("Failed to load audio metadata");
                }

                progressReporter?.Report((0.7f, "Extracting metadata..."));

                // Populate dataset metadata with file information
                var fileInfo = new FileInfo(AudioPath);
                dataset.DatasetMetadata.SampleName = Path.GetFileNameWithoutExtension(fileName);
                dataset.DatasetMetadata.CollectionDate = fileInfo.CreationTime;
                dataset.DatasetMetadata.Notes = $"Audio: {dataset.SampleRate}Hz, {dataset.Channels}ch, {dataset.DurationSeconds:F2}s, {dataset.Encoding}";

                progressReporter?.Report((1.0f, "Audio imported successfully!"));

                Logger.Log($"[AudioLoader] Imported audio: {dataset.SampleRate}Hz, {dataset.Channels}ch, " +
                          $"{dataset.DurationSeconds:F2}s, {dataset.Encoding}, " +
                          $"{(fileInfo.Length / 1024.0 / 1024.0):F2} MB");

                return dataset;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[AudioLoader] Error importing audio: {ex}");
                throw new Exception($"Failed to import audio: {ex.Message}", ex);
            }
        });
    }

    public void Reset()
    {
        AudioPath = "";
        GenerateWaveform = true;
    }

    /// <summary>
    /// Get audio info without creating a full dataset (for preview)
    /// </summary>
    public AudioInfo GetAudioInfo()
    {
        if (!CanImport) return null;

        try
        {
            var tempDataset = new AudioDataset("temp", AudioPath);
            tempDataset.Load();

            if (tempDataset.IsMissing)
                return null;

            return new AudioInfo
            {
                SampleRate = tempDataset.SampleRate,
                Channels = tempDataset.Channels,
                BitsPerSample = tempDataset.BitsPerSample,
                DurationSeconds = tempDataset.DurationSeconds,
                Encoding = tempDataset.Encoding,
                Format = tempDataset.Format,
                FileSize = new FileInfo(AudioPath).Length,
                FileName = Path.GetFileName(AudioPath)
            };
        }
        catch (Exception ex)
        {
            Logger.LogError($"[AudioLoader] Error reading audio info: {ex.Message}");
            return null;
        }
    }

    public class AudioInfo
    {
        public int SampleRate { get; set; }
        public int Channels { get; set; }
        public int BitsPerSample { get; set; }
        public double DurationSeconds { get; set; }
        public string Encoding { get; set; }
        public string Format { get; set; }
        public long FileSize { get; set; }
        public string FileName { get; set; }
    }
}
