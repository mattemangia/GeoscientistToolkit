// GeoscientistToolkit/Business/GeoScriptPhysicoChemExtensions.cs
//
// GeoScript extensions for PhysicoChem reactor simulations
// Provides commands to create and configure reactive transport reactors programmatically

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
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
        var cmd = (CommandNode)node;

        // Parse: CREATE_REACTOR name width height depth
        var match = Regex.Match(cmd.FullText, @"CREATE_REACTOR\s+(\S+)\s+([\d\.]+)\s+([\d\.]+)\s+([\d\.]+)", RegexOptions.IgnoreCase);
        if (!match.Success)
            throw new ArgumentException("CREATE_REACTOR requires 4 arguments: name, width, height, depth");

        string reactorName = match.Groups[1].Value;
        double width = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        double height = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        double depth = double.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);

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

        var cmd = (CommandNode)node;

        // Parse: ADD_DOMAIN name type x y z [params...]
        // Example: ADD_DOMAIN myDomain Box 0 0 0 1 1 1
        var parts = cmd.FullText.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 6)
            throw new ArgumentException("ADD_DOMAIN requires at least 5 arguments: name, type, x, y, z");

        string domainName = parts[1];
        string geomTypeStr = parts[2];
        double x = double.Parse(parts[3], CultureInfo.InvariantCulture);
        double y = double.Parse(parts[4], CultureInfo.InvariantCulture);
        double z = double.Parse(parts[5], CultureInfo.InvariantCulture);

        if (!Enum.TryParse<GeometryType>(geomTypeStr, true, out var geomType))
            throw new ArgumentException($"Invalid geometry type: {geomTypeStr}. Use Box, Sphere, or Cylinder");

        ReactorGeometry geometry;
        switch (geomType)
        {
            case GeometryType.Box:
                if (parts.Length < 9)
                    throw new ArgumentException("Box requires width, height, depth parameters");
                geometry = new ReactorGeometry
                {
                    Type = GeometryType.Box,
                    Center = (x, y, z),
                    Dimensions = (double.Parse(parts[6], CultureInfo.InvariantCulture),
                                  double.Parse(parts[7], CultureInfo.InvariantCulture),
                                  double.Parse(parts[8], CultureInfo.InvariantCulture))
                };
                break;

            case GeometryType.Sphere:
                if (parts.Length < 7)
                    throw new ArgumentException("Sphere requires radius parameter");
                geometry = new ReactorGeometry
                {
                    Type = GeometryType.Sphere,
                    Center = (x, y, z),
                    Radius = double.Parse(parts[6], CultureInfo.InvariantCulture)
                };
                break;

            case GeometryType.Cylinder:
                if (parts.Length < 8)
                    throw new ArgumentException("Cylinder requires radius and height parameters");
                geometry = new ReactorGeometry
                {
                    Type = GeometryType.Cylinder,
                    Center = (x, y, z),
                    Radius = double.Parse(parts[6], CultureInfo.InvariantCulture),
                    Height = double.Parse(parts[7], CultureInfo.InvariantCulture)
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

        var cmd = (CommandNode)node;

        // Parse: SET_MINERALS mineral1 fraction1 mineral2 fraction2 ...
        var parts = cmd.FullText.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if ((parts.Length - 1) % 2 != 0)
            throw new ArgumentException("SET_MINERALS requires pairs of mineral name and fraction");

        var mineralFractions = new Dictionary<string, double>();
        var mineralNames = new List<string>();

        for (int i = 1; i < parts.Length; i += 2)
        {
            string mineralName = parts[i];
            double fraction = double.Parse(parts[i + 1], CultureInfo.InvariantCulture);

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

        var cmd = (CommandNode)node;

        // Parse: RUN_SIMULATION total_time time_step
        var parts = cmd.FullText.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
            throw new ArgumentException("RUN_SIMULATION requires 2 arguments: total_time, time_step");

        double totalTime = double.Parse(parts[1], CultureInfo.InvariantCulture);
        double timeStep = double.Parse(parts[2], CultureInfo.InvariantCulture);

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
