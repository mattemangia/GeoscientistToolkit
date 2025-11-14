// GeoscientistToolkit/Data/Seismic/SegyParser.cs

using System.Text;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Seismic;

/// <summary>
/// Parser for SEG-Y (SEG Y Rev 1) seismic data files.
/// Supports IBM and IEEE floating point formats.
/// </summary>
public class SegyParser
{
    private const int TEXTUAL_HEADER_SIZE = 3200;
    private const int BINARY_HEADER_SIZE = 400;
    private const int TRACE_HEADER_SIZE = 240;

    public SegyHeader Header { get; private set; }
    public List<SegyTrace> Traces { get; private set; } = new();

    /// <summary>
    /// Parse a SEG-Y file from disk.
    /// </summary>
    public static async Task<SegyParser> ParseAsync(string filePath, IProgress<(float progress, string message)> progress = null)
    {
        var parser = new SegyParser();
        await parser.LoadAsync(filePath, progress);
        return parser;
    }

    private async Task LoadAsync(string filePath, IProgress<(float progress, string message)> progress)
    {
        progress?.Report((0.0f, "Opening SEG-Y file..."));

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(stream);

        // Read textual header (3200 bytes EBCDIC)
        progress?.Report((0.05f, "Reading textual header..."));
        var textualHeaderBytes = reader.ReadBytes(TEXTUAL_HEADER_SIZE);
        var textualHeader = ParseTextualHeader(textualHeaderBytes);

        // Read binary header (400 bytes)
        progress?.Report((0.10f, "Reading binary header..."));
        var binaryHeaderBytes = reader.ReadBytes(BINARY_HEADER_SIZE);
        Header = ParseBinaryHeader(binaryHeaderBytes, textualHeader);

        Logger.Log($"[SegyParser] SEG-Y Format: {Header.SampleFormat}, Traces: {Header.NumTraces}, Samples/Trace: {Header.NumSamples}");

        // Read all traces
        var totalTraces = Header.NumTraces > 0 ? Header.NumTraces : EstimateTraceCount(stream, Header.NumSamples, Header.SampleFormat);
        Traces = new List<SegyTrace>(totalTraces);

        progress?.Report((0.15f, $"Reading {totalTraces} seismic traces..."));

        for (int i = 0; i < totalTraces; i++)
        {
            if (stream.Position >= stream.Length)
                break;

            var trace = await ReadTraceAsync(reader, Header.NumSamples, Header.SampleFormat);
            if (trace != null)
            {
                Traces.Add(trace);

                if (i % 100 == 0)
                {
                    var progressValue = 0.15f + (i / (float)totalTraces) * 0.85f;
                    progress?.Report((progressValue, $"Reading trace {i + 1}/{totalTraces}..."));
                }
            }
        }

        // Update header with actual trace count
        if (Header.NumTraces == 0)
            Header.NumTraces = Traces.Count;

        // Calculate statistics
        progress?.Report((0.95f, "Calculating statistics..."));
        CalculateStatistics();

        progress?.Report((1.0f, "SEG-Y file loaded successfully!"));
        Logger.Log($"[SegyParser] Successfully loaded {Traces.Count} traces");
    }

    private string ParseTextualHeader(byte[] bytes)
    {
        // SEG-Y textual header is in EBCDIC encoding
        // Convert to ASCII for display
        var sb = new StringBuilder();
        for (int i = 0; i < bytes.Length; i += 80)
        {
            var line = EbcdicToAscii(bytes, i, Math.Min(80, bytes.Length - i));
            sb.AppendLine(line.Trim());
        }
        return sb.ToString();
    }

