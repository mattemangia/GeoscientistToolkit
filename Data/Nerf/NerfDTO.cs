// GeoscientistToolkit/Data/Nerf/NerfDTO.cs

using System.Numerics;

namespace GeoscientistToolkit.Data.Nerf;

/// <summary>
/// DTO for NerfDataset serialization.
/// </summary>
public class NerfDatasetDTO : DatasetDTO
{
    // Training state
    public string TrainingState { get; set; }
    public int CurrentIteration { get; set; }
    public int TotalIterations { get; set; }
    public float CurrentLoss { get; set; }
    public float CurrentPSNR { get; set; }

    // Scene bounds
    public Vector3 SceneCenter { get; set; }
    public float SceneRadius { get; set; }
    public Vector3 BoundingBoxMin { get; set; }
    public Vector3 BoundingBoxMax { get; set; }

    // Source info
    public string SourceType { get; set; }
    public string SourcePath { get; set; }
    public string VideoPath { get; set; }

    // Timestamps
    public DateTime? TrainingStartTime { get; set; }
    public DateTime? TrainingEndTime { get; set; }

    // Training configuration
    public NerfTrainingConfigDTO TrainingConfig { get; set; }

    // Image frames
    public List<NerfImageFrameDTO> ImageFrames { get; set; } = new();
}

/// <summary>
/// DTO for NerfImageFrame serialization.
/// </summary>
public class NerfImageFrameDTO
{
    public Guid Id { get; set; }
    public string SourcePath { get; set; }
    public int FrameIndex { get; set; }
    public double TimestampSeconds { get; set; }

    // Image dimensions (not the actual data)
    public int Width { get; set; }
    public int Height { get; set; }
    public int Channels { get; set; }

    // Camera intrinsics
    public float FocalLengthX { get; set; }
    public float FocalLengthY { get; set; }
    public float PrincipalPointX { get; set; }
    public float PrincipalPointY { get; set; }

    // Camera pose
    public Matrix4x4 CameraToWorld { get; set; }

    // Pose status
    public string PoseStatus { get; set; }
    public float PoseConfidence { get; set; }

    // Flags
    public bool IsEnabled { get; set; }
    public bool IsKeyframe { get; set; }
}

/// <summary>
/// DTO for NerfTrainingConfig serialization.
/// </summary>
public class NerfTrainingConfigDTO
{
    // Hash grid
    public int HashGridLevels { get; set; }
    public int HashGridFeatures { get; set; }
    public int HashTableSize { get; set; }
    public float HashGridMinResolution { get; set; }
    public float HashGridMaxResolution { get; set; }

    // MLP
    public int MlpHiddenLayers { get; set; }
    public int MlpHiddenWidth { get; set; }
    public int MlpColorLayers { get; set; }
    public int MlpColorWidth { get; set; }

    // Training
    public float LearningRate { get; set; }
    public float LearningRateDecay { get; set; }
    public int BatchSize { get; set; }
    public int RaysPerBatch { get; set; }
    public int SamplesPerRay { get; set; }
    public int ImportanceSamples { get; set; }

    // Loss
    public float ColorLossWeight { get; set; }
    public float DistortionLossWeight { get; set; }
    public float RegularizationWeight { get; set; }

    // Scene
    public float NearPlane { get; set; }
    public float FarPlane { get; set; }
    public bool UseBackgroundModel { get; set; }

    // Performance
    public bool UseGPU { get; set; }
    public bool UseMixedPrecision { get; set; }
}

/// <summary>
/// DTO for sparse point serialization.
/// </summary>
public class SparsePoint3DDTO
{
    public Vector3 Position { get; set; }
    public Vector3 Color { get; set; }
    public float Error { get; set; }
}

/// <summary>
/// DTO for training log entries.
/// </summary>
public class NerfTrainingLogDTO
{
    public int Iteration { get; set; }
    public float Loss { get; set; }
    public float PSNR { get; set; }
    public float LearningRate { get; set; }
    public double ElapsedSeconds { get; set; }
    public DateTime Timestamp { get; set; }
}
