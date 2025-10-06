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
    /// Tool for capturing screenshots of individual ImGui windows.
    /// Uses a decentralized approach where each window reports if it is hovered.
    /// </summary>
    public class ImGuiWindowScreenshotTool
    {
        public static ImGuiWindowScreenshotTool Instance { get; private set; }

        private bool _isSelecting = false;
        private string _hoveredWindowName = null;
        private Vector2 _hoveredWindowPos;
        private Vector2 _hoveredWindowSize;
        private readonly ImGuiExportFileDialog _exportDialog;
        private string _selectedWindowName;

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
            _hoveredWindowName = null;
            Logger.Log("[Screenshot] Window selection mode activated");
        }

        public void CancelSelection()
        {
            _isSelecting = false;
            _hoveredWindowName = null;
            Logger.Log("[Screenshot] Window selection cancelled");
        }

        /// <summary>
        /// Called by any UI window to register itself as the currently hovered window.
        /// The last window to call this in a frame is considered the top-most one.
        /// </summary>
        public void ReportHoveredWindow(string name, Vector2 pos, Vector2 size)
        {
            if (!_isSelecting) return;
            _hoveredWindowName = name;
            _hoveredWindowPos = pos;
            _hoveredWindowSize = size;
        }

        /// <summary>
        /// Call this at the START of the main UI submission loop.
        /// </summary>
        public void PreUpdate()
        {
            // Clear the hovered window at the start of the frame.
            // Panels will re-report themselves if they are hovered during this frame.
            if (_isSelecting)
            {
                _hoveredWindowName = null;
            }
        }

        /// <summary>
        /// Call this at the END of the main UI submission loop.
        /// </summary>
        public void PostUpdate()
        {
            if (_exportDialog.Submit())
            {
                CaptureSelectedWindow();
            }

            if (!_isSelecting) return;

            DrawSelectionOverlay();

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                if (!string.IsNullOrEmpty(_hoveredWindowName))
                {
                    _selectedWindowName = _hoveredWindowName;
                    _isSelecting = false;
                    
                    string defaultName = $"screenshot_{_selectedWindowName.Split("###")[0].Replace(" ", "_")}";
                    _exportDialog.Open(defaultName);
                    
                    Logger.Log($"[Screenshot] Selected window: {_selectedWindowName}");
                }
            }

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) || ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                CancelSelection();
            }
        }

        private void DrawSelectionOverlay()
        {
            var drawList = ImGui.GetForegroundDrawList();
            var viewport = ImGui.GetMainViewport();

            drawList.AddRectFilled(viewport.Pos, viewport.Pos + viewport.Size, 0x7F000000); // Darken screen

            if (!string.IsNullOrEmpty(_hoveredWindowName))
            {
                // Highlight
                drawList.AddRect(_hoveredWindowPos, _hoveredWindowPos + _hoveredWindowSize, 0xFF00BFFF, 4.0f, ImDrawFlags.None, 3.0f);
                drawList.AddRectFilled(_hoveredWindowPos, _hoveredWindowPos + _hoveredWindowSize, 0x3300BFFF, 4.0f);

                // Tooltip
                string tooltipText = $"Click to capture: {_hoveredWindowName.Split("###")[0]}";
                Vector2 tooltipPos = ImGui.GetMousePos() + new Vector2(20, 20);
                drawList.AddText(tooltipPos, 0xFFFFFFFF, tooltipText);
            }
            
            string instructions = "Select a window (Left Click) or cancel (Right Click / ESC)";
            Vector2 instructionSize = ImGui.CalcTextSize(instructions);
            Vector2 instructionPos = new Vector2(viewport.Pos.X + (viewport.Size.X - instructionSize.X) / 2, viewport.Pos.Y + 20);
            drawList.AddText(instructionPos, 0xFFFFFFFF, instructions);
        }

        private void CaptureSelectedWindow()
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

            bool success = _selectedWindowName switch
            {
                "__FULLSCREEN__" => ScreenshotUtility.CaptureFullFramebuffer(filePath, format),
                not null => ScreenshotUtility.CaptureImGuiWindow(_selectedWindowName, filePath, format),
                _ => false
            };
            
            if (success) Logger.Log($"[Screenshot] Saved to: {filePath}");
            else Logger.LogError($"[Screenshot] Failed to save screenshot.");
            
            _selectedWindowName = null;
        }

        public void TakeFullScreenshot()
        {
            string defaultName = ScreenshotUtility.GenerateTimestampedFilename("fullscreen");
            _selectedWindowName = "__FULLSCREEN__";
            _exportDialog.Open(defaultName);
        }
    }
}