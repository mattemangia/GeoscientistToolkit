// GeoscientistToolkit/Data/Nerf/NerfDataset.cs

using System.Numerics;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Nerf;

/// <summary>
/// Dataset for Neural Radiance Fields (NeRF) reconstruction from images.
/// Supports training from image collections or live video streams.
/// </summary>
public class NerfDataset : Dataset, IDisposable, ISerializableDataset
{
    public NerfDataset(string name, string filePath) : base(name, filePath)
    {
        Type = DatasetType.Nerf;
    }

    // NeRF model state
    public NerfTrainingState TrainingState { get; set; } = NerfTrainingState.NotStarted;
    public int CurrentIteration { get; set; } = 0;
    public int TotalIterations { get; set; } = 30000;
    public float CurrentLoss { get; set; } = float.MaxValue;
    public float CurrentPSNR { get; set; } = 0;

    // Image collection for training
    public NerfImageCollection ImageCollection { get; set; } = new();

    // Scene bounds
    public Vector3 SceneCenter { get; set; } = Vector3.Zero;
    public float SceneRadius { get; set; } = 1.0f;
    public Vector3 BoundingBoxMin { get; set; } = -Vector3.One;
    public Vector3 BoundingBoxMax { get; set; } = Vector3.One;

    // Training parameters
    public NerfTrainingConfig TrainingConfig { get; set; } = new();

    // Trained model data (hash grid encoding + MLP weights)
    public NerfModelData ModelData { get; set; }

    // Training history for visualization
    public List<NerfTrainingLog> TrainingHistory { get; set; } = new();

    // Source information
    public NerfSourceType SourceType { get; set; } = NerfSourceType.ImageFolder;
    public string SourcePath { get; set; }
    public string VideoPath { get; set; }

    // Timestamps
    public DateTime? TrainingStartTime { get; set; }
    public DateTime? TrainingEndTime { get; set; }
    public double TrainingDurationSeconds =>
        (TrainingEndTime ?? DateTime.Now).Subtract(TrainingStartTime ?? DateTime.Now).TotalSeconds;

    public void Dispose()
    {
        Unload();
        GC.SuppressFinalize(this);
    }

    public object ToSerializableObject()
    {
        return new NerfDatasetDTO
        {
            TypeName = nameof(NerfDataset),
            Name = Name,
            FilePath = FilePath,
            Metadata = DatasetMetadata != null ? new DatasetMetadataDTO
            {
                SampleName = DatasetMetadata.SampleName,
                LocationName = DatasetMetadata.LocationName,
                Latitude = DatasetMetadata.Latitude,
                Longitude = DatasetMetadata.Longitude,
                CoordinatesX = DatasetMetadata.Coordinates?.X,
                CoordinatesY = DatasetMetadata.Coordinates?.Y,
                Depth = DatasetMetadata.Depth,
                SizeX = DatasetMetadata.Size?.X,
                SizeY = DatasetMetadata.Size?.Y,
                SizeZ = DatasetMetadata.Size?.Z,
                SizeUnit = DatasetMetadata.SizeUnit,
                CollectionDate = DatasetMetadata.CollectionDate,
                Collector = DatasetMetadata.Collector,
                Notes = DatasetMetadata.Notes,
                CustomFields = DatasetMetadata.CustomFields
            } : new DatasetMetadataDTO(),
            TrainingState = TrainingState.ToString(),
            CurrentIteration = CurrentIteration,
            TotalIterations = TotalIterations,
            CurrentLoss = CurrentLoss,
            CurrentPSNR = CurrentPSNR,
            SceneCenter = SceneCenter,
            SceneRadius = SceneRadius,
            BoundingBoxMin = BoundingBoxMin,
            BoundingBoxMax = BoundingBoxMax,
            SourceType = SourceType.ToString(),
            SourcePath = SourcePath,
            VideoPath = VideoPath,
            TrainingStartTime = TrainingStartTime,
            TrainingEndTime = TrainingEndTime,
            TrainingConfig = TrainingConfig?.ToDTO(),
            ImageFrames = ImageCollection?.Frames.Select(f => f.ToDTO()).ToList() ?? new List<NerfImageFrameDTO>()
        };
    }

