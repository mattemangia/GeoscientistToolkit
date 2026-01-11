// GeoscientistToolkit/Data/Seismic/SeismicCubeSerializer.cs

using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.Json;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Seismic;

/// <summary>
/// Serializes and deserializes SeismicCubeDataset to/from compressed binary format (.seiscube).
/// Uses intelligent compression based on data characteristics.
/// </summary>
public static class SeismicCubeSerializer
{
    public const string FileExtension = ".seiscube";
    public const string MagicNumber = "SEISCUBE";
    public const int FormatVersion = 1;

    /// <summary>
    /// Export a seismic cube to a compressed file
    /// </summary>
    public static async Task ExportAsync(
        SeismicCubeDataset cube,
        string outputPath,
        SeismicCubeExportOptions? options = null,
        IProgress<(float progress, string message)>? progress = null)
    {
        options ??= new SeismicCubeExportOptions();

        progress?.Report((0.0f, "Starting seismic cube export..."));
        Logger.Log($"[SeismicCubeSerializer] Exporting cube '{cube.Name}' to {outputPath}");

        try
        {
            using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(fileStream);

            // Write header
            progress?.Report((0.05f, "Writing header..."));
            WriteHeader(writer, cube, options);

            // Write metadata as compressed JSON
            progress?.Report((0.10f, "Writing metadata..."));
            await WriteMetadataAsync(writer, cube, options);

            // Write line data (references to SEG-Y files or embedded data)
            progress?.Report((0.15f, "Writing line data..."));
            await WriteLineDataAsync(writer, cube, options, progress);

            // Write regularized volume if present and requested
            if (cube.RegularizedVolume != null && options.IncludeRegularizedVolume)
            {
                progress?.Report((0.70f, "Compressing volume data..."));
                await WriteVolumeDataAsync(writer, cube, options, progress);
            }

            // Write packages
            progress?.Report((0.95f, "Writing packages..."));
            WritePackages(writer, cube);

            progress?.Report((1.0f, "Export complete!"));
            Logger.Log($"[SeismicCubeSerializer] Successfully exported cube to {outputPath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[SeismicCubeSerializer] Export failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Import a seismic cube from a compressed file
    /// </summary>
    public static async Task<SeismicCubeDataset> ImportAsync(
        string inputPath,
        IProgress<(float progress, string message)>? progress = null)
    {
        progress?.Report((0.0f, "Starting seismic cube import..."));
        Logger.Log($"[SeismicCubeSerializer] Importing cube from {inputPath}");

        try
        {
            using var fileStream = new FileStream(inputPath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fileStream);

            // Read and verify header
            progress?.Report((0.05f, "Reading header..."));
            var header = ReadHeader(reader);
            if (header.Magic != MagicNumber)
            {
                throw new InvalidDataException("Invalid seismic cube file format");
            }

            // Read metadata
            progress?.Report((0.10f, "Reading metadata..."));
            var cube = await ReadMetadataAsync(reader, inputPath);

            // Read line data
            progress?.Report((0.15f, "Reading line data..."));
            await ReadLineDataAsync(reader, cube, header, progress);

            // Read volume data if present
            if (header.HasVolume)
            {
                progress?.Report((0.70f, "Decompressing volume data..."));
                await ReadVolumeDataAsync(reader, cube, header, progress);
            }

            // Read packages
            progress?.Report((0.95f, "Reading packages..."));
            ReadPackages(reader, cube);

            progress?.Report((1.0f, "Import complete!"));
            Logger.Log($"[SeismicCubeSerializer] Successfully imported cube '{cube.Name}'");

            return cube;
        }
        catch (Exception ex)
        {
            Logger.LogError($"[SeismicCubeSerializer] Import failed: {ex.Message}");
            throw;
        }
    }

    #region Header

    private static void WriteHeader(BinaryWriter writer, SeismicCubeDataset cube, SeismicCubeExportOptions options)
    {
        // Magic number (8 bytes)
        writer.Write(Encoding.ASCII.GetBytes(MagicNumber));

        // Version
        writer.Write(FormatVersion);

        // Flags
        byte flags = 0;
        if (options.IncludeRegularizedVolume && cube.RegularizedVolume != null) flags |= 0x01;
        if (options.EmbedSeismicData) flags |= 0x02;
        if (options.CompressionLevel > 0) flags |= 0x04;
        writer.Write(flags);

        // Compression level
        writer.Write((byte)options.CompressionLevel);

        // Reserved bytes for future use
        writer.Write(new byte[16]);

        // Cube statistics
        writer.Write(cube.Lines.Count);
        writer.Write(cube.Intersections.Count);
        writer.Write(cube.Packages.Count);

        // Bounds
        writer.Write(cube.Bounds.MinX);
        writer.Write(cube.Bounds.MaxX);
        writer.Write(cube.Bounds.MinY);
        writer.Write(cube.Bounds.MaxY);
        writer.Write(cube.Bounds.MinZ);
        writer.Write(cube.Bounds.MaxZ);

        // Grid parameters
        writer.Write(cube.GridParameters.InlineCount);
        writer.Write(cube.GridParameters.CrosslineCount);
        writer.Write(cube.GridParameters.SampleCount);
        writer.Write(cube.GridParameters.InlineSpacing);
        writer.Write(cube.GridParameters.CrosslineSpacing);
        writer.Write(cube.GridParameters.SampleInterval);
    }

    private static SeismicCubeFileHeader ReadHeader(BinaryReader reader)
    {
        var header = new SeismicCubeFileHeader();

        // Magic number
        header.Magic = Encoding.ASCII.GetString(reader.ReadBytes(8));

        // Version
        header.Version = reader.ReadInt32();

        // Flags
        byte flags = reader.ReadByte();
        header.HasVolume = (flags & 0x01) != 0;
        header.HasEmbeddedData = (flags & 0x02) != 0;
        header.IsCompressed = (flags & 0x04) != 0;

        // Compression level
        header.CompressionLevel = reader.ReadByte();

        // Skip reserved bytes
        reader.ReadBytes(16);

        // Statistics
        header.LineCount = reader.ReadInt32();
        header.IntersectionCount = reader.ReadInt32();
        header.PackageCount = reader.ReadInt32();

        // Bounds
        header.Bounds = new CubeBounds
        {
            MinX = reader.ReadSingle(),
            MaxX = reader.ReadSingle(),
            MinY = reader.ReadSingle(),
            MaxY = reader.ReadSingle(),
            MinZ = reader.ReadSingle(),
            MaxZ = reader.ReadSingle()
        };

        // Grid parameters
        header.GridParameters = new CubeGridParameters
        {
            InlineCount = reader.ReadInt32(),
            CrosslineCount = reader.ReadInt32(),
            SampleCount = reader.ReadInt32(),
            InlineSpacing = reader.ReadSingle(),
            CrosslineSpacing = reader.ReadSingle(),
            SampleInterval = reader.ReadSingle()
        };

        return header;
    }

    #endregion

    #region Metadata

    private static async Task WriteMetadataAsync(BinaryWriter writer, SeismicCubeDataset cube, SeismicCubeExportOptions options)
    {
        var dto = cube.ToDTO();
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = false });
        var jsonBytes = Encoding.UTF8.GetBytes(json);

        if (options.CompressionLevel > 0)
        {
            using var compressedStream = new MemoryStream();
            await using (var gzip = new GZipStream(compressedStream, GetCompressionLevel(options.CompressionLevel), leaveOpen: true))
            {
                await gzip.WriteAsync(jsonBytes);
            }

            var compressedBytes = compressedStream.ToArray();
            writer.Write(compressedBytes.Length);
            writer.Write(jsonBytes.Length); // Uncompressed size
            writer.Write(compressedBytes);
        }
        else
        {
            writer.Write(jsonBytes.Length);
            writer.Write(jsonBytes.Length);
            writer.Write(jsonBytes);
        }
    }

    private static async Task<SeismicCubeDataset> ReadMetadataAsync(BinaryReader reader, string filePath)
    {
        int compressedSize = reader.ReadInt32();
        int uncompressedSize = reader.ReadInt32();
        byte[] data = reader.ReadBytes(compressedSize);

        byte[] jsonBytes;
        if (compressedSize != uncompressedSize)
        {
            using var compressedStream = new MemoryStream(data);
            using var gzip = new GZipStream(compressedStream, CompressionMode.Decompress);
            using var decompressedStream = new MemoryStream();
            await gzip.CopyToAsync(decompressedStream);
            jsonBytes = decompressedStream.ToArray();
        }
        else
        {
            jsonBytes = data;
        }

        var json = Encoding.UTF8.GetString(jsonBytes);
        var dto = JsonSerializer.Deserialize<SeismicCubeDatasetDTO>(json);

        var name = Path.GetFileNameWithoutExtension(filePath);
        var cube = new SeismicCubeDataset(name, filePath);
        cube.FromDTO(dto);

        return cube;
    }

    #endregion

    #region Line Data

    private static async Task WriteLineDataAsync(
        BinaryWriter writer,
        SeismicCubeDataset cube,
        SeismicCubeExportOptions options,
        IProgress<(float progress, string message)>? progress)
    {
        writer.Write(cube.Lines.Count);

        for (int i = 0; i < cube.Lines.Count; i++)
        {
            var line = cube.Lines[i];
            float lineProgress = 0.15f + (0.55f * i / cube.Lines.Count);
            progress?.Report((lineProgress, $"Processing line {i + 1}/{cube.Lines.Count}: {line.Name}"));

            // Write line info
            WriteString(writer, line.Id);
            WriteString(writer, line.Name);
            WriteString(writer, line.SeismicData?.FilePath ?? "");

            // Write geometry
            WriteVector3(writer, line.Geometry.StartPoint);
            WriteVector3(writer, line.Geometry.EndPoint);
            writer.Write(line.Geometry.TraceSpacing);
            writer.Write(line.Geometry.Azimuth);

            // Write flags
            writer.Write(line.IsVisible);
            writer.Write(line.IsPerpendicular);
            WriteString(writer, line.BaseLineId ?? "");
            writer.Write(line.BaseTraceIndex ?? -1);
            WriteVector4(writer, line.Color);

            // Optionally embed seismic data
            if (options.EmbedSeismicData && line.SeismicData?.SegyData != null)
            {
                writer.Write(true); // Has embedded data
                await WriteEmbeddedSeismicDataAsync(writer, line.SeismicData, options);
            }
            else
            {
                writer.Write(false);
            }
        }
    }

    private static async Task ReadLineDataAsync(
        BinaryReader reader,
        SeismicCubeDataset cube,
        SeismicCubeFileHeader header,
        IProgress<(float progress, string message)>? progress)
    {
        int lineCount = reader.ReadInt32();

        for (int i = 0; i < lineCount; i++)
        {
            float lineProgress = 0.15f + (0.55f * i / lineCount);
            progress?.Report((lineProgress, $"Loading line {i + 1}/{lineCount}"));

            var line = new SeismicCubeLine
            {
                Id = ReadString(reader),
                Name = ReadString(reader)
            };

            string seismicFilePath = ReadString(reader);

            line.Geometry = new LineGeometry
            {
                StartPoint = ReadVector3(reader),
                EndPoint = ReadVector3(reader),
                TraceSpacing = reader.ReadSingle(),
                Azimuth = reader.ReadSingle()
            };

            line.IsVisible = reader.ReadBoolean();
            line.IsPerpendicular = reader.ReadBoolean();
            line.BaseLineId = ReadString(reader);
            if (string.IsNullOrEmpty(line.BaseLineId)) line.BaseLineId = null;
            int baseTraceIndex = reader.ReadInt32();
            line.BaseTraceIndex = baseTraceIndex >= 0 ? baseTraceIndex : null;
            line.Color = ReadVector4(reader);

            bool hasEmbeddedData = reader.ReadBoolean();
            if (hasEmbeddedData)
            {
                line.SeismicData = await ReadEmbeddedSeismicDataAsync(reader);
            }
            else if (!string.IsNullOrEmpty(seismicFilePath) && File.Exists(seismicFilePath))
            {
                // Try to load from file path
                try
                {
                    var segyData = await SegyParser.ParseAsync(seismicFilePath, null);
                    line.SeismicData = new SeismicDataset(Path.GetFileNameWithoutExtension(seismicFilePath), seismicFilePath)
                    {
                        SegyData = segyData
                    };
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"[SeismicCubeSerializer] Could not load line data from {seismicFilePath}: {ex.Message}");
                }
            }

            cube.Lines.Add(line);
        }
    }

