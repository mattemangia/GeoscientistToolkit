// GeoscientistToolkit/Data/Image/ScaleCalibrationTool.cs
using System;
using System.Numerics;
using ImGuiNET;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Image
{
    /// <summary>
    /// Tool for calibrating image scale by drawing a reference line
    /// </summary>
    public class ScaleCalibrationTool
    {
        private bool _isCalibrating = false;
        private bool _hasFirstPoint = false;
        private Vector2 _firstPoint;
        private Vector2 _secondPoint;
        private float _knownDistance = 100.0f;
        private string _unit = "µm";
        private int _unitIndex = 0;
        private readonly string[] _units = { "nm", "µm", "mm", "cm", "m", "km" };
        private readonly float[] _unitConversions = { 0.001f, 1.0f, 1000.0f, 10000.0f, 1000000.0f, 1000000000.0f };

        public bool IsActive => _isCalibrating;

        public void StartCalibration()
        {
            _isCalibrating = true;
            _hasFirstPoint = false;
            Logger.Log("Scale calibration started - click to set first point");
        }

        public void Cancel()
        {
            _isCalibrating = false;
            _hasFirstPoint = false;
        }

        public bool HandleMouseClick(Vector2 imagePos, Vector2 displaySize, int imageWidth, int imageHeight, Vector2 clickPos)
        {
            if (!_isCalibrating) return false;

            // Convert click position to image coordinates
            Vector2 relativePos = clickPos - imagePos;
            if (relativePos.X < 0 || relativePos.Y < 0 || 
                relativePos.X > displaySize.X || relativePos.Y > displaySize.Y)
                return false;

            Vector2 imageCoords = new Vector2(
                relativePos.X / displaySize.X * imageWidth,
                relativePos.Y / displaySize.Y * imageHeight
            );

            if (!_hasFirstPoint)
            {
                _firstPoint = imageCoords;
                _hasFirstPoint = true;
                Logger.Log("First calibration point set - click to set second point");
            }
            else
            {
                _secondPoint = imageCoords;
                _isCalibrating = false;
                
                // Open calibration dialog
                ImGui.OpenPopup("Scale Calibration");
            }

            return true;
        }

        public void DrawOverlay(ImDrawListPtr dl, Vector2 imagePos, Vector2 displaySize, int imageWidth, int imageHeight)
        {
            if (!_isCalibrating) return;

            if (_hasFirstPoint)
            {
                // Convert image coordinates back to screen coordinates
                Vector2 screenFirst = imagePos + new Vector2(
                    _firstPoint.X / imageWidth * displaySize.X,
                    _firstPoint.Y / imageHeight * displaySize.Y
                );

                // Draw first point
                dl.AddCircleFilled(screenFirst, 5, ImGui.GetColorU32(new Vector4(1, 0, 0, 1)));
                dl.AddCircle(screenFirst, 5, ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), 0, 2);

                // If mouse is moving, draw line to current position
                var io = ImGui.GetIO();
                if (io.MousePos.X >= imagePos.X && io.MousePos.X <= imagePos.X + displaySize.X &&
                    io.MousePos.Y >= imagePos.Y && io.MousePos.Y <= imagePos.Y + displaySize.Y)
                {
                    dl.AddLine(screenFirst, io.MousePos, ImGui.GetColorU32(new Vector4(1, 1, 0, 1)), 2);
                    
                    // Calculate and display distance
                    Vector2 relativePos = io.MousePos - imagePos;
                    Vector2 currentImageCoords = new Vector2(
                        relativePos.X / displaySize.X * imageWidth,
                        relativePos.Y / displaySize.Y * imageHeight
                    );
                    
                    float pixelDistance = Vector2.Distance(_firstPoint, currentImageCoords);
                    string distanceText = $"{pixelDistance:F1} pixels";
                    
                    Vector2 textPos = io.MousePos + new Vector2(10, -20);
                    dl.AddText(textPos, ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), distanceText);
                }
            }
        }

        public bool DrawCalibrationDialog(out float pixelSize, out string unit)
        {
            pixelSize = 0;
            unit = _unit;
            bool result = false;

            if (ImGui.BeginPopupModal("Scale Calibration", ImGuiWindowFlags.AlwaysAutoResize))
            {
                float pixelDistance = Vector2.Distance(_firstPoint, _secondPoint);
                
                ImGui.Text($"Measured distance: {pixelDistance:F2} pixels");
                ImGui.Separator();
                
                ImGui.Text("Enter the known distance:");
                ImGui.SetNextItemWidth(150);
                ImGui.InputFloat("##KnownDistance", ref _knownDistance, 1.0f, 10.0f, "%.3f");
                
                ImGui.SameLine();
                ImGui.SetNextItemWidth(100);
                if (ImGui.Combo("##Unit", ref _unitIndex, _units, _units.Length))
                {
                    _unit = _units[_unitIndex];
                }
                
                ImGui.Separator();
                
                // Calculate pixel size in micrometers
                float knownDistanceInMicrometers = _knownDistance * _unitConversions[_unitIndex];
                float calculatedPixelSize = knownDistanceInMicrometers / pixelDistance;
                
                ImGui.Text($"Calculated scale: {calculatedPixelSize:F3} µm/pixel");
                
                ImGui.Separator();
                
                if (ImGui.Button("Apply", new Vector2(120, 0)))
                {
                    pixelSize = calculatedPixelSize;
                    unit = "µm";
                    result = true;
                    _hasFirstPoint = false;
                    ImGui.CloseCurrentPopup();
                }
                
                ImGui.SameLine();
                
                if (ImGui.Button("Cancel", new Vector2(120, 0)))
                {
                    _hasFirstPoint = false;
                    ImGui.CloseCurrentPopup();
                }
                
                ImGui.EndPopup();
            }
            
            return result;
        }
    }
}