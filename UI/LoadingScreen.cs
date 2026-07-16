// GAIA/UI/LoadingScreen.cs

using System.Diagnostics;
using System.Numerics;
using GAIA.Util;
using ImGuiNET;
using Veldrid;
using Veldrid.Sdl2;

namespace GAIA.UI;

/// <summary>
///     A simple loading screen that displays progress during application initialization
/// </summary>
public class LoadingScreen
{
    private readonly Stopwatch _animationTimer = new();
    private readonly CommandList _commandList;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ImGuiController _imGuiController;
    private readonly Sdl2Window _window;

    private string _currentStatus = "Initializing...";
    private float _progress;

    public LoadingScreen(GraphicsDevice graphicsDevice, CommandList commandList, ImGuiController imGuiController,
        Sdl2Window window)
    {
        _graphicsDevice = graphicsDevice;
        _commandList = commandList;
        _imGuiController = imGuiController;
        _window = window;
        _animationTimer.Start();

        // Probing the host machine can shell out to WMI/lspci, so start it off-thread now and
        // let the panel fill in once the results land.
        SystemDiagnostics.BeginGather(graphicsDevice);
    }

    public void UpdateStatus(string status, float progress)
    {
        _currentStatus = status;
        _progress = Math.Clamp(progress, 0.0f, 1.0f);

        // Render immediately to show the update
        Render();
    }

    public void Render()
    {
        // Pump events to keep window responsive and get input snapshot
        var snapshot = _window.PumpEvents();

        // Update ImGui with the single captured snapshot
        _imGuiController.Update(1f / 60f, snapshot);

        // Set up loading screen UI
        var io = ImGui.GetIO();
        var viewport = ImGui.GetMainViewport();

        // Create a full-screen window for the loading screen
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
            // Dark background
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(viewport.Pos, viewport.Pos + viewport.Size,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.12f, 1.0f)));

            // Center content
            var windowSize = ImGui.GetWindowSize();
            var textSize = ImGui.CalcTextSize("GAIA (Geoscience Analysis, Imaging & Automation)");

            // Logo/Title
            ImGui.SetCursorPos(new Vector2((windowSize.X - textSize.X * 2f) * 0.5f, windowSize.Y * 0.35f));
            ImGui.PushFont(ImGui.GetIO().Fonts.Fonts[0]); // Use default font
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
            ImGui.SetWindowFontScale(2.0f);
            ImGui.Text("GAIA (Geoscience Analysis, Imaging & Automation)");
            ImGui.SetWindowFontScale(1.0f);
            ImGui.PopStyleColor();
            ImGui.PopFont();

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

        // Render to screen
        _commandList.Begin();
        _commandList.SetFramebuffer(_graphicsDevice.MainSwapchain.Framebuffer);
        _commandList.ClearColorTarget(0, new RgbaFloat(0.1f, 0.1f, 0.12f, 1.0f));
        _imGuiController.Render(_graphicsDevice, _commandList);
        _commandList.End();

        _graphicsDevice.SubmitCommands(_commandList);
        _graphicsDevice.SwapBuffers(_graphicsDevice.MainSwapchain);
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

        // Right-align the labels against a fixed gutter so the values line up in a column.
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