    private static async Task WriteEmbeddedSeismicDataAsync(
        BinaryWriter writer,
        SeismicDataset dataset,
        SeismicCubeExportOptions options)
    {
        var traces = dataset.SegyData?.Traces;
        if (traces == null || traces.Count == 0)
        {
            writer.Write(0);
            return;
        }

        writer.Write(traces.Count);
        int sampleCount = dataset.GetSampleCount();
        writer.Write(sampleCount);
        writer.Write(dataset.GetSampleIntervalMs());

        // Write trace data with compression
        using var traceStream = new MemoryStream();

        foreach (var trace in traces)
        {
            // Quantize and delta-encode for better compression
            var quantized = QuantizeTrace(trace.Samples, options.QuantizationBits);
            await traceStream.WriteAsync(quantized);
        }

        var traceBytes = traceStream.ToArray();

        if (options.CompressionLevel > 0)
        {
            using var compressedStream = new MemoryStream();
            await using (var gzip = new GZipStream(compressedStream, GetCompressionLevel(options.CompressionLevel), leaveOpen: true))
            {
                await gzip.WriteAsync(traceBytes);
            }

            var compressedBytes = compressedStream.ToArray();
            writer.Write(compressedBytes.Length);
            writer.Write(traceBytes.Length);
            writer.Write(compressedBytes);
        }
        else
        {
            writer.Write(traceBytes.Length);
            writer.Write(traceBytes.Length);
            writer.Write(traceBytes);
        }
    }

