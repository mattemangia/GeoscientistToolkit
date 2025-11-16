// GeoscientistToolkit/Business/GeoScriptMultiphaseExtensions.cs
//
// GeoScript extensions for multiphase flow simulations
// Provides commands to configure and run multiphase reactive transport

using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GeoscientistToolkit.Analysis.Multiphase;
using GeoscientistToolkit.Analysis.Thermodynamic;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.PhysicoChem;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business;

/// <summary>
/// ENABLE_MULTIPHASE: Enables multiphase flow in the current PhysicoChem reactor
/// Usage: ENABLE_MULTIPHASE [WaterSteam|WaterCO2|WaterAir|WaterH2S|WaterMethane]
/// </summary>
public class EnableMultiphaseCommand : IGeoScriptCommand
{
    public string Name => "ENABLE_MULTIPHASE";
    public string HelpText => "Enables multiphase flow (water-steam-NCG) in the current reactor";
    public string Usage => "ENABLE_MULTIPHASE [WaterSteam|WaterCO2|WaterAir|WaterH2S|WaterMethane]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not PhysicoChemDataset dataset)
            throw new InvalidOperationException("ENABLE_MULTIPHASE requires a PhysicoChem dataset");

        var cmd = (CommandNode)node;

        // Parse: ENABLE_MULTIPHASE WaterCO2
        var match = Regex.Match(cmd.FullText, @"ENABLE_MULTIPHASE\s+(\S+)", RegexOptions.IgnoreCase);
        string eosType = "WaterCO2"; // default

        if (match.Success)
        {
            eosType = match.Groups[1].Value;
        }

        dataset.SimulationParams.EnableMultiphaseFlow = true;
        dataset.SimulationParams.MultiphaseEOSType = eosType;

        Logger.Log($"[ENABLE_MULTIPHASE] Enabled multiphase flow with EOS type: {eosType}");
        Logger.Log($"[ENABLE_MULTIPHASE] This enables water-steam-NCG ({eosType}) three-phase flow");

        return Task.FromResult<Dataset>(dataset);
    }
}

/// <summary>
/// SET_MULTIPHASE_PARAMS: Sets multiphase flow parameters
/// Usage: SET_MULTIPHASE_PARAMS S_lr=[value] S_gr=[value] m=[value] alpha=[value]
/// </summary>
public class SetMultiphaseParamsCommand : IGeoScriptCommand
{
    public string Name => "SET_MULTIPHASE_PARAMS";
    public string HelpText => "Sets multiphase flow parameters (residual saturations, van Genuchten parameters)";
    public string Usage => "SET_MULTIPHASE_PARAMS S_lr=[value] S_gr=[value] m=[value] alpha=[value]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not PhysicoChemDataset dataset)
            throw new InvalidOperationException("SET_MULTIPHASE_PARAMS requires a PhysicoChem dataset");

        var cmd = (CommandNode)node;

        // Parse parameters
        var slrMatch = Regex.Match(cmd.FullText, @"S_lr\s*=\s*([\d\.]+)", RegexOptions.IgnoreCase);
        var sgrMatch = Regex.Match(cmd.FullText, @"S_gr\s*=\s*([\d\.]+)", RegexOptions.IgnoreCase);
        var mMatch = Regex.Match(cmd.FullText, @"\bm\s*=\s*([\d\.]+)", RegexOptions.IgnoreCase);
        var alphaMatch = Regex.Match(cmd.FullText, @"alpha\s*=\s*([\d\.e\-\+]+)", RegexOptions.IgnoreCase);

