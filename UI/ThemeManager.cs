// GeoscientistToolkit/UI/ThemeManager.cs

using System.Numerics;
using GeoscientistToolkit.Settings;
using ImGuiNET;

namespace GeoscientistToolkit.UI;

public static class ThemeManager
{
    public static void ApplyTheme(AppearanceSettings settings)
    {
        var style = ImGui.GetStyle();

        // Base Theme
        switch (settings.Theme)
        {
            case "Light":
                ImGui.StyleColorsLight();
                break;
            case "Classic":
                ImGui.StyleColorsClassic();
                break;
            case "Dark":
            default:
                ImGui.StyleColorsDark();
                break;
        }

        // Custom Accent Color
        var accentColor = GetAccentColor(settings.ColorScheme);
        var accentColorHover = accentColor * new Vector4(1.2f, 1.2f, 1.2f, 1.0f);
        var accentColorActive = accentColor * new Vector4(1.4f, 1.4f, 1.4f, 1.0f);

        style.Colors[(int)ImGuiCol.Header] = accentColor * new Vector4(1.0f, 1.0f, 1.0f, 0.31f);
        style.Colors[(int)ImGuiCol.HeaderHovered] = accentColorHover * new Vector4(1.0f, 1.0f, 1.0f, 0.80f);
        style.Colors[(int)ImGuiCol.HeaderActive] = accentColorActive;

        style.Colors[(int)ImGuiCol.Button] = accentColor * new Vector4(1.0f, 1.0f, 1.0f, 0.40f);
        style.Colors[(int)ImGuiCol.ButtonHovered] = accentColorHover * new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
        style.Colors[(int)ImGuiCol.ButtonActive] = accentColorActive;

        style.Colors[(int)ImGuiCol.FrameBg] = accentColor * new Vector4(1.0f, 1.0f, 1.0f, 0.20f);
        style.Colors[(int)ImGuiCol.FrameBgHovered] = accentColor * new Vector4(1.0f, 1.0f, 1.0f, 0.30f);
        style.Colors[(int)ImGuiCol.FrameBgActive] = accentColor * new Vector4(1.0f, 1.0f, 1.0f, 0.40f);

        style.Colors[(int)ImGuiCol.CheckMark] = accentColorActive;
        style.Colors[(int)ImGuiCol.SliderGrab] = accentColor;
        style.Colors[(int)ImGuiCol.SliderGrabActive] = accentColorActive;

        style.Colors[(int)ImGuiCol.Tab] = style.Colors[(int)ImGuiCol.Button];
        style.Colors[(int)ImGuiCol.TabHovered] = style.Colors[(int)ImGuiCol.ButtonHovered];
        style.Colors[(int)ImGuiCol.TabActive] = style.Colors[(int)ImGuiCol.ButtonActive];
        style.Colors[(int)ImGuiCol.TabUnfocused] = style.Colors[(int)ImGuiCol.Tab];
        style.Colors[(int)ImGuiCol.TabUnfocusedActive] = style.Colors[(int)ImGuiCol.TabActive];
    }

    private static Vector4 GetAccentColor(string scheme)
    {
        return scheme switch
        {
            "Green" => new Vector4(0.2f, 0.8f, 0.3f, 1.0f),
            "Orange" => new Vector4(0.9f, 0.5f, 0.1f, 1.0f),
            "Purple" => new Vector4(0.6f, 0.2f, 0.9f, 1.0f),
            "Red" => new Vector4(0.8f, 0.2f, 0.2f, 1.0f),
            "Blue" => new Vector4(0.26f, 0.59f, 0.98f, 1.0f),
            _ => new Vector4(0.26f, 0.59f, 0.98f, 1.0f)
        };
    }
}