// GeoscientistToolkit/UI/ImGuiWindowScreenshotTool.cs

using System.Numerics;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI;

/// <summary>
///     Tool for capturing screenshots of screen regions or full screen.
///     Uses a visual selection approach rather than window detection.
/// </summary>
public class ImGuiWindowScreenshotTool
{
    private readonly ImGuiExportFileDialog _exportDialog;
    private Vector2 _capturePos;
    private Vector2 _captureSize;
    private bool _isDragging;

    private bool _pendingCapture;
    private Vector2 _selectionEnd;
    private Vector2 _selectionStart;
    private bool _showMetalWarningDialog;

    public ImGuiWindowScreenshotTool()
    {
        Instance = this;
        _exportDialog = new ImGuiExportFileDialog("ScreenshotExportDialog", "Save Screenshot");
        _exportDialog.SetExtensions(
            (".png", "PNG Image"),
            (".jpg", "JPEG Image"),
            (".bmp", "Bitmap Image"),
            (".tga", "TGA Image")
        );
    }

    public static ImGuiWindowScreenshotTool Instance { get; private set; }

    public bool IsActive { get; private set; }

    public void StartSelection()
    {
        if (IsActive) return;

        // Check if we're on Metal backend
        var gd = VeldridManager.GraphicsDevice;
        if (gd != null && gd.BackendType == Veldrid.GraphicsBackend.Metal)
        {
            _showMetalWarningDialog = true;
            Logger.LogError("[Screenshot] Screenshot functionality not supported on Metal (macOS) backend");
            return;
        }

        IsActive = true;
        _isDragging = false;
        Logger.Log("[Screenshot] Region selection mode activated");
    }

    public void CancelSelection()
    {
        IsActive = false;
        _isDragging = false;
        Logger.Log("[Screenshot] Selection cancelled");
    }

    /// <summary>
    ///     Call this at the END of the main UI submission loop.
    /// </summary>
    public void PostUpdate()
    {
        // Handle Metal warning dialog
        if (_showMetalWarningDialog)
        {
            ImGui.OpenPopup("Screenshot Not Supported###MetalWarning");
            _showMetalWarningDialog = false;
        }

        // Metal warning dialog
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(600, 0), ImGuiCond.Appearing);
        
