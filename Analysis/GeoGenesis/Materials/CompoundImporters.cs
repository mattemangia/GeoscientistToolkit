// GAIA.GeoGenesis/Materials/CompoundImporters.cs
//
// Importers that bring external thermodynamic / mineralogical data into the GeoGenesis compound
// library. Three community-standard interchange formats are supported:
//
//   • PHREEQC database (.dat)  — the USGS PHREEQC PHASES block (mineral = products; log_k; delta_h).
//                                Reference: Parkhurst & Appelo (2013), USGS TM 6-A43.
//   • CSV                       — a flat table of compound properties (flexible header mapping).
//   • CIF (.cif)                — Crystallographic Information File mineral structures
//                                (IUCr standard; tags _chemical_name_mineral, _chemical_formula_sum,
//                                _cell_volume, _exptl_crystal_density_diffrn, space-group/crystal system).
//
// All importers are pure (no UI, no globals) and return ChemicalCompound lists so they can be unit
// tested and reused by the CLI; the PRISM browser merges the results into CompoundLibrary.Instance.

using System.Globalization;
using System.Text.RegularExpressions;

namespace GAIA.GeoGenesis.Materials;

public static class CompoundImporters
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Detect the format from the file extension and import accordingly.</summary>
    public static List<ChemicalCompound> ImportFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".dat" or ".phr" or ".phreeqc" => ImportPhreeqcDatabase(path),
            ".csv" or ".tsv" or ".txt" => ImportCsv(path),
            ".cif" => new List<ChemicalCompound> { ImportCif(path) },
            _ => throw new NotSupportedException($"Unsupported import format '{ext}'. Use .dat (PHREEQC), .csv or .cif.")
        };
    }

    // ---------------------------------------------------------------------------------------------
    // PHREEQC database — PHASES block
    // ---------------------------------------------------------------------------------------------
    public static List<ChemicalCompound> ImportPhreeqcDatabase(string path)
        => ParsePhreeqcPhases(File.ReadAllLines(path));

    /// <summary>
    /// Parse the PHASES block of a PHREEQC database. Each phase is:
    ///   PhaseName
    ///       Mineral = product1 + product2 + ...        (the dissolution reaction)
    ///       log_k    -8.48
    ///       delta_h  -2.297 kcal     (optional; kcal or kJ)
    /// </summary>
    public static List<ChemicalCompound> ParsePhreeqcPhases(IReadOnlyList<string> lines)
    {
        var result = new List<ChemicalCompound>();
        bool inPhases = false;
        ChemicalCompound? current = null;

        foreach (var raw in lines)
        {
            var line = StripComment(raw);
            if (line.Length == 0) continue;

            var trimmed = line.Trim();

            // Block headers start at column 0 and are all-caps keywords.
            if (!char.IsWhiteSpace(line[0]) && Regex.IsMatch(trimmed, @"^[A-Z_]+$"))
            {
                inPhases = trimmed == "PHASES";
                current = null;
                continue;
            }
            if (!inPhases) continue;

            // A phase NAME line: starts at column 0, is not a keyword/equation.
            if (!char.IsWhiteSpace(line[0]) && !trimmed.Contains('='))
            {
                current = new ChemicalCompound
                {
                    Name = trimmed,
                    Phase = CompoundPhase.Solid,
                    IsUserCompound = true,
                    Sources = { "PHREEQC database import" }
                };
                result.Add(current);
                continue;
            }

            if (current == null) continue;

            // Indented data lines belonging to the current phase.
            if (trimmed.Contains('=') && string.IsNullOrEmpty(current.ChemicalFormula))
            {
                // Dissolution equation: the left-hand side is the mineral formula.
                var lhs = trimmed.Split('=')[0].Trim();
                // Drop a possible leading stoichiometric coefficient.
                var m = Regex.Match(lhs, @"^\s*\d*\.?\d*\s*([A-Za-z(].*)$");
                current.ChemicalFormula = (m.Success ? m.Groups[1].Value : lhs).Trim();
            }
            else if (trimmed.StartsWith("log_k", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("logk", StringComparison.OrdinalIgnoreCase))
            {
                if (TryFirstNumber(trimmed, out var logk)) current.LogKsp_25C = logk;
            }
            else if (trimmed.StartsWith("delta_h", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("-delta_h", StringComparison.OrdinalIgnoreCase))
            {
                if (TryFirstNumber(trimmed, out var dh))
                {
                    // PHREEQC delta_h is in kJ unless "kcal" is present.
                    if (trimmed.Contains("kcal", StringComparison.OrdinalIgnoreCase)) dh *= 4.184;
                    current.DissolutionEnthalpy_kJ_mol = dh;
                }
            }
        }

        return result.Where(c => !string.IsNullOrWhiteSpace(c.ChemicalFormula)).ToList();
    }

    // ---------------------------------------------------------------------------------------------
    // CSV
    // ---------------------------------------------------------------------------------------------
    /// <summary>
    /// Import compounds from a delimited table. The header row names the columns; recognised
    /// (case/space/underscore-insensitive) column names map onto ChemicalCompound properties:
    ///   name, formula, phase, molarmass/molecularweight, density, logksp/logk,
    ///   gibbs/gibbsformation, enthalpy/enthalpyformation, entropy, charge/ioniccharge.
    /// Only name and formula are required.
    /// </summary>
    public static List<ChemicalCompound> ImportCsv(string path)
    {
        var lines = File.ReadAllLines(path).Where(l => l.Trim().Length > 0).ToList();
        if (lines.Count < 2) return new List<ChemicalCompound>();

        char delim = lines[0].Contains('\t') ? '\t' : (lines[0].Contains(';') && !lines[0].Contains(',') ? ';' : ',');
        var header = SplitCsv(lines[0], delim).Select(NormalizeKey).ToList();
        int Col(params string[] names) => header.FindIndex(h => names.Contains(h));

        int iName = Col("name", "compound", "mineral");
        int iFormula = Col("formula", "chemicalformula", "formulasum");
        int iPhase = Col("phase", "state");
        int iMass = Col("molarmass", "molecularweight", "molarmassgmol", "mw");
        int iDensity = Col("density", "densitygcm3");
        int iLogKsp = Col("logksp", "logk", "logksp25c");
        int iGibbs = Col("gibbs", "gibbsformation", "dgf", "deltagf");
        int iEnthalpy = Col("enthalpy", "enthalpyformation", "dhf", "deltahf");
        int iEntropy = Col("entropy", "s");
        int iCharge = Col("charge", "ioniccharge");

        var result = new List<ChemicalCompound>();
        foreach (var line in lines.Skip(1))
        {
            var f = SplitCsv(line, delim);
            string Get(int i) => i >= 0 && i < f.Count ? f[i].Trim() : string.Empty;
            double? Num(int i) => double.TryParse(Get(i), NumberStyles.Any, Inv, out var v) ? v : null;
            int? IntNum(int i) => int.TryParse(Get(i), NumberStyles.Any, Inv, out var v) ? v : null;

            var name = Get(iName);
            var formula = Get(iFormula);
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(formula)) continue;

            var c = new ChemicalCompound
            {
                Name = string.IsNullOrWhiteSpace(name) ? formula : name,
                ChemicalFormula = formula,
                IsUserCompound = true,
                Sources = { "CSV import" },
                MolecularWeight_g_mol = Num(iMass),
                Density_g_cm3 = Num(iDensity),
                LogKsp_25C = Num(iLogKsp),
                GibbsFreeEnergyFormation_kJ_mol = Num(iGibbs),
                EnthalpyFormation_kJ_mol = Num(iEnthalpy),
                Entropy_J_molK = Num(iEntropy),
                IonicCharge = IntNum(iCharge)
            };
            var phaseStr = Get(iPhase);
            if (Enum.TryParse<CompoundPhase>(phaseStr, true, out var ph)) c.Phase = ph;
            else if (c.IonicCharge is not null && c.IonicCharge != 0) c.Phase = CompoundPhase.Aqueous;
            result.Add(c);
        }
        return result;
    }

    // ---------------------------------------------------------------------------------------------
    // CIF (Crystallographic Information File)
    // ---------------------------------------------------------------------------------------------
    /// <summary>
    /// Import a single mineral structure from a CIF file. Extracts the mineral name, sum formula,
    /// crystal system, density and molar mass where present, and derives molar volume from the
    /// unit-cell volume and Z when available.
    /// </summary>
    public static ChemicalCompound ImportCif(string path) => ParseCif(File.ReadAllLines(path), Path.GetFileNameWithoutExtension(path));

    public static ChemicalCompound ParseCif(IReadOnlyList<string> lines, string fallbackName)
    {
        string? Tag(params string[] keys)
        {
            foreach (var line in lines)
            {
                var t = line.Trim();
                foreach (var key in keys)
                {
                    if (t.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                    {
                        var rest = t.Substring(key.Length).Trim();
                        if (rest.Length == 0) continue;
                        return rest.Trim('\'', '"', ' ');
                    }
                }
            }
            return null;
        }

        double? TagNum(params string[] keys)
        {
            var s = Tag(keys);
            if (s == null) return null;
            // CIF numeric values may carry an uncertainty in parentheses, e.g. "6.361(2)".
            s = Regex.Replace(s, @"\(.*\)", "");
            return double.TryParse(s, NumberStyles.Any, Inv, out var v) ? v : null;
        }

        var name = Tag("_chemical_name_mineral", "_chemical_name_systematic", "_chemical_name_common");
        var formula = Tag("_chemical_formula_sum", "_chemical_formula_structural", "_chemical_formula_moiety");
        if (formula != null) formula = formula.Replace(" ", string.Empty);

        var compound = new ChemicalCompound
        {
            Name = string.IsNullOrWhiteSpace(name) ? fallbackName : name!,
            ChemicalFormula = formula ?? string.Empty,
            Phase = CompoundPhase.Solid,
            IsUserCompound = true,
            Sources = { "CIF import" },
            MolecularWeight_g_mol = TagNum("_chemical_formula_weight"),
            Density_g_cm3 = TagNum("_exptl_crystal_density_diffrn", "_exptl_crystal_density_meas")
        };

        var cellVol = TagNum("_cell_volume");                 // Å³
        var z = TagNum("_cell_formula_units_Z");
        if (cellVol is > 0 && z is > 0)
        {
            // Molar volume (cm³/mol) = V_cell[Å³] * 1e-24 * N_A / Z
            compound.MolarVolume_cm3_mol = cellVol.Value * 1e-24 * 6.02214076e23 / z.Value;
        }

        var system = Tag("_space_group_crystal_system", "_symmetry_cell_setting");
        if (system != null && Enum.TryParse<CrystalSystem>(system.Trim(), true, out var cs))
            compound.CrystalSystem = cs;
        else
        {
            var hm = Tag("_symmetry_space_group_name_H-M", "_space_group_name_H-M_alt");
            if (hm != null) compound.CrystalSystem = CrystalSystemFromHermannMauguin(hm);
        }

        return compound;
    }

    private static CrystalSystem? CrystalSystemFromHermannMauguin(string hm)
    {
        hm = hm.Replace(" ", string.Empty).ToUpperInvariant();
        if (hm.Length == 0) return null;
        // Coarse classification from the lattice symbol / symmetry directions.
        if (hm.StartsWith("P") || hm.StartsWith("F") || hm.StartsWith("I") || hm.StartsWith("C") ||
            hm.StartsWith("A") || hm.StartsWith("R"))
        {
            if (hm.StartsWith("R")) return CrystalSystem.Trigonal;
        }
        return null; // leave unset when it cannot be inferred reliably
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------
    private static string StripComment(string line)
    {
        var i = line.IndexOf('#');
        return i >= 0 ? line.Substring(0, i) : line;
    }

    private static bool TryFirstNumber(string s, out double value)
    {
        var m = Regex.Match(s, @"[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?");
        if (m.Success && double.TryParse(m.Value, NumberStyles.Any, Inv, out value)) return true;
        value = 0;
        return false;
    }

    private static string NormalizeKey(string s) =>
        new string(s.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static List<string> SplitCsv(string line, char delim)
    {
        var fields = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool inQuotes = false;
        foreach (var ch in line)
        {
            if (ch == '"') inQuotes = !inQuotes;
            else if (ch == delim && !inQuotes) { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        fields.Add(sb.ToString());
        return fields;
    }
}