        if (slrMatch.Success)
        {
            dataset.SimulationParams.ResidualLiquidSaturation = double.Parse(slrMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            Logger.Log($"[SET_MULTIPHASE_PARAMS] Set S_lr = {dataset.SimulationParams.ResidualLiquidSaturation}");
        }

        if (sgrMatch.Success)
        {
            dataset.SimulationParams.ResidualGasSaturation = double.Parse(sgrMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            Logger.Log($"[SET_MULTIPHASE_PARAMS] Set S_gr = {dataset.SimulationParams.ResidualGasSaturation}");
        }

        if (mMatch.Success)
        {
            dataset.SimulationParams.VanGenuchten_m = double.Parse(mMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            Logger.Log($"[SET_MULTIPHASE_PARAMS] Set van Genuchten m = {dataset.SimulationParams.VanGenuchten_m}");
        }

        if (alphaMatch.Success)
        {
            dataset.SimulationParams.VanGenuchten_alpha = double.Parse(alphaMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            Logger.Log($"[SET_MULTIPHASE_PARAMS] Set van Genuchten alpha = {dataset.SimulationParams.VanGenuchten_alpha:E3} 1/Pa");
        }

        return Task.FromResult<Dataset>(dataset);
    }
}

/// <summary>
/// SET_KR_MODEL: Sets the relative permeability model
/// Usage: SET_KR_MODEL [VanGenuchten|Corey|Linear|Grant]
/// </summary>
public class SetRelativePermeabilityModelCommand : IGeoScriptCommand
{
    public string Name => "SET_KR_MODEL";
    public string HelpText => "Sets the relative permeability model for multiphase flow";
    public string Usage => "SET_KR_MODEL [VanGenuchten|Corey|Linear|Grant]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not PhysicoChemDataset dataset)
            throw new InvalidOperationException("SET_KR_MODEL requires a PhysicoChem dataset");

        var cmd = (CommandNode)node;

        var match = Regex.Match(cmd.FullText, @"SET_KR_MODEL\s+(\S+)", RegexOptions.IgnoreCase);
        if (!match.Success)
            throw new ArgumentException("SET_KR_MODEL requires a model name: VanGenuchten, Corey, Linear, or Grant");

        string model = match.Groups[1].Value;
        dataset.SimulationParams.RelativePermeabilityModel = model;

        Logger.Log($"[SET_KR_MODEL] Set relative permeability model to: {model}");

        return Task.FromResult<Dataset>(dataset);
    }
}

/// <summary>
/// SET_PC_MODEL: Sets the capillary pressure model
/// Usage: SET_PC_MODEL [VanGenuchten|BrooksCorey|Linear|Leverett]
/// </summary>
public class SetCapillaryPressureModelCommand : IGeoScriptCommand
{
    public string Name => "SET_PC_MODEL";
    public string HelpText => "Sets the capillary pressure model for multiphase flow";
    public string Usage => "SET_PC_MODEL [VanGenuchten|BrooksCorey|Linear|Leverett]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not PhysicoChemDataset dataset)
            throw new InvalidOperationException("SET_PC_MODEL requires a PhysicoChem dataset");

        var cmd = (CommandNode)node;

        var match = Regex.Match(cmd.FullText, @"SET_PC_MODEL\s+(\S+)", RegexOptions.IgnoreCase);
        if (!match.Success)
            throw new ArgumentException("SET_PC_MODEL requires a model name: VanGenuchten, BrooksCorey, Linear, or Leverett");

        string model = match.Groups[1].Value;
        dataset.SimulationParams.CapillaryPressureModel = model;

        Logger.Log($"[SET_PC_MODEL] Set capillary pressure model to: {model}");

        return Task.FromResult<Dataset>(dataset);
    }
}

/// <summary>
/// ADD_GAS_PHASE: Adds a gas phase to the domain initial conditions
/// Usage: ADD_GAS_PHASE [domain_name] [gas_type] [saturation] [partial_pressure_Pa]
/// </summary>
public class AddGasPhaseCommand : IGeoScriptCommand
{
    public string Name => "ADD_GAS_PHASE";
    public string HelpText => "Adds a gas phase to a domain's initial conditions";
    public string Usage => "ADD_GAS_PHASE [domain_name] [CO2|CH4|H2S|N2|Air] [saturation] [partial_pressure_Pa]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not PhysicoChemDataset dataset)
            throw new InvalidOperationException("ADD_GAS_PHASE requires a PhysicoChem dataset");

