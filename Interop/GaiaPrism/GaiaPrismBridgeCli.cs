using System.Text.Json;

namespace GAIA.Interop.GaiaPrism;

public static class GaiaPrismBridgeCli
{
    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0 || !args[0].StartsWith("--bridge-", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            if (args[0].Equals("--bridge-validate", StringComparison.OrdinalIgnoreCase))
            {
                RequirePath(args);
                var manifest = GpexArchive.ReadAndValidate(args[1]);
                Console.WriteLine($"VALID {manifest.SchemaId}/{manifest.SchemaVersion} exchange={manifest.ExchangeId} domain={manifest.Domain}");
                return true;
            }
            if (args[0].Equals("--bridge-inspect", StringComparison.OrdinalIgnoreCase))
            {
                RequirePath(args);
                var manifest = GpexArchive.ReadAndValidate(args[1]);
                Console.WriteLine(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
                return true;
            }
            if (args[0].Equals("--bridge-extract", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length < 3) throw new ArgumentException("Usage: --bridge-extract <package.gpex> <directory>");
                GpexArchive.ExtractArtifacts(args[1], args[2]);
                Console.WriteLine($"Extracted validated package to {Path.GetFullPath(args[2])}");
                return true;
            }
            if (args[0].Equals("--bridge-wells", StringComparison.OrdinalIgnoreCase))
            {
                RequirePath(args);
                var package = UpscalingGpexImporter.Read(args[1]);
                Console.WriteLine($"Upscaling exchange {package.Manifest.ExchangeId} direction={package.Manifest.Direction} wells={package.Wells.Wells.Count} pnm={package.PnmSummaries.Count}");
                foreach (var message in package.Validation.Messages)
                    Console.WriteLine($"  {message.Severity}: {message.Code}: {message.Message}");
                foreach (var well in package.Wells.Wells)
                {
                    var zone = IntervalUpscaler.UpscaleLayers(well.Intervals);
                    Console.WriteLine($"WELL {well.Name} ({well.Id}) intervals={well.Intervals.Count} logs={well.Logs.Count} thickness={zone.ThicknessMetres:g6} m");
                    if (zone.PorosityFraction is { } phi) Console.WriteLine($"  zone porosity           = {phi:g6}");
                    if (zone.HorizontalPermeabilityMilliDarcy is { } kh) Console.WriteLine($"  zone kh (arithmetic)    = {kh:g6} mD");
                    if (zone.VerticalPermeabilityMilliDarcy is { } kv) Console.WriteLine($"  zone kv (harmonic)      = {kv:g6} mD");
                    foreach (var interval in well.Intervals.OrderBy(i => i.TopDepthMetres))
                    {
                        var permeability = IntervalUpscaler.PermeabilityMilliDarcy(interval);
                        Console.WriteLine($"  {interval.TopDepthMetres,8:g6}-{interval.BottomDepthMetres,-8:g6} m {interval.Lithology ?? "?",-16} " +
                                          $"phi={IntervalUpscaler.ScalarProperty(interval, UpscalingPropertyNames.Porosity)?.ToString("g4") ?? "-"} " +
                                          $"k={permeability?.ToString("g4") ?? "-"} mD pnm={interval.PnmAssignments.Count}");
                    }
                }
                return true;
            }
            if (args[0].Equals("--bridge-send-prism", StringComparison.OrdinalIgnoreCase))
            {
                RequirePath(args);
                var exploratory = args.Any(a => a.Equals("--allow-exploratory", StringComparison.OrdinalIgnoreCase));
                var result = PrismProcessClient.ImportMaterialAsync(args[1], exploratory).GetAwaiter().GetResult();
                Console.Write(result.StandardOutput); Console.Error.Write(result.StandardError);
                exitCode = result.ExitCode;
                return true;
            }
            throw new ArgumentException($"Unknown bridge command: {args[0]}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"GAIA–PRISM bridge error: {ex.Message}");
            exitCode = 2;
            return true;
        }
    }

    private static void RequirePath(string[] args)
    {
        if (args.Length < 2) throw new ArgumentException($"Usage: {args[0]} <package.gpex>");
    }
}
