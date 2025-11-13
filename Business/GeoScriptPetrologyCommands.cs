// GeoscientistToolkit/Business/GeoScriptPetrologyCommands.cs
//
// GeoScript commands for igneous and metamorphic petrology:
// - FRACTIONATE_MAGMA: Crystallization modeling with trace elements
// - LIQUIDUS_SOLIDUS: Automatic phase diagram generation
// - METAMORPHIC_PT: P-T diagrams for metamorphic minerals
//
// These commands demonstrate "NO HARDCODED REACTIONS" - everything is calculated
// from thermodynamic data using Gibbs energy minimization and phase equilibria.

using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Business.Petrology;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Table;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business;

/// <summary>
///     FRACTIONATE_MAGMA: Models magma crystallization with trace element evolution.
///     Uses Rayleigh fractionation or equilibrium crystallization.
/// </summary>
public class FractionateMagmaCommand : IGeoScriptCommand
{
    public string Name => "FRACTIONATE_MAGMA";

    public string HelpText =>
        "Models magma crystallization following Bowen's series with trace element evolution (Rayleigh fractionation).";

    public string Usage =>
        "FRACTIONATE_MAGMA TYPE 'Basaltic|Andesitic|Rhyolitic' TEMP_RANGE <min>-<max> C STEPS <n> [FRACTIONAL|EQUILIBRIUM] ELEMENTS 'Ni,Rb,Sr,La,...'";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        var cmd = (CommandNode)node;

        // Parse command
        var typeMatch = Regex.Match(cmd.FullText, @"TYPE\s+'([^']+)'", RegexOptions.IgnoreCase);
        var tempMatch = Regex.Match(cmd.FullText, @"TEMP_RANGE\s+([\d\.]+)-([\d\.]+)", RegexOptions.IgnoreCase);
        var stepsMatch = Regex.Match(cmd.FullText, @"STEPS\s+(\d+)", RegexOptions.IgnoreCase);
        var elementsMatch = Regex.Match(cmd.FullText, @"ELEMENTS\s+'([^']+)'", RegexOptions.IgnoreCase);
        var isFractional = !cmd.FullText.Contains("EQUILIBRIUM", StringComparison.OrdinalIgnoreCase);

        if (!typeMatch.Success || !tempMatch.Success || !stepsMatch.Success || !elementsMatch.Success)
            throw new ArgumentException($"Invalid syntax. Usage: {Usage}");

        var magmaType = typeMatch.Groups[1].Value;
        var minTemp = double.Parse(tempMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        var maxTemp = double.Parse(tempMatch.Groups[2].Value, CultureInfo.InvariantCulture);
        var steps = int.Parse(stepsMatch.Groups[1].Value);
        var elements = elementsMatch.Groups[1].Value.Split(',').Select(e => e.Trim()).ToList();

        // Create default initial composition (typical basalt trace elements in ppm)
        var initialComposition = new Dictionary<string, double>
        {
            { "Ni", 100.0 }, { "Cr", 200.0 }, { "Co", 40.0 }, { "Sc", 30.0 }, { "V", 250.0 },
            { "Rb", 2.0 }, { "Sr", 150.0 }, { "Ba", 50.0 }, { "K", 2000.0 },
            { "La", 5.0 }, { "Ce", 12.0 }, { "Sm", 3.5 }, { "Yb", 2.0 }, { "Eu", 1.2 }
        };

        // Configure crystallization
        var config = new CrystallizationConfig
        {
            MagmaType = magmaType,
            InitialTemperature_C = maxTemp,
            FinalTemperature_C = minTemp,
            Steps = steps,
            UseFractionalCrystallization = isFractional,
            InitialComposition = initialComposition
        };

        // Run simulation
        var solver = new FractionalCrystallizationSolver();
        var results = solver.Simulate(config);

        // Export to table
        var table = solver.ExportToTable(results, elements);

        Logger.Log(
            $"[FRACTIONATE_MAGMA] Simulated {magmaType} magma {(isFractional ? "fractional" : "equilibrium")} crystallization over {steps} steps");

        // Add Bowen series info to output
        Logger.Log("=== BOWEN'S REACTION SERIES ===");
        Logger.Log("Discontinuous: " + string.Join(" → ", results.BowenSeriesOrder["Discontinuous"]));
        Logger.Log("Continuous: " + string.Join(" → ", results.BowenSeriesOrder["Continuous"]));

        return Task.FromResult<Dataset>(new TableDataset($"{magmaType}_Crystallization", table));
    }
}

/// <summary>
///     LIQUIDUS_SOLIDUS: Generates liquidus-solidus phase diagrams automatically from thermodynamic data.
/// </summary>
public class LiquidusSolidusCommand : IGeoScriptCommand
{
    public string Name => "LIQUIDUS_SOLIDUS";
    public string HelpText => "Generates liquidus-solidus phase diagram for binary solid solution (e.g., Fo-Fa olivine).";
    public string Usage => "LIQUIDUS_SOLIDUS COMPONENTS '<comp1>','<comp2>' TEMP_RANGE <min>-<max> K PRESSURE <val> BAR";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        var cmd = (CommandNode)node;

