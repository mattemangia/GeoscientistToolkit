// GeoscientistToolkit/Data/PhysicoChem/PhysicoChemDataset.cs
//
// PHYSICOCHEM Dataset: Multiphysics reactor simulation with TOUGH-like capabilities
// Supports 2D-to-3D geometry generation, multiple domains, boundary conditions,
// forces, nucleation, and reactive transport coupling

using System;
using System.Collections.Generic;
using System.Linq;
using GeoscientistToolkit.Data.Mesh3D;
using Newtonsoft.Json;

namespace GeoscientistToolkit.Data.PhysicoChem;

/// <summary>
/// Main dataset for physicochemical reactor simulations.
/// Combines geometry, materials, boundary conditions, initial conditions,
/// and simulation parameters for TOUGH-like multiphysics experiments.
/// </summary>
public class PhysicoChemDataset : Dataset, ISerializableDataset
{
    [JsonProperty]
    public string Description { get; set; }

    [JsonProperty]
    public PhysicoChemMesh Mesh { get; set; } = new();

    [JsonProperty]
    public List<MaterialProperties> Materials { get; set; } = new();

    [JsonProperty]
    public List<BoundaryCondition> BoundaryConditions { get; set; } = new();

    [JsonProperty]
    public List<ForceField> Forces { get; set; } = new();

    [JsonProperty]
    public List<NucleationSite> NucleationSites { get; set; } = new();

    [JsonProperty]
    public SimulationParameters SimulationParams { get; set; } = new();

    [JsonProperty]
    public ParameterSweepConfig ParameterSweep { get; set; }

    [JsonProperty]
    public ParameterSweepManager ParameterSweepManager { get; set; } = new();

    [JsonProperty]
    public SimulationTrackingManager TrackingManager { get; set; } = new();

    /// <summary>
    /// Generated 3D mesh from domains
    /// </summary>
    [JsonIgnore]
    public GridMesh3D GeneratedMesh { get; set; }

    /// <summary>
    /// Current simulation state
    /// </summary>
    [JsonIgnore]
    public PhysicoChemState CurrentState { get; set; }

    /// <summary>
    /// Simulation results history
    /// </summary>
    [JsonIgnore]
    public List<PhysicoChemState> ResultHistory { get; set; } = new();

    /// <summary>
    /// Flag to enable coupling with geothermal simulation
    /// </summary>
    [JsonProperty]
    public bool CoupleWithGeothermal { get; set; } = false;

    [JsonProperty]
    public string GeothermalDatasetPath { get; set; }

    public PhysicoChemDataset(string name, string description = "") : base(name, "")
    {
        Type = DatasetType.Group; // Use Group type for now, can add PhysicoChem type later
        Description = description;
    }



    /// <summary>
    /// Generate 3D mesh from all domains
    /// </summary>
    public void GenerateMesh(int resolution = 50)
    {
        var meshGenerator = new ReactorMeshGenerator();
        GeneratedMesh = meshGenerator.GenerateMeshFromDomains(new List<ReactorDomain>(), resolution);
    }

    /// <summary>
    /// Initialize simulation state
    /// </summary>
    public void InitializeState()
    {
        if (GeneratedMesh == null)
            GenerateMesh();

        CurrentState = new PhysicoChemState(GeneratedMesh.GridSize);

        // Apply initial conditions from domains
        ApplyInitialConditions();
    }

    private void ApplyInitialConditions()
    {
        foreach (var cell in Mesh.Cells.Values)
        {
            if (cell.InitialConditions == null) continue;

            // Apply ICs to the cell
            // (Further implementation will be needed in the solver)
        }
    }

    /// <summary>
    /// Validate dataset consistency
    /// </summary>
    public List<string> Validate()
    {
        var errors = new List<string>();

        if (Mesh == null || Mesh.Cells.Count == 0)
            errors.Add("Mesh has not been generated or is empty");

        return errors;
    }

    /// <summary>
    /// Get estimated memory size of dataset
    /// </summary>
    public override long GetSizeInBytes()
    {
        long size = 0;

        // Estimate mesh size if generated
        if (GeneratedMesh != null)
        {
            var gridSize = GeneratedMesh.GridSize;
            var totalCells = gridSize.X * gridSize.Y * gridSize.Z;

            // Each cell has: temperature, pressure, velocity (3D), concentration fields
            // Assuming ~10 fields per cell, 4 bytes per float
            size += totalCells * 10 * sizeof(float);
        }

        // Estimate result history size
        if (ResultHistory != null && ResultHistory.Count > 0)
        {
            var gridSize = ResultHistory[0].Temperature.GetLength(0) *
                          ResultHistory[0].Temperature.GetLength(1) *
                          ResultHistory[0].Temperature.GetLength(2);
            size += ResultHistory.Count * gridSize * 10 * sizeof(float);
        }

        return size;
    }

