// GeoscientistToolkit/UI/Utils/SliceNavigationHelper.cs

using System.Numerics;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Utils;

/// <summary>
///     Helper class for drawing enhanced slice navigation controls
/// </summary>
public static class SliceNavigationHelper
{
    /// <summary>
    ///     Draws enhanced slice navigation controls with slider, input field, and +/- buttons
    /// </summary>
    /// <param name="label">Label for the control</param>
    /// <param name="currentSlice">Current slice index (0-based)</param>
    /// <param name="maxSlice">Maximum slice index (0-based)</param>
    /// <param name="id">Unique ID for ImGui</param>
    /// <returns>True if slice value changed</returns>
    public static bool DrawSliceControls(string label, ref int currentSlice, int maxSlice, string id = "")
    {
        var changed = false;
        var originalSlice = currentSlice;

        // Start the control group
        ImGui.PushID($"SliceNav_{label}_{id}");

        // Draw label
        ImGui.Text(label);
        ImGui.SameLine();

        // Calculate widths for layout
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var buttonWidth = 25.0f;
        var inputWidth = 60.0f;
        var sliderWidth = availableWidth - buttonWidth * 2 - inputWidth - 120; // Account for spacing and label

        // Decrease button
        if (ImGui.Button("-", new Vector2(buttonWidth, 0)))
        {
            currentSlice = Math.Max(0, currentSlice - 1);
            changed = true;
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Previous slice (or use Ctrl+Scroll)");

        ImGui.SameLine();

        // Slider
        ImGui.SetNextItemWidth(sliderWidth);
        if (ImGui.SliderInt($"##{id}Slider", ref currentSlice, 0, maxSlice, "")) changed = true;

        ImGui.SameLine();

        // Increase button
        if (ImGui.Button("+", new Vector2(buttonWidth, 0)))
        {
            currentSlice = Math.Min(maxSlice, currentSlice + 1);
            changed = true;
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Next slice (or use Ctrl+Scroll)");

        ImGui.SameLine();

        // Manual input field (1-based for user display)
        var displaySlice = currentSlice + 1; // Convert to 1-based for display
        ImGui.SetNextItemWidth(inputWidth);
        if (ImGui.InputInt($"##{id}Input", ref displaySlice, 0, 0, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            // Convert back to 0-based and clamp
            currentSlice = Math.Clamp(displaySlice - 1, 0, maxSlice);
            changed = true;
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Enter slice number (1 to " + (maxSlice + 1) + ")");

        ImGui.SameLine();

        // Display total count
        ImGui.Text($"/ {maxSlice + 1}");

        ImGui.PopID();

        // Ensure value is within bounds (safety check)
        if (currentSlice != originalSlice) currentSlice = Math.Clamp(currentSlice, 0, maxSlice);

        return changed;
    }

    /// <summary>
    ///     Draws compact slice navigation controls (for smaller spaces)
    /// </summary>
    public static bool DrawCompactSliceControls(string label, ref int currentSlice, int maxSlice, string id = "")
    {
        var changed = false;

        ImGui.PushID($"CompactSliceNav_{label}_{id}");

        // Single line with all controls
        ImGui.Text($"{label}:");
        ImGui.SameLine();

        // - button
        if (ImGui.SmallButton("-"))
        {
            currentSlice = Math.Max(0, currentSlice - 1);
            changed = true;
        }

        ImGui.SameLine();

        // Slider
        ImGui.SetNextItemWidth(100);
        if (ImGui.SliderInt($"##{id}", ref currentSlice, 0, maxSlice, "")) changed = true;
        ImGui.SameLine();

        // + button
        if (ImGui.SmallButton("+"))
        {
            currentSlice = Math.Min(maxSlice, currentSlice + 1);
            changed = true;
        }

        ImGui.SameLine();

        // Display
        ImGui.Text($"{currentSlice + 1}/{maxSlice + 1}");

        ImGui.PopID();

        return changed;
    }

    /// <summary>
    ///     Draws keyboard shortcuts helper text
    /// </summary>
    public static void DrawKeyboardShortcutsHelp()
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text("Keyboard Shortcuts:");
            ImGui.Separator();
            ImGui.BulletText("Ctrl+Scroll: Change slice");
            ImGui.BulletText("Scroll: Zoom");
            ImGui.BulletText("Middle Mouse: Pan");
            ImGui.BulletText("Left Click: Set crosshair");
            ImGui.EndTooltip();
        }
    }
}