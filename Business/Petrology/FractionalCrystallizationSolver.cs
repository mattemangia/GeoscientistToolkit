// GeoscientistToolkit/Business/Petrology/FractionalCrystallizationSolver.cs
//
// Solver for magma crystallization modeling using fractional and equilibrium crystallization.
// Implements Rayleigh fractionation for trace elements and Bowen's reaction series.
//
// THEORY:
//
// 1. RAYLEIGH FRACTIONATION (for trace elements):
//    C_L = C_0 * F^(D-1)
//    where:
//      C_L = concentration in residual liquid
//      C_0 = initial concentration
//      F = fraction of melt remaining (0 to 1)
//      D = bulk partition coefficient = Σ(Xi * Kdi)
//      Xi = weight fraction of mineral i crystallizing
//      Kdi = partition coefficient for mineral i
//
// 2. EQUILIBRIUM CRYSTALLIZATION:
//    C_L = C_0 / (D + F*(1-D))
//
// 3. BOWEN'S REACTION SERIES (order of crystallization):
//    Discontinuous:        Continuous:
//    Olivine         -->   Ca-Plagioclase
//    Pyroxene        -->   Ca-Na Plagioclase
//    Amphibole       -->   Na-Plagioclase
//    Biotite         -->   K-Feldspar
//    Muscovite
//    Quartz
//
// SOURCES:
// - Bowen, N.L., 1928. The Evolution of Igneous Rocks. Princeton Univ. Press.
// - Rayleigh, J.W.S., 1896. Theoretical considerations on the fractionation of isotopes.
// - Rollinson, H., 1993. Using Geochemical Data. Longman.
// - Winter, J.D., 2013. Principles of Igneous and Metamorphic Petrology, 2nd ed.

using System.Data;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business.Petrology;

/// <summary>
///     Configuration for crystallization modeling.
/// </summary>
public class CrystallizationConfig
{
    public string MagmaType { get; set; } = "Basaltic"; // Basaltic, Andesitic, Rhyolitic
    public double InitialTemperature_C { get; set; } = 1200;
    public double FinalTemperature_C { get; set; } = 700;
    public int Steps { get; set; } = 50;
    public bool UseFractionalCrystallization { get; set; } = true; // true=fractional, false=equilibrium
    public Dictionary<string, double> InitialComposition { get; set; } = new(); // Element -> ppm or wt%
    public Dictionary<string, double> MineralProportions { get; set; } = new(); // Mineral -> fraction
}

/// <summary>
///     Results from one step of crystallization.
/// </summary>
public class CrystallizationStep
{
    public int StepNumber { get; set; }
    public double Temperature_C { get; set; }
    public double MeltFraction { get; set; } // F (0 to 1)
    public double CumulateFraction { get; set; } // 1 - F
    public Dictionary<string, double> LiquidComposition { get; set; } = new(); // Element -> concentration
    public Dictionary<string, double> CumulateComposition { get; set; } = new(); // Element -> concentration
    public List<string> MineralsCrystallizing { get; set; } = new();
    public Dictionary<string, double> MineralProportions { get; set; } = new();
}

/// <summary>
///     Complete crystallization sequence results.
/// </summary>
public class CrystallizationResults
{
    public List<CrystallizationStep> Steps { get; set; } = new();
    public string MagmaType { get; set; } = "";
    public bool IsFractional { get; set; }
    public Dictionary<string, List<string>> BowenSeriesOrder { get; set; } = new();
}

/// <summary>
///     Solver for magma crystallization modeling.
/// </summary>
public class FractionalCrystallizationSolver
{
    private readonly PartitionCoefficientLibrary _kdLibrary = PartitionCoefficientLibrary.Instance;