    private static async Task<SeismicDataset> ReadEmbeddedSeismicDataAsync(BinaryReader reader)
    {
        int traceCount = reader.ReadInt32();
        if (traceCount == 0) return null;

        int sampleCount = reader.ReadInt32();
        float sampleInterval = reader.ReadSingle();

        int compressedSize = reader.ReadInt32();
        int uncompressedSize = reader.ReadInt32();
        byte[] data = reader.ReadBytes(compressedSize);

        byte[] traceBytes;
        if (compressedSize != uncompressedSize)
        {
            using var compressedStream = new MemoryStream(data);
            using var gzip = new GZipStream(compressedStream, CompressionMode.Decompress);
            using var decompressedStream = new MemoryStream();
            await gzip.CopyToAsync(decompressedStream);
            traceBytes = decompressedStream.ToArray();
        }
        else
        {
            traceBytes = data;
        }

        // Reconstruct traces
        var segyParser = new SegyParser();
        var traces = new List<SegyTrace>();
        int bytesPerTrace = sampleCount * 2; // 16-bit quantized

        for (int i = 0; i < traceCount; i++)
        {
            var quantizedBytes = new byte[bytesPerTrace];
            Array.Copy(traceBytes, i * bytesPerTrace, quantizedBytes, 0, bytesPerTrace);

            var samples = DequantizeTrace(quantizedBytes, sampleCount);

            traces.Add(new SegyTrace
            {
                TraceSequenceNumber = i,
                Samples = samples
            });
        }

        // Create minimal dataset
        var dataset = new SeismicDataset("Embedded", "")
        {
            SegyData = new SegyParser
            {
                Traces = traces,
                Header = new SegyHeader
                {
                    NumSamples = sampleCount,
                    SampleInterval = (int)(sampleInterval * 1000) // Convert ms to microseconds
                }
            }
        };

        return dataset;
    }

