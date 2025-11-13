// GeoscientistToolkit/Business/GeoScript/GeoScriptThermodynamicsExtensions.cs
//
// Additional thermodynamics commands for GeoScript:
// - CALCULATE_PHASES: Separates equilibrium results into solid, aqueous, and gas phases
// - CALCULATE_CARBONATE_ALKALINITY: Calculates HCO3-/CO3- from total alkalinity
// - Enhanced REACT command with phase separation and mineral names
//
// THEORETICAL FOUNDATION:
// - Parkhurst & Appelo (2013): PHREEQC version 3 - User's guide
// - Stumm & Morgan (1996): Aquatic Chemistry, 3rd ed., Chapter 3 (Carbonate system)
// - Butler (1982): Carbon Dioxide Equilibria and Their Applications, Chapter 6 (Alkalinity)

using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Business.Thermodynamics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Data.Table;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business;

/// <summary>
///     CALCULATE_PHASES: Separates thermodynamic equilibrium results into phases.
///     For each sample, calculates what is in solid phase, aqueous phase, and gas phase.
/// </summary>
public class CalculatePhasesCommand : IGeoScriptCommand
{
    public string Name => "CALCULATE_PHASES";
    public string HelpText => "Separates equilibrium results into solid, aqueous, and gas phases.";
    public string Usage => "CALCULATE_PHASES";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not TableDataset tableDs)
            throw new NotSupportedException("CALCULATE_PHASES only works on Table Datasets.");

        var solver = new ThermodynamicSolver();
        var compoundLib = CompoundLibrary.Instance;
        var reactionGen = new ReactionGenerator(compoundLib);

        var sourceTable = tableDs.GetDataTable();
        var resultTable = new DataTable($"{tableDs.Name}_Phases");

        // Add base columns
        resultTable.Columns.Add("SampleID", typeof(int));
        resultTable.Columns.Add("Temperature_K", typeof(double));
        resultTable.Columns.Add("Pressure_bar", typeof(double));
        resultTable.Columns.Add("pH", typeof(double));
        resultTable.Columns.Add("pe", typeof(double));
        resultTable.Columns.Add("IonicStrength_molkg", typeof(double));

        // Phase summary columns
        resultTable.Columns.Add("SolidPhase_g", typeof(double));
        resultTable.Columns.Add("AqueousPhase_g", typeof(double));
        resultTable.Columns.Add("GasPhase_g", typeof(double));
        resultTable.Columns.Add("SolidMinerals", typeof(string));
        resultTable.Columns.Add("AqueousIons", typeof(string));
        resultTable.Columns.Add("Gases", typeof(string));

        var sampleId = 0;
        foreach (DataRow row in sourceTable.Rows)
        {
            sampleId++;

            // Parse temperature and pressure if available
            var tempK = 298.15;
            var presBar = 1.0;

            if (sourceTable.Columns.Contains("Temperature_K") &&
                double.TryParse(row["Temperature_K"].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture,
                    out var temp))
                tempK = temp;
            else if (sourceTable.Columns.Contains("Temperature_C") &&
                     double.TryParse(row["Temperature_C"].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture,
                         out var tempC))
                tempK = tempC + 273.15;

            if (sourceTable.Columns.Contains("Pressure_bar") &&
                double.TryParse(row["Pressure_bar"].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture,
                    out var pres))
                presBar = pres;

            // Create thermodynamic state from row
            var initialState = CreateStateFromDataRow(row, tempK, presBar, compoundLib, reactionGen);

            // Solve for equilibrium
            ThermodynamicState finalState;
            try
            {
                finalState = solver.SolveEquilibrium(initialState);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[CALCULATE_PHASES] Sample {sampleId} equilibration failed: {ex.Message}");
                continue;
            }

            // Separate phases
            var solidMass = 0.0;
            var aqueousMass = 0.0;
            var gasMass = 0.0;
            var solidMinerals = new List<string>();
            var aqueousIons = new List<string>();
            var gases = new List<string>();

            foreach (var (speciesName, moles) in finalState.SpeciesMoles)
            {
                if (moles < 1e-12) continue; // Ignore trace amounts

                var compound = compoundLib.Find(speciesName);
                if (compound == null) continue;

                var mass = moles * (compound.MolecularWeight_g_mol ?? 0);

                switch (compound.Phase)
                {
                    case CompoundPhase.Solid:
                        solidMass += mass;
                        solidMinerals.Add($"{speciesName}({moles:E2} mol)");
                        break;
                    case CompoundPhase.Aqueous:
                        aqueousMass += mass;
                        aqueousIons.Add($"{speciesName}({moles:E2} mol)");
                        break;
                    case CompoundPhase.Gas:
                        gasMass += mass;
                        gases.Add($"{speciesName}({moles:E2} mol)");
                        break;
                    case CompoundPhase.Liquid:
                        // Water is usually the solvent
                        aqueousMass += mass;
                        break;
                }
            }

            // Add result row
            var resultRow = resultTable.NewRow();
            resultRow["SampleID"] = sampleId;
            resultRow["Temperature_K"] = tempK;
            resultRow["Pressure_bar"] = presBar;
            resultRow["pH"] = finalState.pH;
            resultRow["pe"] = finalState.pe;
            resultRow["IonicStrength_molkg"] = finalState.IonicStrength_molkg;
            resultRow["SolidPhase_g"] = solidMass;
            resultRow["AqueousPhase_g"] = aqueousMass;
            resultRow["GasPhase_g"] = gasMass;
            resultRow["SolidMinerals"] = solidMinerals.Count > 0 ? string.Join("; ", solidMinerals) : "None";
            resultRow["AqueousIons"] = aqueousIons.Count > 0 ? string.Join("; ", aqueousIons) : "None";
            resultRow["Gases"] = gases.Count > 0 ? string.Join("; ", gases) : "None";

            resultTable.Rows.Add(resultRow);
        }

        return Task.FromResult<Dataset>(new TableDataset($"{tableDs.Name}_Phases", resultTable));
    }

    private ThermodynamicState CreateStateFromDataRow(DataRow row, double temperatureK, double pressureBar,
        CompoundLibrary compoundLib, ReactionGenerator reactionGen)
    {
        var state = new ThermodynamicState
        {
            Temperature_K = temperatureK,
            Pressure_bar = pressureBar,
            Volume_L = 1.0
        };

        foreach (DataColumn col in row.Table.Columns)
            if (double.TryParse(row[col].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var rawValue))
            {
                var speciesName = col.ColumnName.Split(' ', '(', '[', '_')[0].Trim();
                var compound = compoundLib.Find(speciesName);

                if (compound != null)
                {
                    var moles = ConvertToMoles(col.ColumnName, rawValue, compoundLib);
                    var composition = reactionGen.ParseChemicalFormula(compound.ChemicalFormula);

                    foreach (var (element, stoichiometry) in composition)
                    {
                        var molesOfElement = moles * stoichiometry;
                        state.ElementalComposition[element] =
                            state.ElementalComposition.GetValueOrDefault(element, 0) + molesOfElement;
                    }

                    state.SpeciesMoles[compound.Name] = moles;
                }
            }

        return state;
    }

    private double ConvertToMoles(string columnName, double value, CompoundLibrary compoundLib)
    {
        var unitMatch = Regex.Match(columnName, @"[\(\[_](?<unit>.+)[\)\]_]?");
        var speciesName = columnName.Split(' ', '(', '[', '_')[0].Trim();

        if (!unitMatch.Success)
            return value; // Assume mol/L

        var unit = unitMatch.Groups["unit"].Value.Trim().ToLower();
        var compound = compoundLib.Find(speciesName);
        var molarMass_g_mol = compound?.MolecularWeight_g_mol ?? 1.0;
        if (molarMass_g_mol == 0) molarMass_g_mol = 1.0;

        return unit switch
        {
            "mg/l" or "ppm" => value / 1000.0 / molarMass_g_mol,
            "ug/l" or "ppb" => value / 1_000_000.0 / molarMass_g_mol,
            "g/l" => value / molarMass_g_mol,
            "mol/l" or "m" => value,
            "mmol/l" => value / 1000.0,
            "umol/l" => value / 1_000_000.0,
            _ => value
        };
    }
}

