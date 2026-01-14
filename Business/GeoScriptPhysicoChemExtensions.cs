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

        // Create a default material
        var material = new MaterialProperties
        {
            MaterialID = "Default",
            Porosity = 0.3,
            Permeability = 1e-12,
            ThermalConductivity = 2.0,
            SpecificHeat = 1000.0,
            Density = 2500.0
        };
        dataset.Materials.Add(material);

        // Create a simple mesh with one cell
        var cell = new Cell
        {
            ID = "C1",
            MaterialID = "Default",
            Center = (0, 0, 0),
            Volume = width * height * depth,
            InitialConditions = new InitialConditions
            {
                Temperature = 298.15,
                Pressure = 101325.0
            }
        };
        dataset.Mesh.Cells["C1"] = cell;

        Logger.Log($"[CREATE_REACTOR] Created reactor '{reactorName}' with dimensions {width}×{height}×{depth} m");

        return Task.FromResult<Dataset>(dataset);
    }
}


/// <summary>
/// RUN_SIMULATION: Runs the PhysicoChem simulation
/// Usage: RUN_SIMULATION [total_time] [time_step] [convergence_tolerance=1e-6]
/// </summary>
public class RunSimulationCommand : IGeoScriptCommand
{
    public string Name => "RUN_SIMULATION";
    public string HelpText => "Runs the PhysicoChem reactor simulation";
    public string Usage => "RUN_SIMULATION [total_time_s] [time_step_s] [convergence_tolerance=1e-6]";

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
        var args = GeoScriptArgumentParser.ParseArguments(cmd.FullText);
        var convergenceTolerance = GeoScriptArgumentParser.GetDouble(args, "convergence_tolerance",
            dataset.SimulationParams.ConvergenceTolerance, context);

        dataset.SimulationParams.TotalTime = totalTime;
        dataset.SimulationParams.TimeStep = timeStep;
        dataset.SimulationParams.OutputInterval = totalTime / 10.0; // 10 outputs
        dataset.SimulationParams.ConvergenceTolerance = convergenceTolerance;

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

/// <summary>
/// PHYSICOCHEM_ADD_NUCLEATION_SITE: Adds a nucleation site to a PhysicoChem dataset
/// Usage: PHYSICOCHEM_ADD_NUCLEATION_SITE name=Site1 x=0 y=0 z=0 mineral=Calcite material_id=ReactorFluid rate=1e6 active=true
/// </summary>
public class PhysicoChemAddNucleationSiteCommand : IGeoScriptCommand
{
    public string Name => "PHYSICOCHEM_ADD_NUCLEATION_SITE";
    public string HelpText => "Adds a nucleation site (point) to a PhysicoChem dataset";
    public string Usage =>
        "PHYSICOCHEM_ADD_NUCLEATION_SITE [name=Site1] [x=0] [y=0] [z=0] " +
        "[mineral=Calcite] [material_id=] [rate=1e6] [active=true]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not PhysicoChemDataset dataset)
            throw new NotSupportedException("PHYSICOCHEM_ADD_NUCLEATION_SITE only works on PhysicoChem datasets");

        var cmd = (CommandNode)node;
        var args = GeoScriptArgumentParser.ParseArguments(cmd.FullText);

        var name = GeoScriptArgumentParser.GetString(args, "name", $"Nucleation{dataset.NucleationSites.Count + 1}", context);
        var x = GeoScriptArgumentParser.GetDouble(args, "x", 0.0, context);
        var y = GeoScriptArgumentParser.GetDouble(args, "y", 0.0, context);
        var z = GeoScriptArgumentParser.GetDouble(args, "z", 0.0, context);
        var mineral = GeoScriptArgumentParser.GetString(args, "mineral", "Calcite", context);
        var materialId = GeoScriptArgumentParser.GetString(args, "material_id", string.Empty, context);
        var rate = GeoScriptArgumentParser.GetDouble(args, "rate", 1e6, context);
        var active = GeoScriptArgumentParser.GetBool(args, "active", true, context);

        var site = new NucleationSite(name, (x, y, z), mineral, materialId)
        {
            NucleationRate = rate,
            IsActive = active
        };

        dataset.NucleationSites.Add(site);
        Logger.Log($"[PHYSICOCHEM_ADD_NUCLEATION_SITE] Added '{name}' at ({x}, {y}, {z}) mineral={mineral} material={materialId}");

        return Task.FromResult<Dataset>(dataset);
    }
}

