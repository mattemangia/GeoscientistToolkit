// GeoscientistToolkit/Business/GeoScript/GeoScriptParameterSweepCommands.cs

using System;
using System.Data;
using System.Globalization;
using System.Threading.Tasks;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Business.Thermodynamics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.PhysicoChem;
using GeoscientistToolkit.Data.Table;
using GeoscientistToolkit.Data.TwoDGeology;
using GeoscientistToolkit.Data.TwoDGeology.Geomechanics;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business.GeoScriptParameterSweepCommands;

/// <summary>
/// PHYSICOCHEM_SWEEP - configure a PhysicoChem parameter sweep entry.
/// Usage: PHYSICOCHEM_SWEEP name=Temp target=SimulationParams.TimeStep min=0.1 max=1.0 mode=Temporal interp=Linear enable=true
/// </summary>
public class PhysicoChemSweepCommand : IGeoScriptCommand
{
    public string Name => "PHYSICOCHEM_SWEEP";
    public string HelpText => "Configure PhysicoChem parameter sweeps";
    public string Usage => "PHYSICOCHEM_SWEEP name=<label> target=<path> min=<value> max=<value> [mode=Temporal] [interp=Linear] [enable=true] [clear=true]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not PhysicoChemDataset dataset)
            throw new NotSupportedException("PHYSICOCHEM_SWEEP requires a PhysicoChemDataset");

        var cmd = (CommandNode)node;
        var args = GeoScriptArgumentParser.ParseArguments(cmd.FullText);

        if (GeoScriptArgumentParser.GetBool(args, "clear", false, context))
        {
            dataset.ParameterSweepManager.Sweeps.Clear();
            return Task.FromResult<Dataset>(dataset);
        }

        var name = GeoScriptArgumentParser.GetString(args, "name", "Sweep", context);
        var target = GeoScriptArgumentParser.GetString(args, "target", "SimulationParams.TimeStep", context);
        var minValue = GeoScriptArgumentParser.GetDouble(args, "min", 0.0, context);
        var maxValue = GeoScriptArgumentParser.GetDouble(args, "max", 1.0, context);
        var enable = GeoScriptArgumentParser.GetBool(args, "enable", true, context);

        if (GeoScriptArgumentParser.TryGetString(args, "mode", out var modeValue))
        {
            if (Enum.TryParse(modeValue, true, out SweepMode mode))
                dataset.ParameterSweepManager.Mode = mode;
        }

        if (GeoScriptArgumentParser.TryGetString(args, "interp", out var interpValue))
        {
            if (Enum.TryParse(interpValue, true, out InterpolationType interp))
                dataset.ParameterSweepManager.Sweeps.Add(new ParameterSweep
                {
                    ParameterName = name,
                    TargetPath = target,
                    MinValue = minValue,
                    MaxValue = maxValue,
                    Enabled = enable,
                    Interpolation = interp
                });
        }
        else
        {
            dataset.ParameterSweepManager.Sweeps.Add(new ParameterSweep
            {
                ParameterName = name,
                TargetPath = target,
                MinValue = minValue,
                MaxValue = maxValue,
                Enabled = enable
            });
        }

        dataset.ParameterSweepManager.Enabled = enable;
        dataset.SimulationParams.EnableParameterSweep = enable;

        Logger.Log($"[GeoScript] Added PhysicoChem sweep: {name} ({target})");
        return Task.FromResult<Dataset>(dataset);
    }
}

/// <summary>
/// GEOMECH_SWEEP - configure a geomechanics parameter sweep entry.
/// Usage: GEOMECH_SWEEP name=Load target=LoadFactor min=0.5 max=1.5 mode=Step interp=Linear enable=true
/// </summary>
public class GeomechSweepCommand : IGeoScriptCommand
{
    public string Name => "GEOMECH_SWEEP";
    public string HelpText => "Configure geomechanics parameter sweeps";
    public string Usage => "GEOMECH_SWEEP name=<label> target=<path> min=<value> max=<value> [mode=Step] [interp=Linear] [enable=true] [clear=true]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not TwoDGeologyDataset dataset)
            throw new NotSupportedException("GEOMECH_SWEEP requires a TwoDGeologyDataset");

        var cmd = (CommandNode)node;
        var args = GeoScriptArgumentParser.ParseArguments(cmd.FullText);

        if (GeoScriptArgumentParser.GetBool(args, "clear", false, context))
        {
            dataset.GeomechanicalSimulator.ParameterSweepManager.Sweeps.Clear();
            return Task.FromResult<Dataset>(dataset);
        }

