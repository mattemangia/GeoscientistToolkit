// GAIA/UI/UiScaling.cs

using GAIA.Settings;

namespace GAIA.UI;

/// <summary>
///     The font size and scale factor every ImGui context is built with.
///     A font atlas is baked when a controller is constructed and the pop-out windows each build
///     their own, so this is resolved once at startup - before the first controller exists - rather
///     than passed down through every call site. Changing the settings therefore needs a restart.
/// </summary>
public static class UiScaling
{
    /// <summary>Panel headers, as a multiple of the UI font. Matches the old 21px against 14px.</summary>
    private const float TitleFontRatio = 1.5f;

    private const float MinFontEm = 8f;
    private const float MaxFontEm = 32f;
    private const float MinScale = 0.5f;
    private const float MaxScale = 4f;

    /// <summary>The base UI font size in em pixels, before <see cref="Scale" />.</summary>
    public static float FontEmPixels { get; private set; } = 14f;

    /// <summary>The user's UI scale combined with the monitor's DPI content scale.</summary>
    public static float Scale { get; private set; } = 1f;

    /// <summary>The em the UI font should be rasterised at.</summary>
    public static float UiFontEmPixels => FontEmPixels * Scale;

    /// <summary>The em the panel-header font should be rasterised at.</summary>
    public static float TitleFontEmPixels => FontEmPixels * TitleFontRatio * Scale;

    /// <summary>
    ///     Resolves the sizes from the user's preferences and the monitor.
    ///     <paramref name="dpiContentScale" /> is 1.0 at 96 DPI; ImGui bakes its atlas in physical
    ///     pixels, so without it the UI shrinks as display scaling grows.
    /// </summary>
    public static void Configure(AppearanceSettings appearance, float dpiContentScale)
    {
        var em = appearance?.FontSize > 0 ? appearance.FontSize : 14f;
        FontEmPixels = Math.Clamp(em, MinFontEm, MaxFontEm);

        var userScale = appearance?.UIScale > 0 ? appearance.UIScale : 1f;
        var dpi = dpiContentScale > 0f ? dpiContentScale : 1f;
        Scale = Math.Clamp(userScale * dpi, MinScale, MaxScale);
    }
}