        // Push red color for title bar
        ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.6f, 0.0f, 0.0f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.8f, 0.0f, 0.0f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, new Vector4(0.5f, 0.0f, 0.0f, 1.0f));

        var metalWarningOpen = true;
        if (ImGui.BeginPopupModal("Screenshot Not Supported###MetalWarning", ref metalWarningOpen, 
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove))
        {
            ImGui.PopStyleColor(3); // Pop title colors

            ImGui.Spacing();
            
            // Red warning text
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.0f, 0.0f, 1.0f));
            
            // Center the warning symbol and text
            var warningText = "⚠ Screenshot Functionality not supported on Metal (MTL - MacOS) backend.";
            var textWidth = ImGui.CalcTextSize(warningText).X;
            var windowWidth = ImGui.GetContentRegionAvail().X;
            var indent = (windowWidth - textWidth) * 0.5f;
            if (indent > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + indent);
            
            ImGui.TextWrapped(warningText);
            ImGui.PopStyleColor(); // Pop red text color
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Explanation text
            ImGui.TextWrapped(
                "The Metal graphics API (used on macOS) does not allow capturing screenshots " +
                "from the swapchain backbuffer due to how it manages render targets.");
            
            ImGui.Spacing();
            ImGui.Text("Workarounds:");
            ImGui.BulletText("Use macOS's built-in screenshot tool (Cmd+Shift+4)");
            ImGui.BulletText("Run the application on Windows or Linux");
            ImGui.BulletText("Use external screen capture software");
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Center the OK button
            var buttonWidth = 120.0f;
            var buttonIndent = (windowWidth - buttonWidth) * 0.5f;
            if (buttonIndent > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + buttonIndent);

            if (ImGui.Button("OK", new Vector2(buttonWidth, 35)))
            {
                ImGui.CloseCurrentPopup();
            }

            // Handle Enter/Escape to close
            if (ImGui.IsKeyReleased(ImGuiKey.Enter) || 
                ImGui.IsKeyReleased(ImGuiKey.KeypadEnter) ||
                ImGui.IsKeyReleased(ImGuiKey.Escape))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.Spacing();
            ImGui.EndPopup();
        }
        else
        {
            ImGui.PopStyleColor(3); // Pop title colors if popup didn't open
        }

        // Handle export dialog
        if (_exportDialog.Submit()) CaptureSelectedRegion();

        // Handle pending capture (deferred to ensure UI is rendered)
        if (_pendingCapture)
        {
            _pendingCapture = false;
            var defaultName = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}";
            _exportDialog.Open(defaultName);
        }

        if (!IsActive) return;

        var mousePos = ImGui.GetMousePos();
        var io = ImGui.GetIO();

        // CRITICAL: Block all input to ImGui windows while selecting
        // This prevents window interaction (resizing, clicking buttons, etc.)
        io.WantCaptureMouse = true;
        io.WantCaptureKeyboard = true;

        // Create an invisible full-screen window to capture all input
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
        ImGui.SetNextWindowBgAlpha(0.0f); // Completely transparent

        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        if (ImGui.Begin("##ScreenshotOverlay",
                ImGuiWindowFlags.NoTitleBar |
                ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse |
                ImGuiWindowFlags.NoBringToFrontOnFocus |
                ImGuiWindowFlags.NoNavFocus |
                ImGuiWindowFlags.NoDecoration |
                ImGuiWindowFlags.NoSavedSettings))
            // Make the entire window area interactive to capture all mouse events
            ImGui.InvisibleButton("##ScreenshotCapture", viewport.Size);
        // Now the button has captured the input, preventing other windows from receiving it
        ImGui.End();
        ImGui.PopStyleVar(2);

        // Start dragging on left click
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _isDragging = true;
            _selectionStart = mousePos;
            _selectionEnd = mousePos;
        }

        // Update selection while dragging
        if (_isDragging)
        {
            _selectionEnd = mousePos;

            // Finish selection on mouse release
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                _isDragging = false;

                // Calculate the selection rectangle
                var minX = Math.Min(_selectionStart.X, _selectionEnd.X);
                var minY = Math.Min(_selectionStart.Y, _selectionEnd.Y);
                var maxX = Math.Max(_selectionStart.X, _selectionEnd.X);
                var maxY = Math.Max(_selectionStart.Y, _selectionEnd.Y);

                _capturePos = new Vector2(minX, minY);
                _captureSize = new Vector2(maxX - minX, maxY - minY);

                // Only proceed if selection is large enough
                if (_captureSize.X > 10 && _captureSize.Y > 10)
                {
                    IsActive = false; // This releases the input lock
                    _pendingCapture = true;
                    Logger.Log($"[Screenshot] Selected region: {_captureSize.X}x{_captureSize.Y} at {_capturePos}");
                }
                else
                {
                    // If selection too small, just cancel
                    CancelSelection(); // This also releases the input lock
                }
            }
        }

        // Cancel on right click or escape
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) ||
            ImGui.IsKeyPressed(ImGuiKey.Escape)) CancelSelection(); // This releases the input lock

        DrawSelectionOverlay();
    }

    private void DrawSelectionOverlay()
    {
        var drawList = ImGui.GetForegroundDrawList();
        var viewport = ImGui.GetMainViewport();

        // Semi-transparent overlay
        drawList.AddRectFilled(viewport.Pos, viewport.Pos + viewport.Size, 0x40000000);

        if (_isDragging)
        {
            // Calculate selection rectangle
            var minX = Math.Min(_selectionStart.X, _selectionEnd.X);
            var minY = Math.Min(_selectionStart.Y, _selectionEnd.Y);
            var maxX = Math.Max(_selectionStart.X, _selectionEnd.X);
            var maxY = Math.Max(_selectionStart.Y, _selectionEnd.Y);

            var selectionMin = new Vector2(minX, minY);
            var selectionMax = new Vector2(maxX, maxY);

            // Clear the selected area (make it fully visible)
            drawList.AddRectFilled(selectionMin, selectionMax, 0x00000000);

            // Draw selection border
            drawList.AddRect(selectionMin, selectionMax, 0xFF00BFFF, 0.0f, ImDrawFlags.None, 2.0f);

            // Draw corner handles
            var handleSize = 6.0f;
            var handleColor = 0xFF00BFFF;

            // Top-left
            drawList.AddRectFilled(
                selectionMin - new Vector2(handleSize / 2, handleSize / 2),
                selectionMin + new Vector2(handleSize / 2, handleSize / 2),
                handleColor);

            // Top-right
            drawList.AddRectFilled(
                new Vector2(maxX - handleSize / 2, minY - handleSize / 2),
                new Vector2(maxX + handleSize / 2, minY + handleSize / 2),
                handleColor);

            // Bottom-left
            drawList.AddRectFilled(
                new Vector2(minX - handleSize / 2, maxY - handleSize / 2),
                new Vector2(minX + handleSize / 2, maxY + handleSize / 2),
                handleColor);

            // Bottom-right
            drawList.AddRectFilled(
                selectionMax - new Vector2(handleSize / 2, handleSize / 2),
                selectionMax + new Vector2(handleSize / 2, handleSize / 2),
                handleColor);

            // Show dimensions
            var sizeText = $"{(int)(maxX - minX)} x {(int)(maxY - minY)}";
            var textSize = ImGui.CalcTextSize(sizeText);
            var textPos = new Vector2(minX + 5, minY - textSize.Y - 5);

            if (textPos.Y < viewport.Pos.Y + 50) // If too close to top, show inside
                textPos.Y = minY + 5;

            // Background for text
            var padding = new Vector2(4, 2);
            drawList.AddRectFilled(
                textPos - padding,
                textPos + textSize + padding,
                0xDD000000,
                2.0f);

            drawList.AddText(textPos, 0xFFFFFFFF, sizeText);
        }

        // Draw instructions
        var instructions = _isDragging
            ? "Release to capture selection"
            : "Click and drag to select region (Right Click / ESC to cancel)";

        var instructionSize = ImGui.CalcTextSize(instructions);
        var instructionPos = new Vector2(
            viewport.Pos.X + (viewport.Size.X - instructionSize.X) / 2,
            viewport.Pos.Y + 20);

        // Background for instructions
        var instructionPadding = new Vector2(12, 6);
        drawList.AddRectFilled(
            instructionPos - instructionPadding,
            instructionPos + instructionSize + instructionPadding,
            0xDD000000,
            4.0f);

        drawList.AddText(instructionPos, 0xFFFFFFFF, instructions);

        // Draw crosshair at mouse position when not dragging
        if (!_isDragging)
        {
            var mousePos = ImGui.GetMousePos();
            var crosshairSize = 20.0f;
            var crosshairColor = 0x80FFFFFF;

            // Horizontal line
            drawList.AddLine(
                new Vector2(mousePos.X - crosshairSize, mousePos.Y),
                new Vector2(mousePos.X + crosshairSize, mousePos.Y),
                crosshairColor, 1.0f);

            // Vertical line
            drawList.AddLine(
                new Vector2(mousePos.X, mousePos.Y - crosshairSize),
                new Vector2(mousePos.X, mousePos.Y + crosshairSize),
                crosshairColor, 1.0f);
        }
    }

    private void CaptureSelectedRegion()
    {
        var filePath = _exportDialog.SelectedPath;
        if (string.IsNullOrEmpty(filePath)) return;

        var format = Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".jpeg" or ".jpg" => ScreenshotUtility.ImageFormat.JPEG,
            ".bmp" => ScreenshotUtility.ImageFormat.BMP,
            ".tga" => ScreenshotUtility.ImageFormat.TGA,
            _ => ScreenshotUtility.ImageFormat.PNG
        };

        var success = ScreenshotUtility.CaptureFramebufferRegion(
            (int)_capturePos.X,
            (int)_capturePos.Y,
            (int)_captureSize.X,
            (int)_captureSize.Y,
            filePath,
            format);

        if (success)
            Logger.Log($"[Screenshot] Saved to: {filePath}");
        else
            Logger.LogError($"[Screenshot] Failed to save screenshot to: {filePath}");
    }

    public void TakeFullScreenshot()
    {
        // Check if we're on Metal backend
        var gd = VeldridManager.GraphicsDevice;
        if (gd != null && gd.BackendType == Veldrid.GraphicsBackend.Metal)
        {
            _showMetalWarningDialog = true;
            Logger.LogError("[Screenshot] Screenshot functionality not supported on Metal (macOS) backend");
            return;
        }

        var viewport = ImGui.GetMainViewport();
        _capturePos = viewport.Pos;
        _captureSize = viewport.Size;

        var defaultName = $"fullscreen_{DateTime.Now:yyyyMMdd_HHmmss}";
        _exportDialog.Open(defaultName);
    }
}