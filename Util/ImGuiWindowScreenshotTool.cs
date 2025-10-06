// GeoscientistToolkit/UI/ImGuiWindowScreenshotTool.cs
using System;
using System.Numerics;
using System.IO;
using ImGuiNET;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.UI
{
    /// <summary>
    /// Tool for capturing screenshots of screen regions or full screen.
    /// Uses a visual selection approach rather than window detection.
    /// </summary>
    public class ImGuiWindowScreenshotTool
    {
        public static ImGuiWindowScreenshotTool Instance { get; private set; }

        private bool _isSelecting = false;
        private bool _isDragging = false;
        private Vector2 _selectionStart;
        private Vector2 _selectionEnd;
        private readonly ImGuiExportFileDialog _exportDialog;
        private Vector2 _capturePos;
        private Vector2 _captureSize;
        private bool _pendingCapture = false;

        public bool IsActive => _isSelecting;

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

        public void StartSelection()
        {
            if (_isSelecting) return;
            _isSelecting = true;
            _isDragging = false;
            Logger.Log("[Screenshot] Region selection mode activated");
        }

        public void CancelSelection()
        {
            _isSelecting = false;
            _isDragging = false;
            Logger.Log("[Screenshot] Selection cancelled");
        }

        /// <summary>
        /// Call this at the END of the main UI submission loop.
        /// </summary>
        public void PostUpdate()
        {
            // Handle export dialog
            if (_exportDialog.Submit())
            {
                CaptureSelectedRegion();
            }

            // Handle pending capture (deferred to ensure UI is rendered)
            if (_pendingCapture)
            {
                _pendingCapture = false;
                string defaultName = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}";
                _exportDialog.Open(defaultName);
            }

            if (!_isSelecting) return;

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
            {
                // Make the entire window area interactive to capture all mouse events
                ImGui.InvisibleButton("##ScreenshotCapture", viewport.Size);
                
                // Now the button has captured the input, preventing other windows from receiving it
            }
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
                        _isSelecting = false;  // This releases the input lock
                        _pendingCapture = true;
                        Logger.Log($"[Screenshot] Selected region: {_captureSize.X}x{_captureSize.Y} at {_capturePos}");
                    }
                    else
                    {
                        // If selection too small, just cancel
                        CancelSelection();  // This also releases the input lock
                    }
                }
            }

            // Cancel on right click or escape
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) || ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                CancelSelection();  // This releases the input lock
            }

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
                    selectionMin - new Vector2(handleSize/2, handleSize/2),
                    selectionMin + new Vector2(handleSize/2, handleSize/2),
                    handleColor);
                
                // Top-right
                drawList.AddRectFilled(
                    new Vector2(maxX - handleSize/2, minY - handleSize/2),
                    new Vector2(maxX + handleSize/2, minY + handleSize/2),
                    handleColor);
                
                // Bottom-left
                drawList.AddRectFilled(
                    new Vector2(minX - handleSize/2, maxY - handleSize/2),
                    new Vector2(minX + handleSize/2, maxY + handleSize/2),
                    handleColor);
                
                // Bottom-right
                drawList.AddRectFilled(
                    selectionMax - new Vector2(handleSize/2, handleSize/2),
                    selectionMax + new Vector2(handleSize/2, handleSize/2),
                    handleColor);
                
                // Show dimensions
                var sizeText = $"{(int)(maxX - minX)} x {(int)(maxY - minY)}";
                var textSize = ImGui.CalcTextSize(sizeText);
                var textPos = new Vector2(minX + 5, minY - textSize.Y - 5);
                
                if (textPos.Y < viewport.Pos.Y + 50) // If too close to top, show inside
                {
                    textPos.Y = minY + 5;
                }
                
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
            string instructions = _isDragging 
                ? "Release to capture selection"
                : "Click and drag to select region (Right Click / ESC to cancel)";
            
            Vector2 instructionSize = ImGui.CalcTextSize(instructions);
            Vector2 instructionPos = new Vector2(
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
            string filePath = _exportDialog.SelectedPath;
            if (string.IsNullOrEmpty(filePath)) return;

            var format = Path.GetExtension(filePath).ToLowerInvariant() switch
            {
                ".jpeg" or ".jpg" => ScreenshotUtility.ImageFormat.JPEG,
                ".bmp" => ScreenshotUtility.ImageFormat.BMP,
                ".tga" => ScreenshotUtility.ImageFormat.TGA,
                _ => ScreenshotUtility.ImageFormat.PNG,
            };

            bool success = ScreenshotUtility.CaptureFramebufferRegion(
                (int)_capturePos.X, 
                (int)_capturePos.Y,
                (int)_captureSize.X, 
                (int)_captureSize.Y,
                filePath, 
                format);
            
            if (success)
            {
                Logger.Log($"[Screenshot] Saved to: {filePath}");
            }
            else
            {
                Logger.LogError($"[Screenshot] Failed to save screenshot to: {filePath}");
            }
        }

        public void TakeFullScreenshot()
        {
            var viewport = ImGui.GetMainViewport();
            _capturePos = viewport.Pos;
            _captureSize = viewport.Size;
            
            string defaultName = $"fullscreen_{DateTime.Now:yyyyMMdd_HHmmss}";
            _exportDialog.Open(defaultName);
        }
    }
}