    /// <summary>
    /// Load/initialize the dataset
    /// </summary>
    public override void Load()
    {
        // For PHYSICOCHEM datasets, most initialization happens in InitializeState()
        // This method is called when the dataset is added to a project
        // No file loading needed as this is a computational dataset
    }

    /// <summary>
    /// Unload dataset and free memory
    /// </summary>
    public override void Unload()
    {
        // Clear simulation state and history to free memory
        CurrentState = null;
        ResultHistory?.Clear();
        GeneratedMesh = null;
    }

    /// <summary>
    /// Serialize dataset to DTO for saving
    /// </summary>
    public object ToSerializableObject()
    {
        var dto = new PhysicoChemDatasetDTO
        {
            TypeName = nameof(PhysicoChemDataset),
            Name = Name,
            FilePath = FilePath,
            Description = Description,
            CoupleWithGeothermal = CoupleWithGeothermal,
            GeothermalDatasetPath = GeothermalDatasetPath
        };


        // Serialize boundary conditions
        foreach (var bc in BoundaryConditions)
        {
            dto.BoundaryConditions.Add(new BoundaryConditionDTO
            {
                Name = bc.Name,
                Type = bc.Type.ToString(),
                Location = bc.Location.ToString(),
                Variable = bc.Variable.ToString(),
                Value = bc.Value,
                FluxValue = bc.FluxValue,
                IsTimeDependendent = bc.IsTimeDependendent,
                TimeExpression = bc.TimeExpression,
                SpeciesName = bc.SpeciesName,
                CustomRegionCenter = new System.Numerics.Vector3((float)bc.CustomRegionCenter.X, (float)bc.CustomRegionCenter.Y, (float)bc.CustomRegionCenter.Z),
                CustomRegionRadius = bc.CustomRegionRadius,
                IsActive = bc.IsActive
            });
        }

        // Serialize forces
        foreach (var force in Forces)
        {
            dto.Forces.Add(new ForceFieldDTO
            {
                Name = force.Name,
                Type = force.Type.ToString(),
                IsActive = force.IsActive,
                GravityVector = new System.Numerics.Vector3((float)force.GravityVector.X, (float)force.GravityVector.Y, (float)force.GravityVector.Z),
                VortexCenter = new System.Numerics.Vector3((float)force.VortexCenter.X, (float)force.VortexCenter.Y, (float)force.VortexCenter.Z),
                VortexAxis = new System.Numerics.Vector3((float)force.VortexAxis.X, (float)force.VortexAxis.Y, (float)force.VortexAxis.Z),
                VortexStrength = force.VortexStrength,
                VortexRadius = force.VortexRadius,
                IsTimeDependendent = force.IsTimeDependendent
            });
        }

        // Serialize nucleation sites
        foreach (var site in NucleationSites)
        {
            dto.NucleationSites.Add(new NucleationSiteDTO
            {
                Name = site.Name,
                Position = new System.Numerics.Vector3((float)site.Position.X, (float)site.Position.Y, (float)site.Position.Z),
                MineralType = site.MineralType,
                NucleationRate = site.NucleationRate,
                InitialRadius = site.InitialRadius,
                ActivationEnergy = site.ActivationEnergy,
                CriticalSupersaturation = site.CriticalSupersaturation,
                IsActive = site.IsActive
            });
        }

        // Serialize simulation parameters
        dto.SimulationParams = new SimulationParametersDTO
        {
            TotalTime = SimulationParams.TotalTime,
            TimeStep = SimulationParams.TimeStep,
            OutputInterval = SimulationParams.OutputInterval,
            EnableReactiveTransport = SimulationParams.EnableReactiveTransport,
            EnableHeatTransfer = SimulationParams.EnableHeatTransfer,
            EnableFlow = SimulationParams.EnableFlow,
            EnableForces = SimulationParams.EnableForces,
            EnableNucleation = SimulationParams.EnableNucleation,
            ConvergenceTolerance = SimulationParams.ConvergenceTolerance,
            MaxIterations = SimulationParams.MaxIterations,
            UseGPU = SimulationParams.UseGPU,
            SolverType = SimulationParams.SolverType
        };

        dto.Mesh = Mesh;

