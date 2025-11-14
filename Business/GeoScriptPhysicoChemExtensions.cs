// GeoscientistToolkit/Business/GeoScriptPhysicoChemExtensions.cs
//
// GeoScript extensions for PhysicoChem reactor simulations
// Provides commands to create and configure reactive transport reactors programmatically

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using GeoscientistToolkit.Analysis.PhysicoChem;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Data.PhysicoChem;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business;

/// <summary>
/// CREATE_REACTOR: Creates a new PhysicoChem reactor dataset
/// Usage: CREATE_REACTOR [name] [width] [height] [depth]
/// </summary>
public class CreateReactorCommand : IGeoScriptCommand
{
    public string Name => "CREATE_REACTOR";
    public string HelpText => "Creates a new PhysicoChem reactor dataset with a box domain";
    public string Usage => "CREATE_REACTOR [name] [width] [height] [depth]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        var args = GeoScriptUtil.GetFunctionArguments(node);
        if (args.Count < 4)
            throw new ArgumentException("CREATE_REACTOR requires 4 arguments: name, width, height, depth");

        string reactorName = args[0];
        double width = double.Parse(args[1], CultureInfo.InvariantCulture);
        double height = double.Parse(args[2], CultureInfo.InvariantCulture);
        double depth = double.Parse(args[3], CultureInfo.InvariantCulture);

        var dataset = new PhysicoChemDataset(reactorName, null)
        {
            Description = $"Reactor created via GeoScript: {width}×{height}×{depth} m"
        };

        // Add a default box domain
        var geometry = new ReactorGeometry
        {
            Type = GeometryType.Box,
            Center = (0, 0, 0),
            Dimensions = (width, height, depth)
        };

        var material = new MaterialProperties
        {
            Porosity = 0.3,
            Permeability = 1e-12,
            ThermalConductivity = 2.0,
            SpecificHeat = 1000.0,
            Density = 2500.0
        };

        var initialConditions = new InitialConditions
        {
            Temperature = 298.15,
            Pressure = 101325.0
        };

        var domain = new ReactorDomain("MainDomain", geometry)
        {
            Material = material,
            InitialConditions = initialConditions
        };

        dataset.AddDomain(domain);

        Logger.Log($"[CREATE_REACTOR] Created reactor '{reactorName}' with dimensions {width}×{height}×{depth} m");

        return Task.FromResult<Dataset>(dataset);
    }
}

/// <summary>
/// ADD_DOMAIN: Adds a domain to the current PhysicoChem reactor
/// Usage: ADD_DOMAIN [name] [type] [x] [y] [z] [size_params...]
/// </summary>
public class AddDomainCommand : IGeoScriptCommand
{
    public string Name => "ADD_DOMAIN";
    public string HelpText => "Adds a domain to the current PhysicoChem reactor";
    public string Usage => "ADD_DOMAIN [name] [Box|Sphere|Cylinder] [x] [y] [z] [size_params...]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not PhysicoChemDataset dataset)
            throw new NotSupportedException("ADD_DOMAIN only works on PhysicoChem datasets");

        var args = GeoScriptUtil.GetFunctionArguments(node);
        if (args.Count < 5)
            throw new ArgumentException("ADD_DOMAIN requires at least 5 arguments");

        string domainName = args[0];
        string geomTypeStr = args[1];
        double x = double.Parse(args[2], CultureInfo.InvariantCulture);
        double y = double.Parse(args[3], CultureInfo.InvariantCulture);
        double z = double.Parse(args[4], CultureInfo.InvariantCulture);

        if (!Enum.TryParse<GeometryType>(geomTypeStr, true, out var geomType))
            throw new ArgumentException($"Invalid geometry type: {geomTypeStr}. Use Box, Sphere, or Cylinder");

        ReactorGeometry geometry;
        switch (geomType)
        {
            case GeometryType.Box:
                if (args.Count < 8)
                    throw new ArgumentException("Box requires width, height, depth parameters");
                geometry = new ReactorGeometry
                {
                    Type = GeometryType.Box,
                    Center = (x, y, z),
                    Dimensions = (double.Parse(args[5], CultureInfo.InvariantCulture),
                                  double.Parse(args[6], CultureInfo.InvariantCulture),
                                  double.Parse(args[7], CultureInfo.InvariantCulture))
                };
                break;

            case GeometryType.Sphere:
                if (args.Count < 6)
                    throw new ArgumentException("Sphere requires radius parameter");
                geometry = new ReactorGeometry
                {
                    Type = GeometryType.Sphere,
                    Center = (x, y, z),
                    Radius = double.Parse(args[5], CultureInfo.InvariantCulture)
                };
                break;

            case GeometryType.Cylinder:
                if (args.Count < 7)
                    throw new ArgumentException("Cylinder requires radius and height parameters");
                geometry = new ReactorGeometry
                {
                    Type = GeometryType.Cylinder,
                    Center = (x, y, z),
                    Radius = double.Parse(args[5], CultureInfo.InvariantCulture),
                    Height = double.Parse(args[6], CultureInfo.InvariantCulture)
                };
                break;

            default:
                throw new NotSupportedException($"Geometry type {geomType} not supported in GeoScript");
        }