        var compMatch = Regex.Match(cmd.FullText, @"COMPONENTS\s+'([^']+)',\s*'([^']+)'", RegexOptions.IgnoreCase);
        var tempMatch = Regex.Match(cmd.FullText, @"TEMP_RANGE\s+([\d\.]+)-([\d\.]+)", RegexOptions.IgnoreCase);
        var presMatch = Regex.Match(cmd.FullText, @"PRESSURE\s+([\d\.]+)", RegexOptions.IgnoreCase);

        if (!compMatch.Success || !tempMatch.Success || !presMatch.Success)
            throw new ArgumentException($"Invalid syntax. Usage: {Usage}");

        var comp1 = compMatch.Groups[1].Value;
        var comp2 = compMatch.Groups[2].Value;
        var minTemp = double.Parse(tempMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        var maxTemp = double.Parse(tempMatch.Groups[2].Value, CultureInfo.InvariantCulture);
        var pressure = double.Parse(presMatch.Groups[1].Value, CultureInfo.InvariantCulture);

        var calculator = new PhaseDiagramCalculator();
        var boundaries = calculator.CalculateBinaryLiquidusSolidus(comp1, comp2, minTemp, maxTemp, pressure, 50);

        var table = calculator.ExportBoundaryToTable(boundaries, $"{comp1}_{comp2}_Liquidus_Solidus");

        Logger.Log(
            $"[LIQUIDUS_SOLIDUS] Generated phase diagram for {comp1}-{comp2} at {pressure} bar ({boundaries.Count} points)");

        return Task.FromResult<Dataset>(new TableDataset($"{comp1}_{comp2}_PhaseDiagram", table));
    }
}

/// <summary>
///     METAMORPHIC_PT: Generates P-T phase diagrams for metamorphic minerals (e.g., Ky-And-Sil triple point).
/// </summary>
public class MetamorphicPTCommand : IGeoScriptCommand
{
    public string Name => "METAMORPHIC_PT";

    public string HelpText =>
        "Generates P-T phase diagram for metamorphic minerals with triple point (e.g., Kyanite-Andalusite-Sillimanite).";

    public string Usage =>
        "METAMORPHIC_PT PHASES '<phase1>','<phase2>','<phase3>' T_RANGE <min>-<max> K P_RANGE <min>-<max> BAR";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        var cmd = (CommandNode)node;

        var phaseMatch = Regex.Match(cmd.FullText, @"PHASES\s+'([^']+)',\s*'([^']+)',\s*'([^']+)'",
            RegexOptions.IgnoreCase);
        var tempMatch = Regex.Match(cmd.FullText, @"T_RANGE\s+([\d\.]+)-([\d\.]+)", RegexOptions.IgnoreCase);
        var presMatch = Regex.Match(cmd.FullText, @"P_RANGE\s+([\d\.]+)-([\d\.]+)", RegexOptions.IgnoreCase);

        if (!phaseMatch.Success || !tempMatch.Success || !presMatch.Success)
            throw new ArgumentException($"Invalid syntax. Usage: {Usage}");

        var phase1 = phaseMatch.Groups[1].Value;
        var phase2 = phaseMatch.Groups[2].Value;
        var phase3 = phaseMatch.Groups[3].Value;
        var minT = double.Parse(tempMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        var maxT = double.Parse(tempMatch.Groups[2].Value, CultureInfo.InvariantCulture);
        var minP = double.Parse(presMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        var maxP = double.Parse(presMatch.Groups[2].Value, CultureInfo.InvariantCulture);

        var calculator = new PhaseDiagramCalculator();

        // Calculate all three boundaries
        var boundary12 = calculator.CalculatePolymorphBoundary(phase1, phase2, minT, maxT, minP, maxP, 40);
        var boundary23 = calculator.CalculatePolymorphBoundary(phase2, phase3, minT, maxT, minP, maxP, 40);
        var boundary13 = calculator.CalculatePolymorphBoundary(phase1, phase3, minT, maxT, minP, maxP, 40);

        // Calculate triple point
        var triplePoint = calculator.CalculateTriplePoint(phase1, phase2, phase3, minT, maxT, minP, maxP);

        // Combine all boundaries
        var allBoundaries = new List<PhaseBoundaryPoint>();
        allBoundaries.AddRange(boundary12);
        allBoundaries.AddRange(boundary23);
        allBoundaries.AddRange(boundary13);

        // Add triple point as special marker
        if (triplePoint.HasValue)
        {
            allBoundaries.Add(new PhaseBoundaryPoint
            {
                Temperature_K = triplePoint.Value.Temperature_K,
                Pressure_bar = triplePoint.Value.Pressure_bar,
                Phase1 = phase1,
                Phase2 = $"{phase2}+{phase3}",
                BoundaryType = "TriplePoint"
            });

            Logger.Log("=== TRIPLE POINT ===");
            Logger.Log($"Temperature: {triplePoint.Value.Temperature_K - 273.15:F1}°C ({triplePoint.Value.Temperature_K:F1} K)");
            Logger.Log($"Pressure: {triplePoint.Value.Pressure_bar / 1000.0:F2} kbar ({triplePoint.Value.Pressure_bar:F0} bar)");
        }

        var table = calculator.ExportBoundaryToTable(allBoundaries, $"{phase1}_{phase2}_{phase3}_PT");

        Logger.Log($"[METAMORPHIC_PT] Generated P-T diagram for {phase1}-{phase2}-{phase3} ({allBoundaries.Count} points)");

        return Task.FromResult<Dataset>(new TableDataset($"{phase1}_{phase2}_{phase3}_PT", table));
    }
}