        // Serialize result history (lightweight version - only statistics, not full 3D arrays)
        if (ResultHistory != null && ResultHistory.Count > 0)
        {
            foreach (var state in ResultHistory)
            {
                var stateDto = new PhysicoChemStateDTO
                {
                    CurrentTime = state.CurrentTime,
                    GridSizeX = state.Temperature.GetLength(0),
                    GridSizeY = state.Temperature.GetLength(1),
                    GridSizeZ = state.Temperature.GetLength(2),
                    TemperatureAvg = CalculateAverage(state.Temperature),
                    PressureAvg = CalculateAverage(state.Pressure),
                    ActiveNucleiCount = state.ActiveNuclei.Count
                };

                // Serialize active nuclei
                foreach (var nucleus in state.ActiveNuclei)
                {
                    stateDto.ActiveNuclei.Add(new NucleusDTO
                    {
                        Id = nucleus.Id,
                        Position = new System.Numerics.Vector3((float)nucleus.Position.X, (float)nucleus.Position.Y, (float)nucleus.Position.Z),
                        Radius = nucleus.Radius,
                        MineralType = nucleus.MineralType,
                        GrowthRate = nucleus.GrowthRate,
                        BirthTime = nucleus.BirthTime
                    });
                }

                dto.ResultHistory.Add(stateDto);
            }
        }

        return dto;
    }

    /// <summary>
    /// Import dataset from DTO
    /// </summary>
    public void ImportFromDTO(PhysicoChemDatasetDTO dto)
    {
        Description = dto.Description;
        CoupleWithGeothermal = dto.CoupleWithGeothermal;
        GeothermalDatasetPath = dto.GeothermalDatasetPath;


        // Import boundary conditions
        BoundaryConditions.Clear();
        foreach (var bcDto in dto.BoundaryConditions)
        {
            var bc = new BoundaryCondition
            {
                Name = bcDto.Name,
                Type = Enum.Parse<BoundaryType>(bcDto.Type),
                Location = Enum.Parse<BoundaryLocation>(bcDto.Location),
                Variable = Enum.Parse<BoundaryVariable>(bcDto.Variable),
                Value = bcDto.Value,
                FluxValue = bcDto.FluxValue,
                IsTimeDependendent = bcDto.IsTimeDependendent,
                TimeExpression = bcDto.TimeExpression,
                SpeciesName = bcDto.SpeciesName,
                CustomRegionCenter = (bcDto.CustomRegionCenter.X, bcDto.CustomRegionCenter.Y, bcDto.CustomRegionCenter.Z),
                CustomRegionRadius = bcDto.CustomRegionRadius,
                IsActive = bcDto.IsActive
            };
            BoundaryConditions.Add(bc);
        }

        // Import forces
        Forces.Clear();
        foreach (var forceDto in dto.Forces)
        {
            var force = new ForceField
            {
                Name = forceDto.Name,
                Type = Enum.Parse<ForceType>(forceDto.Type),
                IsActive = forceDto.IsActive,
                GravityVector = (forceDto.GravityVector.X, forceDto.GravityVector.Y, forceDto.GravityVector.Z),
                VortexCenter = (forceDto.VortexCenter.X, forceDto.VortexCenter.Y, forceDto.VortexCenter.Z),
                VortexAxis = (forceDto.VortexAxis.X, forceDto.VortexAxis.Y, forceDto.VortexAxis.Z),
                VortexStrength = forceDto.VortexStrength,
                VortexRadius = forceDto.VortexRadius,
                IsTimeDependendent = forceDto.IsTimeDependendent
            };
            Forces.Add(force);
        }

        // Import nucleation sites
        NucleationSites.Clear();
        foreach (var siteDto in dto.NucleationSites)
        {
            var site = new NucleationSite
            {
                Name = siteDto.Name,
                Position = (siteDto.Position.X, siteDto.Position.Y, siteDto.Position.Z),
                MineralType = siteDto.MineralType,
                NucleationRate = siteDto.NucleationRate,
                InitialRadius = siteDto.InitialRadius,
                ActivationEnergy = siteDto.ActivationEnergy,
                CriticalSupersaturation = siteDto.CriticalSupersaturation,
                IsActive = siteDto.IsActive
            };
            NucleationSites.Add(site);
        }

        // Import simulation parameters
        SimulationParams.TotalTime = dto.SimulationParams.TotalTime;
        SimulationParams.TimeStep = dto.SimulationParams.TimeStep;
        SimulationParams.OutputInterval = dto.SimulationParams.OutputInterval;
        SimulationParams.EnableReactiveTransport = dto.SimulationParams.EnableReactiveTransport;
        SimulationParams.EnableHeatTransfer = dto.SimulationParams.EnableHeatTransfer;
        SimulationParams.EnableFlow = dto.SimulationParams.EnableFlow;
        SimulationParams.EnableForces = dto.SimulationParams.EnableForces;
        SimulationParams.EnableNucleation = dto.SimulationParams.EnableNucleation;
        SimulationParams.ConvergenceTolerance = dto.SimulationParams.ConvergenceTolerance;
        SimulationParams.MaxIterations = dto.SimulationParams.MaxIterations;
        SimulationParams.UseGPU = dto.SimulationParams.UseGPU;
        SimulationParams.SolverType = dto.SimulationParams.SolverType;

        Mesh = dto.Mesh;

        // Note: Full result history with 3D arrays is NOT imported
        // Only statistics are available from the DTO
    }

