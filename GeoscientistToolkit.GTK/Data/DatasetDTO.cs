// GeoscientistToolkit/Data/DatasetDTO.cs
// Stripped for GTK - Added MaterialDTO, NMRResultsDTO, ThermalResultsDTO with fields
// Added Borehole DTOs

using System;
using System.Collections.Generic;
using System.Numerics;
using Newtonsoft.Json;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Data.PhysicoChem;
using GeoscientistToolkit.Data.Borehole; // For Enums like ContactType

namespace GeoscientistToolkit.Data;

public class DatasetDTO
{
    public string TypeName { get; set; }
    public string Name { get; set; }
    public string FilePath { get; set; }
    public string Description { get; set; }
    public DatasetMetadataDTO Metadata { get; set; }
}

public class DatasetMetadataDTO
{
    public string SampleName { get; set; }
    public string LocationName { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public float? CoordinatesX { get; set; }
    public float? CoordinatesY { get; set; }
    public float Depth { get; set; }
    public float? SizeX { get; set; }
    public float? SizeY { get; set; }
    public float? SizeZ { get; set; }
    public string SizeUnit { get; set; }
    public DateTime CollectionDate { get; set; }
    public string Collector { get; set; }
    public string Notes { get; set; }
    public Dictionary<string, string> CustomFields { get; set; }
}

public class PhysicoChemDatasetDTO : DatasetDTO
{
    public bool CoupleWithGeothermal { get; set; }
    public string GeothermalDatasetPath { get; set; }
    public List<BoundaryConditionDTO> BoundaryConditions { get; set; } = new();
    public List<ForceFieldDTO> Forces { get; set; } = new();
    public List<NucleationSiteDTO> NucleationSites { get; set; } = new();
    public SimulationParametersDTO SimulationParams { get; set; }
    public PhysicoChemMesh Mesh { get; set; }
    public List<PhysicoChemStateDTO> ResultHistory { get; set; } = new();
}

public class BoundaryConditionDTO
{
    public string Name { get; set; }
    public string Type { get; set; }
    public string Location { get; set; }
    public string Variable { get; set; }
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
    public string Type { get; set; }
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

public class PhysicoChemStateDTO
{
    public double CurrentTime { get; set; }
    public int GridSizeX { get; set; }
    public int GridSizeY { get; set; }
    public int GridSizeZ { get; set; }
    public double TemperatureAvg { get; set; }
    public double PressureAvg { get; set; }
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

public class CtImageStackDatasetDTO : DatasetDTO
{
    public string VoxelUnit { get; set; }
    public double VoxelSizeX { get; set; }
    public double VoxelSizeY { get; set; }
    public double VoxelSizeZ { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Depth { get; set; }
    public List<MaterialDTO> Materials { get; set; }
    public NMRResultsDTO NmrResults { get; set; }
    public ThermalResultsDTO ThermalResults { get; set; }
}

public class MaterialDTO
{
    public int MaterialID { get; set; }
    public int ID { get; set; } // Alias for MaterialID?
    public string Name { get; set; }
    public Vector4 Color { get; set; }
    public bool IsVisible { get; set; }
    public long VoxelCount { get; set; }
    public double VolumePercentage { get; set; }
    public double PhysicalVolume { get; set; }

    // Properties for physical simulation
    public double Density { get; set; }
    public double Porosity { get; set; }
    public double Permeability { get; set; }
    public double ThermalConductivity { get; set; }
    public double SpecificHeat { get; set; }

    // Missing fields
    public int MinValue { get; set; }
    public int MaxValue { get; set; }
    public bool IsExterior { get; set; }
    public string PhysicalMaterialName { get; set; }
}

public class NMRResultsDTO
{
    public double[] T2Histogram { get; set; }
    public double[] T2Bins { get; set; }
    public double TotalPorosity { get; set; }
    public double Permeability { get; set; }
    public double BoundFluidVolume { get; set; }
    public double FreeFluidVolume { get; set; }

    // Missing fields
    public double[] T1Histogram { get; set; }
    public double[] T1HistogramBins { get; set; }
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
    public string MaterialRelaxivities { get; set; }
    public double ComputationTimeSeconds { get; set; }
    public string ComputationMethod { get; set; }
    public int T1T2Map_T1Count { get; set; }
    public int T1T2Map_T2Count { get; set; }
    public double[] T1T2MapData { get; set; }
}

public class ThermalResultsDTO
{
    public double EffectiveConductivity { get; set; }
    public double Anisotropy { get; set; }
    public double[] TemperatureProfile { get; set; }

    // Missing fields
    public Dictionary<string, double> MaterialConductivities { get; set; }
    public Dictionary<string, double> AnalyticalEstimates { get; set; }
    public double ComputationTimeSeconds { get; set; }
    public int IterationsPerformed { get; set; }
    public double FinalError { get; set; }
    public int TempField_W { get; set; }
    public int TempField_H { get; set; }
    public int TempField_D { get; set; }
    public double[] TemperatureFieldData { get; set; }
}

public class TableDatasetDTO : DatasetDTO
{
    // Stub for TableDataset compilation
    public List<string> Headers { get; set; }
    public List<List<object>> Rows { get; set; }
}

// Borehole DTOs
public class BoreholeDatasetDTO : DatasetDTO
{
    public string WellName { get; set; }
    public string Field { get; set; }
    public float TotalDepth { get; set; }
    public float WellDiameter { get; set; }
    public Vector2 SurfaceCoordinates { get; set; }
    public float Elevation { get; set; }
    public float DepthScaleFactor { get; set; }
    public bool ShowGrid { get; set; }
    public bool ShowLegend { get; set; }
    public float TrackWidth { get; set; }
    public List<LithologyUnitDTO> LithologyUnits { get; set; }
    public Dictionary<string, ParameterTrackDTO> ParameterTracks { get; set; }
}

public class LithologyUnitDTO
{
    public string ID { get; set; }
    public string Name { get; set; }
    public string LithologyType { get; set; }
    public float DepthFrom { get; set; }
    public float DepthTo { get; set; }
    public Vector4 Color { get; set; }
    public string Description { get; set; }
    public string GrainSize { get; set; }
    public ContactType UpperContactType { get; set; }
    public ContactType LowerContactType { get; set; }
    public Dictionary<string, float> Parameters { get; set; }
    public Dictionary<string, ParameterSource> ParameterSources { get; set; }
}

public class ParameterTrackDTO
{
    public string Name { get; set; }
    public string Unit { get; set; }
    public float MinValue { get; set; }
    public float MaxValue { get; set; }
    public bool IsLogarithmic { get; set; }
    public Vector4 Color { get; set; }
    public bool IsVisible { get; set; }
    public List<ParameterPoint> Points { get; set; }
}