/// <summary>
///     CALCULATE_CARBONATE_ALKALINITY: Calculates HCO3- and CO3-2 from total alkalinity and pH.
///     Uses the carbonate equilibrium system (H2CO3/HCO3-/CO3-2) to speciate total alkalinity.
/// </summary>
public class CalculateCarbonateAlkalinityCommand : IGeoScriptCommand
{
    public string Name => "CALCULATE_CARBONATE_ALKALINITY";

    public string HelpText =>
        "Calculates HCO3- and CO3-- from total alkalinity, pH, and temperature using carbonate equilibria.";

    public string Usage =>
        "CALCULATE_CARBONATE_ALKALINITY ALKALINITY_COL 'ColumnName' [TEMP_COL 'TempColumn'] [PH_COL 'pHColumn']";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not TableDataset tableDs)
            throw new NotSupportedException("CALCULATE_CARBONATE_ALKALINITY only works on Table Datasets.");

        var cmd = (CommandNode)node;

        // Parse command arguments
        var alkColMatch = Regex.Match(cmd.FullText, @"ALKALINITY_COL\s+'([^']+)'", RegexOptions.IgnoreCase);
        var tempColMatch = Regex.Match(cmd.FullText, @"TEMP_COL\s+'([^']+)'", RegexOptions.IgnoreCase);
        var phColMatch = Regex.Match(cmd.FullText, @"PH_COL\s+'([^']+)'", RegexOptions.IgnoreCase);

        if (!alkColMatch.Success)
            throw new ArgumentException(
                "ALKALINITY_COL is required. Usage: CALCULATE_CARBONATE_ALKALINITY ALKALINITY_COL 'ColumnName'");

        var alkCol = alkColMatch.Groups[1].Value;
        var tempCol = tempColMatch.Success ? tempColMatch.Groups[1].Value : null;
        var phCol = phColMatch.Success ? phColMatch.Groups[1].Value : "pH";

        var sourceTable = tableDs.GetDataTable();
        if (!sourceTable.Columns.Contains(alkCol))
            throw new ArgumentException($"Alkalinity column '{alkCol}' not found.");
        if (!sourceTable.Columns.Contains(phCol))
            throw new ArgumentException($"pH column '{phCol}' not found.");

        var resultTable = sourceTable.Copy();
        resultTable.Columns.Add("HCO3-_mol_L", typeof(double));
        resultTable.Columns.Add("CO3-2_mol_L", typeof(double));
        resultTable.Columns.Add("H2CO3_mol_L", typeof(double));
        resultTable.Columns.Add("DIC_mol_L", typeof(double)); // Dissolved Inorganic Carbon

        foreach (DataRow row in resultTable.Rows)
        {
            // Get alkalinity (in eq/L or mg/L as CaCO3)
            if (!double.TryParse(row[alkCol].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture,
                    out var alkalinity))
                continue;

            // Get pH
            if (!double.TryParse(row[phCol].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var pH))
                continue;

            // Get temperature (default 25°C)
            var tempC = 25.0;
            if (tempCol != null && sourceTable.Columns.Contains(tempCol) &&
                double.TryParse(row[tempCol].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var t))
                tempC = t;

            var tempK = tempC + 273.15;

            // Convert alkalinity from mg/L as CaCO3 to eq/L if needed
            // (Common unit for alkalinity is mg/L as CaCO3)
            // 1 meq/L = 50 mg/L as CaCO3
            // So: alkalinity_eq_L = alkalinity_mg_L / 50000
            var alkalinity_eq_L = alkalinity / 50000.0; // Assume input is mg/L as CaCO3

            // Calculate carbonate species using equilibrium constants
            // pKa1 (H2CO3 <=> HCO3- + H+) ~ 6.35 at 25°C
            // pKa2 (HCO3- <=> CO3-2 + H+) ~ 10.33 at 25°C

            // Temperature correction for pKa (simplified van't Hoff)
            var pKa1 = CalculatepKa1(tempK);
            var pKa2 = CalculatepKa2(tempK);

            var Ka1 = Math.Pow(10, -pKa1);
            var Ka2 = Math.Pow(10, -pKa2);
            var H = Math.Pow(10, -pH);

            // Alkalinity equation (for carbonate system):
            // Alk = [HCO3-] + 2*[CO3-2] + [OH-] - [H+]
            // For simplicity, we assume [OH-] - [H+] is small compared to carbonate alkalinity
            // So: Alk ≈ [HCO3-] + 2*[CO3-2]

            // Alpha factors for carbonate speciation:
            // α0 = [H2CO3*]/DIC = H^2 / (H^2 + H*Ka1 + Ka1*Ka2)
            // α1 = [HCO3-]/DIC = H*Ka1 / (H^2 + H*Ka1 + Ka1*Ka2)
            // α2 = [CO3-2]/DIC = Ka1*Ka2 / (H^2 + H*Ka1 + Ka1*Ka2)

            var denominator = H * H + H * Ka1 + Ka1 * Ka2;
            var alpha0 = H * H / denominator; // H2CO3*
            var alpha1 = H * Ka1 / denominator; // HCO3-
            var alpha2 = Ka1 * Ka2 / denominator; // CO3-2

            // From alkalinity: Alk = DIC * (α1 + 2*α2)
            // So: DIC = Alk / (α1 + 2*α2)
            var DIC = alkalinity_eq_L / (alpha1 + 2.0 * alpha2);

            var H2CO3_conc = DIC * alpha0;
            var HCO3_conc = DIC * alpha1;
            var CO3_conc = DIC * alpha2;

            row["HCO3-_mol_L"] = HCO3_conc;
            row["CO3-2_mol_L"] = CO3_conc;
            row["H2CO3_mol_L"] = H2CO3_conc;
            row["DIC_mol_L"] = DIC;
        }

        return Task.FromResult<Dataset>(new TableDataset($"{tableDs.Name}_Carbonate", resultTable));
    }

    /// <summary>
    ///     Calculate pKa1 for carbonic acid at a given temperature.
    ///     Based on Millero (1995) fit for seawater, applicable to freshwater as well.
    ///     Millero, F.J., 1995. Thermodynamics of the carbon dioxide system in the oceans.
    ///     Geochimica et Cosmochimica Acta, 59(4), 661-677.
    /// </summary>
    private double CalculatepKa1(double tempK)
    {
        var T = tempK;
        var T_inv = 1.0 / T;

        // Millero (1995) equation for pKa1 (simplified for freshwater, S=0):
        // pK1 = -126.34048 + 6320.813/T + 19.568224*ln(T)
        var pKa1 = -126.34048 + 6320.813 * T_inv + 19.568224 * Math.Log(T);

        return pKa1;
    }

    /// <summary>
    ///     Calculate pKa2 for bicarbonate at a given temperature.
    ///     Based on Millero (1995).
    /// </summary>
    private double CalculatepKa2(double tempK)
    {
        var T = tempK;
        var T_inv = 1.0 / T;

        // Millero (1995) equation for pKa2 (simplified for freshwater, S=0):
        // pK2 = -90.18333 + 5143.692/T + 14.613358*ln(T)
        var pKa2 = -90.18333 + 5143.692 * T_inv + 14.613358 * Math.Log(T);

        return pKa2;
    }
}

/// <summary>
///     Extension to register the new commands in the CommandRegistry.
/// </summary>
public static class ThermodynamicsCommandRegistryExtensions
{
    public static void RegisterThermodynamicsExtensions()
    {
        // This will be called from the main CommandRegistry static constructor
        // For now, we manually register by modifying GeoScript.cs
    }
}