    #endregion

    #region Volume Data

    private static async Task WriteVolumeDataAsync(
        BinaryWriter writer,
        SeismicCubeDataset cube,
        SeismicCubeExportOptions options,
        IProgress<(float progress, string message)>? progress)
    {
        var volume = cube.RegularizedVolume;
        if (volume == null)
        {
            writer.Write(0L);
            return;
        }

        int nx = volume.GetLength(0);
        int ny = volume.GetLength(1);
        int nz = volume.GetLength(2);

        // Find amplitude range for normalization
        float min = float.MaxValue, max = float.MinValue;
        foreach (var value in volume)
        {
            if (value < min) min = value;
            if (value > max) max = value;
        }

        writer.Write(nx);
        writer.Write(ny);
        writer.Write(nz);
        writer.Write(min);
        writer.Write(max);

        // Quantize volume to 16-bit
        int totalVoxels = nx * ny * nz;
        var quantizedData = new byte[totalVoxels * 2];
        float range = max - min;
        if (range < 1e-10f) range = 1f;

        int idx = 0;
        for (int i = 0; i < nx; i++)
        {
            for (int j = 0; j < ny; j++)
            {
                for (int k = 0; k < nz; k++)
                {
                    float normalized = (volume[i, j, k] - min) / range;
                    ushort quantized = (ushort)(normalized * 65535);
                    quantizedData[idx++] = (byte)(quantized & 0xFF);
                    quantizedData[idx++] = (byte)(quantized >> 8);
                }
            }

            float volumeProgress = 0.70f + (0.20f * i / nx);
            if (i % 10 == 0)
            {
                progress?.Report((volumeProgress, $"Compressing volume: {100 * i / nx}%"));
            }
        }

        // Compress the data
        using var compressedStream = new MemoryStream();
        await using (var gzip = new GZipStream(compressedStream, GetCompressionLevel(options.CompressionLevel), leaveOpen: true))
        {
            await gzip.WriteAsync(quantizedData);
        }

        var compressedBytes = compressedStream.ToArray();
        writer.Write(compressedBytes.Length);
        writer.Write(quantizedData.Length);
        writer.Write(compressedBytes);

        float compressionRatio = (float)compressedBytes.Length / quantizedData.Length * 100;
        Logger.Log($"[SeismicCubeSerializer] Volume compressed: {compressionRatio:F1}% of original size");
    }

