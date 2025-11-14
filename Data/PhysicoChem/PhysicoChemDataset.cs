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
public class PhysicoChemDataset : Dataset
{
    [JsonProperty]
    public string Description { get; set; }

    [JsonProperty]
    public List<ReactorDomain> Domains { get; set; } = new();

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

    public PhysicoChemDataset(string name, string description = "")
    {
        Name = name;
        Description = description;
    }

    /// <summary>
    /// Add a new reactor domain
    /// </summary>
    public void AddDomain(ReactorDomain domain)
    {
        Domains.Add(domain);
    }

    /// <summary>
    /// Perform boolean operation between domains
    /// </summary>
    public ReactorDomain BooleanOperation(ReactorDomain domain1, ReactorDomain domain2, BooleanOp operation)
    {
        var result = new ReactorDomain
        {
            Name = $"{domain1.Name}_{operation}_{domain2.Name}",
            BooleanOperation = operation,
            ParentDomains = new List<ReactorDomain> { domain1, domain2 }
        };

        return result;
    }

    /// <summary>
    /// Generate 3D mesh from all domains
    /// </summary>
    public void GenerateMesh(int resolution = 50)
    {
        var meshGenerator = new ReactorMeshGenerator();
        GeneratedMesh = meshGenerator.GenerateMeshFromDomains(Domains, resolution);
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
        foreach (var domain in Domains)
        {
            if (domain.InitialConditions == null) continue;

            // Apply ICs to cells within this domain
            // (Implementation depends on mesh structure)
        }
    }

    /// <summary>
    /// Validate dataset consistency
    /// </summary>
    public List<string> Validate()
    {
        var errors = new List<string>();

        if (Domains.Count == 0)
            errors.Add("No domains defined");

        foreach (var domain in Domains)
        {
            if (domain.Geometry == null)
                errors.Add($"Domain '{domain.Name}' has no geometry");
        }

        return errors;
    }
}

/// <summary>
/// Simulation parameters for PhysicoChem experiments
/// </summary>
public class SimulationParameters
{
    [JsonProperty]
    public double TotalTime { get; set; } = 3600.0; // seconds

    [JsonProperty]
    public double TimeStep { get; set; } = 1.0; // seconds

    [JsonProperty]
    public double OutputInterval { get; set; } = 60.0; // seconds

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
