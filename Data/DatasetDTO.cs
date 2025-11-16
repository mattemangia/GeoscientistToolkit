// GeoscientistToolkit/Data/DatasetDTO.cs

using System.Numerics;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Business.GIS;

namespace GeoscientistToolkit.Data;

// Base DTO
public class DatasetDTO
{
    public string TypeName { get; set; }
    public string Name { get; set; }
    public string FilePath { get; set; }
    public DatasetMetadataDTO Metadata { get; set; } = new();
}

public class ProjectMetadataDTO
{
    public string Organisation { get; set; }
    public string Department { get; set; }
    public int? Year { get; set; }
    public string Expedition { get; set; }
    public string Author { get; set; }
    public string ProjectDescription { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string FundingSource { get; set; }
    public string License { get; set; }
    public Dictionary<string, string> CustomFields { get; set; } = new();
}

public class DatasetMetadataDTO
{
    public string SampleName { get; set; }
    public string LocationName { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public float? CoordinatesX { get; set; } // ADDED
    public float? CoordinatesY { get; set; } // ADDED
    public double? Depth { get; set; }
    public float? SizeX { get; set; }
    public float? SizeY { get; set; }
    public float? SizeZ { get; set; }
    public string SizeUnit { get; set; }
    public DateTime? CollectionDate { get; set; }
    public string Collector { get; set; }
    public string Notes { get; set; }
    public Dictionary<string, string> CustomFields { get; set; } = new();
}

public class TableDatasetDTO : DatasetDTO
{
    public string SourceFormat { get; set; }
    public string Delimiter { get; set; }
    public bool HasHeaders { get; set; }
    public string Encoding { get; set; }
    public int RowCount { get; set; }
    public int ColumnCount { get; set; }
    public List<string> ColumnNames { get; set; }
    public List<string> ColumnTypes { get; set; }
}

public class MaterialDTO
{
    public byte ID { get; set; }
    public string Name { get; set; }
    public Vector4 Color { get; set; }
    public byte MinValue { get; set; }
    public byte MaxValue { get; set; }
    public bool IsExterior { get; set; }
    public double Density { get; set; }
    public bool IsVisible { get; set; }
    public string PhysicalMaterialName { get; set; }
}

public class ImageDatasetDTO : DatasetDTO
{
    public float PixelSize { get; set; }
    public string Unit { get; set; }
    public long Tags { get; set; }
    public Dictionary<string, string> ImageMetadata { get; set; }
    public string SegmentationPath { get; set; }
}

public class CtImageStackDatasetDTO : DatasetDTO
{
    public float PixelSize { get; set; }
    public float SliceThickness { get; set; }
    public string Unit { get; set; }
    public int BinningSize { get; set; }
    public List<MaterialDTO> Materials { get; set; } = new();
    public ThermalResultsDTO ThermalResults { get; set; }
    public NMRResultsDTO NmrResults { get; set; }
}

public partial class DatasetGroupDTO : DatasetDTO
{
    public List<DatasetDTO> Datasets { get; set; } = new();
}

public class AcousticVolumeDatasetDTO : DatasetDTO
{
    public double PWaveVelocity { get; set; }
    public double SWaveVelocity { get; set; }
    public double VpVsRatio { get; set; }
    public int TimeSteps { get; set; }
    public double ComputationTimeSeconds { get; set; }
    public float YoungsModulusMPa { get; set; }
    public float PoissonRatio { get; set; }
    public float ConfiningPressureMPa { get; set; }
    public float SourceFrequencyKHz { get; set; }
    public float SourceEnergyJ { get; set; }
    public string SourceDatasetPath { get; set; }
    public string SourceMaterialName { get; set; }
    public bool HasTimeSeries { get; set; }
    public bool HasDamageField { get; set; }
    public bool HasCalibration { get; set; }
}

public class ProjectFileDTO
{
    public string ProjectName { get; set; }
    public List<DatasetDTO> Datasets { get; set; } = new();
    public ProjectMetadataDTO ProjectMetadata { get; set; } = new();
    public List<ChemicalCompoundDTO> CustomCompounds { get; set; } = new();
}

public class StreamingCtVolumeDatasetDTO : DatasetDTO
{
    public string PartnerFilePath { get; set; }
}

public class PoreDTO
{
    public int ID { get; set; }
    public Vector3 Position { get; set; }
    public float Area { get; set; }
    public float VolumeVoxels { get; set; }
    public float VolumePhysical { get; set; }
    public int Connections { get; set; }
    public float Radius { get; set; }
}

public class ThroatDTO
{
    public int ID { get; set; }
    public int Pore1ID { get; set; }
    public int Pore2ID { get; set; }
    public float Radius { get; set; }
}

public class PNMDatasetDTO : DatasetDTO
{
    public float VoxelSize { get; set; }
    public float Tortuosity { get; set; }
    public float DarcyPermeability { get; set; }
    public float NavierStokesPermeability { get; set; }
    public float LatticeBoltzmannPermeability { get; set; }
    public float BulkDiffusivity { get; set; }
    public float EffectiveDiffusivity { get; set; }
    public float FormationFactor { get; set; }
    public float TransportTortuosity { get; set; }
    public List<PoreDTO> Pores { get; set; } = new();
    public List<ThroatDTO> Throats { get; set; } = new();
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public int ImageDepth { get; set; }
}

public class MicroPoreNetworkDTO
{
    public int MacroPoreID { get; set; }
    public List<PoreDTO> MicroPores { get; set; } = new();
    public List<ThroatDTO> MicroThroats { get; set; } = new();
    public float MicroPorosity { get; set; }
    public float MicroPermeability { get; set; }
    public float MicroSurfaceArea { get; set; }
    public float MicroVolume { get; set; }
    public float SEMPixelSize { get; set; }
    public string SEMImagePath { get; set; }
    public Vector2 SEMImagePosition { get; set; }
}

public class DualPNMCouplingDTO
{
    public float TotalMicroPorosity { get; set; }
    public float EffectiveMacroPermeability { get; set; }
    public float EffectiveMicroPermeability { get; set; }
    public float CombinedPermeability { get; set; }
    public string CouplingMode { get; set; }
}

public class DualPNMDatasetDTO : PNMDatasetDTO
{
    public List<MicroPoreNetworkDTO> MicroNetworks { get; set; } = new();
    public DualPNMCouplingDTO Coupling { get; set; } = new();
    public string CTDatasetPath { get; set; }
    public List<string> SEMImagePaths { get; set; } = new();
}

public class ThermalResultsDTO
{
    public int TempField_W { get; set; }
    public int TempField_H { get; set; }
    public int TempField_D { get; set; }
    public float[] TemperatureFieldData { get; set; }
    public double EffectiveConductivity { get; set; }
    public Dictionary<byte, double> MaterialConductivities { get; set; }
    public Dictionary<string, double> AnalyticalEstimates { get; set; }
    public double ComputationTimeSeconds { get; set; }
    public int IterationsPerformed { get; set; }
    public double FinalError { get; set; }
}

public class NMRResultsDTO
{
    public double[] TimePoints { get; set; }
    public double[] Magnetization { get; set; }
    public double[] T2Histogram { get; set; }
    public double[] T2HistogramBins { get; set; }
    public double[] T1Histogram { get; set; }
    public double[] T1HistogramBins { get; set; }
    public int T1T2Map_T1Count { get; set; }
    public int T1T2Map_T2Count { get; set; }
    public double[] T1T2MapData { get; set; }
    public bool HasT1T2Data { get; set; }
    public double[] PoreSizes { get; set; }
    public double[] PoreSizeDistribution { get; set; }
    public double MeanT2 { get; set; }
    public double GeometricMeanT2 { get; set; }
    public double T2PeakValue { get; set; }
    public int NumberOfWalkers { get; set; }
    public int TotalSteps { get; set; }
    public double TimeStep { get; set; }
    public string PoreMaterial { get; set; }
    public Dictionary<string, double> MaterialRelaxivities { get; set; }
    public double ComputationTimeSeconds { get; set; }
    public string ComputationMethod { get; set; }
}

public class ChemicalCompoundDTO
{
    public string Name { get; set; } = "Unnamed";
    public string ChemicalFormula { get; set; } = "";
    public CompoundPhase Phase { get; set; } = CompoundPhase.Solid;
    public CrystalSystem? CrystalSystem { get; set; }
    public double? GibbsFreeEnergyFormation_kJ_mol { get; set; }
    public double? EnthalpyFormation_kJ_mol { get; set; }
    public double? Entropy_J_molK { get; set; }
    public double? HeatCapacity_J_molK { get; set; }
    public double? MolarVolume_cm3_mol { get; set; }
    public double? MolecularWeight_g_mol { get; set; }
    public double? Density_g_cm3 { get; set; }
    public double? LogKsp_25C { get; set; }
    public double? Solubility_g_100mL_25C { get; set; }
    public double? DissolutionEnthalpy_kJ_mol { get; set; }
    public double? ActivationEnergy_Dissolution_kJ_mol { get; set; }
    public double? ActivationEnergy_Precipitation_kJ_mol { get; set; }
    public double? RateConstant_Dissolution_mol_m2_s { get; set; }
    public double? RateConstant_Precipitation_mol_m2_s { get; set; }
    public double? ReactionOrder_Dissolution { get; set; }
    public double? SpecificSurfaceArea_m2_g { get; set; }
    public double[]? HeatCapacityPolynomial_a_b_c_d { get; set; }
    public double[]? TemperatureRange_K { get; set; }
    public int? IonicCharge { get; set; }
    public Dictionary<string, double>? ActivityCoefficientParams { get; set; }
    public double? IonicConductivity_S_cm2_mol { get; set; }
    public double? RefractiveIndex { get; set; }
    public double? MohsHardness { get; set; }
    public string Color { get; set; } = "";
    public string Cleavage { get; set; } = "";
    public List<string> Synonyms { get; set; } = new();
    public string Notes { get; set; } = "";
    public List<string> Sources { get; set; } = new();
    public Dictionary<string, double> CustomParams { get; set; } = new();
}

public class GISFeatureDTO
{
    public FeatureType Type { get; set; }
    public List<Vector2> Coordinates { get; set; }
    public Dictionary<string, object> Properties { get; set; }
    public string Id { get; set; }
    public GeologicalMapping.GeologicalFeatureType? GeologicalType { get; set; }
    public float? Strike { get; set; }
    public float? Dip { get; set; }
    public string DipDirection { get; set; }
    public float? Plunge { get; set; }
    public float? Trend { get; set; }
    public string FormationName { get; set; }
    public string BoreholeName { get; set; }
    public string LithologyCode { get; set; }
    public string AgeCode { get; set; }
    public string Description { get; set; }
    public float? Thickness { get; set; }
    public float? Displacement { get; set; }
    public string MovementSense { get; set; }
    public bool? IsInferred { get; set; }
    public bool? IsCovered { get; set; }
}

public class GISLayerDTO
{
    public string Name { get; set; }
    public string Type { get; set; }
    public bool IsVisible { get; set; }
    public bool IsEditable { get; set; }
    public Vector4 Color { get; set; }
    public List<GISFeatureDTO> Features { get; set; } = new();
}

// AGGIUNTO: DTO per TwoDGeology
/// <summary>
/// DTO for serializing TwoDGeologyDataset (2D geological profiles)
/// NOTE: The actual CrossSection data is stored/loaded separately via TwoDGeologySerializer
/// </summary>
public class TwoDGeologyDatasetDTO : DatasetDTO
{
    // Base properties from DatasetDTO are inherited (TypeName, Name, FilePath, Metadata)
    // The actual CrossSection data is stored/loaded separately via TwoDGeologySerializer
}
// DTO for SubsurfaceGISDataset
public class SubsurfaceVoxelDTO
{
    public Vector3 Position { get; set; }
    public string LithologyType { get; set; }
    public Dictionary<string, float> Parameters { get; set; } = new();
    public float Confidence { get; set; }
}

public class SubsurfaceLayerBoundaryDTO
{
    public string LayerName { get; set; }
    public List<Vector3> Points { get; set; } = new();
    public float[,] ElevationGrid { get; set; }
    public BoundingBoxDTO GridBounds { get; set; }
}

public class BoundingBoxDTO
{
    public Vector2 Min { get; set; }
    public Vector2 Max { get; set; }
}

public class SubsurfaceGISDatasetDTO : GISDatasetDTO
{
    public List<string> SourceBoreholeNames { get; set; } = new();
    public List<SubsurfaceVoxelDTO> VoxelGrid { get; set; } = new();
    public List<SubsurfaceLayerBoundaryDTO> LayerBoundaries { get; set; } = new();
    public Vector3 GridOrigin { get; set; }
    public Vector3 GridSize { get; set; }
    public Vector3 VoxelSize { get; set; }
    public int GridResolutionX { get; set; }
    public int GridResolutionY { get; set; }
    public int GridResolutionZ { get; set; }
    public float InterpolationRadius { get; set; }
    public int InterpolationMethod { get; set; }
    public float IDWPower { get; set; }
    public string HeightmapDatasetName { get; set; }
}

// Seismic line package DTO
public class SeismicLinePackageDTO
{
    public string Name { get; set; } = "";
    public int StartTrace { get; set; }
    public int EndTrace { get; set; }
    public bool IsVisible { get; set; } = true;
    public Vector4 Color { get; set; } = new Vector4(1, 1, 0, 1);
    public string Notes { get; set; } = "";
}

// Seismic dataset DTO
public class SeismicDatasetDTO : DatasetDTO
{
    // SEG-Y format information
    public int SampleFormat { get; set; } // 1=IBM float, 5=IEEE float, etc.
    public int NumTraces { get; set; }
    public int NumSamples { get; set; }
    public float SampleInterval { get; set; } // in microseconds
    public float TraceInterval { get; set; } // spacing between traces in meters

