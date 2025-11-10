// GeoscientistToolkit/Analysis/Geothermal/GeothermalMeshPreview.cs

using System.Numerics;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.UI.Visualization;
using GeoscientistToolkit.Util;
using ImGuiNET;
using Veldrid;

namespace GeoscientistToolkit.Analysis.Geothermal;

/// <summary>
///     Provides a 3D mesh preview for geothermal simulation configuration.
///     Allows users to visualize the borehole and mesh parameters before running the simulation.
/// </summary>
public class GeothermalMeshPreview : IDisposable
{
    private Mesh3DDataset _boreholeMesh;
    private Mesh3DDataset _domainMesh;
    private List<Mesh3DDataset> _lithologyMeshes = new List<Mesh3DDataset>(); // CRITICAL FIX: Store lithology meshes
    private bool _isInitialized;
    private int _selectedLithologyLayer = -1;
    private bool _showBorehole = true;
    private bool _showDomain = true;
    private bool _showGridLines;
    private bool _showLegendOverlay = true;
    private bool _showLithologyLayers = true;

    // Overlay information state
    private bool _showParameterOverlay = true;
    private GeothermalVisualization3D _visualization3D;

    public GeothermalMeshPreview()
    {
        // Use VeldridManager like Mesh3DViewer does
    }

    public void Dispose()
    {
        _visualization3D?.Dispose();
    }

    /// <summary>
    ///     Generates and displays the preview mesh.
    /// </summary>
    public void GeneratePreview(BoreholeDataset borehole, GeothermalSimulationOptions options)
    {
        if (borehole == null || options == null)
            return;

        // Get graphics device from VeldridManager like Mesh3DViewer does
        var graphicsDevice = VeldridManager.GraphicsDevice;

        if (graphicsDevice == null)
        {
            var errorMsg = "GeothermalMeshPreview: VeldridManager.GraphicsDevice is null. Cannot generate preview. " +
                           "Ensure GraphicsDevice is properly initialized in VeldridManager.";
            Console.WriteLine($"ERROR: {errorMsg}");
            throw new InvalidOperationException(errorMsg);
        }

        try
        {
            // Dispose previous visualization
            _visualization3D?.Dispose();

            // Create visualization using VeldridManager graphics device
            _visualization3D = new GeothermalVisualization3D(graphicsDevice);
            
            // Set preview options for proper domain info rendering
            _visualization3D.SetPreviewOptions(options, borehole.TotalDepth);

            // Generate borehole mesh
            _boreholeMesh = GeothermalMeshGenerator.CreateBoreholeMesh(borehole, options);

            // Generate domain visualization mesh
            _domainMesh = CreateDomainMesh(borehole, options);

            // Generate lithology layers mesh and store them
            _lithologyMeshes = CreateLithologyLayersMeshes(borehole, options);

            // Add meshes to visualization
            if (_showBorehole && _boreholeMesh != null)
                _visualization3D.AddMesh(_boreholeMesh);

            if (_showDomain && _domainMesh != null)
                _visualization3D.AddMesh(_domainMesh);

            if (_showLithologyLayers && _lithologyMeshes != null)
                foreach (var mesh in _lithologyMeshes)
                    _visualization3D.AddMesh(mesh);

            _isInitialized = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error generating preview: {ex.Message}");
            _isInitialized = false;
            throw; // Re-throw to let caller handle it
        }
    }