        var material = new MaterialProperties
        {
            Porosity = 0.3,
            Permeability = 1e-12,
            ThermalConductivity = 2.0,
            SpecificHeat = 1000.0,
            Density = 2500.0
        };

        var initialConditions = new InitialConditions
        {
            Temperature = 298.15,
            Pressure = 101325.0
        };

        var domain = new ReactorDomain(domainName, geometry)
        {
            Material = material,
            InitialConditions = initialConditions
        };

        dataset.AddDomain(domain);
        Logger.Log($"[ADD_DOMAIN] Added {geomType} domain '{domainName}' at ({x}, {y}, {z})");

        return Task.FromResult<Dataset>(dataset);
    }
}

/// <summary>
/// SET_MINERALS: Sets mineral composition for the last added domain
/// Usage: SET_MINERALS [mineral1] [fraction1] [mineral2] [fraction2] ...
/// </summary>
public class SetMineralsCommand : IGeoScriptCommand
{
    public string Name => "SET_MINERALS";
    public string HelpText => "Sets mineral composition for the last added domain";
    public string Usage => "SET_MINERALS [mineral1] [fraction1] [mineral2] [fraction2] ...";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not PhysicoChemDataset dataset)
            throw new NotSupportedException("SET_MINERALS only works on PhysicoChem datasets");

        if (dataset.Domains.Count == 0)
            throw new InvalidOperationException("No domains found. Add a domain first.");

        var args = GeoScriptUtil.GetFunctionArguments(node);
        if (args.Count % 2 != 0)
            throw new ArgumentException("SET_MINERALS requires pairs of mineral name and fraction");

        var mineralFractions = new Dictionary<string, double>();
        var mineralNames = new List<string>();

        for (int i = 0; i < args.Count; i += 2)
        {
            string mineralName = args[i];
            double fraction = double.Parse(args[i + 1], CultureInfo.InvariantCulture);

            // Verify mineral exists in library
            var compound = CompoundLibrary.Instance.Compounds
                .FirstOrDefault(c => c.Name.Equals(mineralName, StringComparison.OrdinalIgnoreCase) &&
                                     c.Phase == CompoundPhase.Solid);

            if (compound == null)
            {
                Logger.LogWarning($"[SET_MINERALS] Mineral '{mineralName}' not found in library, adding anyway");
            }

            mineralFractions[mineralName] = fraction;
            mineralNames.Add(mineralName);
        }

        // Normalize fractions
        double total = mineralFractions.Values.Sum();
        if (Math.Abs(total - 1.0) > 0.001)
        {
            Logger.LogWarning($"[SET_MINERALS] Fractions sum to {total:F3}, normalizing to 1.0");
            foreach (var key in mineralFractions.Keys.ToList())
            {
                mineralFractions[key] /= total;
            }
        }

        var lastDomain = dataset.Domains[dataset.Domains.Count - 1];
        if (lastDomain.Material == null)
            lastDomain.Material = new MaterialProperties();

        lastDomain.Material.MineralComposition = string.Join(", ", mineralNames);
        lastDomain.Material.MineralFractions = mineralFractions;

        Logger.Log($"[SET_MINERALS] Set mineral composition: {lastDomain.Material.MineralComposition}");

        return Task.FromResult<Dataset>(dataset);
    }
}

/// <summary>
/// RUN_SIMULATION: Runs the PhysicoChem simulation
/// Usage: RUN_SIMULATION [total_time] [time_step]
/// </summary>
public class RunSimulationCommand : IGeoScriptCommand
{
    public string Name => "RUN_SIMULATION";
    public string HelpText => "Runs the PhysicoChem reactor simulation";
    public string Usage => "RUN_SIMULATION [total_time_s] [time_step_s]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not PhysicoChemDataset dataset)
            throw new NotSupportedException("RUN_SIMULATION only works on PhysicoChem datasets");

        var args = GeoScriptUtil.GetFunctionArguments(node);
        if (args.Count < 2)
            throw new ArgumentException("RUN_SIMULATION requires 2 arguments: total_time, time_step");

        double totalTime = double.Parse(args[0], CultureInfo.InvariantCulture);
        double timeStep = double.Parse(args[1], CultureInfo.InvariantCulture);

        dataset.SimulationParams.TotalTime = totalTime;
        dataset.SimulationParams.TimeStep = timeStep;
        dataset.SimulationParams.OutputInterval = totalTime / 10.0; // 10 outputs

        // Generate mesh if not exists
        if (dataset.GeneratedMesh == null)
        {
            Logger.Log("[RUN_SIMULATION] Generating mesh...");
            dataset.GenerateMesh(resolution: 50);
        }

        // Initialize and run
        dataset.InitializeState();

        var progress = new Progress<(float, string)>(report =>
        {
            if (report.Item1 % 0.1f < 0.01f) // Log every 10%
                Logger.Log($"[RUN_SIMULATION] {report.Item2}");
        });

        var solver = new PhysicoChemSolver(dataset, progress);
        solver.RunSimulation();

        Logger.Log($"[RUN_SIMULATION] Completed: {dataset.ResultHistory.Count} timesteps");

        return Task.FromResult<Dataset>(dataset);
    }
}
