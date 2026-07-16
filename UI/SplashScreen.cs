// GAIA/UI/SplashScreen.cs

using System.Numerics;
using ImGuiNET;

namespace GAIA.UI;

/// <summary>
///     A splash screen that displays the application logo during startup.
///     Pure ImGui drawing - the host (OpenTkApplication) pumps events,
///     clears the framebuffer and swaps buffers via RenderStartupFrame.
/// </summary>
public sealed class SplashScreen
{
    private readonly StartupLogo _logo;

    public SplashScreen(StartupLogo logo)
    {
        _logo = logo;
    }

    /// <summary>
    ///     Renders the splash screen for the specified duration. The host must
    ///     supply a frame renderer that pumps events, clears and swaps buffers.
    /// </summary>
    public void Show(int durationMs, Action<Action> renderFrame)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < durationMs)
        {
            renderFrame(Draw);
            Thread.Sleep(16);
        }
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

        if (ImGui.Begin("Splash Screen", windowFlags))
        {
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(viewport.Pos, viewport.Pos + viewport.Size,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.12f, 1.0f)));

            var windowSize = ImGui.GetWindowSize();

            var displaySize = _logo.Measure(Math.Min(windowSize.X, windowSize.Y) * 0.5f);
            _logo.DrawAt(
                new Vector2(
                    (windowSize.X - displaySize.X) * 0.5f,
                    (windowSize.Y - displaySize.Y) * 0.5f - 50f),
                displaySize);

            var titleText = "GAIA (Geoscience Analysis, Imaging & Automation)";
            var titleSize = ImGui.CalcTextSize(titleText);
            ImGui.SetCursorPos(new Vector2((windowSize.X - titleSize.X * 1.5f) * 0.5f, windowSize.Y * 0.7f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
            ImGui.SetWindowFontScale(1.5f);
            ImGui.Text(titleText);
            ImGui.SetWindowFontScale(1.0f);
            ImGui.PopStyleColor();

            var subtitleText = "Matteo Mangiagalli - 2026";
            var subtitleSize = ImGui.CalcTextSize(subtitleText);
            ImGui.SetCursorPos(new Vector2((windowSize.X - subtitleSize.X) * 0.5f,
                windowSize.Y * 0.7f + titleSize.Y * 1.5f + 15));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.65f, 1.0f));
            ImGui.Text(subtitleText);
            ImGui.PopStyleColor();

            var uniText = "Università degli Studi di Urbino Carlo Bo";
            var uniSize = ImGui.CalcTextSize(uniText);
            ImGui.SetCursorPos(new Vector2((windowSize.X - uniSize.X) * 0.5f,
                windowSize.Y * 0.7f + titleSize.Y * 1.5f + 35));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.55f, 1.0f));
            ImGui.Text(uniText);
            ImGui.PopStyleColor();

            ImGui.End();
        }

        ImGui.PopStyleVar(3);
    }
}