    private static float CalculateAverage(float[,,] field)
    {
        int nx = field.GetLength(0);
        int ny = field.GetLength(1);
        int nz = field.GetLength(2);

        float sum = 0;
        int count = 0;

        for (int i = 0; i < nx; i++)
        for (int j = 0; j < ny; j++)
        for (int k = 0; k < nz; k++)
        {
            sum += field[i, j, k];
            count++;
        }

        return count > 0 ? sum / count : 0;
    }
}

/// <summary>
/// Simulation parameters for PhysicoChem experiments
/// </summary>
public class SimulationParameters
{
    [JsonProperty]
    public SimulationMode Mode { get; set; } = SimulationMode.TimeBased;

    [JsonProperty]
    public double TotalTime { get; set; } = 3600.0; // seconds (for time-based mode)

    [JsonProperty]
    public int MaxSteps { get; set; } = 1000; // maximum steps (for step-based mode)

    [JsonProperty]
    public double TimeStep { get; set; } = 1.0; // seconds

    [JsonProperty]
    public double OutputInterval { get; set; } = 60.0; // seconds

    [JsonProperty]
    public int OutputEveryNSteps { get; set; } = 10; // for step-based mode

    [JsonProperty]
    public bool EnableReactiveTransport { get; set; } = true;

    [JsonProperty]
    public bool EnableHeatTransfer { get; set; } = true;

    [JsonProperty]
    public bool EnableFlow { get; set; } = true;

    [JsonProperty]
    public bool EnableForces { get; set; } = true;

    [JsonProperty]
    public bool EnableNucleation { get; set; } = false;

    [JsonProperty]
    public double ConvergenceTolerance { get; set; } = 1e-6;

    [JsonProperty]
    public int MaxIterations { get; set; } = 100;

    [JsonProperty]
    public bool UseGPU { get; set; } = false;

    [JsonProperty]
    public string SolverType { get; set; } = "SequentialIterative"; // or "FullyCoupled"

    [JsonProperty]
    public bool EnableParameterSweep { get; set; } = false;

    [JsonProperty]
    public bool EnableTracking { get; set; } = true;

    [JsonProperty]
    public double TrackingSampleInterval { get; set; } = 1.0; // seconds

    // ==================== MULTIPHASE FLOW PARAMETERS ====================

    [JsonProperty]
    public bool EnableMultiphaseFlow { get; set; } = false; // Enable multiphase water-steam-NCG flow

    [JsonProperty]
    public string MultiphaseEOSType { get; set; } = "WaterCO2"; // WaterSteam, WaterCO2, WaterAir, WaterH2S, WaterMethane

    [JsonProperty]
    public double ResidualLiquidSaturation { get; set; } = 0.05; // S_lr

    [JsonProperty]
    public double ResidualGasSaturation { get; set; } = 0.01; // S_gr

    [JsonProperty]
    public double VanGenuchten_m { get; set; } = 0.5; // van Genuchten m parameter for kr

    [JsonProperty]
    public double VanGenuchten_alpha { get; set; } = 1e-4; // van Genuchten alpha (1/Pa) for Pc

    [JsonProperty]
    public string RelativePermeabilityModel { get; set; } = "VanGenuchten"; // VanGenuchten, Corey, Linear, Grant

    [JsonProperty]
    public string CapillaryPressureModel { get; set; } = "VanGenuchten"; // VanGenuchten, BrooksCorey, Linear, Leverett
}

/// <summary>
/// Simulation execution mode
/// </summary>
public enum SimulationMode
{
    /// <summary>
    /// Run simulation for a fixed amount of time (use TotalTime)
    /// </summary>
    TimeBased,

    /// <summary>
    /// Run simulation for a fixed number of steps (use MaxSteps)
    /// </summary>
    StepBased
}