    public override long GetSizeInBytes()
    {
        long size = 0;

        // Image collection size
        if (ImageCollection != null)
        {
            size += ImageCollection.GetTotalSizeBytes();
        }

        // Model data size
        if (ModelData != null)
        {
            size += ModelData.GetSizeBytes();
        }

        return size;
    }

    public override void Load()
    {
        if (string.IsNullOrEmpty(FilePath) || !Directory.Exists(FilePath))
        {
            // Try to load from file path as a saved NeRF model
            var modelPath = Path.Combine(FilePath, "model.nerfmodel");
            if (File.Exists(modelPath))
            {
                LoadModel(modelPath);
            }
            return;
        }

        Logger.Log($"Loading NeRF dataset from: {FilePath}");
    }

    public override void Unload()
    {
        ImageCollection?.Clear();
        ModelData = null;
        TrainingHistory.Clear();
        GC.Collect();
    }

    /// <summary>
    /// Import images from a folder
    /// </summary>
    public async Task ImportFromFolderAsync(string folderPath, IProgress<float> progress = null)
    {
        if (!Directory.Exists(folderPath))
        {
            Logger.LogError($"Folder not found: {folderPath}");
            return;
        }

        SourceType = NerfSourceType.ImageFolder;
        SourcePath = folderPath;

        var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".tiff", ".tif" };
        var imageFiles = Directory.GetFiles(folderPath)
            .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()))
            .OrderBy(f => f)
            .ToList();

        Logger.Log($"Found {imageFiles.Count} images in folder");

        ImageCollection = new NerfImageCollection();

        for (int i = 0; i < imageFiles.Count; i++)
        {
            var frame = await NerfImageFrame.LoadFromFileAsync(imageFiles[i]);
            if (frame != null)
            {
                ImageCollection.AddFrame(frame);
            }
            progress?.Report((float)(i + 1) / imageFiles.Count);
        }

        Logger.Log($"Loaded {ImageCollection.FrameCount} images for NeRF training");
    }

    /// <summary>
    /// Import keyframes from a video file
    /// </summary>
    public async Task ImportFromVideoAsync(string videoPath, int keyframeInterval = 15, IProgress<float> progress = null)
    {
        if (!File.Exists(videoPath))
        {
            Logger.LogError($"Video file not found: {videoPath}");
            return;
        }

        SourceType = NerfSourceType.VideoFile;
        VideoPath = videoPath;

        ImageCollection = new NerfImageCollection();
        await ImageCollection.ExtractKeyframesFromVideoAsync(videoPath, keyframeInterval, progress);

        Logger.Log($"Extracted {ImageCollection.FrameCount} keyframes from video");
    }

    /// <summary>
    /// Save the trained model to disk
    /// </summary>
    public void SaveModel(string outputPath)
    {
        if (ModelData == null)
        {
            Logger.LogWarning("No model data to save");
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            ModelData.SaveToFile(outputPath);
            Logger.Log($"Saved NeRF model to: {outputPath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to save model: {ex.Message}");
        }
    }

    /// <summary>
    /// Load a trained model from disk
    /// </summary>
    public void LoadModel(string modelPath)
    {
        if (!File.Exists(modelPath))
        {
            Logger.LogError($"Model file not found: {modelPath}");
            return;
        }

        try
        {
            ModelData = NerfModelData.LoadFromFile(modelPath);
            TrainingState = NerfTrainingState.Completed;
            Logger.Log($"Loaded NeRF model from: {modelPath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to load model: {ex.Message}");
        }
    }
}

/// <summary>
/// NeRF training states
/// </summary>
public enum NerfTrainingState
{
    NotStarted,
    Preparing,
    Training,
    Paused,
    Completed,
    Failed
}

/// <summary>
/// Source types for NeRF data
/// </summary>
public enum NerfSourceType
{
    ImageFolder,
    VideoFile,
    LiveStream,
    ColmapProject,
    PhotogrammetryOutput
}

/// <summary>
/// Training configuration for NeRF
/// </summary>
public class NerfTrainingConfig
{
    // Network architecture
    public int HashGridLevels { get; set; } = 16;
    public int HashGridFeatures { get; set; } = 2;
    public int HashTableSize { get; set; } = 1 << 19; // 2^19
    public float HashGridMinResolution { get; set; } = 16.0f;
    public float HashGridMaxResolution { get; set; } = 2048.0f;