    /// <summary>
    ///     Simulates crystallization of a magma following Bowen's reaction series.
    /// </summary>
    public CrystallizationResults Simulate(CrystallizationConfig config)
    {
        var results = new CrystallizationResults
        {
            MagmaType = config.MagmaType,
            IsFractional = config.UseFractionalCrystallization,
            BowenSeriesOrder = GetBowenSeriesOrder(config.MagmaType)
        };

        var currentLiquid = new Dictionary<string, double>(config.InitialComposition);
        var tempStep = (config.InitialTemperature_C - config.FinalTemperature_C) / config.Steps;

        for (var i = 0; i <= config.Steps; i++)
        {
            var temp = config.InitialTemperature_C - i * tempStep;
            var F = 1.0 - (double)i / config.Steps; // Melt fraction

            // Determine which minerals are crystallizing at this temperature
            var minerals = GetCrystallizingMinerals(temp, config.MagmaType);
            var mineralProps = NormalizeMineralProportions(minerals, config);

            var step = new CrystallizationStep
            {
                StepNumber = i,
                Temperature_C = temp,
                MeltFraction = F,
                CumulateFraction = 1.0 - F,
                MineralsCrystallizing = minerals,
                MineralProportions = mineralProps
            };

            // Calculate liquid composition for each element
            foreach (var (element, C0) in config.InitialComposition)
            {
                double C_L;

                if (config.UseFractionalCrystallization)
                {
                    // Rayleigh fractionation: C_L = C_0 * F^(D-1)
                    var D = CalculateBulkPartitionCoefficient(element, mineralProps, config.MagmaType);
                    C_L = F > 0.001 ? C0 * Math.Pow(F, D - 1.0) : C0 * Math.Pow(0.001, D - 1.0);
                }
                else
                {
                    // Equilibrium crystallization: C_L = C_0 / (D + F*(1-D))
                    var D = CalculateBulkPartitionCoefficient(element, mineralProps, config.MagmaType);
                    C_L = C0 / (D + F * (1.0 - D));
                }

                step.LiquidComposition[element] = C_L;

                // Cumulate composition (average of solid formed)
                var C_solid = C_L * CalculateBulkPartitionCoefficient(element, mineralProps, config.MagmaType);
                step.CumulateComposition[element] = C_solid;
            }

            results.Steps.Add(step);

            // Update current liquid for next step (only for fractional)
            if (config.UseFractionalCrystallization)
                currentLiquid = new Dictionary<string, double>(step.LiquidComposition);
        }

        Logger.Log(
            $"[FractionalCrystallizationSolver] Simulated {config.Steps} steps of {(config.UseFractionalCrystallization ? "fractional" : "equilibrium")} crystallization");

        return results;
    }

    /// <summary>
    ///     Calculates bulk partition coefficient: D = Σ(Xi * Kdi)
    /// </summary>
    private double CalculateBulkPartitionCoefficient(string element, Dictionary<string, double> mineralProportions,
        string magmaType)
    {
        var D = 0.0;

        foreach (var (mineral, proportion) in mineralProportions)
        {
            var Kd = _kdLibrary.GetKd(element, mineral, magmaType) ?? 0.1; // Default if not found
            D += proportion * Kd;
        }

        return D;
    }

    /// <summary>
    ///     Determines which minerals crystallize at a given temperature based on Bowen's series.
    /// </summary>
    private List<string> GetCrystallizingMinerals(double temperature_C, string magmaType)
    {
        var minerals = new List<string>();

        if (magmaType.Equals("Basaltic", StringComparison.OrdinalIgnoreCase))
        {
            // Basaltic magma crystallization sequence (Bowen's series)
            if (temperature_C > 1150) minerals.Add("Olivine");
            if (temperature_C is > 1100 and <= 1200) minerals.Add("Olivine");
            if (temperature_C is > 1000 and <= 1150)
            {
                minerals.Add("Clinopyroxene");
                minerals.Add("Plagioclase");
            }

            if (temperature_C is > 900 and <= 1050)
            {
                minerals.Add("Clinopyroxene");
                minerals.Add("Plagioclase");
            }

            if (temperature_C is > 700 and <= 900)
            {
                minerals.Add("Plagioclase");
                if (temperature_C < 850) minerals.Add("Magnetite");
            }
        }
        else if (magmaType.Equals("Andesitic", StringComparison.OrdinalIgnoreCase))
        {
            if (temperature_C > 1100)
            {
                minerals.Add("Olivine");
                minerals.Add("Clinopyroxene");
            }

            if (temperature_C is > 950 and <= 1150)
            {
                minerals.Add("Orthopyroxene");
                minerals.Add("Plagioclase");
            }

            if (temperature_C is > 800 and <= 1000)
            {
                minerals.Add("Plagioclase");
                minerals.Add("Clinopyroxene");
            }

            if (temperature_C <= 900) minerals.Add("Magnetite");
        }
        else if (magmaType.Equals("Rhyolitic", StringComparison.OrdinalIgnoreCase))
        {
            if (temperature_C > 900) minerals.Add("Plagioclase");
            if (temperature_C is > 800 and <= 950)
            {
                minerals.Add("Plagioclase");
                minerals.Add("K-Feldspar");
            }

            if (temperature_C is > 700 and <= 850)
            {
                minerals.Add("K-Feldspar");
                minerals.Add("Biotite");
            }

            if (temperature_C <= 750) minerals.Add("Quartz");
        }

        return minerals.Count > 0 ? minerals : new List<string> { "Plagioclase" }; // Default
    }

