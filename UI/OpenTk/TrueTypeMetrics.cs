// GAIA/UI/OpenTk/TrueTypeMetrics.cs

using System.Buffers.Binary;
using System.Text;

namespace GAIA.UI.OpenTk;

/// <summary>
///     Reads the vertical metrics needed to size an ImGui font predictably.
///     AddFontFromFileTTF's size_pixels is not the font size: stb_truetype scales the face so that
///     ascent minus descent equals it, and that span differs a lot per face. Segoe UI reserves room
///     for accents (ascent 2210, descent -514 against a 2048 em), so a nominal 14 collapses to a
///     ~10.5px em, while Arial lands near 12.5px and DejaVu Sans - the Linux pick - is larger again.
///     That is why the same build looked tiny on Windows and fine on Linux.
///     Multiplying the wanted em by <see cref="SpanPerEm" /> cancels the difference out, so one
///     setting means the same apparent size everywhere.
/// </summary>
internal static class TrueTypeMetrics
{
    /// <summary>
    ///     (ascent - descent) / unitsPerEm for a font file, or 1 when the file cannot be parsed - in
    ///     which case the caller's em is used unchanged, which is no worse than the old behaviour.
    /// </summary>
    public static float SpanPerEm(string path)
    {
        try
        {
            var data = File.ReadAllBytes(path);

            // A collection (.ttc, e.g. macOS Helvetica) points at its faces; use the first.
            var offset = 0;
            if (Tag(data, 0) == "ttcf") offset = (int)ReadU32(data, 12);

            int head = 0, hhea = 0;
            var tableCount = ReadU16(data, offset + 4);
            for (var i = 0; i < tableCount; i++)
            {
                var record = offset + 12 + i * 16;
                switch (Tag(data, record))
                {
                    case "head":
                        head = (int)ReadU32(data, record + 8);
                        break;
                    case "hhea":
                        hhea = (int)ReadU32(data, record + 8);
                        break;
                }
            }

            if (head == 0 || hhea == 0) return 1f;

            var unitsPerEm = ReadU16(data, head + 18);
            var span = ReadS16(data, hhea + 4) - ReadS16(data, hhea + 6); // ascender - descender
            if (unitsPerEm <= 0 || span <= 0) return 1f;

            return span / (float)unitsPerEm;
        }
        catch
        {
            return 1f;
        }
    }

    private static string Tag(byte[] data, int offset) => Encoding.ASCII.GetString(data, offset, 4);

    private static uint ReadU32(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));

    private static ushort ReadU16(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));

    private static short ReadS16(byte[] data, int offset) =>
        BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(offset, 2));
}
