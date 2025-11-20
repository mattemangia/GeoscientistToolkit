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