/// <summary>
/// PHYSICOCHEM_ADD_FORCE: Adds a force field to a PhysicoChem dataset
/// Usage: PHYSICOCHEM_ADD_FORCE name=Gravity type=gravity gravity=0,0,-9.81
/// </summary>
public class PhysicoChemAddForceCommand : IGeoScriptCommand
{
    public string Name => "PHYSICOCHEM_ADD_FORCE";
    public string HelpText => "Adds a force field (gravity/vortex/centrifugal) to a PhysicoChem dataset";
    public string Usage =>
        "PHYSICOCHEM_ADD_FORCE [name=Force1] [type=gravity] [active=true] " +
        "[gravity=0,0,-9.81] [gravity_x=0] [gravity_y=0] [gravity_z=-9.81] " +
        "[gravity_preset=earth|moon|mars|venus|jupiter|saturn|mercury] [gravity_magnitude=9.81] " +
        "[center=0,0,0] [axis=0,0,1] [strength=1] [radius=1]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not PhysicoChemDataset dataset)
            throw new NotSupportedException("PHYSICOCHEM_ADD_FORCE only works on PhysicoChem datasets");

        var cmd = (CommandNode)node;
        var args = GeoScriptArgumentParser.ParseArguments(cmd.FullText);

        var name = GeoScriptArgumentParser.GetString(args, "name", $"Force{dataset.Forces.Count + 1}", context);
        var typeName = GeoScriptArgumentParser.GetString(args, "type", ForceType.Gravity.ToString(), context);
        if (!Enum.TryParse<ForceType>(typeName, true, out var forceType))
            throw new ArgumentException($"Unknown force type: {typeName}");

        var force = new ForceField(name, forceType)
        {
            IsActive = GeoScriptArgumentParser.GetBool(args, "active", true, context)
        };

        switch (forceType)
        {
            case ForceType.Gravity:
                var gravity = GeoScriptArgumentParser.GetVector3(args, "gravity", new Vector3(0, 0, -9.81f), context);

                if (GeoScriptArgumentParser.TryGetString(args, "gravity_preset", out var preset))
                {
                    var mag = preset.ToLowerInvariant() switch
                    {
                        "earth" => 9.81f,
                        "moon" => 1.62f,
                        "mars" => 3.72f,
                        "venus" => 8.87f,
                        "jupiter" => 24.79f,
                        "saturn" => 10.44f,
                        "mercury" => 3.70f,
                        _ => throw new ArgumentException($"Unknown gravity preset: {preset}")
                    };
                    gravity = new Vector3(0, 0, -mag);
                }

                if (GeoScriptArgumentParser.TryGetString(args, "gravity_magnitude", out var magnitudeValue))
                {
                    var magnitude = float.Parse(magnitudeValue, CultureInfo.InvariantCulture);
                    gravity = new Vector3(0, 0, -MathF.Abs(magnitude));
                }

                if (GeoScriptArgumentParser.TryGetString(args, "gravity_x", out var gx))
                    gravity.X = float.Parse(gx, CultureInfo.InvariantCulture);
                if (GeoScriptArgumentParser.TryGetString(args, "gravity_y", out var gy))
                    gravity.Y = float.Parse(gy, CultureInfo.InvariantCulture);
                if (GeoScriptArgumentParser.TryGetString(args, "gravity_z", out var gz))
                    gravity.Z = float.Parse(gz, CultureInfo.InvariantCulture);

                force.GravityVector = (gravity.X, gravity.Y, gravity.Z);
                break;

            case ForceType.Vortex:
            case ForceType.Centrifugal:
                var center = GeoScriptArgumentParser.GetVector3(args, "center", Vector3.Zero, context);
                if (GeoScriptArgumentParser.TryGetString(args, "vortex_center", out var vortexCenterValue))
                    center = ParseVector3(vortexCenterValue, center);

                var axis = GeoScriptArgumentParser.GetVector3(args, "axis", new Vector3(0, 0, 1), context);
                if (GeoScriptArgumentParser.TryGetString(args, "vortex_axis", out var vortexAxisValue))
                    axis = ParseVector3(vortexAxisValue, axis);

                force.VortexCenter = (center.X, center.Y, center.Z);
                force.VortexAxis = (axis.X, axis.Y, axis.Z);
                force.VortexStrength = GeoScriptArgumentParser.GetDouble(args, "strength", 1.0, context);
                force.VortexRadius = GeoScriptArgumentParser.GetDouble(args, "radius", 1.0, context);
                break;
        }

        dataset.Forces.Add(force);
        Logger.Log($"[PHYSICOCHEM_ADD_FORCE] Added {forceType} force '{name}'");

        return Task.FromResult<Dataset>(dataset);
    }

    private static Vector3 ParseVector3(string value, Vector3 fallback)
    {
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
            return fallback;

        return new Vector3(
            float.Parse(parts[0], CultureInfo.InvariantCulture),
            float.Parse(parts[1], CultureInfo.InvariantCulture),
            float.Parse(parts[2], CultureInfo.InvariantCulture));
    }
}