        var cmd = (CommandNode)node;

        // Parse: ADD_GAS_PHASE domain_name CO2 0.2 5e5
        var match = Regex.Match(cmd.FullText, @"ADD_GAS_PHASE\s+(\S+)\s+(\S+)\s+([\d\.]+)\s+([\d\.e\+\-]+)", RegexOptions.IgnoreCase);
        if (!match.Success)
            throw new ArgumentException("ADD_GAS_PHASE requires 4 arguments: domain_name, gas_type, saturation, partial_pressure_Pa");

        string domainName = match.Groups[1].Value;
        string gasType = match.Groups[2].Value;
        double saturation = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        double partialPressure = double.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);

        var domain = dataset.Domains.FirstOrDefault(d => d.Name.Equals(domainName, StringComparison.OrdinalIgnoreCase));
        if (domain == null)
            throw new ArgumentException($"Domain '{domainName}' not found");

        if (domain.InitialConditions == null)
            domain.InitialConditions = new InitialConditions();

        // Set gas saturation (adjust liquid saturation accordingly)
        domain.InitialConditions.LiquidSaturation = Math.Max(0, 1.0 - saturation);

        // Add dissolved gas concentration based on gas type and partial pressure
        string dissolvedGasSpecies = gasType.ToUpper() switch
        {
            "CO2" => "CO₂(aq)",
            "CH4" => "CH₄(aq)",
            "H2S" => "H₂S(aq)",
            "N2" => "N₂(aq)",
            "AIR" => "N₂(aq)", // Air is mostly N2
            _ => $"{gasType}(aq)"
        };

        // Estimate dissolved concentration using Henry's law (simplified)
        // C = H * P, where H ~ 1e-5 mol/(L·Pa) for CO2
        double H = gasType.ToUpper() switch
        {
            "CO2" => 1e-5,
            "CH4" => 2.5e-6,
            "H2S" => 1e-4,
            "N2" => 6.5e-7,
            "AIR" => 6.5e-7,
            _ => 1e-6
        };

        double dissolvedConc = H * partialPressure; // mol/L

        domain.InitialConditions.Concentrations[dissolvedGasSpecies] = dissolvedConc;

        Logger.Log($"[ADD_GAS_PHASE] Added {gasType} gas phase to domain '{domainName}'");
        Logger.Log($"[ADD_GAS_PHASE]   Gas saturation: {saturation}");
        Logger.Log($"[ADD_GAS_PHASE]   Partial pressure: {partialPressure:E3} Pa");
        Logger.Log($"[ADD_GAS_PHASE]   Dissolved {dissolvedGasSpecies}: {dissolvedConc:E3} mol/L");

        return Task.FromResult<Dataset>(dataset);
    }
}

/// <summary>
/// SET_TWO_PHASE_CONDITIONS: Sets initial conditions for two-phase (liquid-vapor) equilibrium
/// Usage: SET_TWO_PHASE_CONDITIONS [domain_name] [temperature_K] [quality]
/// </summary>
public class SetTwoPhaseConditionsCommand : IGeoScriptCommand
{
    public string Name => "SET_TWO_PHASE_CONDITIONS";
    public string HelpText => "Sets two-phase (liquid-vapor) initial conditions for a domain";
    public string Usage => "SET_TWO_PHASE_CONDITIONS [domain_name] [temperature_K] [quality_0_to_1]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not PhysicoChemDataset dataset)
            throw new InvalidOperationException("SET_TWO_PHASE_CONDITIONS requires a PhysicoChem dataset");

        var cmd = (CommandNode)node;

        // Parse: SET_TWO_PHASE_CONDITIONS domain_name 473.15 0.5
        var match = Regex.Match(cmd.FullText, @"SET_TWO_PHASE_CONDITIONS\s+(\S+)\s+([\d\.]+)\s+([\d\.]+)", RegexOptions.IgnoreCase);
        if (!match.Success)
            throw new ArgumentException("SET_TWO_PHASE_CONDITIONS requires 3 arguments: domain_name, temperature_K, quality");