    private SegyHeader ParseBinaryHeader(byte[] bytes, string textualHeader)
    {
        var header = new SegyHeader
        {
            TextualHeader = textualHeader
        };

        // All values in binary header are big-endian
        header.JobId = ReadInt32BigEndian(bytes, 0);
        header.LineNumber = ReadInt32BigEndian(bytes, 4);
        header.ReelNumber = ReadInt32BigEndian(bytes, 8);
        header.NumTracesPerEnsemble = ReadInt16BigEndian(bytes, 12);
        header.NumAuxTracesPerEnsemble = ReadInt16BigEndian(bytes, 14);
        header.SampleInterval = ReadInt16BigEndian(bytes, 16); // microseconds
        header.SampleIntervalOriginal = ReadInt16BigEndian(bytes, 18);
        header.NumSamples = ReadInt16BigEndian(bytes, 20);
        header.NumSamplesOriginal = ReadInt16BigEndian(bytes, 22);
        header.SampleFormat = ReadInt16BigEndian(bytes, 24);
        header.EnsembleFold = ReadInt16BigEndian(bytes, 26);
        header.TraceSorting = ReadInt16BigEndian(bytes, 28);
        header.MeasurementSystem = ReadInt16BigEndian(bytes, 54);
        header.ImpulseSignalPolarity = ReadInt16BigEndian(bytes, 56);
        header.VibratoryPolarityCode = ReadInt16BigEndian(bytes, 58);

        // SEG-Y Revision number (bytes 300-301)
        header.SegyRevision = ReadInt16BigEndian(bytes, 300);

        // Extended textual headers (bytes 304-305)
        header.NumExtendedTextualHeaders = ReadInt16BigEndian(bytes, 304);

        return header;
    }

    private async Task<SegyTrace> ReadTraceAsync(BinaryReader reader, int numSamples, int sampleFormat)
    {
        try
        {
            // Read trace header (240 bytes)
            var headerBytes = reader.ReadBytes(TRACE_HEADER_SIZE);
            if (headerBytes.Length < TRACE_HEADER_SIZE)
                return null;

            var trace = new SegyTrace
            {
                TraceSequenceNumber = ReadInt32BigEndian(headerBytes, 0),
                TraceSequenceNumberInLine = ReadInt32BigEndian(headerBytes, 4),
                FieldRecordNumber = ReadInt32BigEndian(headerBytes, 8),
                TraceNumberInField = ReadInt32BigEndian(headerBytes, 12),
                EnergySourcePoint = ReadInt32BigEndian(headerBytes, 16),
                EnsembleNumber = ReadInt32BigEndian(headerBytes, 20),
                TraceNumberInEnsemble = ReadInt32BigEndian(headerBytes, 24),
                TraceIdentificationCode = ReadInt16BigEndian(headerBytes, 28),
                NumSamplesInTrace = ReadInt16BigEndian(headerBytes, 114),
                SampleIntervalInTrace = ReadInt16BigEndian(headerBytes, 116),

                // Coordinates (may need scaling)
                CoordinateScalar = ReadInt16BigEndian(headerBytes, 70),
                SourceX = ReadInt32BigEndian(headerBytes, 72),
                SourceY = ReadInt32BigEndian(headerBytes, 76),
                GroupX = ReadInt32BigEndian(headerBytes, 80),
                GroupY = ReadInt32BigEndian(headerBytes, 84),

                // CDP coordinates
                CdpX = ReadInt32BigEndian(headerBytes, 180),
                CdpY = ReadInt32BigEndian(headerBytes, 184)
            };

            // Use trace-specific sample count if available, otherwise use header value
            var sampleCount = trace.NumSamplesInTrace > 0 ? trace.NumSamplesInTrace : numSamples;

            // Read sample data based on format
            trace.Samples = ReadSamples(reader, sampleCount, sampleFormat);

            return trace;
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[SegyParser] Error reading trace: {ex.Message}");
            return null;
        }
    }

    private float[] ReadSamples(BinaryReader reader, int count, int format)
    {
        var samples = new float[count];

        switch (format)
        {
            case 1: // 4-byte IBM floating point
                for (int i = 0; i < count; i++)
                {
                    var bytes = reader.ReadBytes(4);
                    samples[i] = IbmFloatToIeee(bytes);
                }
                break;

            case 5: // 4-byte IEEE floating point
                for (int i = 0; i < count; i++)
                {
                    var bytes = reader.ReadBytes(4);
                    Array.Reverse(bytes); // Convert from big-endian
                    samples[i] = BitConverter.ToSingle(bytes, 0);
                }
                break;

            case 2: // 4-byte two's complement integer
                for (int i = 0; i < count; i++)
                {
                    var bytes = reader.ReadBytes(4);
                    samples[i] = ReadInt32BigEndian(bytes, 0);
                }
                break;

            case 3: // 2-byte two's complement integer
                for (int i = 0; i < count; i++)
                {
                    var bytes = reader.ReadBytes(2);
                    samples[i] = ReadInt16BigEndian(bytes, 0);
                }
                break;

            case 8: // 1-byte two's complement integer
                for (int i = 0; i < count; i++)
                {
                    samples[i] = (sbyte)reader.ReadByte();
                }
                break;

            default:
                Logger.LogWarning($"[SegyParser] Unsupported sample format: {format}, defaulting to IEEE float");
                goto case 5;
        }

        return samples;
    }