    // Survey information
    public string SurveyName { get; set; } = "";
    public string LineNumber { get; set; } = "";
    public int CoordinateUnits { get; set; } // 1=length, 2=arc seconds
    public int MeasurementSystem { get; set; } // 1=meters, 2=feet

    // Processing information
    public string ProcessingHistory { get; set; } = "";
    public bool IsStack { get; set; }
    public bool IsMigrated { get; set; }
    public string DataType { get; set; } = ""; // "amplitude", "velocity", "depth", etc.

    // Display settings
    public float GainValue { get; set; } = 1.0f;
    public int ColorMapIndex { get; set; } = 0;
    public bool ShowWiggleTrace { get; set; } = true;
    public bool ShowVariableArea { get; set; } = true;

    // Line packages for grouping traces
    public List<SeismicLinePackageDTO> LinePackages { get; set; } = new();

    // Statistics
    public float MinAmplitude { get; set; }
    public float MaxAmplitude { get; set; }
    public float RmsAmplitude { get; set; }
}

// PhysicoChem dataset DTO
public class PhysicoChemDatasetDTO : DatasetDTO
{
    public string Description { get; set; }
    public List<ReactorDomainDTO> Domains { get; set; } = new();
    public List<BoundaryConditionDTO> BoundaryConditions { get; set; } = new();
    public List<ForceFieldDTO> Forces { get; set; } = new();
    public List<NucleationSiteDTO> NucleationSites { get; set; } = new();
    public SimulationParametersDTO SimulationParams { get; set; } = new();
    public ParameterSweepConfigDTO ParameterSweep { get; set; }
    public GridMesh3DDTO GeneratedMesh { get; set; }
    public List<PhysicoChemStateDTO> ResultHistory { get; set; } = new();
    public bool CoupleWithGeothermal { get; set; }
    public string GeothermalDatasetPath { get; set; }
}

public class ReactorDomainDTO
{
    public string Name { get; set; }
    public ReactorGeometryDTO Geometry { get; set; }
    public MaterialPropertiesDTO Material { get; set; }
    public InitialConditionsDTO InitialConditions { get; set; }
    public string BooleanOperation { get; set; } // Serialize as string
    public bool IsActive { get; set; }
    public bool AllowInteraction { get; set; }
}

public class ReactorGeometryDTO
{
    public string GeometryType { get; set; } // Serialize enum as string
    public string InterpolationMode { get; set; }
    public Vector3 Center { get; set; }
    public Vector3 Dimensions { get; set; }
    public double Radius { get; set; }
    public double InnerRadius { get; set; }
    public double Height { get; set; }
    public List<Vector2> Profile2D { get; set; } = new();
    public double ExtrusionDepth { get; set; }
    public int RadialSegments { get; set; }
    public List<Vector3> CustomPoints { get; set; } = new();
    public string MeshFilePath { get; set; }
}

public class MaterialPropertiesDTO
{
    public double Porosity { get; set; }
    public double Permeability { get; set; }
    public double ThermalConductivity { get; set; }
    public double SpecificHeat { get; set; }
    public double Density { get; set; }
    public string MineralComposition { get; set; }
    public Dictionary<string, double> MineralFractions { get; set; } = new();
}

public class InitialConditionsDTO
{
    public double Temperature { get; set; }
    public double Pressure { get; set; }
    public Dictionary<string, double> Concentrations { get; set; } = new();
    public Vector3 InitialVelocity { get; set; }
    public double LiquidSaturation { get; set; }
    public string FluidType { get; set; }
}

public class BoundaryConditionDTO
{
    public string Name { get; set; }
    public string Type { get; set; } // Enum as string
    public string Location { get; set; } // Enum as string
    public string Variable { get; set; } // Enum as string
    public double Value { get; set; }
    public double FluxValue { get; set; }
    public bool IsTimeDependendent { get; set; }
    public string TimeExpression { get; set; }
    public string SpeciesName { get; set; }
    public Vector3 CustomRegionCenter { get; set; }
    public double CustomRegionRadius { get; set; }
    public bool IsActive { get; set; }
}

public class ForceFieldDTO
{
    public string Name { get; set; }
    public string Type { get; set; } // Enum as string
    public bool IsActive { get; set; }
    public Vector3 GravityVector { get; set; }
    public Vector3 VortexCenter { get; set; }
    public Vector3 VortexAxis { get; set; }
    public double VortexStrength { get; set; }
    public double VortexRadius { get; set; }
    public bool IsTimeDependendent { get; set; }
}

public class NucleationSiteDTO
{
    public string Name { get; set; }
    public Vector3 Position { get; set; }
    public string MineralType { get; set; }
    public double NucleationRate { get; set; }
    public double InitialRadius { get; set; }
    public double ActivationEnergy { get; set; }
    public double CriticalSupersaturation { get; set; }
    public bool IsActive { get; set; }
}

public class SimulationParametersDTO
{
    public double TotalTime { get; set; }
    public double TimeStep { get; set; }
    public double OutputInterval { get; set; }
    public bool EnableReactiveTransport { get; set; }
    public bool EnableHeatTransfer { get; set; }
    public bool EnableFlow { get; set; }
    public bool EnableForces { get; set; }
    public bool EnableNucleation { get; set; }
    public double ConvergenceTolerance { get; set; }
    public int MaxIterations { get; set; }
    public bool UseGPU { get; set; }
    public string SolverType { get; set; }
}

public class ParameterSweepConfigDTO
{
    public bool Enabled { get; set; }
    public string ParameterName { get; set; }
    public double MinValue { get; set; }
    public double MaxValue { get; set; }
    public int Steps { get; set; }
}

public class GridMesh3DDTO
{
    public Vector3Int GridSize { get; set; }
    public Vector3 Origin { get; set; }
    public Vector3 Spacing { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class Vector3Int
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}

public class PhysicoChemStateDTO
{
    public double CurrentTime { get; set; }
    // Note: Large 3D arrays are NOT serialized by default to keep file size manageable
    // Only final state statistics or compressed data should be saved
    public int GridSizeX { get; set; }
    public int GridSizeY { get; set; }
    public int GridSizeZ { get; set; }
    public float TemperatureAvg { get; set; }
    public float PressureAvg { get; set; }
    public int ActiveNucleiCount { get; set; }
    public List<NucleusDTO> ActiveNuclei { get; set; } = new();
}

public class NucleusDTO
{
    public int Id { get; set; }
    public Vector3 Position { get; set; }
    public double Radius { get; set; }
    public string MineralType { get; set; }
    public double GrowthRate { get; set; }
    public double BirthTime { get; set; }
}

// Media dataset DTOs
public class VideoDatasetDTO : DatasetDTO
{
    public int Width { get; set; }
    public int Height { get; set; }
    public double DurationSeconds { get; set; }
    public double FrameRate { get; set; }
    public int TotalFrames { get; set; }
    public string Codec { get; set; }
    public string Format { get; set; }
    public long BitRate { get; set; }
    public Dictionary<string, string> VideoMetadata { get; set; } = new();
    public bool HasAudioTrack { get; set; }
    public int AudioChannels { get; set; }
    public int AudioSampleRate { get; set; }
    public string AudioCodec { get; set; }
}

public class AudioDatasetDTO : DatasetDTO
{
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public int BitsPerSample { get; set; }
    public double DurationSeconds { get; set; }
    public long TotalSamples { get; set; }
    public string Format { get; set; }
    public string Encoding { get; set; }
    public long BitRate { get; set; }
    public Dictionary<string, string> AudioMetadata { get; set; } = new();
}

public class TextDatasetDTO : DatasetDTO
{
    public string Format { get; set; } // "txt" or "rtf"
    public int LineCount { get; set; }
    public int CharacterCount { get; set; }
    public int WordCount { get; set; }
    public string GeneratedBy { get; set; }
    public DateTime? GeneratedDate { get; set; }
    public bool IsGeneratedReport { get; set; }
}
