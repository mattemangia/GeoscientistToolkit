// GeoscientistToolkit/Analysis/AcousticSimulation/SimulationParameters.cs
using System.Numerics;

namespace GeoscientistToolkit.Analysis.AcousticSimulation
{
    /// <summary>
    /// A comprehensive data structure to hold all parameters for an acoustic simulation run.
    /// </summary>
    public class SimulationParameters
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int Depth { get; set; }
        public float PixelSize { get; set; }
        public byte SelectedMaterialID { get; set; }
        public int Axis { get; set; }
        public bool UseFullFaceTransducers { get; set; }
        public float ConfiningPressureMPa { get; set; }
        public float FailureAngleDeg { get; set; }
        public float CohesionMPa { get; set; }
        public float SourceEnergyJ { get; set; }
        public float SourceFrequencyKHz { get; set; }
        public int SourceAmplitude { get; set; }
        public int TimeSteps { get; set; }
        public float YoungsModulusMPa { get; set; }
        public float PoissonRatio { get; set; }
        public bool UseElasticModel { get; set; }
        public bool UsePlasticModel { get; set; }
        public bool UseBrittleModel { get; set; }
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
    }
}