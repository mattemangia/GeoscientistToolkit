// GeoscientistToolkit/Analysis/AcousticSimulation/SimulationParameters.cs

using System.Numerics;

namespace GeoscientistToolkit.Analysis.AcousticSimulation;

public struct BoundingBox
{
    public (int X, int Y, int Z) Min;
    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }
    public (int X, int Y, int Z) Max => (Min.X + Width - 1, Min.Y + Height - 1, Min.Z + Depth - 1);

    public BoundingBox(int minX, int minY, int minZ, int width, int height, int depth)
    {
        Min = (minX, minY, minZ);
        Width = width;
        Height = height;
        Depth = depth;
    }

    public override string ToString()
    {
        return $"Min({Min.X},{Min.Y},{Min.Z}), Size({Width},{Height},{Depth})";
    }
}

public class SimulationParameters
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int Depth { get; set; }
    public BoundingBox? SimulationExtent { get; set; } = null;
    public float PixelSize { get; set; }

    // Legacy single material support (kept for backwards compatibility)
    public byte SelectedMaterialID { get; set; }

    // NEW: Multi-material support
    public HashSet<byte> SelectedMaterialIDs { get; set; } = new();

    public int Axis { get; set; }
    public bool UseFullFaceTransducers { get; set; }
    public float ConfiningPressureMPa { get; set; }
    public float FailureAngleDeg { get; set; }
    public float CohesionMPa { get; set; }
    public float SourceEnergyJ { get; set; }
    public float SourceFrequencyKHz { get; set; }
    public int SourceAmplitude { get; set; }
    public int TimeSteps { get; set; }

    public float ArtificialDampingFactor { get; set; } = 0.2f;
    public float YoungsModulusMPa { get; set; }
    public float PoissonRatio { get; set; }
    public bool UseElasticModel { get; set; }
    public bool UsePlasticModel { get; set; }
    public bool UseBrittleModel { get; set; }
    public float PlasticHardeningModulusMPa { get; set; } = 0f;
    public bool UseDynamicTensileStrength { get; set; } = true;
    public float TensileStrengthFactor { get; set; } = 1e-4f;
    public float MinTensileStrengthMPa { get; set; } = 0.5f;
    public float MaxTensileStrengthMPa { get; set; } = 20f;
    public bool UseGPU { get; set; }
    public bool UseRickerWavelet { get; set; }
    public Vector3 TxPosition { get; set; }
    public Vector3 RxPosition { get; set; }
    public bool EnableRealTimeVisualization { get; set; }
    public bool SaveTimeSeries { get; set; }
    public int SnapshotInterval { get; set; }
    public bool UseChunkedProcessing { get; set; }
    public int ChunkSizeMB { get; set; }
    public bool EnableOffloading { get; set; }
    public string OffloadDirectory { get; set; }
    public float TimeStepSeconds { get; set; } = 1e-6f;

    // Helper method to check if a material is selected
    public bool IsMaterialSelected(byte materialId)
    {
        // If multi-material set is populated, use it
        if (SelectedMaterialIDs.Count > 0)
            return SelectedMaterialIDs.Contains(materialId);

        // Otherwise fall back to legacy single material
        return materialId == SelectedMaterialID;
    }

    // Helper to create GPU lookup table (256 bytes, one per possible material ID)
    public byte[] CreateMaterialLookupTable()
    {
        var lookup = new byte[256];

        if (SelectedMaterialIDs.Count > 0)
            foreach (var id in SelectedMaterialIDs)
                lookup[id] = 1;
        else
            lookup[SelectedMaterialID] = 1;

        return lookup;
    }
}
