using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.SlopeStability
{
    /// <summary>
    /// 2D cross-section viewer for slope stability analysis.
    /// Provides geological section views similar to professional analysis tools.
    /// Shows blocks, joint traces, displacement vectors, and color-coded stability factors.
    /// </summary>
    public class SlopeStability2DViewer : IDatasetViewer
    {
        private readonly SlopeStabilityDataset _dataset;
        private SectionPlane _currentSection;
        private List<SectionPlane> _predefinedSections;
        private int _selectedSectionIndex = 0;

        // View settings
        private Vector2 _viewOffset = Vector2.Zero;
        private float _zoom = 1.0f;
        private bool _showBlocks = true;
        private bool _showJointTraces = true;
        private bool _showDisplacementVectors = true;
        private bool _showGrid = true;
        private bool _showWaterTable = true;
        private ColorMappingMode _colorMode = ColorMappingMode.DisplacementMagnitude;

        // Section editing
        private bool _isEditingSection = false;
        private Vector3 _customSectionOrigin = Vector3.Zero;
        private Vector3 _customSectionNormal = Vector3.UnitX;

        // Rendering cache
        private List<Block2D> _sectionBlocks = new List<Block2D>();
        private List<JointTrace2D> _jointTraces = new List<JointTrace2D>();
        private bool _needsRebuild = true;

        public SlopeStability2DViewer(SlopeStabilityDataset dataset)
        {
            _dataset = dataset;
            InitializePredefinedSections();
            _currentSection = _predefinedSections[0];
        }

        public void DrawToolbarControls()
        {
            RenderToolbar();
        }

        public void DrawContent(ref float zoom, ref Vector2 pan)
        {
            _zoom = zoom;
            _viewOffset = pan;

            // Split view: controls on left, viewport on right
            if (ImGui.BeginChild("LeftPanel", new System.Numerics.Vector2(250, 0), ImGuiChildFlags.Border))
            {
                RenderControlPanel();
            }
            ImGui.EndChild();

            ImGui.SameLine();

            if (ImGui.BeginChild("Viewport", new System.Numerics.Vector2(0, 0), ImGuiChildFlags.Border))
            {
                RenderViewport();
            }
            ImGui.EndChild();

            zoom = _zoom;
            pan = _viewOffset;
        }

        public void Dispose()
        {
        }

        private void InitializePredefinedSections()
        {
            _predefinedSections = new List<SectionPlane>();

            // Calculate slope bounds for section placement
            var bounds = CalculateSlopeBounds();
            Vector3 center = (bounds.min + bounds.max) / 2.0f;

            // Section 1: Along-strike (perpendicular to dip direction)
            _predefinedSections.Add(new SectionPlane
            {
                Name = "Along-Strike Section",
                Origin = center,
                Normal = Vector3.UnitX,  // YZ plane
                UpDirection = Vector3.UnitZ,
                Description = "Section perpendicular to slope dip direction"
            });

            // Section 2: Along-dip (parallel to dip direction)
            _predefinedSections.Add(new SectionPlane
            {
                Name = "Along-Dip Section",
                Origin = center,
                Normal = Vector3.UnitY,  // XZ plane
                UpDirection = Vector3.UnitZ,
                Description = "Section parallel to slope dip direction"
            });

            // Section 3: Horizontal plan view
            _predefinedSections.Add(new SectionPlane
            {
                Name = "Plan View (Horizontal)",
                Origin = center,
                Normal = Vector3.UnitZ,  // XY plane
                UpDirection = Vector3.UnitY,
                Description = "Horizontal section through slope"
            });

            // Section 4: Custom section
            _predefinedSections.Add(new SectionPlane
            {
                Name = "Custom Section",
                Origin = center,
                Normal = _customSectionNormal,
                UpDirection = Vector3.UnitZ,
                Description = "User-defined section plane"
            });
        }

        private void RenderToolbar()
        {
            // Section selection
            ImGui.Text("Section:");
            ImGui.SameLine();

            if (ImGui.Combo("##Section", ref _selectedSectionIndex,
                _predefinedSections.Select(s => s.Name).ToArray(),
                _predefinedSections.Count))
            {
                _currentSection = _predefinedSections[_selectedSectionIndex];
                _needsRebuild = true;
            }

            ImGui.SameLine();
            if (ImGui.Button("Edit Section"))
            {
                _isEditingSection = !_isEditingSection;
            }

            ImGui.SameLine();
            if (ImGui.Button("Rebuild"))
            {
                _needsRebuild = true;
            }

            // Zoom controls
            ImGui.SameLine();
            ImGui.Text($"Zoom: {_zoom:F2}x");
            ImGui.SameLine();
            if (ImGui.Button("-"))
                _zoom = Math.Max(0.1f, _zoom / 1.2f);
            ImGui.SameLine();
            if (ImGui.Button("+"))
                _zoom = Math.Min(10.0f, _zoom * 1.2f);
        }

        private void RenderControlPanel()
        {
            ImGui.Text("Display Options");
            ImGui.Separator();

            ImGui.Checkbox("Show Blocks", ref _showBlocks);
            ImGui.Checkbox("Show Joint Traces", ref _showJointTraces);
            ImGui.Checkbox("Show Displacement Vectors", ref _showDisplacementVectors);
            ImGui.Checkbox("Show Grid", ref _showGrid);
            ImGui.Checkbox("Show Water Table", ref _showWaterTable);

            ImGui.Separator();
            ImGui.Text("Color Mapping");

            int colorModeInt = (int)_colorMode;
            if (ImGui.Combo("Color Mode", ref colorModeInt,
                "Displacement\0Velocity\0Material\0Safety Factor\0Stress\0"))
            {
                _colorMode = (ColorMappingMode)colorModeInt;
            }

            if (_isEditingSection)
            {
                ImGui.Separator();
                ImGui.Text("Custom Section");

                ImGui.DragFloat3("Origin", ref _customSectionOrigin, 0.1f);
                ImGui.DragFloat3("Normal", ref _customSectionNormal, 0.01f);

                if (ImGui.Button("Apply"))
                {
                    _customSectionNormal = Vector3.Normalize(_customSectionNormal);
                    _predefinedSections[3].Origin = _customSectionOrigin;
                    _predefinedSections[3].Normal = _customSectionNormal;

                    if (_selectedSectionIndex == 3)
                    {
                        _currentSection = _predefinedSections[3];
                        _needsRebuild = true;
                    }
                }
            }

            ImGui.Separator();
            ImGui.Text("Section Info");
            ImGui.TextWrapped(_currentSection.Description);
            ImGui.Text($"Origin: ({_currentSection.Origin.X:F1}, {_currentSection.Origin.Y:F1}, {_currentSection.Origin.Z:F1})");
            ImGui.Text($"Normal: ({_currentSection.Normal.X:F2}, {_currentSection.Normal.Y:F2}, {_currentSection.Normal.Z:F2})");

            if (_dataset.HasResults)
            {
                ImGui.Separator();
                ImGui.Text($"Blocks in section: {_sectionBlocks.Count}");
                ImGui.Text($"Joint traces: {_jointTraces.Count}");
            }
        }

        private void RenderViewport()
        {
            // Rebuild section geometry if needed
            if (_needsRebuild && _dataset.Blocks.Count > 0)
            {
                BuildSectionGeometry();
                _needsRebuild = false;
            }

            var drawList = ImGui.GetWindowDrawList();
            var windowPos = ImGui.GetWindowPos();
            var windowSize = ImGui.GetWindowSize();
            Vector2 center = new Vector2(windowPos.X + windowSize.X / 2, windowPos.Y + windowSize.Y / 2);

            // Draw grid
            if (_showGrid)
            {
                DrawGrid(drawList, center, windowSize);
            }

            // Draw water table
            if (_showWaterTable && _dataset.Parameters.IncludeFluidPressure)
            {
                DrawWaterTable(drawList, center);
            }

            // Draw blocks
            if (_showBlocks)
            {
                DrawBlocks(drawList, center);
            }

            // Draw joint traces
            if (_showJointTraces)
            {
                DrawJointTraces(drawList, center);
            }

            // Draw displacement vectors
            if (_showDisplacementVectors && _dataset.HasResults)
            {
                DrawDisplacementVectors(drawList, center);
            }

            // Handle mouse interactions
            HandleMouseInput(center, windowSize);
        }

        private void BuildSectionGeometry()
        {
            _sectionBlocks.Clear();
            _jointTraces.Clear();

            // Project blocks onto section plane
            foreach (var block in _dataset.Blocks)
            {
                var block2D = ProjectBlockToSection(block);
                if (block2D != null)
                    _sectionBlocks.Add(block2D);
            }

            // Calculate joint traces
            foreach (var jointSet in _dataset.JointSets)
            {
                var traces = CalculateJointTraces(jointSet);
                _jointTraces.AddRange(traces);
            }
        }

        private Block2D ProjectBlockToSection(Block block)
        {
            // Get block position (use final position if results available)
            Vector3 blockPos = _dataset.HasResults
                ? _dataset.Results.BlockResults.FirstOrDefault(r => r.BlockId == block.Id)?.FinalPosition ?? block.Position
                : block.Position;

            // Calculate distance from section plane
            float distanceToPlane = Vector3.Dot(blockPos - _currentSection.Origin, _currentSection.Normal);

            // Only include blocks near the section plane (within threshold)
            float threshold = 2.0f;  // 2 meters on either side
            if (Math.Abs(distanceToPlane) > threshold)
                return null;

            // Project position onto section plane
            Vector3 projected3D = blockPos - distanceToPlane * _currentSection.Normal;

            // Convert 3D projected position to 2D coordinates in section plane
            Vector3 right = Vector3.Normalize(Vector3.Cross(_currentSection.UpDirection, _currentSection.Normal));
            Vector3 localPos = projected3D - _currentSection.Origin;

            Vector2 pos2D = new Vector2(
                Vector3.Dot(localPos, right),
                Vector3.Dot(localPos, _currentSection.UpDirection)
            );

            // Get displacement if available
            Vector2 displacement2D = Vector2.Zero;
            float displacementMag = 0.0f;

            if (_dataset.HasResults)
            {
                var result = _dataset.Results.BlockResults.FirstOrDefault(r => r.BlockId == block.Id);
                if (result != null)
                {
                    Vector3 disp3D = result.Displacement;
                    displacement2D = new Vector2(
                        Vector3.Dot(disp3D, right),
                        Vector3.Dot(disp3D, _currentSection.UpDirection)
                    );
                    displacementMag = result.Displacement.Length();
                }
            }

            return new Block2D
            {
                BlockId = block.Id,
                Position = pos2D,
                Displacement = displacement2D,
                DisplacementMagnitude = displacementMag,
                Radius = MathF.Cbrt(block.Volume) / 2.0f,  // Approximate as sphere
                Color = GetBlockColor(block, displacementMag),
                IsFixed = block.IsFixed
            };
        }

        private List<JointTrace2D> CalculateJointTraces(JointSet jointSet)
        {
            var traces = new List<JointTrace2D>();

            // Get joint plane normal
            Vector3 jointNormal = jointSet.GetNormal();

            // Calculate intersection line between joint plane and section plane
            Vector3 lineDir = Vector3.Cross(jointNormal, _currentSection.Normal);

            // If parallel, no intersection
            if (lineDir.LengthSquared() < 1e-6f)
                return traces;

            lineDir = Vector3.Normalize(lineDir);

            // Calculate multiple joint planes at different spacings
            int numPlanes = 20;
            for (int i = -numPlanes; i <= numPlanes; i++)
            {
                float offset = i * jointSet.Spacing;

                // Find a point on the intersection line
                // This is a bit complex, simplified here
                Vector3 pointOnLine = _currentSection.Origin + offset * jointNormal;

                // Project to 2D
                Vector3 right = Vector3.Normalize(Vector3.Cross(_currentSection.UpDirection, _currentSection.Normal));
                Vector3 localPos = pointOnLine - _currentSection.Origin;

                Vector2 pos2D = new Vector2(
                    Vector3.Dot(localPos, right),
                    Vector3.Dot(localPos, _currentSection.UpDirection)
                );

                Vector2 dir2D = new Vector2(
                    Vector3.Dot(lineDir, right),
                    Vector3.Dot(lineDir, _currentSection.UpDirection)
                );

                if (dir2D.LengthSquared() > 1e-6f)
                {
                    dir2D = Vector2.Normalize(dir2D);

                    traces.Add(new JointTrace2D
                    {
                        Position = pos2D,
                        Direction = dir2D,
                        JointSetId = jointSet.Id,
                        Length = 1000.0f  // Very long line
                    });
                }
            }

            return traces;
        }

        private void DrawGrid(ImDrawListPtr drawList, Vector2 center, Vector2 windowSize)
        {
            uint gridColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.3f, 0.3f, 1.0f));
            float gridSpacing = 10.0f * _zoom;

            for (float x = -500; x <= 500; x += gridSpacing)
            {
                drawList.AddLine(
                    new Vector2(center.X + x, center.Y - 500),
                    new Vector2(center.X + x, center.Y + 500),
                    gridColor);
            }

            for (float y = -500; y <= 500; y += gridSpacing)
            {
                drawList.AddLine(
                    new Vector2(center.X - 500, center.Y + y),
                    new Vector2(center.X + 500, center.Y + y),
                    gridColor);
            }

            // Draw axes
            uint axisColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
            drawList.AddLine(
                new Vector2(center.X - 500, center.Y),
                new Vector2(center.X + 500, center.Y),
                axisColor, 2.0f);
            drawList.AddLine(
                new Vector2(center.X, center.Y - 500),
                new Vector2(center.X, center.Y + 500),
                axisColor, 2.0f);
        }

        private void DrawWaterTable(ImDrawListPtr drawList, Vector2 center)
        {
            // Project water table to section
            Vector3 waterTablePoint = new Vector3(0, 0, _dataset.Parameters.WaterTableZ);
            float distanceToPlane = Vector3.Dot(waterTablePoint - _currentSection.Origin, _currentSection.Normal);
            Vector3 projected3D = waterTablePoint - distanceToPlane * _currentSection.Normal;

            Vector3 right = Vector3.Normalize(Vector3.Cross(_currentSection.UpDirection, _currentSection.Normal));
            Vector3 localPos = projected3D - _currentSection.Origin;

            float y2D = Vector3.Dot(localPos, _currentSection.UpDirection) * _zoom;

            uint waterColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.0f, 0.5f, 1.0f, 0.3f));
            drawList.AddLine(
                new Vector2(center.X - 500, center.Y - y2D),
                new Vector2(center.X + 500, center.Y - y2D),
                waterColor, 3.0f);
        }

        private void DrawBlocks(ImDrawListPtr drawList, Vector2 center)
        {
            foreach (var block in _sectionBlocks)
            {
                Vector2 screenPos = new Vector2(
                    center.X + (block.Position.X + _viewOffset.X) * _zoom,
                    center.Y - (block.Position.Y + _viewOffset.Y) * _zoom
                );

                float radius = block.Radius * _zoom;

                drawList.AddCircleFilled(screenPos, radius, block.Color);
                drawList.AddCircle(screenPos, radius, ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 1)), 0, 1.5f);

                if (block.IsFixed)
                {
                    // Draw X for fixed blocks
                    drawList.AddLine(
                        screenPos - new Vector2(radius, radius),
                        screenPos + new Vector2(radius, radius),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(1, 0, 0, 1)), 2.0f);
                    drawList.AddLine(
                        screenPos + new Vector2(-radius, radius),
                        screenPos + new Vector2(radius, -radius),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(1, 0, 0, 1)), 2.0f);
                }
            }
        }

        private void DrawJointTraces(ImDrawListPtr drawList, Vector2 center)
        {
            uint jointColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1.0f, 0.0f, 0.0f, 0.8f));

            foreach (var trace in _jointTraces)
            {
                Vector2 start = new Vector2(
                    center.X + (trace.Position.X - trace.Direction.X * trace.Length / 2 + _viewOffset.X) * _zoom,
                    center.Y - (trace.Position.Y - trace.Direction.Y * trace.Length / 2 + _viewOffset.Y) * _zoom
                );

                Vector2 end = new Vector2(
                    center.X + (trace.Position.X + trace.Direction.X * trace.Length / 2 + _viewOffset.X) * _zoom,
                    center.Y - (trace.Position.Y + trace.Direction.Y * trace.Length / 2 + _viewOffset.Y) * _zoom
                );

                drawList.AddLine(start, end, jointColor, 1.5f);
            }
        }

        private void DrawDisplacementVectors(ImDrawListPtr drawList, Vector2 center)
        {
            uint vectorColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.0f, 1.0f, 0.0f, 1.0f));
            float scale = 100.0f;  // Scale for visibility

            foreach (var block in _sectionBlocks)
            {
                if (block.Displacement.LengthSquared() < 1e-6f)
                    continue;

                Vector2 screenPos = new Vector2(
                    center.X + (block.Position.X + _viewOffset.X) * _zoom,
                    center.Y - (block.Position.Y + _viewOffset.Y) * _zoom
                );

                Vector2 vectorEnd = new Vector2(
                    screenPos.X + block.Displacement.X * scale * _zoom,
                    screenPos.Y - block.Displacement.Y * scale * _zoom
                );

                drawList.AddLine(screenPos, vectorEnd, vectorColor, 2.0f);

                // Draw arrowhead
                Vector2 dir = Vector2.Normalize(vectorEnd - screenPos);
                Vector2 perp = new Vector2(-dir.Y, dir.X);
                Vector2 arrowTip1 = vectorEnd - dir * 5 + perp * 3;
                Vector2 arrowTip2 = vectorEnd - dir * 5 - perp * 3;

                drawList.AddTriangleFilled(vectorEnd, arrowTip1, arrowTip2, vectorColor);
            }
        }

        private void HandleMouseInput(Vector2 center, Vector2 windowSize)
        {
            var io = ImGui.GetIO();

            // Pan with middle mouse button or right mouse button
            if (ImGui.IsWindowHovered() && (ImGui.IsMouseDragging(ImGuiMouseButton.Middle) || ImGui.IsMouseDragging(ImGuiMouseButton.Right)))
            {
                Vector2 delta = io.MouseDelta;
                _viewOffset.X += delta.X / _zoom;
                _viewOffset.Y -= delta.Y / _zoom;
            }

            // Zoom with mouse wheel
            if (ImGui.IsWindowHovered() && Math.Abs(io.MouseWheel) > 0.01f)
            {
                float zoomFactor = 1.0f + io.MouseWheel * 0.1f;
                _zoom = Math.Clamp(_zoom * zoomFactor, 0.1f, 10.0f);
            }

            // Reset view with Home key
            if (ImGui.IsWindowFocused() && ImGui.IsKeyPressed(ImGuiKey.Home))
            {
                _viewOffset = Vector2.Zero;
                _zoom = 1.0f;
            }
        }

        private uint GetBlockColor(Block block, float displacementMag)
        {
            Vector4 color;

            switch (_colorMode)
            {
                case ColorMappingMode.DisplacementMagnitude:
                    // Red = high displacement, Blue = low displacement
                    float t = Math.Clamp(displacementMag / 1.0f, 0.0f, 1.0f);
                    color = new Vector4(t, 0, 1 - t, 1);
                    break;

                case ColorMappingMode.Material:
                    // Use material color
                    var material = _dataset.GetMaterial(block.MaterialId);
                    if (material != null)
                    {
                        // Generate color from material ID
                        uint hash = (uint)material.Id * 2654435761;
                        color = new Vector4(
                            ((hash >> 16) & 0xFF) / 255.0f,
                            ((hash >> 8) & 0xFF) / 255.0f,
                            (hash & 0xFF) / 255.0f,
                            1.0f);
                    }
                    else
                    {
                        color = new Vector4(0.7f, 0.7f, 0.7f, 1.0f);
                    }
                    break;

                default:
                    color = new Vector4(0.7f, 0.7f, 0.7f, 1.0f);
                    break;
            }

            return ImGui.ColorConvertFloat4ToU32(color);
        }

        private (Vector3 min, Vector3 max) CalculateSlopeBounds()
        {
            if (_dataset.Blocks.Count == 0)
                return (Vector3.Zero, Vector3.One * 10);

            Vector3 min = new Vector3(float.MaxValue);
            Vector3 max = new Vector3(float.MinValue);

            foreach (var block in _dataset.Blocks)
            {
                min = Vector3.Min(min, block.Position);
                max = Vector3.Max(max, block.Position);
            }

            return (min, max);
        }
    }

    /// <summary>
    /// Defines a 2D section plane through the 3D slope.
    /// </summary>
    public class SectionPlane
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public Vector3 Origin { get; set; }
        public Vector3 Normal { get; set; }
        public Vector3 UpDirection { get; set; }
    }

    /// <summary>
    /// 2D representation of a block in the section view.
    /// </summary>
    public class Block2D
    {
        public int BlockId { get; set; }
        public Vector2 Position { get; set; }
        public Vector2 Displacement { get; set; }
        public float DisplacementMagnitude { get; set; }
        public float Radius { get; set; }
        public uint Color { get; set; }
        public bool IsFixed { get; set; }
    }

    /// <summary>
    /// 2D trace of a joint on the section plane.
    /// </summary>
    public class JointTrace2D
    {
        public Vector2 Position { get; set; }
        public Vector2 Direction { get; set; }
        public float Length { get; set; }
        public int JointSetId { get; set; }
    }
}