    // MLP configuration
    public int MlpHiddenLayers { get; set; } = 2;
    public int MlpHiddenWidth { get; set; } = 64;
    public int MlpColorLayers { get; set; } = 2;
    public int MlpColorWidth { get; set; } = 64;

    // Training parameters
    public float LearningRate { get; set; } = 1e-2f;
    public float LearningRateDecay { get; set; } = 0.1f;
    public int BatchSize { get; set; } = 4096;
    public int RaysPerBatch { get; set; } = 4096;
    public int SamplesPerRay { get; set; } = 64;
    public int ImportanceSamples { get; set; } = 64;

    // Loss weights
    public float ColorLossWeight { get; set; } = 1.0f;
    public float DistortionLossWeight { get; set; } = 0.01f;
    public float RegularizationWeight { get; set; } = 1e-5f;

    // Scene configuration
    public float NearPlane { get; set; } = 0.1f;
    public float FarPlane { get; set; } = 100.0f;
    public bool UseBackgroundModel { get; set; } = false;

    // Performance
    public bool UseGPU { get; set; } = true;
    public bool UseMixedPrecision { get; set; } = true;

    public NerfTrainingConfigDTO ToDTO()
    {
        return new NerfTrainingConfigDTO
        {
            HashGridLevels = HashGridLevels,
            HashGridFeatures = HashGridFeatures,
            HashTableSize = HashTableSize,
            HashGridMinResolution = HashGridMinResolution,
            HashGridMaxResolution = HashGridMaxResolution,
            MlpHiddenLayers = MlpHiddenLayers,
            MlpHiddenWidth = MlpHiddenWidth,
            MlpColorLayers = MlpColorLayers,
            MlpColorWidth = MlpColorWidth,
            LearningRate = LearningRate,
            LearningRateDecay = LearningRateDecay,
            BatchSize = BatchSize,
            RaysPerBatch = RaysPerBatch,
            SamplesPerRay = SamplesPerRay,
            ImportanceSamples = ImportanceSamples,
            ColorLossWeight = ColorLossWeight,
            DistortionLossWeight = DistortionLossWeight,
            RegularizationWeight = RegularizationWeight,
            NearPlane = NearPlane,
            FarPlane = FarPlane,
            UseBackgroundModel = UseBackgroundModel,
            UseGPU = UseGPU,
            UseMixedPrecision = UseMixedPrecision
        };
    }

    public static NerfTrainingConfig FromDTO(NerfTrainingConfigDTO dto)
    {
        if (dto == null) return new NerfTrainingConfig();

        return new NerfTrainingConfig
        {
            HashGridLevels = dto.HashGridLevels,
            HashGridFeatures = dto.HashGridFeatures,
            HashTableSize = dto.HashTableSize,
            HashGridMinResolution = dto.HashGridMinResolution,
            HashGridMaxResolution = dto.HashGridMaxResolution,
            MlpHiddenLayers = dto.MlpHiddenLayers,
            MlpHiddenWidth = dto.MlpHiddenWidth,
            MlpColorLayers = dto.MlpColorLayers,
            MlpColorWidth = dto.MlpColorWidth,
            LearningRate = dto.LearningRate,
            LearningRateDecay = dto.LearningRateDecay,
            BatchSize = dto.BatchSize,
            RaysPerBatch = dto.RaysPerBatch,
            SamplesPerRay = dto.SamplesPerRay,
            ImportanceSamples = dto.ImportanceSamples,
            ColorLossWeight = dto.ColorLossWeight,
            DistortionLossWeight = dto.DistortionLossWeight,
            RegularizationWeight = dto.RegularizationWeight,
            NearPlane = dto.NearPlane,
            FarPlane = dto.FarPlane,
            UseBackgroundModel = dto.UseBackgroundModel,
            UseGPU = dto.UseGPU,
            UseMixedPrecision = dto.UseMixedPrecision
        };
    }
}

/// <summary>
/// Training log entry for monitoring progress
/// </summary>
public class NerfTrainingLog
{
    public int Iteration { get; set; }
    public float Loss { get; set; }
    public float PSNR { get; set; }
    public float LearningRate { get; set; }
    public double ElapsedSeconds { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
