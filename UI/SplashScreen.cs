// GeoscientistToolkit/UI/SplashScreen.cs

using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using ImGuiNET;
using Veldrid;
using Veldrid.Sdl2;
using StbImageSharp;

namespace GeoscientistToolkit.UI;

/// <summary>
///     A splash screen that displays the application logo during startup
/// </summary>
public class SplashScreen : IDisposable
{
    private readonly CommandList _commandList;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ImGuiController _imGuiController;
    private readonly Sdl2Window _window;
    private readonly Stopwatch _displayTimer = new();
    private IntPtr _logoTextureId;
    private Vector2 _logoSize;
    private bool _disposed;

    public SplashScreen(GraphicsDevice graphicsDevice, CommandList commandList, ImGuiController imGuiController,
        Sdl2Window window)
    {
        _graphicsDevice = graphicsDevice;
        _commandList = commandList;
        _imGuiController = imGuiController;
        _window = window;

        LoadLogoTexture();
        _displayTimer.Start();
    }

    private void LoadLogoTexture()
    {
        try
        {
            // Load embedded image resource
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "GeoscientistToolkit.image.png";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                Console.WriteLine($"Warning: Could not find embedded resource '{resourceName}'");
                return;
            }

            // Load image using StbImageSharp
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

            // Create texture
            var texture = _graphicsDevice.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
                (uint)image.Width,
                (uint)image.Height,
                1,
                1,
                PixelFormat.R8_G8_B8_A8_UNorm,
                TextureUsage.Sampled));

            _graphicsDevice.UpdateTexture(
                texture,
                image.Data,
                0, 0, 0,
                (uint)image.Width,
                (uint)image.Height,
                1,
                0,
                0);

            // Create texture view
            var textureView = _graphicsDevice.ResourceFactory.CreateTextureView(texture);

            // Bind to ImGui
            _logoTextureId = _imGuiController.GetOrCreateImGuiBinding(_graphicsDevice.ResourceFactory, textureView);
            _logoSize = new Vector2(image.Width, image.Height);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading splash screen logo: {ex.Message}");
        }
    }

    public void Show(int durationMs = 2000)
    {
        _displayTimer.Restart();

        while (_displayTimer.ElapsedMilliseconds < durationMs)
        {
            Render();
            Thread.Sleep(16); // ~60fps
        }
    }

    private void Render()
    {
        // Pump events to keep window responsive
        _window.PumpEvents();

        // Update ImGui
        _imGuiController.Update(1f / 60f, _window.PumpEvents());

        // Set up splash screen UI
        var viewport = ImGui.GetMainViewport();

        // Create a full-screen window for the splash screen
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
            // Dark background
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(viewport.Pos, viewport.Pos + viewport.Size,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.12f, 1.0f)));

            var windowSize = ImGui.GetWindowSize();

            // Display logo if loaded
            if (_logoTextureId != IntPtr.Zero)
            {
                // Scale logo to fit window nicely (max 80% of window size)
                var maxSize = Math.Min(windowSize.X, windowSize.Y) * 0.5f;
                var scale = Math.Min(maxSize / _logoSize.X, maxSize / _logoSize.Y);
                var displaySize = _logoSize * scale;

                var logoPos = new Vector2(
                    (windowSize.X - displaySize.X) * 0.5f,
                    (windowSize.Y - displaySize.Y) * 0.5f - 50f);

                ImGui.SetCursorPos(logoPos);
                ImGui.Image(_logoTextureId, displaySize);
            }

            // Application name
            var titleText = "Geoscientist's Toolkit";
            var titleSize = ImGui.CalcTextSize(titleText);
            ImGui.SetCursorPos(new Vector2((windowSize.X - titleSize.X * 1.5f) * 0.5f, windowSize.Y * 0.7f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
            ImGui.SetWindowFontScale(1.5f);
            ImGui.Text(titleText);
            ImGui.SetWindowFontScale(1.0f);
            ImGui.PopStyleColor();

            // Subtitle/Author info
            var subtitleText = "Matteo Mangiagalli - 2025";
            var subtitleSize = ImGui.CalcTextSize(subtitleText);
            ImGui.SetCursorPos(new Vector2((windowSize.X - subtitleSize.X) * 0.5f, windowSize.Y * 0.7f + titleSize.Y * 1.5f + 15));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.65f, 1.0f));
            ImGui.Text(subtitleText);
            ImGui.PopStyleColor();

            // University info
            var uniText = "Università degli Studi di Urbino Carlo Bo";
            var uniSize = ImGui.CalcTextSize(uniText);
            ImGui.SetCursorPos(new Vector2((windowSize.X - uniSize.X) * 0.5f, windowSize.Y * 0.7f + titleSize.Y * 1.5f + 35));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.55f, 1.0f));
            ImGui.Text(uniText);
            ImGui.PopStyleColor();

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

    public void Dispose()
    {
        if (!_disposed)
        {
            // ImGui controller will handle texture cleanup
            _disposed = true;
        }
    }
}
