// GAIA.GeoGenesis/Reactor/ReactorPersistence.cs
//
// Save / load reactor runs (configuration + recorded frames) so results can be reviewed later.
// Results are written as JSON; within a PRISM dataset they live under <dataset>/GeoGenesis/reactor
// (the same convention GeoGenesis/Aquifer dataset artefacts use), so they travel with the project.

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GAIA.GeoGenesis.Reactor;

public static class ReactorPersistence
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        IncludeFields = false
    };

    public const string FileExtension = ".ggreactor.json";

    /// <summary>Suffix of the persistent convergence log written next to each saved run.</summary>
    public const string ConvergenceLogExtension = ".convergence.csv";

    /// <summary>Directory where reactor runs for a PRISM dataset are stored (created on demand).</summary>
    public static string DatasetReactorDirectory(string datasetPath)
    {
        var dir = Path.Combine(datasetPath, "GeoGenesis", "reactor");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string Save(ReactorResult result, string path)
    {
        if (!path.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase) &&
            !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            path += FileExtension;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        using (var fs = File.Create(path))
            JsonSerializer.Serialize(fs, result, Options);
        // The convergence log travels with the run as a sibling CSV so it is persistent and
        // inspectable in the dataset without re-opening the (large) frame file.
        File.WriteAllText(ConvergenceLogPath(path), BuildConvergenceCsv(result));
        return path;
    }

    /// <summary>Path of the convergence log that <see cref="Save"/> writes alongside a run file.</summary>
    public static string ConvergenceLogPath(string runPath)
    {
        var dir = Path.GetDirectoryName(runPath) ?? string.Empty;
        var name = Path.GetFileName(runPath);
        if (name.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase))
            name = name[..^FileExtension.Length];
        else
            name = Path.GetFileNameWithoutExtension(name);
        return Path.Combine(dir, name + ConvergenceLogExtension);
    }

    /// <summary>
    /// Build the convergence log: one row per recorded frame, one column per variable holding the
    /// relative L2 change vs. the previous frame (see <see cref="ReactorResult.ConvergenceSeries"/>).
    /// </summary>
    public static string BuildConvergenceCsv(ReactorResult result)
    {
        var vars = result.Variables;
        var series = vars.ToDictionary(v => v, result.ConvergenceSeries);
        var sb = new StringBuilder();
        sb.Append("frame,time_days");
        foreach (var v in vars) { sb.Append(','); sb.Append(v); }
        sb.Append('\n');
        for (int f = 0; f < result.Frames.Count; f++)
        {
            sb.Append(f.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(result.Frames[f].TimeDays.ToString("G6", CultureInfo.InvariantCulture));
            foreach (var v in vars)
            {
                sb.Append(',');
                sb.Append(series[v][f].ToString("G6", CultureInfo.InvariantCulture));
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Save into a PRISM dataset's GeoGenesis/reactor folder under <paramref name="name"/>.</summary>
    public static string SaveToDataset(ReactorResult result, string datasetPath, string name)
    {
        var safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        return Save(result, Path.Combine(DatasetReactorDirectory(datasetPath), safe + FileExtension));
    }

    public static ReactorResult Load(string path)
    {
        using var fs = File.OpenRead(path);
        return JsonSerializer.Deserialize<ReactorResult>(fs, Options)
               ?? throw new InvalidDataException($"Could not read reactor result from '{path}'.");
    }

    /// <summary>List saved reactor runs (full paths) for a PRISM dataset.</summary>
    public static IReadOnlyList<string> ListDatasetRuns(string datasetPath)
    {
        var dir = Path.Combine(datasetPath, "GeoGenesis", "reactor");
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        return Directory.EnumerateFiles(dir, "*" + FileExtension).OrderBy(f => f).ToList();
    }
}
