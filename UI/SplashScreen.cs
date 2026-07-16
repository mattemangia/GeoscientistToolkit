// GAIA/UI/SplashScreen.cs

using System.Numerics;
using System.Reflection;
using GAIA.Util;
using ImGuiNET;
using OpenTK.Graphics.OpenGL;
using StbImageSharp;

namespace GAIA.UI;

/// <summary>
///     A splash screen that displays the application logo during startup.
///     Pure ImGui drawing — the host (OpenTkApplication) pumps events,
///     clears the framebuffer and swaps buffers via RenderStartupFrame.
/// </summary>
public sealed class SplashScreen : IDisposable
{
    private IntPtr _logoTextureId;
    private Vector2 _logoSize;
    private bool _disposed;

    public SplashScreen()
    {
        LoadLogoTexture();
    }

    private void LoadLogoTexture()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "GAIA.image.png";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                Logger.LogWarning($"Could not find embedded resource '{resourceName}'");
                return;
            }

            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

            var logoTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, logoTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                image.Width, image.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, image.Data);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                (int)TextureMagFilter.Linear);
            GL.BindTexture(TextureTarget.Texture2D, 0);

            _logoTextureId = (IntPtr)logoTexture;
            _logoSize = new Vector2(image.Width, image.Height);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error loading splash screen logo: {ex.Message}");
        }
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

            if (_logoTextureId != IntPtr.Zero)
            {
                var maxSize = Math.Min(windowSize.X, windowSize.Y) * 0.5f;
                var scale = Math.Min(maxSize / _logoSize.X, maxSize / _logoSize.Y);
                var displaySize = _logoSize * scale;

                var logoPos = new Vector2(
                    (windowSize.X - displaySize.X) * 0.5f,
                    (windowSize.Y - displaySize.Y) * 0.5f - 50f);

                ImGui.SetCursorPos(logoPos);
                ImGui.Image(_logoTextureId, displaySize);
            }

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_logoTextureId != IntPtr.Zero)
            {
                GL.DeleteTexture((int)_logoTextureId);
                _logoTextureId = IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Could not dispose splash logo texture: {ex.Message}");
        }
    }
}
