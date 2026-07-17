// GAIA.GeoGenesis/Contaminants/TabularImporter.cs
//
// Imports contaminant / chemical-composition tables from CSV and Excel (.xls/.xlsx). Designed for
// the messy reality of field data: the separator can be auto-detected or chosen by the user, the
// table can be transposed (analytes in rows vs columns), the header row is selectable, and the
// sample-id / X / Y / Z / time columns are mapped explicitly so any layout maps correctly. Multiple
// time steps for the same wells are supported via a time column.

using System.Globalization;
using ClosedXML.Excel;

namespace GAIA.GeoGenesis.Contaminants;

/// <summary>Which columns hold what; everything else listed in <see cref="AnalyteColumns"/> is a concentration.</summary>
public sealed class ColumnMapping
{
    public int HeaderRow { get; set; } = 0;     // 0-based index of the header row in the (possibly transposed) grid
    public int? WellColumn { get; set; }
    public int? XColumn { get; set; }
    public int? YColumn { get; set; }
    public int? ZColumn { get; set; }
    public int? TimeColumn { get; set; }        // optional, for time series
    public List<int> AnalyteColumns { get; set; } = new();
}

public static class TabularImporter
{
    private static readonly char[] CandidateSeparators = { ',', ';', '\t', '|' };

    /// <summary>
    ///     Guess the most likely CSV delimiter by choosing the candidate that splits the first
    ///     non-empty lines into the largest, most consistent number of fields. Returns null if no
    ///     candidate yields more than one column (caller should ask the user).
    /// </summary>
    public static char? DetectSeparator(IEnumerable<string> lines)
    {
        var sample = lines.Where(l => !string.IsNullOrWhiteSpace(l)).Take(20).ToList();
        if (sample.Count == 0) return null;

        char? best = null;
        double bestScore = 0;
        foreach (var sep in CandidateSeparators)
        {
            var counts = sample.Select(l => l.Split(sep).Length).ToList();
            var avg = counts.Average();
            if (avg <= 1) continue;
            // Prefer many columns with low variance (consistent across rows).
            var variance = counts.Select(c => (c - avg) * (c - avg)).Average();
            var score = avg / (1.0 + variance);
            if (score > bestScore) { bestScore = score; best = sep; }
        }
        return best;
    }

    public static char? DetectSeparatorFromFile(string path)
        => DetectSeparator(File.ReadLines(path).Take(20));

    /// <summary>Read a delimited text file into a row-major grid, honouring simple quoted fields.</summary>
    public static List<string[]> ReadCsv(string path, char separator)
        => File.ReadLines(path)
               .Where(l => l.Length > 0)
               .Select(l => SplitDelimited(l, separator))
               .ToList();

    /// <summary>Read one worksheet of an .xls/.xlsx workbook into a row-major grid.</summary>
    public static List<string[]> ReadExcel(string path, int sheetIndex = 0)
    {
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheets.Worksheet(sheetIndex + 1); // ClosedXML is 1-based
        var used = ws.RangeUsed();
        if (used == null) return new List<string[]>();
        int rows = used.RowCount(), cols = used.ColumnCount();
        var grid = new List<string[]>(rows);
        int r0 = used.FirstRow().RowNumber(), c0 = used.FirstColumn().ColumnNumber();
        for (int r = 0; r < rows; r++)
        {
            var row = new string[cols];
            for (int c = 0; c < cols; c++)
                row[c] = ws.Cell(r0 + r, c0 + c).GetString().Trim();
            grid.Add(row);
        }
        return grid;
    }

    /// <summary>Read a CSV or Excel file by extension. For CSV, <paramref name="separatorOverride"/> wins; else auto-detect.</summary>
    public static List<string[]> Read(string path, char? separatorOverride = null, int sheetIndex = 0)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".xlsx" or ".xls") return ReadExcel(path, sheetIndex);
        var sep = separatorOverride ?? DetectSeparatorFromFile(path)
                  ?? throw new InvalidOperationException("Could not detect a CSV separator — please choose one.");
        return ReadCsv(path, sep);
    }

    /// <summary>Transpose a (ragged-safe) grid so rows become columns.</summary>
    public static List<string[]> Transpose(List<string[]> grid)
    {
        if (grid.Count == 0) return new List<string[]>();
        int cols = grid.Max(r => r.Length);
        var result = new List<string[]>(cols);
        for (int c = 0; c < cols; c++)
        {
            var row = new string[grid.Count];
            for (int r = 0; r < grid.Count; r++)
                row[r] = c < grid[r].Length ? grid[r][c] : string.Empty;
            result.Add(row);
        }
        return result;
    }

    /// <summary>Header labels from the mapping's header row.</summary>
    public static string[] HeaderLabels(List<string[]> grid, int headerRow)
        => headerRow >= 0 && headerRow < grid.Count ? grid[headerRow] : Array.Empty<string>();

    /// <summary>Materialise a <see cref="ContaminantDataset"/> from the grid using the column mapping.</summary>
    public static ContaminantDataset Import(List<string[]> grid, ColumnMapping map)
    {
        var ds = new ContaminantDataset();
        if (grid.Count == 0) return ds;

        var header = HeaderLabels(grid, map.HeaderRow);
        string AnalyteName(int col) => col < header.Length && !string.IsNullOrWhiteSpace(header[col]) ? header[col].Trim() : $"col{col}";

        for (int r = map.HeaderRow + 1; r < grid.Count; r++)
        {
            var row = grid[r];
            if (row.All(string.IsNullOrWhiteSpace)) continue;

            string Cell(int? c) => c.HasValue && c.Value >= 0 && c.Value < row.Length ? row[c.Value].Trim() : string.Empty;
            double? Num(int? c)
            {
                var s = Cell(c);
                return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v
                     : double.TryParse(s.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var v2) ? v2
                     : null;
            }

            var sample = new SamplePoint
            {
                Well = map.WellColumn.HasValue ? Cell(map.WellColumn) : $"S{r}",
                X = Num(map.XColumn) ?? 0,
                Y = Num(map.YColumn) ?? 0,
                Z = Num(map.ZColumn) ?? 0,
                TimeDays = Num(map.TimeColumn)
            };

            foreach (var col in map.AnalyteColumns)
            {
                var v = Num(col);
                if (v.HasValue) sample.Concentrations[AnalyteName(col)] = v.Value;
            }

            if (sample.Concentrations.Count > 0 || map.WellColumn.HasValue)
                ds.Samples.Add(sample);
        }
        return ds;
    }

    private static string[] SplitDelimited(string line, char sep)
    {
        var fields = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool inQuotes = false;
        foreach (var ch in line)
        {
            if (ch == '"') inQuotes = !inQuotes;
            else if (ch == sep && !inQuotes) { fields.Add(sb.ToString().Trim()); sb.Clear(); }
            else sb.Append(ch);
        }
        fields.Add(sb.ToString().Trim());
        return fields.ToArray();
    }
}