    private void CalculateStatistics()
    {
        if (Traces.Count == 0) return;

        var allSamples = Traces.SelectMany(t => t.Samples).ToArray();
        Header.MinAmplitude = allSamples.Min();
        Header.MaxAmplitude = allSamples.Max();

        // Calculate RMS
        var sumSquares = allSamples.Sum(s => s * s);
        Header.RmsAmplitude = (float)Math.Sqrt(sumSquares / allSamples.Length);
    }

    private int EstimateTraceCount(FileStream stream, int samplesPerTrace, int sampleFormat)
    {
        var bytesPerSample = GetBytesPerSample(sampleFormat);
        var bytesPerTrace = TRACE_HEADER_SIZE + (samplesPerTrace * bytesPerSample);
        var remainingBytes = stream.Length - TEXTUAL_HEADER_SIZE - BINARY_HEADER_SIZE;
        return (int)(remainingBytes / bytesPerTrace);
    }

    private int GetBytesPerSample(int format)
    {
        return format switch
        {
            1 => 4, // IBM float
            2 => 4, // 4-byte int
            3 => 2, // 2-byte int
            5 => 4, // IEEE float
            8 => 1, // 1-byte int
            _ => 4
        };
    }

    // Utility functions for big-endian reading
    private static short ReadInt16BigEndian(byte[] bytes, int offset)
    {
        if (offset + 1 >= bytes.Length) return 0;
        return (short)((bytes[offset] << 8) | bytes[offset + 1]);
    }

    private static int ReadInt32BigEndian(byte[] bytes, int offset)
    {
        if (offset + 3 >= bytes.Length) return 0;
        return (bytes[offset] << 24) | (bytes[offset + 1] << 16) |
               (bytes[offset + 2] << 8) | bytes[offset + 3];
    }

    // Convert IBM floating point to IEEE floating point
    private static float IbmFloatToIeee(byte[] ibm)
    {
        if (ibm.Length != 4) return 0.0f;

        // IBM format: SEEEEEEE MMMMMMMM MMMMMMMM MMMMMMMM
        // S = sign bit, E = exponent (base 16), M = mantissa

        int sign = (ibm[0] & 0x80) != 0 ? -1 : 1;
        int exponent = ibm[0] & 0x7F;
        int mantissa = (ibm[1] << 16) | (ibm[2] << 8) | ibm[3];

        if (mantissa == 0) return 0.0f;

        // Convert to IEEE
        // IBM exponent is base 16, bias 64
        // IEEE exponent is base 2, bias 127

        double value = mantissa / 16777216.0; // Normalize mantissa (2^24)
        value *= Math.Pow(16.0, exponent - 64); // Apply IBM exponent

        return (float)(sign * value);
    }

