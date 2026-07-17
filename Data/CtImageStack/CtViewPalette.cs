// GAIA/Data/CtImageStack/CtViewPalette.cs

using System.Numerics;

namespace GAIA.Data.CtImageStack;

/// <summary>
///     Axis colours shared by the 2D slice views, the 3D viewer and their control panels.
///     Crosshairs and cutting planes deliberately use two disjoint hue families: both are drawn
///     as thin lines over the same slice, and a cut sitting on the crosshair position used to
///     overdraw it in a near-identical green, which made the crosshair look absent.
/// </summary>
public static class CtViewPalette
{
    // Crosshairs / slice indicators: cyan, magenta, amber (X, Y, Z).
    public static readonly Vector3 CrosshairX = new(0f, 0.898f, 1f);
    public static readonly Vector3 CrosshairY = new(1f, 0.310f, 0.847f);
    public static readonly Vector3 CrosshairZ = new(1f, 0.769f, 0f);

    // Cutting planes: red, green, blue (X, Y, Z) — the convention the panel labels already use.
    public static readonly Vector3 CutX = new(1f, 0.322f, 0.322f);
    public static readonly Vector3 CutY = new(0.298f, 0.686f, 0.314f);
    public static readonly Vector3 CutZ = new(0.267f, 0.541f, 1f);

    // Arbitrary clipping planes and the volume bounding box.
    public static readonly Vector3 ClipPlane = new(1f, 0.839f, 0.404f);
    public static readonly Vector3 BoundingBox = new(0.62f, 0.68f, 0.78f);

    public static Vector3 Crosshair(int axis) => axis switch
    {
        0 => CrosshairX,
        1 => CrosshairY,
        _ => CrosshairZ
    };

    public static Vector3 Cut(int axis) => axis switch
    {
        0 => CutX,
        1 => CutY,
        _ => CutZ
    };

    public static string CrosshairName(int axis) => axis switch
    {
        0 => "Cyan",
        1 => "Magenta",
        _ => "Amber"
    };

    public static string CutName(int axis) => axis switch
    {
        0 => "Red",
        1 => "Green",
        _ => "Blue"
    };

    /// <summary>Packs a colour for ImGui draw lists, which expect 0xAABBGGRR.</summary>
    public static uint ToImGui(Vector3 rgb, float alpha = 1f)
    {
        var r = (uint)(Math.Clamp(rgb.X, 0f, 1f) * 255f + 0.5f);
        var g = (uint)(Math.Clamp(rgb.Y, 0f, 1f) * 255f + 0.5f);
        var b = (uint)(Math.Clamp(rgb.Z, 0f, 1f) * 255f + 0.5f);
        var a = (uint)(Math.Clamp(alpha, 0f, 1f) * 255f + 0.5f);
        return (a << 24) | (b << 16) | (g << 8) | r;
    }

    public static Vector4 ToVector4(Vector3 rgb, float alpha = 1f) => new(rgb.X, rgb.Y, rgb.Z, alpha);
}
