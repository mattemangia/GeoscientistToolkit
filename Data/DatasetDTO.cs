// GeoscientistToolkit/Data/DatasetDTO.cs

using System.Numerics;
using GeoscientistToolkit.Data.Materials;
// ADDED: For enums used in ChemicalCompoundDTO

// Added for DateTime

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

    // NEW: Properties for storing simulation results
    public ThermalResultsDTO ThermalResults { get; set; }
    public NMRResultsDTO NmrResults { get; set; }
}

public class DatasetGroupDTO : DatasetDTO
{
    public List<DatasetDTO> Datasets { get; set; } = new();
}

// --- MODIFIED ---
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
    public bool HasDamageField { get; set; } // ADDED
    public bool HasCalibration { get; set; } // ADDED
}

public class ProjectFileDTO
{
    public string ProjectName { get; set; }
    public List<DatasetDTO> Datasets { get; set; } = new();

    public ProjectMetadataDTO ProjectMetadata { get; set; } = new();

    // ADDED: To store user-defined compounds in the project file.
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
    public List<PoreDTO> Pores { get; set; } = new();
    public List<ThroatDTO> Throats { get; set; } = new();
}

// --- NEW DTOS FOR SIMULATION RESULTS ---

/// <summary>
///     DTO for serializing ThermalResults.
/// </summary>
public class ThermalResultsDTO
{
    // For flattened 3D temperature field
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

/// <summary>
///     DTO for serializing NMRResults.
/// </summary>
public class NMRResultsDTO
{
    public double[] TimePoints { get; set; }
    public double[] Magnetization { get; set; }
    public double[] T2Histogram { get; set; }
    public double[] T2HistogramBins { get; set; }
    public double[] T1Histogram { get; set; }
    public double[] T1HistogramBins { get; set; }

    // For flattened 2D T1T2Map
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

// --- NEW DTO FOR CHEMICAL COMPOUNDS ---

/// <summary>
///     DTO for serializing a user-defined ChemicalCompound.
/// </summary>
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