    // Convert EBCDIC to ASCII
    private static string EbcdicToAscii(byte[] ebcdic, int offset, int length)
    {
        // Simplified EBCDIC to ASCII conversion
        // For full conversion, you'd use Encoding.GetEncoding(37)
        var sb = new StringBuilder(length);

        for (int i = 0; i < length; i++)
        {
            var b = ebcdic[offset + i];

            // Simple conversion for printable characters
            if (b == 0x40) sb.Append(' '); // Space
            else if (b >= 0x81 && b <= 0x89) sb.Append((char)('a' + (b - 0x81))); // a-i
            else if (b >= 0x91 && b <= 0x99) sb.Append((char)('j' + (b - 0x91))); // j-r
            else if (b >= 0xA2 && b <= 0xA9) sb.Append((char)('s' + (b - 0xA2))); // s-z
            else if (b >= 0xC1 && b <= 0xC9) sb.Append((char)('A' + (b - 0xC1))); // A-I
            else if (b >= 0xD1 && b <= 0xD9) sb.Append((char)('J' + (b - 0xD1))); // J-R
            else if (b >= 0xE2 && b <= 0xE9) sb.Append((char)('S' + (b - 0xE2))); // S-Z
            else if (b >= 0xF0 && b <= 0xF9) sb.Append((char)('0' + (b - 0xF0))); // 0-9
            else if (b == 0x4B) sb.Append('.');
            else if (b == 0x4C) sb.Append('<');
            else if (b == 0x4D) sb.Append('(');
            else if (b == 0x4E) sb.Append('+');
            else if (b == 0x5A) sb.Append('!');
            else if (b == 0x5B) sb.Append('$');
            else if (b == 0x5C) sb.Append('*');
            else if (b == 0x5D) sb.Append(')');
            else if (b == 0x5E) sb.Append(';');
            else if (b == 0x60) sb.Append('-');
            else if (b == 0x61) sb.Append('/');
            else if (b == 0x6B) sb.Append(',');
            else if (b == 0x6C) sb.Append('%');
            else if (b == 0x6D) sb.Append('_');
            else if (b == 0x6E) sb.Append('>');
            else if (b == 0x7A) sb.Append(':');
            else if (b == 0x7B) sb.Append('#');
            else if (b == 0x7C) sb.Append('@');
            else if (b == 0x7D) sb.Append('\'');
            else if (b == 0x7E) sb.Append('=');
            else if (b == 0x7F) sb.Append('"');
            else if (b < 0x40) sb.Append(' '); // Control characters
            else sb.Append('?'); // Unknown
        }

        return sb.ToString();
    }
}

/// <summary>
/// SEG-Y file header information
/// </summary>
public class SegyHeader
{
    public string TextualHeader { get; set; } = "";

    // Binary header fields
    public int JobId { get; set; }
    public int LineNumber { get; set; }
    public int ReelNumber { get; set; }
    public int NumTracesPerEnsemble { get; set; }
    public int NumAuxTracesPerEnsemble { get; set; }
    public int SampleInterval { get; set; } // microseconds
    public int SampleIntervalOriginal { get; set; }
    public int NumSamples { get; set; }
    public int NumSamplesOriginal { get; set; }
    public int SampleFormat { get; set; } // 1=IBM, 5=IEEE, etc.
    public int EnsembleFold { get; set; }
    public int TraceSorting { get; set; }
    public int MeasurementSystem { get; set; } // 1=meters, 2=feet
    public int ImpulseSignalPolarity { get; set; }
    public int VibratoryPolarityCode { get; set; }
    public int SegyRevision { get; set; }
    public int NumExtendedTextualHeaders { get; set; }
    public int NumTraces { get; set; } // Calculated or from file

    // Statistics (calculated)
    public float MinAmplitude { get; set; }
    public float MaxAmplitude { get; set; }
    public float RmsAmplitude { get; set; }
}

/// <summary>
/// Individual seismic trace
/// </summary>
public class SegyTrace
{
    // Trace header fields
    public int TraceSequenceNumber { get; set; }
    public int TraceSequenceNumberInLine { get; set; }
    public int FieldRecordNumber { get; set; }
    public int TraceNumberInField { get; set; }
    public int EnergySourcePoint { get; set; }
    public int EnsembleNumber { get; set; }
    public int TraceNumberInEnsemble { get; set; }
    public short TraceIdentificationCode { get; set; }
    public short NumSamplesInTrace { get; set; }
    public short SampleIntervalInTrace { get; set; }

    // Coordinates
    public short CoordinateScalar { get; set; } // Use to scale X/Y values
    public int SourceX { get; set; }
    public int SourceY { get; set; }
    public int GroupX { get; set; }
    public int GroupY { get; set; }
    public int CdpX { get; set; }
    public int CdpY { get; set; }

    // Sample data
    public float[] Samples { get; set; } = Array.Empty<float>();

    // Helper to get scaled coordinates
    public (double x, double y) GetScaledSourceCoordinates()
    {
        var scalar = CoordinateScalar == 0 ? 1.0 :
                     CoordinateScalar > 0 ? CoordinateScalar :
                     1.0 / Math.Abs(CoordinateScalar);
        return (SourceX * scalar, SourceY * scalar);
    }

    public (double x, double y) GetScaledGroupCoordinates()
    {
        var scalar = CoordinateScalar == 0 ? 1.0 :
                     CoordinateScalar > 0 ? CoordinateScalar :
                     1.0 / Math.Abs(CoordinateScalar);
        return (GroupX * scalar, GroupY * scalar);
    }
}
