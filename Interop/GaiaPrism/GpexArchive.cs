using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace GAIA.Interop.GaiaPrism;

public static class GpexArchive
{
    public const long DefaultMaximumEntryBytes = 2L * 1024 * 1024 * 1024;
    public const long DefaultMaximumPackageBytes = 4L * 1024 * 1024 * 1024;

    public static void Write(string destination, ExchangeManifest manifest,
        IReadOnlyDictionary<string, string>? artifactSources = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ValidateManifest(manifest);
        var target = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var artifacts = new List<ArtifactReference>();
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                if (artifactSources != null)
                {
                    foreach (var pair in artifactSources.OrderBy(p => p.Key, StringComparer.Ordinal))
                    {
                        var relative = NormalizePayloadPath(pair.Key);
                        var source = Path.GetFullPath(pair.Value);
                        var info = new FileInfo(source);
                        if (!info.Exists) throw new FileNotFoundException("GPEX artifact was not found.", source);
                        if (info.Length > DefaultMaximumEntryBytes) throw new InvalidDataException($"Artifact exceeds the configured entry limit: {source}");
                        var entry = zip.CreateEntry(relative, CompressionLevel.Optimal);
                        using var input = File.OpenRead(source);
                        using var output = entry.Open();
                        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                        var buffer = new byte[81920];
                        long length = 0;
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            output.Write(buffer, 0, read);
                            hash.AppendData(buffer, 0, read);
                            length = checked(length + read);
                        }
                        artifacts.Add(new ArtifactReference
                        {
                            Id = Path.GetFileNameWithoutExtension(relative), Path = relative,
                            MediaType = MediaTypeFor(relative), Length = length,
                            Sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()
                        });
                    }
                }
                var finalManifest = manifest with { Artifacts = artifacts };
                var manifestEntry = zip.CreateEntry(GaiaPrismExchangeSchema.ManifestEntryName, CompressionLevel.Optimal);
                using var writer = manifestEntry.Open();
                JsonSerializer.Serialize(writer, finalManifest, GaiaPrismExchangeSchema.JsonOptions);
            }
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static ExchangeManifest ReadAndValidate(string packagePath,
        long maximumEntryBytes = DefaultMaximumEntryBytes,
        long maximumPackageBytes = DefaultMaximumPackageBytes)
    {
        var fullPath = Path.GetFullPath(packagePath);
        var info = new FileInfo(fullPath);
        if (!info.Exists) throw new FileNotFoundException("GPEX package was not found.", fullPath);
        if (info.Length > maximumPackageBytes) throw new InvalidDataException("GPEX package exceeds the configured size limit.");
        using var zip = ZipFile.OpenRead(fullPath);
        var entries = ValidateEntries(zip, maximumEntryBytes);
        if (!entries.TryGetValue(GaiaPrismExchangeSchema.ManifestEntryName, out var manifestEntry))
            throw new InvalidDataException("GPEX package has no manifest.json.");
        using var manifestStream = manifestEntry.Open();
        var manifest = JsonSerializer.Deserialize<ExchangeManifest>(manifestStream, GaiaPrismExchangeSchema.JsonOptions)
            ?? throw new InvalidDataException("GPEX manifest is empty or invalid.");
        ValidateManifest(manifest);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var declaredPaths = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        foreach (var artifact in manifest.Artifacts)
        {
            if (!ids.Add(artifact.Id)) throw new InvalidDataException($"Duplicate artifact id: {artifact.Id}");
            var path = NormalizePayloadPath(artifact.Path);
            if (!declaredPaths.Add(path)) throw new InvalidDataException($"Duplicate artifact path: {path}");
            if (!entries.TryGetValue(path, out var entry)) throw new InvalidDataException($"Manifest artifact is missing: {path}");
            if (entry.Length != artifact.Length) throw new InvalidDataException($"Artifact length mismatch: {path}");
            total = checked(total + entry.Length);
            if (entry.Length > maximumEntryBytes || total > maximumPackageBytes) throw new InvalidDataException("GPEX uncompressed payload exceeds configured limits.");
            using var content = entry.Open();
            var actual = SHA256.HashData(content);
            if (!CryptographicOperations.FixedTimeEquals(actual, ParseSha256(artifact.Sha256)))
                throw new InvalidDataException($"Artifact checksum mismatch: {path}");
        }
        var undeclared = entries.Keys.FirstOrDefault(path => path.StartsWith(GaiaPrismExchangeSchema.PayloadPrefix, StringComparison.Ordinal) && !declaredPaths.Contains(path));
        if (undeclared != null) throw new InvalidDataException($"Undeclared payload: {undeclared}");
        return manifest;
    }

    public static void ExtractArtifacts(string packagePath, string destinationDirectory)
    {
        var manifest = ReadAndValidate(packagePath);
        var root = Path.GetFullPath(destinationDirectory) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);
        using var zip = ZipFile.OpenRead(packagePath);
        foreach (var artifact in manifest.Artifacts)
        {
            var destination = Path.GetFullPath(Path.Combine(root, artifact.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(root, StringComparison.Ordinal)) throw new InvalidDataException("Unsafe GPEX extraction path.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            zip.GetEntry(artifact.Path)!.ExtractToFile(destination, overwrite: true);
        }
    }

    public static void ValidateManifest(ExchangeManifest manifest)
    {
        if (manifest.SchemaId != GaiaPrismExchangeSchema.Id || manifest.SchemaVersion != GaiaPrismExchangeSchema.Version)
            throw new NotSupportedException($"Unsupported exchange schema '{manifest.SchemaId}' version '{manifest.SchemaVersion}'.");
        if (manifest.ExchangeId == Guid.Empty || string.IsNullOrWhiteSpace(manifest.Domain) || string.IsNullOrWhiteSpace(manifest.DomainContractVersion))
            throw new InvalidDataException("Exchange id, domain and domain contract version are required.");
        ValidateSupport(manifest.SourceSupport, "source");
        ValidateSupport(manifest.TargetSupport, "target");
        foreach (var property in manifest.EffectiveProperties)
        {
            if (string.IsNullOrWhiteSpace(property.Unit) || string.IsNullOrWhiteSpace(property.Name)) throw new InvalidDataException("Every property requires name and unit.");
            if (property.Shape.Length == 0 || property.Shape.Any(v => v <= 0) || Product(property.Shape) != property.Values.Length)
                throw new InvalidDataException($"Property shape does not match values: {property.Name}");
            if (property.Qualification >= QualificationStatus.Validated && manifest.RevAssessment?.Status != RevStatus.Representative)
                throw new InvalidDataException($"Validated property requires a representative REV: {property.Name}");
        }
    }

    private static Dictionary<string, ZipArchiveEntry> ValidateEntries(ZipArchive zip, long limit)
    {
        var result = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries)
        {
            var path = ValidateArchivePath(entry.FullName);
            if (!result.TryAdd(path, entry)) throw new InvalidDataException($"Duplicate GPEX entry: {path}");
            if (entry.Length > limit) throw new InvalidDataException($"GPEX entry exceeds configured size limit: {path}");
            if (entry.CompressedLength > 0 && entry.Length / Math.Max(1, entry.CompressedLength) > 1000)
                throw new InvalidDataException($"Suspicious GPEX compression ratio: {path}");
        }
        return result;
    }

    private static void ValidateSupport(ScaleSupport support, string name)
    {
        if (support.OriginMetres.Length != 3 || support.ExtentMetres.Length != 3 || support.OrientationMatrix.Length != 9)
            throw new InvalidDataException($"The {name} support requires three-component origin/extent and a 3x3 orientation matrix.");
    }

    private static int Product(IEnumerable<int> values)
    {
        try { return values.Aggregate(1, checked((left, right) => left * right)); }
        catch (OverflowException) { return -1; }
    }

    private static string NormalizePayloadPath(string path)
    {
        path = path.Replace('\\', '/').TrimStart('/');
        if (!path.StartsWith(GaiaPrismExchangeSchema.PayloadPrefix, StringComparison.Ordinal)) path = GaiaPrismExchangeSchema.PayloadPrefix + path;
        return ValidateArchivePath(path);
    }

    private static string ValidateArchivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.StartsWith('\\') || path.Contains('\\') || Path.IsPathRooted(path))
            throw new InvalidDataException($"Unsafe archive path: {path}");
        if (path.Split('/').Any(segment => segment is "" or "." or "..")) throw new InvalidDataException($"Unsafe archive path: {path}");
        return path;
    }

    private static byte[] ParseSha256(string value)
    {
        if (value.Length != 64) throw new InvalidDataException("Invalid SHA-256 checksum.");
        try { return Convert.FromHexString(value); }
        catch (FormatException ex) { throw new InvalidDataException("Invalid SHA-256 checksum.", ex); }
    }

    private static string MediaTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".json" => "application/json", ".csv" => "text/csv",
        ".vti" => "application/vnd.vtk.vti+xml", ".vtu" => "application/vnd.vtk.vtu+xml",
        ".nc" => "application/x-netcdf", _ => "application/octet-stream"
    };
}