    private static async Task ReadVolumeDataAsync(
        BinaryReader reader,
        SeismicCubeDataset cube,
        SeismicCubeFileHeader header,
        IProgress<(float progress, string message)>? progress)
    {
        int nx = reader.ReadInt32();
        int ny = reader.ReadInt32();
        int nz = reader.ReadInt32();

        if (nx == 0 || ny == 0 || nz == 0) return;

        float min = reader.ReadSingle();
        float max = reader.ReadSingle();

        int compressedSize = reader.ReadInt32();
        int uncompressedSize = reader.ReadInt32();
        byte[] compressedData = reader.ReadBytes(compressedSize);

        // Decompress
        byte[] quantizedData;
        if (compressedSize != uncompressedSize)
        {
            using var compressedStream = new MemoryStream(compressedData);
            using var gzip = new GZipStream(compressedStream, CompressionMode.Decompress);
            using var decompressedStream = new MemoryStream();
            await gzip.CopyToAsync(decompressedStream);
            quantizedData = decompressedStream.ToArray();
        }
        else
        {
            quantizedData = compressedData;
        }

        // Reconstruct volume
        var volume = new float[nx, ny, nz];
        float range = max - min;
        int idx = 0;

        for (int i = 0; i < nx; i++)
        {
            for (int j = 0; j < ny; j++)
            {
                for (int k = 0; k < nz; k++)
                {
                    ushort quantized = (ushort)(quantizedData[idx] | (quantizedData[idx + 1] << 8));
                    idx += 2;
                    float normalized = quantized / 65535f;
                    volume[i, j, k] = min + normalized * range;
                }
            }

            float volumeProgress = 0.70f + (0.20f * i / nx);
            if (i % 10 == 0)
            {
                progress?.Report((volumeProgress, $"Loading volume: {100 * i / nx}%"));
            }
        }

        // Set volume via reflection (RegularizedVolume has private setter)
        var field = typeof(SeismicCubeDataset).GetProperty("RegularizedVolume");
        field?.SetValue(cube, volume);
    }

    #endregion

    #region Packages

    private static void WritePackages(BinaryWriter writer, SeismicCubeDataset cube)
    {
        writer.Write(cube.Packages.Count);

        foreach (var package in cube.Packages)
        {
            WriteString(writer, package.Id);
            WriteString(writer, package.Name);
            WriteString(writer, package.Description);
            WriteVector4(writer, package.Color);
            writer.Write(package.IsVisible);
            WriteString(writer, package.LithologyType);
            WriteString(writer, package.SeismicFacies);
            writer.Write(package.Confidence);

            // Write horizon points
            writer.Write(package.HorizonPoints.Count);
            foreach (var point in package.HorizonPoints)
            {
                WriteVector3(writer, point);
            }
        }
    }

