using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Data;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.SlopeStability
{
    /// <summary>
    /// 3D viewer for slope stability analysis with color mapping and animation.
    /// </summary>
    public class SlopeStabilityViewer : IDatasetViewer
    {
        private readonly SlopeStabilityDataset _dataset;

        // View controls
        private float _cameraDistance = 50.0f;
        private float _cameraYaw = 45.0f;
        private float _cameraPitch = 30.0f;
        private Vector3 _cameraTarget = Vector3.Zero;
        private Matrix4x4 _viewMatrix;
        private Matrix4x4 _projectionMatrix;

        // Rendering options
        private bool _showBlocks = true;
        private bool _showJointPlanes = false;
        private bool _showContactPoints = false;
        private bool _showDisplacementVectors = false;
        private bool _showGrid = true;
        private bool _useWireframe = false;

        // Color mapping
        private ColorMappingMode _colorMode = ColorMappingMode.DisplacementMagnitude;
        private float _colorScaleMin = 0.0f;
        private float _colorScaleMax = 1.0f;
        private bool _autoScaleColors = true;

        // Animation
        private bool _animationEnabled = false;
        private int _currentAnimationFrame = 0;
        private float _animationSpeed = 1.0f;
        private bool _showFinalState = false;

        // Selection
        private HashSet<int> _selectedBlockIds = new HashSet<int>();
        private bool _selectionMode = false;

        // Lighting
        private Vector3 _lightDirection = Vector3.Normalize(new Vector3(1, 1, -1));
        private bool _enableLighting = true;

        public SlopeStabilityViewer(SlopeStabilityDataset dataset)
        {
            _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
            Initialize();
        }

        private void Initialize()
        {
            // Calculate camera target (center of all blocks)
            if (_dataset.Blocks.Count > 0)
            {
                Vector3 sum = Vector3.Zero;
                foreach (var block in _dataset.Blocks)
                {
                    sum += block.Centroid;
                }
                _cameraTarget = sum / _dataset.Blocks.Count;
            }

            UpdateViewMatrix();
        }

        public void DrawToolbarControls()
        {
            ImGui.Text("Slope Stability Analysis Viewer");
            ImGui.Separator();

            // View controls
            if (ImGui.CollapsingHeader("View Controls", ImGuiTreeNodeFlags.DefaultOpen))
            {
                if (ImGui.SliderFloat("Distance", ref _cameraDistance, 10.0f, 500.0f))
                    UpdateViewMatrix();

                if (ImGui.SliderFloat("Yaw", ref _cameraYaw, 0.0f, 360.0f))
                    UpdateViewMatrix();

                if (ImGui.SliderFloat("Pitch", ref _cameraPitch, -89.0f, 89.0f))
                    UpdateViewMatrix();

                if (ImGui.Button("Reset Camera"))
                {
                    _cameraDistance = 50.0f;
                    _cameraYaw = 45.0f;
                    _cameraPitch = 30.0f;
                    UpdateViewMatrix();
                }

                ImGui.Separator();
            }

            // Rendering options
            if (ImGui.CollapsingHeader("Rendering", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Checkbox("Show Blocks", ref _showBlocks);
                ImGui.Checkbox("Show Joint Planes", ref _showJointPlanes);
                ImGui.Checkbox("Show Contact Points", ref _showContactPoints);
                ImGui.Checkbox("Show Displacement Vectors", ref _showDisplacementVectors);
                ImGui.Checkbox("Show Grid", ref _showGrid);
                ImGui.Checkbox("Wireframe", ref _useWireframe);
                ImGui.Checkbox("Enable Lighting", ref _enableLighting);

                ImGui.Separator();
            }

            // Color mapping
            if (ImGui.CollapsingHeader("Color Mapping", ImGuiTreeNodeFlags.DefaultOpen))
            {
                string[] colorModes = Enum.GetNames(typeof(ColorMappingMode));
                int currentMode = (int)_colorMode;

                if (ImGui.Combo("Color By", ref currentMode, colorModes, colorModes.Length))
                {
                    _colorMode = (ColorMappingMode)currentMode;
                    if (_autoScaleColors)
                        UpdateColorScale();
                }

                ImGui.Checkbox("Auto Scale", ref _autoScaleColors);

                if (_autoScaleColors)
                {
                    ImGui.BeginDisabled();
                }

                ImGui.SliderFloat("Min", ref _colorScaleMin, 0.0f, 10.0f);
                ImGui.SliderFloat("Max", ref _colorScaleMax, 0.0f, 10.0f);

                if (_autoScaleColors)
                {
                    ImGui.EndDisabled();
                    UpdateColorScale();
                }

                ImGui.Separator();
            }

            // Animation controls
            if (_dataset.HasResults && _dataset.Results.HasTimeHistory)
            {
                if (ImGui.CollapsingHeader("Animation", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    ImGui.Checkbox("Enable Animation", ref _animationEnabled);

                    if (_animationEnabled)
                    {
                        int maxFrame = _dataset.Results.TimeHistory.Count - 1;
                        ImGui.SliderInt("Frame", ref _currentAnimationFrame, 0, maxFrame);
                        ImGui.SliderFloat("Speed", ref _animationSpeed, 0.1f, 10.0f);

                        if (ImGui.Button("Play"))
                        {
                            // Animation logic would be in update loop
                        }

                        ImGui.SameLine();

                        if (ImGui.Button("Reset"))
                        {
                            _currentAnimationFrame = 0;
                        }
                    }

                    ImGui.Separator();
                }
            }

            // Final state visualization
            if (_dataset.HasResults)
            {
                if (ImGui.CollapsingHeader("Final State"))
                {
                    ImGui.Checkbox("Show Final State", ref _showFinalState);

                    if (_showFinalState)
                    {
                        ImGui.Text("Showing block positions after settlement");
                    }

                    ImGui.Separator();
                }
            }

            // Selection tools
            if (ImGui.CollapsingHeader("Selection"))
            {
                ImGui.Checkbox("Selection Mode", ref _selectionMode);
                ImGui.Text($"Selected: {_selectedBlockIds.Count} blocks");

                if (ImGui.Button("Clear Selection"))
                {
                    _selectedBlockIds.Clear();
                }

                if (_selectedBlockIds.Count > 0)
                {
                    if (ImGui.Button("Fix Selected Blocks"))
                    {
                        foreach (var id in _selectedBlockIds)
                        {
                            var block = _dataset.Blocks.FirstOrDefault(b => b.Id == id);
                            if (block != null)
                                block.IsFixed = true;
                        }
                    }

                    if (ImGui.Button("Unfix Selected Blocks"))
                    {
                        foreach (var id in _selectedBlockIds)
                        {
                            var block = _dataset.Blocks.FirstOrDefault(b => b.Id == id);
                            if (block != null)
                                block.IsFixed = false;
                        }
                    }
                }

                ImGui.Separator();
            }

            // Statistics
            if (ImGui.CollapsingHeader("Statistics"))
            {
                ImGui.Text($"Total Blocks: {_dataset.Blocks.Count}");
                ImGui.Text($"Joint Sets: {_dataset.JointSets.Count}");
                ImGui.Text($"Materials: {_dataset.Materials.Count}");

                if (_dataset.HasResults)
                {
                    var results = _dataset.Results;
                    ImGui.Text($"Max Displacement: {results.MaxDisplacement:F3} m");
                    ImGui.Text($"Mean Displacement: {results.MeanDisplacement:F3} m");
                    ImGui.Text($"Failed Blocks: {results.NumFailedBlocks}");
                    ImGui.Text($"Sliding Contacts: {results.NumSlidingContacts}");
                    ImGui.Text($"Computation Time: {results.ComputationTimeSeconds:F2} s");
                }
            }
        }

        public void DrawContent(ref float zoom, ref Vector2 pan)
        {
            // Get available size
            var availableSize = ImGui.GetContentRegionAvail();

            if (availableSize.X <= 0 || availableSize.Y <= 0)
                return;

            // Update projection matrix
            float aspectRatio = availableSize.X / availableSize.Y;
            _projectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(
                MathF.PI / 4.0f, aspectRatio, 0.1f, 1000.0f);

            // Render 3D scene
            // NOTE: Actual rendering would use Veldrid like other viewers in the project
            // For now, we show a placeholder

            // Create a child window for 3D rendering
            ImGui.BeginChild("3DView", availableSize, true);

            // Draw placeholder text (actual rendering would use Veldrid)
            var drawList = ImGui.GetWindowDrawList();
            var windowPos = ImGui.GetCursorScreenPos();

            DrawScene(drawList, windowPos, availableSize);

            // Handle mouse interaction
            if (ImGui.IsWindowHovered())
            {
                HandleMouseInput();
            }

            ImGui.EndChild();
        }

        /// <summary>
        /// Draws the 3D scene (simplified - actual implementation would use Veldrid).
        /// </summary>
        private void DrawScene(ImDrawListPtr drawList, Vector2 windowPos, Vector2 size)
        {
            // Background
            drawList.AddRectFilled(windowPos, windowPos + size,
                ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.15f, 1.0f)));

            // Grid
            if (_showGrid)
            {
                DrawGrid(drawList, windowPos, size);
            }

            // Blocks
            if (_showBlocks)
            {
                DrawBlocks(drawList, windowPos, size);
            }

            // Joint planes
            if (_showJointPlanes)
            {
                DrawJointPlanes(drawList, windowPos, size);
            }

            // Contact points
            if (_showContactPoints && _dataset.HasResults)
            {
                DrawContactPoints(drawList, windowPos, size);
            }

            // Displacement vectors
            if (_showDisplacementVectors && _dataset.HasResults)
            {
                DrawDisplacementVectors(drawList, windowPos, size);
            }

            // Color scale legend
            DrawColorScaleLegend(drawList, windowPos, size);
        }

        private void DrawGrid(ImDrawListPtr drawList, Vector2 windowPos, Vector2 size)
        {
            // Simple grid rendering
            uint gridColor = ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 0.5f));

            for (int i = 0; i <= 10; i++)
            {
                float x = windowPos.X + (size.X * i / 10.0f);
                drawList.AddLine(new Vector2(x, windowPos.Y), new Vector2(x, windowPos.Y + size.Y), gridColor);

                float y = windowPos.Y + (size.Y * i / 10.0f);
                drawList.AddLine(new Vector2(windowPos.X, y), new Vector2(windowPos.X + size.X, y), gridColor);
            }
        }

        private void DrawBlocks(ImDrawListPtr drawList, Vector2 windowPos, Vector2 size)
        {
            foreach (var block in _dataset.Blocks)
            {
                // Get block color based on color mapping mode
                Vector4 color = GetBlockColor(block);

                // Project block centroid to screen
                Vector2 screenPos = ProjectToScreen(block.Position, windowPos, size);

                // Draw block as box (simplified)
                float blockSize = 5.0f / _cameraDistance;
                blockSize = Math.Max(blockSize, 2.0f);

                uint colorU32 = ImGui.GetColorU32(color);

                if (_selectedBlockIds.Contains(block.Id))
                {
                    // Highlight selected blocks
                    colorU32 = ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 0.0f, 1.0f));
                }

                drawList.AddRectFilled(
                    screenPos - new Vector2(blockSize, blockSize),
                    screenPos + new Vector2(blockSize, blockSize),
                    colorU32);

                // Draw outline
                if (_useWireframe || _selectedBlockIds.Contains(block.Id))
                {
                    drawList.AddRect(
                        screenPos - new Vector2(blockSize, blockSize),
                        screenPos + new Vector2(blockSize, blockSize),
                        ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 1.0f)));
                }
            }
        }

        private void DrawJointPlanes(ImDrawListPtr drawList, Vector2 windowPos, Vector2 size)
        {
            foreach (var jointSet in _dataset.JointSets)
            {
                // Draw representation of joint planes (simplified)
                Vector3 normal = jointSet.GetNormal();
                Vector3 planeCenter = _cameraTarget;

                Vector2 screenPos = ProjectToScreen(planeCenter, windowPos, size);

                uint color = ImGui.GetColorU32(jointSet.Color);

                drawList.AddCircle(screenPos, 20.0f, color, 16);
            }
        }

        private void DrawContactPoints(ImDrawListPtr drawList, Vector2 windowPos, Vector2 size)
        {
            foreach (var contact in _dataset.Results.ContactResults)
            {
                Vector2 screenPos = ProjectToScreen(contact.ContactPoint, windowPos, size);

                uint color = contact.HasSlipped ?
                    ImGui.GetColorU32(new Vector4(1.0f, 0.0f, 0.0f, 1.0f)) :
                    ImGui.GetColorU32(new Vector4(0.0f, 1.0f, 0.0f, 1.0f));

                drawList.AddCircleFilled(screenPos, 3.0f, color);
            }
        }

        private void DrawDisplacementVectors(ImDrawListPtr drawList, Vector2 windowPos, Vector2 size)
        {
            foreach (var blockResult in _dataset.Results.BlockResults)
            {
                if (blockResult.Displacement.Length() < 0.001f)
                    continue;

                var block = _dataset.Blocks.FirstOrDefault(b => b.Id == blockResult.BlockId);
                if (block == null)
                    continue;

                Vector2 startPos = ProjectToScreen(blockResult.InitialPosition, windowPos, size);
                Vector2 endPos = ProjectToScreen(blockResult.FinalPosition, windowPos, size);

                uint color = ImGui.GetColorU32(new Vector4(1.0f, 0.5f, 0.0f, 1.0f));
                drawList.AddLine(startPos, endPos, color, 2.0f);

                // Arrow head
                Vector2 dir = Vector2.Normalize(endPos - startPos);
                Vector2 perp = new Vector2(-dir.Y, dir.X);

                drawList.AddTriangleFilled(
                    endPos,
                    endPos - dir * 8.0f + perp * 4.0f,
                    endPos - dir * 8.0f - perp * 4.0f,
                    color);
            }
        }

        private void DrawColorScaleLegend(ImDrawListPtr drawList, Vector2 windowPos, Vector2 size)
        {
            // Draw color scale legend in bottom right
            float legendWidth = 30.0f;
            float legendHeight = 200.0f;
            float margin = 20.0f;

            Vector2 legendPos = new Vector2(
                windowPos.X + size.X - legendWidth - margin,
                windowPos.Y + size.Y - legendHeight - margin);

            // Draw gradient bar
            for (int i = 0; i < 100; i++)
            {
                float t = i / 100.0f;
                float value = _colorScaleMin + t * (_colorScaleMax - _colorScaleMin);

                Vector4 color = GetColorFromValue(value);
                uint colorU32 = ImGui.GetColorU32(color);

                float y = legendPos.Y + legendHeight * (1.0f - t);
                float yNext = legendPos.Y + legendHeight * (1.0f - (i + 1) / 100.0f);

                drawList.AddRectFilled(
                    new Vector2(legendPos.X, y),
                    new Vector2(legendPos.X + legendWidth, yNext),
                    colorU32);
            }

            // Draw border
            drawList.AddRect(legendPos, legendPos + new Vector2(legendWidth, legendHeight),
                ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 1.0f)));

            // Draw labels
            string minLabel = $"{_colorScaleMin:F2}";
            string maxLabel = $"{_colorScaleMax:F2}";

            drawList.AddText(new Vector2(legendPos.X + legendWidth + 5, legendPos.Y + legendHeight - 10),
                ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 1.0f)), minLabel);

            drawList.AddText(new Vector2(legendPos.X + legendWidth + 5, legendPos.Y - 5),
                ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 1.0f)), maxLabel);

            // Color mode label
            drawList.AddText(new Vector2(legendPos.X - 50, legendPos.Y - 20),
                ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 1.0f)),
                _colorMode.ToString());
        }

        /// <summary>
        /// Projects a 3D world position to 2D screen coordinates.
        /// </summary>
        private Vector2 ProjectToScreen(Vector3 worldPos, Vector2 windowPos, Vector2 size)
        {
            // Simple orthographic projection (simplified)
            // Actual implementation would use proper view/projection matrices

            Vector3 viewPos = worldPos - _cameraTarget;

            // Rotate based on camera angles
            float yawRad = _cameraYaw * MathF.PI / 180.0f;
            float pitchRad = _cameraPitch * MathF.PI / 180.0f;

            float cosYaw = MathF.Cos(yawRad);
            float sinYaw = MathF.Sin(yawRad);
            float cosPitch = MathF.Cos(pitchRad);
            float sinPitch = MathF.Sin(pitchRad);

            // Rotate around Y (yaw)
            float x = viewPos.X * cosYaw - viewPos.Z * sinYaw;
            float z = viewPos.X * sinYaw + viewPos.Z * cosYaw;

            // Rotate around X (pitch)
            float y = viewPos.Y * cosPitch - z * sinPitch;
            z = viewPos.Y * sinPitch + z * cosPitch;

            // Project to screen
            float scale = 2.0f / _cameraDistance;

            float screenX = windowPos.X + size.X / 2.0f + x * scale * size.X / 2.0f;
            float screenY = windowPos.Y + size.Y / 2.0f - y * scale * size.Y / 2.0f;

            return new Vector2(screenX, screenY);
        }

        /// <summary>
        /// Gets the color for a block based on current color mapping mode.
        /// </summary>
        private Vector4 GetBlockColor(Block block)
        {
            float value = 0.0f;

            switch (_colorMode)
            {
                case ColorMappingMode.Material:
                    var material = _dataset.GetMaterial(block.MaterialId);
                    return material?.Color ?? new Vector4(0.7f, 0.7f, 0.7f, 1.0f);

                case ColorMappingMode.DisplacementMagnitude:
                    value = block.TotalDisplacement.Length();
                    break;

                case ColorMappingMode.Velocity:
                    value = block.Velocity.Length();
                    break;

                case ColorMappingMode.Fixed:
                    return block.IsFixed ?
                        new Vector4(0.5f, 0.5f, 0.5f, 1.0f) :
                        new Vector4(0.7f, 0.7f, 0.9f, 1.0f);

                case ColorMappingMode.Mass:
                    value = block.Mass;
                    break;
            }

            return GetColorFromValue(value);
        }

        /// <summary>
        /// Maps a value to a color using a colormap (viridis-like).
        /// </summary>
        private Vector4 GetColorFromValue(float value)
        {
            // Normalize value
            float t = (_colorScaleMax - _colorScaleMin) > 1e-8f ?
                (value - _colorScaleMin) / (_colorScaleMax - _colorScaleMin) :
                0.0f;

            t = Math.Clamp(t, 0.0f, 1.0f);

            // Simple blue-to-red colormap
            if (t < 0.5f)
            {
                // Blue to cyan to green
                float localT = t * 2.0f;
                return new Vector4(0.0f, localT, 1.0f - localT, 1.0f);
            }
            else
            {
                // Green to yellow to red
                float localT = (t - 0.5f) * 2.0f;
                return new Vector4(localT, 1.0f - localT, 0.0f, 1.0f);
            }
        }

        /// <summary>
        /// Updates color scale min/max based on current data.
        /// </summary>
        private void UpdateColorScale()
        {
            if (!_dataset.HasResults)
                return;

            float min = float.MaxValue;
            float max = float.MinValue;

            foreach (var block in _dataset.Blocks)
            {
                float value = 0.0f;

                switch (_colorMode)
                {
                    case ColorMappingMode.DisplacementMagnitude:
                        value = block.TotalDisplacement.Length();
                        break;
                    case ColorMappingMode.Velocity:
                        value = block.Velocity.Length();
                        break;
                    case ColorMappingMode.Mass:
                        value = block.Mass;
                        break;
                }

                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }

            _colorScaleMin = min;
            _colorScaleMax = max;
        }

        private void UpdateViewMatrix()
        {
            float yawRad = _cameraYaw * MathF.PI / 180.0f;
            float pitchRad = _cameraPitch * MathF.PI / 180.0f;

            Vector3 cameraPos = _cameraTarget + new Vector3(
                _cameraDistance * MathF.Cos(pitchRad) * MathF.Cos(yawRad),
                _cameraDistance * MathF.Cos(pitchRad) * MathF.Sin(yawRad),
                _cameraDistance * MathF.Sin(pitchRad));

            _viewMatrix = Matrix4x4.CreateLookAt(cameraPos, _cameraTarget, Vector3.UnitZ);
        }

        private void HandleMouseInput()
        {
            var io = ImGui.GetIO();

            // Mouse wheel for zoom
            if (io.MouseWheel != 0)
            {
                _cameraDistance -= io.MouseWheel * 5.0f;
                _cameraDistance = Math.Clamp(_cameraDistance, 5.0f, 500.0f);
                UpdateViewMatrix();
            }

            // Right mouse button to rotate
            if (ImGui.IsMouseDown(ImGuiMouseButton.Right))
            {
                _cameraYaw += io.MouseDelta.X * 0.5f;
                _cameraPitch -= io.MouseDelta.Y * 0.5f;
                _cameraPitch = Math.Clamp(_cameraPitch, -89.0f, 89.0f);
                UpdateViewMatrix();
            }

            // Left click for selection
            if (_selectionMode && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                // Simple selection: find nearest block to click position
                // Actual implementation would use ray casting
            }
        }

        public void Dispose()
        {
            // Cleanup resources
        }
    }

    /// <summary>
    /// Color mapping modes for visualization.
    /// </summary>
    public enum ColorMappingMode
    {
        Material,
        DisplacementMagnitude,
        Velocity,
        Fixed,
        Mass,
        Contacts
    }
}