    /// <summary>
    ///     Renders the preview window with 3D visualization and overlays.
    /// </summary>
    public void Render(BoreholeDataset borehole, GeothermalSimulationOptions options)
    {
        if (!_isInitialized)
        {
            ImGui.Text("Preview not initialized. Click 'Generate Preview' to visualize the mesh.");
            return;
        }

        // Split window into 3D view and overlay panel
        var availRegion = ImGui.GetContentRegionAvail();
        var overlayWidth = 300f;

        // Left side: 3D visualization with scrollbars
        // CRITICAL: NoMove prevents window dragging, NoScrollWithMouse allows our custom mouse handling
        ImGui.BeginChild("3DPreviewView", new Vector2(availRegion.X - overlayWidth, availRegion.Y),
            ImGuiChildFlags.Border, ImGuiWindowFlags.HorizontalScrollbar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollWithMouse);

        if (_visualization3D != null)
        {
            // Render controls at the top
            RenderVisualizationControls();
            ImGui.Separator();


            // Get remaining space for the 3D view
            var viewRegion = ImGui.GetContentRegionAvail();

            // Set a minimum size for the 3D view to ensure good visibility
            var minViewWidth = 640f;
            var minViewHeight = 480f;
            var renderWidth = Math.Max(viewRegion.X, minViewWidth);
            var renderHeight = Math.Max(viewRegion.Y, minViewHeight);

            // Resize render target if needed
            if (renderWidth > 0 && renderHeight > 0)
            {
                _visualization3D.Resize((uint)renderWidth, (uint)renderHeight);
            }

            // Render 3D view to texture
            _visualization3D.Render();

            // Display the rendered texture in ImGui
            var renderTargetBinding = _visualization3D.GetRenderTargetImGuiBinding();
            if (renderTargetBinding != IntPtr.Zero && renderWidth > 0 && renderHeight > 0)
            {
                var imagePos = ImGui.GetCursorScreenPos();
                
                // DEFINITIVE FIX: Use InvisibleButton to capture ALL mouse input
                // ImGui.Image() alone is NOT interactive and lets clicks fall through to parent window
                var imageBounds = new Vector2(renderWidth, renderHeight);
                
                // Draw the image first (as background)
                var dl = ImGui.GetWindowDrawList();
                dl.AddImage(renderTargetBinding, imagePos, imagePos + imageBounds);
                
                // Place an invisible button over the entire image area to capture mouse input
                ImGui.SetCursorScreenPos(imagePos);
                ImGui.InvisibleButton("3DViewInteraction", imageBounds, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight | ImGuiButtonFlags.MouseButtonMiddle);
                
                // CRITICAL: Store interaction state IMMEDIATELY after InvisibleButton
                bool imageHovered = ImGui.IsItemHovered();
                bool imageActive = ImGui.IsItemActive();
                
                // Render overlay info on top of the 3D view
                var drawList = ImGui.GetWindowDrawList();
                var overlayPos = new Vector2(imagePos.X + 10, imagePos.Y + 10);
                
                // Semi-transparent background for overlay
                drawList.AddRectFilled(
                    overlayPos, 
                    new Vector2(overlayPos.X + 220, overlayPos.Y + 80),
                    ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.6f)),
                    4.0f);
                
                // Domain dimensions text
                ImGui.SetCursorScreenPos(new Vector2(overlayPos.X + 5, overlayPos.Y + 5));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0f, 1f, 1f, 1f));
                ImGui.Text($"Domain Radius: {options.DomainRadius:F1} m");
                ImGui.SetCursorScreenPos(new Vector2(overlayPos.X + 5, overlayPos.Y + 25));
                ImGui.Text($"Borehole Depth: {borehole.TotalDepth:F1} m");
                ImGui.SetCursorScreenPos(new Vector2(overlayPos.X + 5, overlayPos.Y + 45));
                ImGui.Text($"Extension: {options.DomainExtension:F1} m");
                ImGui.SetCursorScreenPos(new Vector2(overlayPos.X + 5, overlayPos.Y + 60));
                ImGui.TextColored(new Vector4(0.5f, 0.8f, 1f, 1f), "Cyan = Domain boundary");
                ImGui.PopStyleColor();

                // Handle mouse input on the image
                // CRITICAL: Use stored image states (captured immediately after Image() call)
                if (imageHovered || imageActive)
                {
                    var io = ImGui.GetIO();
                    var mousePos = new Vector2(io.MousePos.X - imagePos.X, io.MousePos.Y - imagePos.Y);
                    
                    // CRITICAL FIX: Capture ALL mouse input to prevent interference
                    // This prevents: 1) Window dragging, 2) Parent scrolling, 3) Input bleeding
                    bool hasMouseInput = ImGui.IsMouseDown(ImGuiMouseButton.Left) || 
                                        ImGui.IsMouseDown(ImGuiMouseButton.Right) || 
                                        ImGui.IsMouseDown(ImGuiMouseButton.Middle) || 
                                        Math.Abs(io.MouseWheel) > 0.001f;
                    
                    if (hasMouseInput || imageActive)
                    {
                        // Set focus on this child window
                        ImGui.SetWindowFocus();
                        
                        // Tell ImGui we OWN all mouse input - prevents ANY parent interference
                        io.WantCaptureMouse = true;
                        io.WantCaptureMouseUnlessPopupClose = true;
                    }

                    // Left mouse button - Rotate
                    if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
                    {
                        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                        {
                            _visualization3D.StartRotation(mousePos);
                        }
                        else
                        {
                            _visualization3D.UpdateRotation(mousePos);
                        }
                    }
                    else if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                    {
                        _visualization3D.StopRotation();
                    }

                    // Right mouse button - Pan
                    if (ImGui.IsMouseDown(ImGuiMouseButton.Right))
                    {
                        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                        {
                            _visualization3D.StartPanning(mousePos);
                        }
                        else
                        {
                            _visualization3D.UpdatePanning(mousePos);
                        }
                    }
                    else if (ImGui.IsMouseReleased(ImGuiMouseButton.Right))
                    {
                        _visualization3D.StopPanning();
                    }

                    // Middle mouse button - Pan (alternative to right button)
                    if (ImGui.IsMouseDown(ImGuiMouseButton.Middle))
                    {
                        if (ImGui.IsMouseClicked(ImGuiMouseButton.Middle))
                        {
                            _visualization3D.StartPanning(mousePos);
                        }
                        else
                        {
                            _visualization3D.UpdatePanning(mousePos);
                        }
                    }
                    else if (ImGui.IsMouseReleased(ImGuiMouseButton.Middle))
                    {
                        _visualization3D.StopPanning();
                    }

                    // Mouse wheel - Zoom
                    if (io.MouseWheel != 0)
                    {
                        _visualization3D.HandleMouseWheel(io.MouseWheel);
                    }
                }
            }
            else
            {
                ImGui.Text("Render target not available");
            }


            ImGui.EndChild();

            // Right side: Parameter overlays with scrollbars
            ImGui.SameLine();
            ImGui.BeginChild("ParameterOverlay", new Vector2(overlayWidth, availRegion.Y),
                ImGuiChildFlags.Border, ImGuiWindowFlags.HorizontalScrollbar);

            RenderParameterOverlays(borehole, options);

            ImGui.EndChild();
        }

        void RenderVisualizationControls()
        {
            ImGui.Text("Visualization Options");
            ImGui.Spacing();

            if (ImGui.Checkbox("Show Borehole", ref _showBorehole))
                UpdateVisibility();

            if (ImGui.Checkbox("Show Domain Boundary", ref _showDomain))
                UpdateVisibility();

            if (ImGui.Checkbox("Show Lithology Layers", ref _showLithologyLayers))
                UpdateVisibility();

            ImGui.Checkbox("Show Grid Lines", ref _showGridLines);

            ImGui.Spacing();
            ImGui.Text("Camera Controls:");
            ImGui.BulletText("Left Mouse: Rotate");
            ImGui.BulletText("Right Mouse: Pan");
            ImGui.BulletText("Wheel: Zoom");
            ImGui.BulletText("R: Reset View");
        }

        void RenderParameterOverlays(BoreholeDataset borehole, GeothermalSimulationOptions options)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 1f));

            // Collapsible sections for different parameter categories

            // === Borehole Parameters ===
            if (ImGui.CollapsingHeader("Borehole Parameters", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                RenderInfoRow("Well Name:", borehole.WellName);
                RenderInfoRow("Total Depth:", $"{borehole.TotalDepth:F1} m");
                RenderInfoRow("Diameter:", $"{borehole.WellDiameter * 1000:F0} mm");
                RenderInfoRow("Lithology Units:", $"{borehole.LithologyUnits.Count}");
                ImGui.Unindent();
                ImGui.Spacing();
            }

            // === Mesh Parameters ===
            if (ImGui.CollapsingHeader("Mesh Configuration", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                RenderInfoRow("Radial Points:", $"{options.RadialGridPoints}");
                RenderInfoRow("Angular Points:", $"{options.AngularGridPoints}");
                RenderInfoRow("Vertical Points:", $"{options.VerticalGridPoints}");
                RenderInfoRow("Total Cells:",
                    $"{options.RadialGridPoints * options.AngularGridPoints * options.VerticalGridPoints:N0}");

                ImGui.Spacing();
                ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Domain:");
                RenderInfoRow("Radius:", $"{options.DomainRadius:F1} m");
                RenderInfoRow("Extension:", $"{options.DomainExtension:F1} m");
                ImGui.Unindent();
                ImGui.Spacing();
            }

            // === Heat Exchanger ===
            if (ImGui.CollapsingHeader("Heat Exchanger", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                RenderInfoRow("Type:", options.HeatExchangerType.ToString());
                RenderInfoRow("Pipe Diameter:", $"{options.PipeInnerDiameter * 1000:F1} mm");
                RenderInfoRow("Grout Conductivity:", $"{options.GroutThermalConductivity:F2} W/(mÃ‚Â·K)");

                if (options.HeatExchangerType == HeatExchangerType.UTube)
                    RenderInfoRow("Pipe Spacing:", $"{options.PipeSpacing * 1000:F1} mm");
                ImGui.Unindent();
                ImGui.Spacing();
            }

            // === Operating Conditions ===
            if (ImGui.CollapsingHeader("Operating Conditions", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                // FluidMassFlowRate is in kg/s, convert to L/min (assuming water density ~1000 kg/mÃ‚Â³
                var flowRateLmin = options.FluidMassFlowRate * 60.0; // kg/s to L/min (for water)
                RenderInfoRow("Flow Rate:", $"{flowRateLmin:F2} L/min");
                RenderInfoRow("Inlet Temp:", $"{options.FluidInletTemperature - 273.15:F1} Ã‚Â°C");
                RenderInfoRow("Surface Temp:", $"{options.SurfaceTemperature - 273.15:F1} Ã‚Â°C");
                RenderInfoRow("Simulation Time:", $"{options.SimulationTime / (365.25 * 24 * 3600):F1} years");
                ImGui.Unindent();
                ImGui.Spacing();
            }

            // === Lithology Layers ===
            if (ImGui.CollapsingHeader("Lithology Layers", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();

                for (var i = 0; i < borehole.LithologyUnits.Count; i++)
                {
                    var unit = borehole.LithologyUnits[i];
                    var isSelected = _selectedLithologyLayer == i;

                    ImGui.PushID(i);

                    // Layer header with color indicator
                    var colorBox = unit.Color;
                    ImGui.ColorButton("##LayerColor", colorBox, ImGuiColorEditFlags.NoTooltip, new Vector2(20, 20));
                    ImGui.SameLine();

                    if (ImGui.Selectable($"{unit.Name}##Layer", isSelected))
                        _selectedLithologyLayer = isSelected ? -1 : i;

                    if (isSelected)
                    {
                        ImGui.Indent();
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.8f, 1f));

                        RenderInfoRow("Type:", unit.LithologyType ?? "Unknown");
                        RenderInfoRow("From:", $"{unit.DepthFrom:F1} m");
                        RenderInfoRow("To:", $"{unit.DepthTo:F1} m");
                        RenderInfoRow("Thickness:", $"{unit.DepthTo - unit.DepthFrom:F1} m");

                        ImGui.Spacing();
                        ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Thermal Properties:");

                        var layerName = !string.IsNullOrEmpty(unit.Name) ? unit.Name : unit.RockType ?? "Unknown";

                        if (options.LayerThermalConductivities.TryGetValue(layerName, out var conductivity))
                            RenderInfoRow("Conductivity:", $"{conductivity:F2} W/(mÃ‚Â·K)");

                        if (options.LayerSpecificHeats.TryGetValue(layerName, out var specificHeat))
                            RenderInfoRow("Specific Heat:",
                                $"{specificHeat:F0} J/(kgÃ‚Â·K)");

                        if (options.LayerDensities.TryGetValue(layerName, out var density))
                            RenderInfoRow("Density:", $"{density:F0} kg/mÃ‚Â³");

                        if (options.LayerPorosities.TryGetValue(layerName, out var porosity))
                            RenderInfoRow("Porosity:", $"{porosity * 100:F1} %");

                        if (options.LayerPermeabilities.TryGetValue(layerName, out var permeability))
                            RenderInfoRow("Permeability:",
                                $"{permeability:E2} mÃ‚Â²");

                        ImGui.PopStyleColor();
                        ImGui.Unindent();
                    }

                    ImGui.PopID();
                }

                ImGui.Unindent();
            }

            // === Warnings & Recommendations ===
            if (ImGui.CollapsingHeader("Analysis & Recommendations"))
            {
                ImGui.Indent();
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.8f, 0.3f, 1f));

                // Check for potential issues
                var totalCells = options.RadialGridPoints * options.AngularGridPoints * options.VerticalGridPoints;

                if (totalCells > 100000)
                    ImGui.TextWrapped("Warning: High cell count may result in long simulation times.");

                if (options.DomainRadius < borehole.TotalDepth * 0.5)
                {
                    ImGui.Spacing();
                    ImGui.TextWrapped("Warning: Domain radius seems small relative to borehole depth. Consider increasing it.");
                }

                if (borehole.LithologyUnits.Count == 0)
                {
                    ImGui.Spacing();
                    ImGui.TextWrapped("Info: No lithology units defined. Default properties will be used.");
                }

                // Recommendations
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.5f, 1f, 0.5f, 1f), "Recommendations:");
                ImGui.BulletText("Verify lithology layer properties");
                ImGui.BulletText("Check mesh resolution is adequate");
                ImGui.BulletText("Review boundary conditions");

                ImGui.PopStyleColor();
                ImGui.Unindent();
            }

            ImGui.PopStyleColor();
        }

        void RenderInfoRow(string label, string value)
        {
            ImGui.Text(label);
            ImGui.SameLine(ImGui.GetWindowWidth() - ImGui.CalcTextSize(value).X - 20);
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1f, 1f), value);
        }

        void UpdateVisibility()
        {
            if (!_isInitialized || _visualization3D == null)
                return;

            _visualization3D.ClearDynamicMeshes();

            if (_showBorehole && _boreholeMesh != null)
                _visualization3D.AddMesh(_boreholeMesh);

            if (_showDomain && _domainMesh != null)
                _visualization3D.AddMesh(_domainMesh);
            
            // CRITICAL FIX: Re-add lithology layers when visibility is toggled
            if (_showLithologyLayers && _lithologyMeshes != null)
                foreach (var mesh in _lithologyMeshes)
                    _visualization3D.AddMesh(mesh);
        }
    }

    /// <summary>
        ///     Creates a wireframe mesh representing the simulation domain boundary.
        /// </summary>
        Mesh3DDataset CreateDomainMesh(BoreholeDataset borehole, GeothermalSimulationOptions options)
        {
            var vertices = new List<Vector3>();
            var faces = new List<int[]>();

            var radius = (float)options.DomainRadius;
            var depth = borehole.TotalDepth + (float)options.DomainExtension;
            var topExtension = (float)options.DomainExtension;
            var angularSegments = 32;

            // Create top and bottom circles
            for (var i = 0; i <= 1; i++)
            {
                var z = i == 0 ? topExtension : -depth;
                for (var j = 0; j < angularSegments; j++)
                {
                    var angle = j * 2.0f * MathF.PI / angularSegments;
                    var x = radius * MathF.Cos(angle);
                    var y = radius * MathF.Sin(angle);
                    vertices.Add(new Vector3(x, y, z));
                }
            }

            // Create faces for wireframe (only edge lines)
            // Top circle
            for (var j = 0; j < angularSegments; j++)
            {
                var next = (j + 1) % angularSegments;
                faces.Add(new[] { j, next });
            }

            // Bottom circle
            for (var j = 0; j < angularSegments; j++)
            {
                var next = (j + 1) % angularSegments;
                faces.Add(new[] { angularSegments + j, angularSegments + next });
            }

            // Vertical lines
            for (var j = 0; j < angularSegments; j += 4) // Only every 4th line for clarity
                faces.Add(new[] { j, angularSegments + j });

            var tempPath = Path.Combine(Path.GetTempPath(), "domain_mesh_preview.obj");

            var mesh = Mesh3DDataset.CreateFromData(
                "Domain_Boundary",
                tempPath,
                vertices,
                faces,
                1.0f,
                "m"
            );
            
            // Set a bright color for better visibility
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                mesh.Colors.Add(new Vector4(0.0f, 1.0f, 1.0f, 1.0f)); // Cyan wireframe
            }
            
            return mesh;
        }

        /// <summary>
        ///     Creates cylindrical meshes for each lithology layer.
        /// </summary>
        List<Mesh3DDataset> CreateLithologyLayersMeshes(BoreholeDataset borehole,
            GeothermalSimulationOptions options)
        {
            var meshes = new List<Mesh3DDataset>();

            if (borehole.LithologyUnits == null || borehole.LithologyUnits.Count == 0)
                return meshes;

            var angularSegments = 24;
            var boreholeRadius = (float)(borehole.WellDiameter / 2.0);
            var domainRadius = (float)options.DomainRadius;

            foreach (var unit in borehole.LithologyUnits)
            {
                var vertices = new List<Vector3>();
                var faces = new List<int[]>();

                var depthFrom = -unit.DepthFrom;
                var depthTo = -unit.DepthTo;

                // Inner cylinder (borehole)
                for (var i = 0; i <= 1; i++)
                {
                    var z = i == 0 ? depthFrom : depthTo;
                    for (var j = 0; j < angularSegments; j++)
                    {
                        var angle = j * 2.0f * MathF.PI / angularSegments;
                        var x = boreholeRadius * MathF.Cos(angle);
                        var y = boreholeRadius * MathF.Sin(angle);
                        vertices.Add(new Vector3(x, y, z));
                    }
                }

                // Outer cylinder (domain boundary) - semi-transparent
                var outerRadius = boreholeRadius * 3f; // Smaller visualization radius
                for (var i = 0; i <= 1; i++)
                {
                    var z = i == 0 ? depthFrom : depthTo;
                    for (var j = 0; j < angularSegments; j++)
                    {
                        var angle = j * 2.0f * MathF.PI / angularSegments;
                        var x = outerRadius * MathF.Cos(angle);
                        var y = outerRadius * MathF.Sin(angle);
                        vertices.Add(new Vector3(x, y, z));
                    }
                }

                // Create faces for the layer boundaries (wireframe style)
                // Top inner ring
                for (var j = 0; j < angularSegments; j++)
                {
                    var next = (j + 1) % angularSegments;
                    faces.Add(new[] { j, next });
                }

                // Bottom inner ring
                for (var j = 0; j < angularSegments; j++)
                {
                    var next = (j + 1) % angularSegments;
                    faces.Add(new[] { angularSegments + j, angularSegments + next });
                }

                // Radial lines from inner to outer at layer boundaries
                for (var j = 0; j < angularSegments; j += 6)
                {
                    faces.Add(new[] { j, 2 * angularSegments + j }); // Top
                    faces.Add(new[] { angularSegments + j, 3 * angularSegments + j }); // Bottom
                }

                var tempPath = Path.Combine(Path.GetTempPath(), $"layer_{unit.Name}_preview.obj");

                var mesh = Mesh3DDataset.CreateFromData(
                    $"Layer_{unit.Name}",
                    tempPath,
                    vertices,
                    faces,
                    1.0f,
                    "m"
                );
                
                // Add distinct color based on lithology index for better visibility
                var hue = (borehole.LithologyUnits.IndexOf(unit) * 360.0f / borehole.LithologyUnits.Count) % 360.0f;
                var color = HsvToRgb(hue, 0.7f, 0.9f);
                for (int i = 0; i < mesh.Vertices.Count; i++)
                {
                    mesh.Colors.Add(new Vector4(color.X, color.Y, color.Z, 0.8f));
                }

                meshes.Add(mesh);
            }

            return meshes;
        }
        
        /// <summary>
        ///     Helper method to convert HSV color space to RGB.
        /// </summary>
        private static Vector3 HsvToRgb(float h, float s, float v)
        {
            h = h % 360.0f;
            float c = v * s;
            float x = c * (1 - MathF.Abs((h / 60.0f) % 2 - 1));
            float m = v - c;
            
            float r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            
            return new Vector3(r + m, g + m, b + m);
        }
    }