    private static void ReadPackages(BinaryReader reader, SeismicCubeDataset cube)
    {
        int packageCount = reader.ReadInt32();

        for (int i = 0; i < packageCount; i++)
        {
            var package = new SeismicCubePackage
            {
                Id = ReadString(reader),
                Name = ReadString(reader),
                Description = ReadString(reader),
                Color = ReadVector4(reader),
                IsVisible = reader.ReadBoolean(),
                LithologyType = ReadString(reader),
                SeismicFacies = ReadString(reader),
                Confidence = reader.ReadSingle()
            };

            int pointCount = reader.ReadInt32();
            for (int j = 0; j < pointCount; j++)
            {
                package.HorizonPoints.Add(ReadVector3(reader));
            }

            cube.Packages.Add(package);
        }
    }

    #endregion

    #region Helper Methods

    private static CompressionLevel GetCompressionLevel(int level)
    {
        return level switch
        {
            0 => CompressionLevel.NoCompression,
            1 => CompressionLevel.Fastest,
            2 => CompressionLevel.Optimal,
            _ => CompressionLevel.SmallestSize
        };
    }

    private static byte[] QuantizeTrace(float[] samples, int bits = 16)
    {
        if (samples.Length == 0) return Array.Empty<byte>();

        float min = samples.Min();
        float max = samples.Max();
        float range = max - min;
        if (range < 1e-10f) range = 1f;

        var result = new byte[samples.Length * 2]; // 16-bit output

        for (int i = 0; i < samples.Length; i++)
        {
            float normalized = (samples[i] - min) / range;
            ushort quantized = (ushort)(normalized * 65535);
            result[i * 2] = (byte)(quantized & 0xFF);
            result[i * 2 + 1] = (byte)(quantized >> 8);
        }

        return result;
    }

    private static float[] DequantizeTrace(byte[] data, int sampleCount)
    {
        var samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            ushort quantized = (ushort)(data[i * 2] | (data[i * 2 + 1] << 8));
            samples[i] = quantized / 65535f; // Normalized 0-1
        }

        return samples;
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        value ??= "";
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static string ReadString(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length <= 0) return "";
        var bytes = reader.ReadBytes(length);
        return Encoding.UTF8.GetString(bytes);
    }

    private static void WriteVector3(BinaryWriter writer, Vector3 v)
    {
        writer.Write(v.X);
        writer.Write(v.Y);
        writer.Write(v.Z);
    }

    private static Vector3 ReadVector3(BinaryReader reader)
    {
        return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }

    private static void WriteVector4(BinaryWriter writer, Vector4 v)
    {
        writer.Write(v.X);
        writer.Write(v.Y);
        writer.Write(v.Z);
        writer.Write(v.W);
    }

    private static Vector4 ReadVector4(BinaryReader reader)
    {
        return new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }

    #endregion
}

/// <summary>
/// Options for exporting seismic cubes
/// </summary>
public class SeismicCubeExportOptions
{
    /// <summary>
    /// Compression level: 0=None, 1=Fast, 2=Optimal, 3=Maximum
    /// </summary>
    public int CompressionLevel { get; set; } = 2;

    /// <summary>
    /// Include the regularized volume in the export
    /// </summary>
    public bool IncludeRegularizedVolume { get; set; } = true;

    /// <summary>
    /// Embed seismic trace data instead of referencing external files
    /// </summary>
    public bool EmbedSeismicData { get; set; } = true;

    /// <summary>
    /// Number of bits for amplitude quantization (8, 12, or 16)
    /// </summary>
    public int QuantizationBits { get; set; } = 16;
}

/// <summary>
/// Header information for seismic cube files
/// </summary>
internal class SeismicCubeFileHeader
{
    public string Magic { get; set; } = "";
    public int Version { get; set; }
    public bool HasVolume { get; set; }
    public bool HasEmbeddedData { get; set; }
    public bool IsCompressed { get; set; }
    public int CompressionLevel { get; set; }
    public int LineCount { get; set; }
    public int IntersectionCount { get; set; }
    public int PackageCount { get; set; }
    public CubeBounds Bounds { get; set; } = new();
    public CubeGridParameters GridParameters { get; set; } = new();
}