        string domainName = match.Groups[1].Value;
        double temperature_K = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        double quality = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);

        var domain = dataset.Domains.FirstOrDefault(d => d.Name.Equals(domainName, StringComparison.OrdinalIgnoreCase));
        if (domain == null)
            throw new ArgumentException($"Domain '{domainName}' not found");

        if (domain.InitialConditions == null)
            domain.InitialConditions = new InitialConditions();

        // Set temperature
        domain.InitialConditions.Temperature = temperature_K;

        // Get saturation pressure at this temperature
        double P_sat = PhaseTransitionHandler.GetSaturationPressure(temperature_K) * 1e6; // MPa to Pa
        domain.InitialConditions.Pressure = P_sat; // Set to saturation pressure

        // Calculate vapor saturation from quality
        // For simplicity, assume S_v ≈ quality (more accurate would use densities)
        domain.InitialConditions.LiquidSaturation = 1.0 - quality;

        Logger.Log($"[SET_TWO_PHASE_CONDITIONS] Set two-phase conditions for domain '{domainName}'");
        Logger.Log($"[SET_TWO_PHASE_CONDITIONS]   Temperature: {temperature_K} K");
        Logger.Log($"[SET_TWO_PHASE_CONDITIONS]   Saturation pressure: {P_sat:E3} Pa");
        Logger.Log($"[SET_TWO_PHASE_CONDITIONS]   Vapor quality: {quality}");
        Logger.Log($"[SET_TWO_PHASE_CONDITIONS]   Liquid saturation: {domain.InitialConditions.LiquidSaturation}");

        return Task.FromResult<Dataset>(dataset);
    }
}

/// <summary>
/// RUN_MULTIPHASE: Runs a multiphase reactive transport simulation
/// Usage: RUN_MULTIPHASE [time_seconds] [output_interval_seconds]
/// </summary>
public class RunMultiphaseCommand : IGeoScriptCommand
{
    public string Name => "RUN_MULTIPHASE";
    public string HelpText => "Runs a multiphase reactive transport simulation";
    public string Usage => "RUN_MULTIPHASE [time_seconds] [output_interval_seconds]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not PhysicoChemDataset dataset)
            throw new InvalidOperationException("RUN_MULTIPHASE requires a PhysicoChem dataset");

        var cmd = (CommandNode)node;

        // Parse: RUN_MULTIPHASE 3600 60
        var match = Regex.Match(cmd.FullText, @"RUN_MULTIPHASE\s+([\d\.]+)\s+([\d\.]+)", RegexOptions.IgnoreCase);
        if (!match.Success)
            throw new ArgumentException("RUN_MULTIPHASE requires 2 arguments: time_seconds, output_interval_seconds");

        double totalTime = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        double outputInterval = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);

        // Update simulation parameters
        dataset.SimulationParams.TotalTime = totalTime;
        dataset.SimulationParams.OutputInterval = outputInterval;
        dataset.SimulationParams.EnableMultiphaseFlow = true;

        Logger.Log($"[RUN_MULTIPHASE] Starting multiphase reactive transport simulation");
        Logger.Log($"[RUN_MULTIPHASE]   Total time: {totalTime} s");
        Logger.Log($"[RUN_MULTIPHASE]   Output interval: {outputInterval} s");
        Logger.Log($"[RUN_MULTIPHASE]   EOS type: {dataset.SimulationParams.MultiphaseEOSType}");
        Logger.Log($"[RUN_MULTIPHASE]   Reactive transport: {dataset.SimulationParams.EnableReactiveTransport}");

        // TODO: Actually run the simulation here
        // This would call the MultiphaseReactiveTransportSolver

        Logger.Log($"[RUN_MULTIPHASE] Simulation would run here (not yet implemented in GeoScript executor)");
        Logger.Log($"[RUN_MULTIPHASE] Use the simulator tool directly to run multiphase simulations");

        return Task.FromResult<Dataset>(dataset);
    }
}
