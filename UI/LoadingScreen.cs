// GAIA/UI/LoadingScreen.cs

using System.Diagnostics;
using System.Numerics;
using GAIA.Util;
using ImGuiNET;

namespace GAIA.UI;

/// <summary>
///     A loading screen that displays progress during application initialization.
///     Pure ImGui drawing — the host (OpenTkApplication) pumps events,
///     clears the framebuffer and swaps buffers via RenderStartupFrame.
/// </summary>
public sealed class LoadingScreen
{
    private readonly Stopwatch _animationTimer = new();
    private string _currentStatus = "Initializing...";
    private float _progress;

    public LoadingScreen()
    {
        _animationTimer.Start();

        // Probing the host machine can shell out to WMI/lspci, so start it off-thread now and
        // let the panel fill in once the results land.
        SystemDiagnostics.BeginGather();
    }

    public void UpdateStatus(string status, float progress)
    {
        _currentStatus = status;
        _progress = Math.Clamp(progress, 0.0f, 1.0f);
    }

    public void Draw()
    {
        var viewport = ImGui.GetMainViewport();

        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
        ImGui.SetNextWindowViewport(viewport.ID);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        var windowFlags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
                          ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing |
                          ImGuiWindowFlags.NoBringToFrontOnFocus;

        if (ImGui.Begin("Loading Screen", windowFlags))
        {
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(viewport.Pos, viewport.Pos + viewport.Size,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.12f, 1.0f)));

            var windowSize = ImGui.GetWindowSize();
            var textSize = ImGui.CalcTextSize("GAIA (Geoscience Analysis, Imaging & Automation)");

            // Title
            ImGui.SetCursorPos(new Vector2((windowSize.X - textSize.X * 2f) * 0.5f, windowSize.Y * 0.35f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
            ImGui.SetWindowFontScale(2.0f);
            ImGui.Text("GAIA (Geoscience Analysis, Imaging & Automation)");
            ImGui.SetWindowFontScale(1.0f);
            ImGui.PopStyleColor();

            // Subtitle
            var subtitleText = "The Swiss Army Knife for Geosciences";
            var subtitleSize = ImGui.CalcTextSize(subtitleText);
            ImGui.SetCursorPos(new Vector2((windowSize.X - subtitleSize.X) * 0.5f,
                windowSize.Y * 0.35f + textSize.Y * 2f + 10));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.65f, 1.0f));
            ImGui.Text(subtitleText);
            ImGui.PopStyleColor();

            // Progress bar
            var progressBarWidth = Math.Min(400f, windowSize.X * 0.6f);
            var progressBarHeight = 6f;
            var progressBarPos = new Vector2((windowSize.X - progressBarWidth) * 0.5f, windowSize.Y * 0.55f);

            ImGui.SetCursorPos(progressBarPos);
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(0.2f, 0.7f, 1.0f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.15f, 0.15f, 0.17f, 1.0f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, progressBarHeight * 0.5f);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
            ImGui.ProgressBar(_progress, new Vector2(progressBarWidth, progressBarHeight), "");
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);

            // Status text
            var statusSize = ImGui.CalcTextSize(_currentStatus);
            ImGui.SetCursorPos(new Vector2((windowSize.X - statusSize.X) * 0.5f,
                windowSize.Y * 0.55f + progressBarHeight + 15));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.75f, 1.0f));
            ImGui.Text(_currentStatus);
            ImGui.PopStyleColor();

            // Animated dots for visual feedback
            var animTime = (float)_animationTimer.Elapsed.TotalSeconds;
            var dotCount = (int)(animTime * 2) % 4;
            var dots = new string('.', dotCount);
            var dotsSize = ImGui.CalcTextSize("....");
            ImGui.SetCursorPos(new Vector2((windowSize.X - dotsSize.X) * 0.5f,
                windowSize.Y * 0.55f + progressBarHeight + 35));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.55f, 1.0f));
            ImGui.Text(dots);
            ImGui.PopStyleColor();

            DrawDiagnostics(windowSize, progressBarWidth, windowSize.Y * 0.55f + progressBarHeight + 60);

            ImGui.End();
        }

        ImGui.PopStyleVar(3);
    }

    /// <summary>
    ///     Renders the host machine facts as a key/value block centred under the progress bar.
    ///     Stays blank until the background probe finishes, so startup is never held up.
    /// </summary>
    private void DrawDiagnostics(Vector2 windowSize, float blockWidth, float topY)
    {
        var facts = SystemDiagnostics.Snapshot;
        if (facts.Count == 0) return;

        var left = (windowSize.X - blockWidth) * 0.5f;
        var lineHeight = ImGui.GetTextLineHeightWithSpacing();

        var labelWidth = facts.Max(f => ImGui.CalcTextSize(f.Key).X) + 12f;

        for (var i = 0; i < facts.Count; i++)
        {
            var (key, value) = facts[i];
            var y = topY + i * lineHeight;

            ImGui.SetCursorPos(new Vector2(left + labelWidth - ImGui.CalcTextSize(key).X, y));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.45f, 0.45f, 0.5f, 1.0f));
            ImGui.Text(key);
            ImGui.PopStyleColor();

            ImGui.SetCursorPos(new Vector2(left + labelWidth + 10f, y));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.72f, 0.74f, 0.78f, 1.0f));
            ImGui.Text(Truncate(value, blockWidth - labelWidth - 10f));
            ImGui.PopStyleColor();
        }
    }

    /// <summary>
    ///     Clips over-long values (GPU names in particular) with an ellipsis so they cannot
    ///     overflow the centred block.
    /// </summary>
    private static string Truncate(string value, float maxWidth)
    {
        if (ImGui.CalcTextSize(value).X <= maxWidth) return value;

        var trimmed = value;
        while (trimmed.Length > 1 && ImGui.CalcTextSize(trimmed + "...").X > maxWidth)
            trimmed = trimmed[..^1];

        return trimmed + "...";
    }
}