        var name = GeoScriptArgumentParser.GetString(args, "name", "Sweep", context);
        var target = GeoScriptArgumentParser.GetString(args, "target", "LoadFactor", context);
        var minValue = GeoScriptArgumentParser.GetDouble(args, "min", 0.0, context);
        var maxValue = GeoScriptArgumentParser.GetDouble(args, "max", 1.0, context);
        var enable = GeoScriptArgumentParser.GetBool(args, "enable", true, context);

        if (GeoScriptArgumentParser.TryGetString(args, "mode", out var modeValue))
        {
            if (Enum.TryParse(modeValue, true, out GeomechanicsSweepMode mode))
                dataset.GeomechanicalSimulator.ParameterSweepManager.Mode = mode;
        }

        var sweep = new GeomechanicsParameterSweep
        {
            ParameterName = name,
            TargetPath = target,
            MinValue = minValue,
            MaxValue = maxValue,
            Enabled = enable
        };

        if (GeoScriptArgumentParser.TryGetString(args, "interp", out var interpValue) &&
            Enum.TryParse(interpValue, true, out GeomechanicsInterpolationType interp))
        {
            sweep.Interpolation = interp;
        }

        dataset.GeomechanicalSimulator.ParameterSweepManager.Sweeps.Add(sweep);
        dataset.GeomechanicalSimulator.ParameterSweepManager.Enabled = enable;

        Logger.Log($"[GeoScript] Added geomechanics sweep: {name} ({target})");
        return Task.FromResult<Dataset>(dataset);
    }
}

/// <summary>
/// THERMO_SWEEP - run a thermodynamic sweep and create a table dataset.
/// Usage: THERMO_SWEEP composition="'H2O'=55.5,'CO2'=1.0" minT=273 maxT=473 minP=1 maxP=1000 grid=25 name=ThermoSweep
/// </summary>
public class ThermoSweepCommand : IGeoScriptCommand
{
    public string Name => "THERMO_SWEEP";
    public string HelpText => "Run thermodynamic equilibrium sweeps over temperature and pressure.";
    public string Usage => "THERMO_SWEEP composition=<list> minT=<K> maxT=<K> minP=<bar> maxP=<bar> grid=<points> name=<dataset>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        var cmd = (CommandNode)node;
        var args = GeoScriptArgumentParser.ParseArguments(cmd.FullText);

        var compositionText = GeoScriptArgumentParser.GetString(args, "composition", "'H2O'=55.5", context);
        var minT = GeoScriptArgumentParser.GetDouble(args, "minT", 273.15, context);
        var maxT = GeoScriptArgumentParser.GetDouble(args, "maxT", 473.15, context);
        var minP = GeoScriptArgumentParser.GetDouble(args, "minP", 1.0, context);
        var maxP = GeoScriptArgumentParser.GetDouble(args, "maxP", 1000.0, context);
        var grid = GeoScriptArgumentParser.GetInt(args, "grid", 25, context);
        var name = GeoScriptArgumentParser.GetString(args, "name", "ThermoSweep", context);

        var generator = new PhaseDiagramGenerator();
        var composition = ParseComposition(compositionText);
        var ptData = generator.GeneratePTDiagram(composition, (float)minT, (float)maxT, (float)minP, (float)maxP, grid);

        var resultTable = new DataTable(name);
        resultTable.Columns.Add("Temperature_K", typeof(double));
        resultTable.Columns.Add("Pressure_bar", typeof(double));
        resultTable.Columns.Add("DominantPhase", typeof(string));
        foreach (var point in ptData.Points)
        {
            resultTable.Rows.Add(point.Temperature_K, point.Pressure_bar, point.DominantPhase);
        }

        var dataset = new TableDataset(name, resultTable);
        ProjectManager.Instance.AddDataset(dataset);
        Logger.Log($"[GeoScript] Created thermodynamic sweep dataset: {dataset.Name}");

        return Task.FromResult<Dataset>(dataset);
    }

    private static System.Collections.Generic.Dictionary<string, double> ParseComposition(string compStr)
    {
        var composition = new System.Collections.Generic.Dictionary<string, double>();
        var parts = compStr.Split(',');
        foreach (var part in parts)
        {
            var pair = part.Split('=');
            if (pair.Length == 2)
            {
                var name = pair[0].Trim().Trim('\'');
                var moles = double.Parse(pair[1].Trim(), CultureInfo.InvariantCulture);
                composition[name] = moles;
            }
        }

        return composition;
    }
}