    /// <summary>
    ///     Gets Bowen's reaction series order for a magma type.
    /// </summary>
    private Dictionary<string, List<string>> GetBowenSeriesOrder(string magmaType)
    {
        return new Dictionary<string, List<string>>
        {
            {
                "Discontinuous", new List<string>
                {
                    "Olivine",
                    "Clinopyroxene",
                    "Orthopyroxene",
                    "Amphibole",
                    "Biotite",
                    "Muscovite",
                    "Quartz"
                }
            },
            {
                "Continuous", new List<string>
                {
                    "Ca-Plagioclase (An-rich)",
                    "Ca-Na-Plagioclase",
                    "Na-Plagioclase (Ab-rich)",
                    "K-Feldspar"
                }
            }
        };
    }

    /// <summary>
    ///     Normalizes mineral proportions to sum to 1.0
    /// </summary>
    private Dictionary<string, double> NormalizeMineralProportions(List<string> minerals,
        CrystallizationConfig config)
    {
        var proportions = new Dictionary<string, double>();

        if (minerals.Count == 0) return proportions;

        // If user provided proportions, use those; otherwise equal proportions
        var totalSpecified = 0.0;
        foreach (var mineral in minerals)
        {
            if (config.MineralProportions.TryGetValue(mineral, out var prop))
            {
                proportions[mineral] = prop;
                totalSpecified += prop;
            }
        }

        if (totalSpecified > 0)
        {
            // Normalize user-provided proportions
            var keys = proportions.Keys.ToList();
            foreach (var key in keys) proportions[key] /= totalSpecified;

            // Add missing minerals with equal proportions
            var missingMinerals = minerals.Where(m => !proportions.ContainsKey(m)).ToList();
            if (missingMinerals.Count > 0)
            {
                var remaining = 0.0;
                foreach (var m in missingMinerals)
                {
                    proportions[m] = remaining / missingMinerals.Count;
                }
            }
        }
        else
        {
            // Equal proportions for all
            var equalProp = 1.0 / minerals.Count;
            foreach (var mineral in minerals) proportions[mineral] = equalProp;
        }

        return proportions;
    }

    /// <summary>
    ///     Exports crystallization results to a DataTable for visualization.
    /// </summary>
    public DataTable ExportToTable(CrystallizationResults results, List<string> elementsToPlot)
    {
        var table = new DataTable("CrystallizationSequence");
        table.Columns.Add("Step", typeof(int));
        table.Columns.Add("Temperature_C", typeof(double));
        table.Columns.Add("MeltFraction_F", typeof(double));
        table.Columns.Add("CumulateFraction", typeof(double));
        table.Columns.Add("Minerals", typeof(string));

        foreach (var element in elementsToPlot)
        {
            table.Columns.Add($"{element}_Liquid_ppm", typeof(double));
            table.Columns.Add($"{element}_Cumulate_ppm", typeof(double));
            table.Columns.Add($"{element}_Enrichment", typeof(double)); // C_L / C_0
        }

        foreach (var step in results.Steps)
        {
            var row = table.NewRow();
            row["Step"] = step.StepNumber;
            row["Temperature_C"] = step.Temperature_C;
            row["MeltFraction_F"] = step.MeltFraction;
            row["CumulateFraction"] = step.CumulateFraction;
            row["Minerals"] = string.Join(", ", step.MineralsCrystallizing);

            foreach (var element in elementsToPlot)
            {
                if (step.LiquidComposition.TryGetValue(element, out var C_L))
                {
                    row[$"{element}_Liquid_ppm"] = C_L;

                    // Calculate enrichment factor
                    var C_0 = results.Steps[0].LiquidComposition.GetValueOrDefault(element, 1.0);
                    row[$"{element}_Enrichment"] = C_0 > 0 ? C_L / C_0 : 1.0;
                }

                if (step.CumulateComposition.TryGetValue(element, out var C_solid))
                    row[$"{element}_Cumulate_ppm"] = C_solid;
            }

            table.Rows.Add(row);
        }

        return table;
    }
}