/// <summary>
/// Current state of the PhysicoChem simulation
/// </summary>
public class PhysicoChemState
{
    public double CurrentTime { get; set; }

    // Physical fields (3D grids)
    public float[,,] Temperature { get; set; } // K
    public float[,,] Pressure { get; set; } // Pa
    public float[,,] Porosity { get; set; } // fraction
    public float[,,] Permeability { get; set; } // m²

    // Velocity field
    public float[,,] VelocityX { get; set; } // m/s
    public float[,,] VelocityY { get; set; }
    public float[,,] VelocityZ { get; set; }

    // Chemical composition (species -> concentration field)
    public Dictionary<string, float[,,]> Concentrations { get; set; } = new();

    // Mineral volume fractions
    public Dictionary<string, float[,,]> Minerals { get; set; } = new();

    // Phase fractions (for multiphase flow)
    public float[,,] LiquidSaturation { get; set; }
    public float[,,] VaporSaturation { get; set; }
    public float[,,] GasSaturation { get; set; }

    // Force field contributions
    public float[,,] ForceX { get; set; } // N/m³
    public float[,,] ForceY { get; set; }
    public float[,,] ForceZ { get; set; }

    // Nucleation tracking
    public List<Nucleus> ActiveNuclei { get; set; } = new();

    // Computed properties for tracking
    public double AverageTemperature => CalculateAverage(Temperature);
    public double AveragePressure => CalculateAverage(Pressure);
    public double TotalMass { get; set; }
    public double MaxVelocity { get; set; }

    public PhysicoChemState((int x, int y, int z) gridSize)
    {
        int nx = gridSize.x;
        int ny = gridSize.y;
        int nz = gridSize.z;

        Temperature = new float[nx, ny, nz];
        Pressure = new float[nx, ny, nz];
        Porosity = new float[nx, ny, nz];
        Permeability = new float[nx, ny, nz];

        VelocityX = new float[nx, ny, nz];
        VelocityY = new float[nx, ny, nz];
        VelocityZ = new float[nx, ny, nz];

        LiquidSaturation = new float[nx, ny, nz];
        VaporSaturation = new float[nx, ny, nz];
        GasSaturation = new float[nx, ny, nz];

        ForceX = new float[nx, ny, nz];
        ForceY = new float[nx, ny, nz];
        ForceZ = new float[nx, ny, nz];
    }

    public PhysicoChemState Clone()
    {
        var clone = new PhysicoChemState((Temperature.GetLength(0),
                                          Temperature.GetLength(1),
                                          Temperature.GetLength(2)))
        {
            CurrentTime = CurrentTime,
            Temperature = (float[,,])Temperature.Clone(),
            Pressure = (float[,,])Pressure.Clone(),
            Porosity = (float[,,])Porosity.Clone(),
            Permeability = (float[,,])Permeability.Clone(),
            VelocityX = (float[,,])VelocityX.Clone(),
            VelocityY = (float[,,])VelocityY.Clone(),
            VelocityZ = (float[,,])VelocityZ.Clone(),
            LiquidSaturation = (float[,,])LiquidSaturation.Clone(),
            VaporSaturation = (float[,,])VaporSaturation.Clone(),
            GasSaturation = (float[,,])GasSaturation.Clone(),
            ForceX = (float[,,])ForceX.Clone(),
            ForceY = (float[,,])ForceY.Clone(),
            ForceZ = (float[,,])ForceZ.Clone()
        };

        foreach (var kvp in Concentrations)
            clone.Concentrations[kvp.Key] = (float[,,])kvp.Value.Clone();

        foreach (var kvp in Minerals)
            clone.Minerals[kvp.Key] = (float[,,])kvp.Value.Clone();

        clone.ActiveNuclei = new List<Nucleus>(ActiveNuclei);

        return clone;
    }

    private double CalculateAverage(float[,,] field)
    {
        if (field == null) return 0.0;

        double sum = 0;
        int count = 0;

        foreach (var value in field)
        {
            sum += value;
            count++;
        }

        return count > 0 ? sum / count : 0.0;
    }
}

/// <summary>
/// Active nucleus tracking
/// </summary>
public class Nucleus
{
    public int Id { get; set; }
    public (double X, double Y, double Z) Position { get; set; }
    public double Radius { get; set; } // m
    public string MineralType { get; set; }
    public double GrowthRate { get; set; } // m/s
    public double BirthTime { get; set; } // s
}

/// <summary>
/// Boolean operations for domain combination
/// </summary>
public enum BooleanOp
{
    Union,
    Subtract,
    Intersect,
    SymmetricDifference
}
