// GeoscientistToolkit/Data/Loaders/AcousticVolumeLoader.cs

using System.Text.Json;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Loaders;

/// <summary>
///     Loader for acoustic volume datasets with time series data
/// </summary>
public class AcousticVolumeLoader : IDataLoader
{
    private AcousticVolumeInfo _volumeInfo;
    public string DirectoryPath { get; set; }
    public string Name => "Acoustic Volume Loader";
    public string Description => "Loads acoustic simulation results including wave fields and time series data";

    public bool CanImport => !string.IsNullOrEmpty(DirectoryPath) && Directory.Exists(DirectoryPath) &&
                             GetVolumeInfo().IsValid;

    public string ValidationMessage => GetValidationMessage();

    public void Reset()
    {
        DirectoryPath = "";
        _volumeInfo = null;
    }

    public async Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progress)
    {
        return await Task.Run(() => Load(progress));
    }

    private string GetValidationMessage()
    {
        if (string.IsNullOrEmpty(DirectoryPath))
            return "Please select an acoustic volume directory";

        if (!Directory.Exists(DirectoryPath))
            return "The specified directory does not exist";

        var info = GetVolumeInfo();
        if (!info.IsValid)
        {
            if (!string.IsNullOrEmpty(info.ErrorMessage))
                return info.ErrorMessage;
            if (!info.HasMetadata)
                return "Missing required metadata.json file";
            if (!info.HasPWaveField && !info.HasSWaveField && !info.HasCombinedField)
                return "No wave field data found in directory";
            return "Invalid acoustic volume directory structure";
        }

        return "Ready to import acoustic volume";
    }

    public AcousticVolumeInfo GetVolumeInfo()
    {
        if (_volumeInfo != null) return _volumeInfo;
        if (string.IsNullOrEmpty(DirectoryPath) || !Directory.Exists(DirectoryPath))
            return new AcousticVolumeInfo { IsValid = false };

        _volumeInfo = new AcousticVolumeInfo
        {
            DirectoryName = Path.GetFileName(DirectoryPath)
        };

        try
        {
            // Check for metadata
            var metadataPath = Path.Combine(DirectoryPath, "metadata.json");
            _volumeInfo.HasMetadata = File.Exists(metadataPath);

            // Check for wave field files
            var pWavePath = Path.Combine(DirectoryPath, "PWaveField.bin");
            var sWavePath = Path.Combine(DirectoryPath, "SWaveField.bin");
            var combinedPath = Path.Combine(DirectoryPath, "CombinedField.bin");
            var damagePath = Path.Combine(DirectoryPath, "DamageField.bin"); // ADDED

            _volumeInfo.HasPWaveField = File.Exists(pWavePath);
            _volumeInfo.HasSWaveField = File.Exists(sWavePath);
            _volumeInfo.HasCombinedField = File.Exists(combinedPath);
            _volumeInfo.HasDamageField = File.Exists(damagePath); // ADDED

            // Check for time series
            var timeSeriesDir = Path.Combine(DirectoryPath, "TimeSeries");
            if (Directory.Exists(timeSeriesDir))
            {
                var snapshots = Directory.GetFiles(timeSeriesDir, "snapshot_*.bin");
                _volumeInfo.HasTimeSeries = snapshots.Length > 0;
                _volumeInfo.TimeSeriesFrameCount = snapshots.Length;
            }

            // Calculate total size
            _volumeInfo.TotalSize = 0;
            foreach (var file in Directory.GetFiles(DirectoryPath, "*", SearchOption.AllDirectories))
            {
                var fileInfo = new FileInfo(file);
                _volumeInfo.TotalSize += fileInfo.Length;
            }

            _volumeInfo.IsValid = _volumeInfo.HasMetadata &&
                                  (_volumeInfo.HasPWaveField || _volumeInfo.HasSWaveField ||
                                   _volumeInfo.HasCombinedField);

            if (!_volumeInfo.IsValid && !_volumeInfo.HasMetadata)
                _volumeInfo.ErrorMessage = "Missing metadata.json file";
            else if (!_volumeInfo.IsValid) _volumeInfo.ErrorMessage = "No wave field data found";
        }
        catch (Exception ex)
        {
            _volumeInfo.IsValid = false;
            _volumeInfo.ErrorMessage = ex.Message;
        }

        return _volumeInfo;
    }

    private Dataset Load(IProgress<(float progress, string message)> progress)
    {
        if (!CanImport)
            throw new InvalidOperationException("Cannot import: directory not specified or doesn't exist");

        progress?.Report((0.0f, "Initializing acoustic volume dataset..."));

        var datasetName = Path.GetFileName(DirectoryPath);
        var dataset = new AcousticVolumeDataset(datasetName, DirectoryPath);

        try
        {
            progress?.Report((0.1f, "Loading metadata..."));

            // Load metadata
            var metadataPath = Path.Combine(DirectoryPath, "metadata.json");
            if (File.Exists(metadataPath))
            {
                var json = File.ReadAllText(metadataPath);
                var metadata = JsonSerializer.Deserialize<AcousticMetadata>(json);
                ApplyMetadata(dataset, metadata);
            }

            var progressBase = 0.2f;
            var progressPerVolume = 0.18f; // Adjusted to make space for DamageField
            var volumeCount = 0;

            // Load P-Wave field
            var pWavePath = Path.Combine(DirectoryPath, "PWaveField.bin");
            if (File.Exists(pWavePath))
            {
                progress?.Report((progressBase + volumeCount * progressPerVolume, "Loading P-Wave field..."));
                dataset.PWaveField = LoadVolume(pWavePath, progress, progressBase + volumeCount * progressPerVolume,
                    progressPerVolume);
                volumeCount++;
            }

            // Load S-Wave field
            var sWavePath = Path.Combine(DirectoryPath, "SWaveField.bin");
            if (File.Exists(sWavePath))
            {
                progress?.Report((progressBase + volumeCount * progressPerVolume, "Loading S-Wave field..."));
                dataset.SWaveField = LoadVolume(sWavePath, progress, progressBase + volumeCount * progressPerVolume,
                    progressPerVolume);
                volumeCount++;
            }

            // Load Combined field
            var combinedPath = Path.Combine(DirectoryPath, "CombinedField.bin");
            if (File.Exists(combinedPath))
            {
                progress?.Report((progressBase + volumeCount * progressPerVolume, "Loading Combined wave field..."));
                dataset.CombinedWaveField = LoadVolume(combinedPath, progress,
                    progressBase + volumeCount * progressPerVolume, progressPerVolume);
                volumeCount++;
            }

            // MODIFIED: Load Damage Field
            var damagePath = Path.Combine(DirectoryPath, "DamageField.bin");
            if (File.Exists(damagePath))
            {
                progress?.Report((progressBase + volumeCount * progressPerVolume, "Loading Damage field..."));
                dataset.DamageField = LoadVolume(damagePath, progress, progressBase + volumeCount * progressPerVolume,
                    progressPerVolume);
                volumeCount++;
            }

            // Load time series if available
            progressBase = 0.85f;
            var timeSeriesDir = Path.Combine(DirectoryPath, "TimeSeries");
            if (Directory.Exists(timeSeriesDir))
            {
                progress?.Report((progressBase, "Loading time series snapshots..."));
                LoadTimeSeries(dataset, timeSeriesDir, progress, progressBase, 0.15f);
            }

            progress?.Report((1.0f, "Acoustic volume dataset loaded successfully"));

            Logger.Log($"[AcousticVolumeLoader] Loaded acoustic volume: {datasetName}");
            return dataset;
        }
        catch (Exception ex)
        {
            Logger.LogError($"[AcousticVolumeLoader] Failed to load acoustic volume: {ex.Message}");
            throw;
        }
    }

    private void ApplyMetadata(AcousticVolumeDataset dataset, AcousticMetadata metadata)
    {
        dataset.PWaveVelocity = metadata.PWaveVelocity;
        dataset.SWaveVelocity = metadata.SWaveVelocity;
        dataset.VpVsRatio = metadata.VpVsRatio;
        dataset.TimeSteps = metadata.TimeSteps;
        dataset.ComputationTime = TimeSpan.FromSeconds(metadata.ComputationTimeSeconds);
        dataset.YoungsModulusMPa = metadata.YoungsModulusMPa;
        dataset.PoissonRatio = metadata.PoissonRatio;
        dataset.ConfiningPressureMPa = metadata.ConfiningPressureMPa;
        dataset.SourceFrequencyKHz = metadata.SourceFrequencyKHz;
        dataset.SourceEnergyJ = metadata.SourceEnergyJ;
        dataset.SourceDatasetPath = metadata.SourceDatasetPath;
        dataset.SourceMaterialName = metadata.SourceMaterialName;
    }

    private ChunkedVolume LoadVolume(string path, IProgress<(float, string)> progress, float baseProgress,
        float progressRange)
    {
        try
        {
            // Read header to get dimensions
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                var width = reader.ReadInt32();
                var height = reader.ReadInt32();
                var depth = reader.ReadInt32();

                var volume = new ChunkedVolume(width, height, depth);

                // Read slices
                for (var z = 0; z < depth; z++)
                {
                    if (z % 10 == 0)
                    {
                        var sliceProgress = baseProgress + progressRange * z / depth;
                        progress?.Report((sliceProgress, $"Loading slice {z + 1}/{depth}..."));
                    }

                    var slice = reader.ReadBytes(width * height);
                    volume.WriteSliceZ(z, slice);
                }

                return volume;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[AcousticVolumeLoader] Failed to load volume from {path}: {ex.Message}");
            throw;
        }
    }

    private void LoadTimeSeries(AcousticVolumeDataset dataset, string timeSeriesDir,
        IProgress<(float, string)> progress, float baseProgress, float progressRange)
    {
        var files = Directory.GetFiles(timeSeriesDir, "snapshot_*.bin")
            .OrderBy(f => int.Parse(Path.GetFileNameWithoutExtension(f).Replace("snapshot_", "")))
            .ToList();

        dataset.TimeSeriesSnapshots = new List<WaveFieldSnapshot>();

        for (var i = 0; i < files.Count; i++)
        {
            if (i % 5 == 0)
            {
                var snapshotProgress = baseProgress + progressRange * i / files.Count;
                progress?.Report((snapshotProgress, $"Loading snapshot {i + 1}/{files.Count}..."));
            }

            try
            {
                var snapshot = WaveFieldSnapshot.LoadFromFile(files[i]);
                if (snapshot != null) dataset.TimeSeriesSnapshots.Add(snapshot);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[AcousticVolumeLoader] Failed to load snapshot {files[i]}: {ex.Message}");
            }
        }

        Logger.Log($"[AcousticVolumeLoader] Loaded {dataset.TimeSeriesSnapshots.Count} time series snapshots");
    }

    public class AcousticVolumeInfo
    {
        public bool IsValid { get; set; }
        public string DirectoryName { get; set; }
        public bool HasMetadata { get; set; }
        public bool HasPWaveField { get; set; }
        public bool HasSWaveField { get; set; }
        public bool HasCombinedField { get; set; }
        public bool HasDamageField { get; set; } // ADDED
        public bool HasTimeSeries { get; set; }
        public int TimeSeriesFrameCount { get; set; }
        public long TotalSize { get; set; }
        public string ErrorMessage { get; set; }
    }
}