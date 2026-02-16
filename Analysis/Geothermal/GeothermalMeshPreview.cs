// GeoscientistToolkit/Analysis/Geothermal/GeothermalMeshPreview.cs

using System.Numerics;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.Geothermal;

/// <summary>
///     Provides a 3D mesh preview for geothermal simulation configuration.
///     Allows users to visualize the borehole and mesh parameters before running the simulation.
/// </summary>
public class GeothermalMeshPreview : IDisposable
{
    private Mesh3DDataset _boreholeMesh;
    private bool _detailMode;
    private Mesh3DDataset _domainMesh;
    private List<Mesh3DDataset> _hxMeshes = new();
    private bool _isInitialized;
    private BoreholeDataset _lastBorehole;
    private GeothermalSimulationOptions _lastOptions;
    private List<Mesh3DDataset> _lithologyMeshes = new(); // CRITICAL FIX: Store lithology meshes
    private int _selectedLithologyLayer = -1;
    private bool _showBorehole = true;
    private bool _showDomain = true;
    private bool _showGridLines;
    private bool _showHeatExchanger = true;
    private bool _showLegendOverlay = true;
    private bool _showLithologyLayers = true;

    // Overlay information state
    private bool _showParameterOverlay = true;
    private GeothermalVisualization3D _visualization3D;

    private bool HasLastInputs => _lastBorehole != null && _lastOptions != null;

    public void Dispose()
    {
        _visualization3D?.Dispose();
    }

    public void SetDetailMode(bool enabled, BoreholeDataset borehole = null, GeothermalSimulationOptions options = null)
    {
        _detailMode = enabled;

        // Rebuild preview meshes with the right scaling (no solver impact)
        if (borehole != null && options != null) GeneratePreview(borehole, options);

        // Reframe the camera for detail mode (keeps boundary visible)
        if (_visualization3D != null && options != null && borehole != null)
            _visualization3D.FrameDetailView((float)options.DomainRadius, borehole.TotalDepth);
    }

    /// <summary>
    ///     Generates and displays the preview mesh.
    /// </summary>
    public void GeneratePreview(BoreholeDataset borehole, GeothermalSimulationOptions options)
    {
        if (borehole == null || options == null)
            return;
        _lastBorehole = borehole;
        _lastOptions = options;
        var graphicsDevice = VeldridManager.GraphicsDevice;
        if (graphicsDevice == null)
        {
            var errorMsg = "GeothermalMeshPreview: VeldridManager.GraphicsDevice is null. Cannot generate preview.";
            Console.WriteLine($"ERROR: {errorMsg}");
            throw new InvalidOperationException(errorMsg);
        }

        try
        {
            _visualization3D?.Dispose();
            _visualization3D = new GeothermalVisualization3D(graphicsDevice);

            // Let the visualizer know the scene extents (if you have a setter, keep it; otherwise optional)
            _visualization3D.SetPreviewOptions(options, borehole.TotalDepth);

            // DOMAIN BOUNDARY (always built)
            _domainMesh = CreateDomainMesh(borehole, options);

            // LITHOLOGIES at boundary (full cylindrical shells)
            _lithologyMeshes = CreateLithologyLayersMeshes(borehole, options);

            // BOREHOLE: physical in standard mode, visually-exaggerated in detail mode
            _boreholeMesh = CreateBoreholePreviewMesh(borehole, options, _detailMode);

            // HEAT EXCHANGER: same idea (physical vs exaggerated)
            var hxMeshes = CreateHeatExchangerMeshes(borehole, options, _detailMode);

            // ----- Add to scene (detail mode keeps boundary visible regardless of toggle) -----
            if (_domainMesh != null)
                _visualization3D.AddMesh(_domainMesh); // boundary always visible (as you wanted)

            if (_showLithologyLayers && _lithologyMeshes != null)
                foreach (var m in _lithologyMeshes)
                    _visualization3D.AddMesh(m);

            if (_showBorehole && _boreholeMesh != null)
                _visualization3D.AddMesh(_boreholeMesh);

            // If you want a separate HX toggle later, wire it in your UI; here we follow lithology toggle semantics
            if (_showLithologyLayers && hxMeshes != null)
                foreach (var m in hxMeshes)
                    _visualization3D.AddMesh(m);

            // Auto-frame for the chosen mode
            if (_detailMode)
                _visualization3D.FrameDetailView((float)options.DomainRadius, borehole.TotalDepth);
            else
                _visualization3D.SetCameraDistance(MathF.Max(20f, 1.2f * (float)options.DomainRadius));

            _isInitialized = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error generating preview: {ex.Message}");
            _isInitialized = false;
            throw;
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
            ImGuiChildFlags.Border,
            ImGuiWindowFlags.HorizontalScrollbar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollWithMouse);

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
            if (renderWidth > 0 && renderHeight > 0) _visualization3D.Resize((uint)renderWidth, (uint)renderHeight);

            // Render 3D view to texture
            _visualization3D.Render();

            // Display the rendered texture in ImGui
            var renderTargetBinding = _visualization3D.GetRenderTargetImGuiBinding();
            if (renderTargetBinding != IntPtr.Zero && renderWidth > 0 && renderHeight > 0)
            {
                var imagePos = ImGui.GetCursorScreenPos();

                // FIX: Use InvisibleButton to capture ALL mouse input
                // ImGui.Image() alone is NOT interactive and lets clicks fall through to parent window
                var imageBounds = new Vector2(renderWidth, renderHeight);

                // Draw the image first (as background)
                var dl = ImGui.GetWindowDrawList();
                dl.AddImage(renderTargetBinding, imagePos, imagePos + imageBounds);

                // Place an invisible button over the entire image area to capture mouse input
                ImGui.SetCursorScreenPos(imagePos);
                ImGui.InvisibleButton("3DViewInteraction", imageBounds,
                    ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight |
                    ImGuiButtonFlags.MouseButtonMiddle);

                // CRITICAL: Store interaction state IMMEDIATELY after InvisibleButton
                var imageHovered = ImGui.IsItemHovered();
                var imageActive = ImGui.IsItemActive();

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
                    var hasMouseInput = ImGui.IsMouseDown(ImGuiMouseButton.Left) ||
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
                            _visualization3D.StartRotation(mousePos);
                        else
                            _visualization3D.UpdateRotation(mousePos);
                    }
                    else if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                    {
                        _visualization3D.StopRotation();
                    }

                    // Right mouse button - Pan
                    if (ImGui.IsMouseDown(ImGuiMouseButton.Right))
                    {
                        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                            _visualization3D.StartPanning(mousePos);
                        else
                            _visualization3D.UpdatePanning(mousePos);
                    }
                    else if (ImGui.IsMouseReleased(ImGuiMouseButton.Right))
                    {
                        _visualization3D.StopPanning();
                    }

                    // Middle mouse button - Pan (alternative to right button)
                    if (ImGui.IsMouseDown(ImGuiMouseButton.Middle))
                    {
                        if (ImGui.IsMouseClicked(ImGuiMouseButton.Middle))
                            _visualization3D.StartPanning(mousePos);
                        else
                            _visualization3D.UpdatePanning(mousePos);
                    }
                    else if (ImGui.IsMouseReleased(ImGuiMouseButton.Middle))
                    {
                        _visualization3D.StopPanning();
                    }

                    // Mouse wheel - Zoom
                    if (io.MouseWheel != 0) _visualization3D.HandleMouseWheel(io.MouseWheel);
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
            if (_visualization3D == null)
                return;

            // A compact two-column control panel using Dear ImGui tables
            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(6, 4));
            if (ImGui.BeginTable("MeshPreviewControls", 2,
                    ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersInnerV))
            {
                // -------- Column 1: View mode + visibility toggles --------
                ImGui.TableNextColumn();

                // View mode switch
                if (_lastBorehole != null && _lastOptions != null)
                {
                    if (ImGui.Button(_detailMode ? "Switch to Overview" : "Switch to Detail View (Borehole/HX)"))
                    {
                        // Flip mode and enforce "always-visible" set when entering Detail
                        _detailMode = !_detailMode;

                        if (_detailMode)
                        {
                            _showDomain = true; // keep boundary
                            _showLithologyLayers = true; // always visible in detail
                            _showBorehole = true; // detail focuses on small stuff
                            _showHeatExchanger = true; // show HX in detail view
                        }

                        // Rebuild + frame appropriately
                        GeneratePreview(_lastBorehole, _lastOptions);
                        UpdateVisibility();

                        if (_detailMode)
                            _visualization3D.FrameDetailView((float)_lastOptions.DomainRadius,
                                _lastBorehole.TotalDepth);
                    }

                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(
                            "Detail View exaggerates tiny radii (preview-only) and frames camera on borehole/HX while keeping boundary and lithologies visible.");
                }
                else
                {
                    ImGui.BeginDisabled(true);
                    ImGui.Button("Switch to Detail View (Borehole/HX)");
                    ImGui.EndDisabled();

                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Load a borehole and options first.");
                }

                ImGui.Separator();

                // Visibility toggles (decoupled: HX no longer tied to Borehole)
                var changed = false;

                changed |= ImGui.Checkbox("Show Boundary", ref _showDomain);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Toggle the cylindrical domain boundary.");

                changed |= ImGui.Checkbox("Show Lithology Layers", ref _showLithologyLayers);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Toggle lithology shells along the domain boundary.");

                changed |= ImGui.Checkbox("Show Borehole", ref _showBorehole);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Toggle the borehole cylinder mesh.");

                changed |= ImGui.Checkbox("Show Heat Exchanger", ref _showHeatExchanger);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Toggle U-tube/coaxial pipes (preview only).");

                if (changed)
                    UpdateVisibility();

                // -------- Column 2: Camera helpers + utilities --------
                ImGui.TableNextColumn();

                if (_lastBorehole != null && _lastOptions != null)
                {
                    if (ImGui.Button("Zoom to Borehole/HX"))
                        _visualization3D.FrameDetailView((float)_lastOptions.DomainRadius, _lastBorehole.TotalDepth);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Frame a close view around the borehole and heat exchanger.");

                    ImGui.SameLine();
                    if (ImGui.Button("Fit Overview"))
                        // Pull back to a comfortable overview distance
                        _visualization3D.SetCameraDistance(MathF.Max(20f, 1.2f * (float)_lastOptions.DomainRadius));
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Pull back to see the whole domain comfortably.");
                }
                else
                {
                    ImGui.BeginDisabled(true);
                    ImGui.Button("Zoom to Borehole/HX");
                    ImGui.SameLine();
                    ImGui.Button("Fit Overview");
                    ImGui.EndDisabled();
                }

                // Quick refresh (re-adds meshes per current toggles)
                if (ImGui.Button("Refresh View")) UpdateVisibility();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Re-apply current visibility toggles.");

                ImGui.EndTable();
            }

            ImGui.PopStyleVar();
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
                RenderInfoRow("Grout Conductivity:", $"{options.GroutThermalConductivity:F2} W/(m·K)");

                if (options.HeatExchangerType == HeatExchangerType.UTube)
                    RenderInfoRow("Pipe Spacing:", $"{options.PipeSpacing * 1000:F1} mm");
                ImGui.Unindent();
                ImGui.Spacing();
            }

            // === Operating Conditions ===
            if (ImGui.CollapsingHeader("Operating Conditions", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                // FluidMassFlowRate is in kg/s, convert to L/min (assuming water density ~1000 kg/m³
                var flowRateLmin = options.FluidMassFlowRate * 60.0; // kg/s to L/min (for water)
                RenderInfoRow("Flow Rate:", $"{flowRateLmin:F2} L/min");
                RenderInfoRow("Inlet Temp:", $"{options.FluidInletTemperature - 273.15:F1} °C");
                RenderInfoRow("Surface Temp:", $"{options.SurfaceTemperature - 273.15:F1} °C");
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
                            RenderInfoRow("Conductivity:", $"{conductivity:F2} W/(m·K)");

                        if (options.LayerSpecificHeats.TryGetValue(layerName, out var specificHeat))
                            RenderInfoRow("Specific Heat:",
                                $"{specificHeat:F0} J/(kg·K)");

                        if (options.LayerDensities.TryGetValue(layerName, out var density))
                            RenderInfoRow("Density:", $"{density:F0} kg/m³");

                        if (options.LayerPorosities.TryGetValue(layerName, out var porosity))
                            RenderInfoRow("Porosity:", $"{porosity * 100:F1} %");

                        if (options.LayerPermeabilities.TryGetValue(layerName, out var permeability))
                            RenderInfoRow("Permeability:",
                                $"{permeability:E2} m²");

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
                    ImGui.TextWrapped(
                        "Warning: Domain radius seems small relative to borehole depth. Consider increasing it.");
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
    private Mesh3DDataset CreateDomainMesh(BoreholeDataset borehole, GeothermalSimulationOptions options)
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
        for (var i = 0; i < mesh.Vertices.Count; i++)
            mesh.Colors.Add(new Vector4(0.0f, 1.0f, 1.0f, 1.0f)); // Cyan wireframe

        return mesh;
    }

// Builds simple cylinder meshes for the HX so it’s visible in preview.
// U-tube: two parallel cylinders at ±PipeSpacing/2 along X.
// Coaxial: one inner and one outer concentric cylinder.
    private List<Mesh3DDataset> CreateHeatExchangerMeshes(BoreholeDataset borehole, GeothermalSimulationOptions options,
        bool exaggerateSmall)
    {
        var meshes = new List<Mesh3DDataset>();

        var depthTop = (float)options.DomainExtension;
        var depthBot = -(borehole.TotalDepth + (float)options.DomainExtension);

        int ang = 24, zseg = 48;

        Mesh3DDataset MakeCylinder(string name, float r, Vector3 center, Vector4 color)
        {
            var verts = new List<Vector3>();
            var faces = new List<int[]>();

            for (var i = 0; i <= zseg; i++)
            {
                var t = i / (float)zseg;
                var z = depthTop + (depthBot - depthTop) * t;
                for (var j = 0; j < ang; j++)
                {
                    var a = j * 2f * MathF.PI / ang;
                    verts.Add(new Vector3(center.X + r * MathF.Cos(a),
                        center.Y + r * MathF.Sin(a),
                        z));
                }
            }

            for (var i = 0; i < zseg; i++)
            for (var j = 0; j < ang; j++)
            {
                var jn = (j + 1) % ang;
                var v0 = i * ang + j;
                var v1 = i * ang + jn;
                var v2 = (i + 1) * ang + j;
                var v3 = (i + 1) * ang + jn;
                faces.Add(new[] { v0, v2, v1 });
                faces.Add(new[] { v1, v2, v3 });
            }

            var mesh = Mesh3DDataset.CreateFromData(
                name,
                Path.Combine(Path.GetTempPath(), $"{name}_hx_preview.obj"),
                verts, faces, 1f, "m");

            for (var i = 0; i < mesh.Vertices.Count; i++)
                mesh.Colors.Add(color);

            return mesh;
        }

        var domainR = (float)options.DomainRadius;
        var minVisR = 0.015f * domainR; // 1.5% of domain for visibility

        if (options.HeatExchangerType == HeatExchangerType.UTube)
        {
            var roPhys = (float)(options.PipeOuterDiameter / 2.0);
            var ro = exaggerateSmall ? MathF.Max(roPhys, minVisR) : roPhys;

            var sx = (float)(options.PipeSpacing / 2.0);

            meshes.Add(MakeCylinder("HX_Pipe_A", ro, new Vector3(+sx, 0, 0), new Vector4(0.95f, 0.35f, 0.10f, 1)));
            meshes.Add(MakeCylinder("HX_Pipe_B", ro, new Vector3(-sx, 0, 0), new Vector4(0.95f, 0.35f, 0.10f, 1)));
        }
        else // Coaxial
        {
            var rInPhys = (float)(options.PipeInnerDiameter / 2.0);
            var rOutPhys = (float)(options.PipeOuterDiameter / 2.0);

            var rIn = exaggerateSmall ? MathF.Max(rInPhys, 0.010f * domainR) : rInPhys;
            var rOut = exaggerateSmall ? MathF.Max(rOutPhys, minVisR) : rOutPhys;

            meshes.Add(MakeCylinder("HX_Inner", rIn, Vector3.Zero, new Vector4(0.80f, 0.20f, 0.80f, 1)));
            meshes.Add(MakeCylinder("HX_Outer", rOut, Vector3.Zero, new Vector4(0.20f, 0.80f, 0.90f, 1)));
        }

        return meshes;
    }

    /// <summary>
    ///     Creates cylindrical meshes for each lithology layer.
    /// </summary>
    // Creates cylindrical wireframe bands for each lithology layer,
// with ring radii scaled to the domain so they remain visible at any scale.
    private List<Mesh3DDataset> CreateLithologyLayersMeshes(BoreholeDataset borehole,
        GeothermalSimulationOptions options)
    {
        var meshes = new List<Mesh3DDataset>();
        if (borehole?.LithologyUnits == null || borehole.LithologyUnits.Count == 0)
            return meshes;

        const int ang = 64;
        var Rdomain = (float)options.DomainRadius;
        var Rout = 0.995f * Rdomain; // just inside domain to avoid z-fighting
        var shell = MathF.Max(0.015f * Rdomain, 0.25f); // visual thickness
        var Rin = MathF.Max(0f, Rout - shell);

        Mesh3DDataset MakeBand(string name, float zTop, float zBot, Vector4 color)
        {
            var verts = new List<Vector3>();
            var faces = new List<int[]>();

            for (var j = 0; j < ang; j++)
            {
                var a = j * 2f * MathF.PI / ang;
                verts.Add(new Vector3(Rout * MathF.Cos(a), Rout * MathF.Sin(a), zTop)); // 0..ang-1  : OT
            }

            for (var j = 0; j < ang; j++)
            {
                var a = j * 2f * MathF.PI / ang;
                verts.Add(new Vector3(Rout * MathF.Cos(a), Rout * MathF.Sin(a), zBot)); // ang..2ang-1: OB
            }

            for (var j = 0; j < ang; j++)
            {
                var a = j * 2f * MathF.PI / ang;
                verts.Add(new Vector3(Rin * MathF.Cos(a), Rin * MathF.Sin(a), zTop)); // 2ang..3ang-1: IT
            }

            for (var j = 0; j < ang; j++)
            {
                var a = j * 2f * MathF.PI / ang;
                verts.Add(new Vector3(Rin * MathF.Cos(a), Rin * MathF.Sin(a), zBot)); // 3ang..4ang-1: IB
            }

            int OT(int j)
            {
                return j;
            }

            int OB(int j)
            {
                return ang + j;
            }

            int IT(int j)
            {
                return 2 * ang + j;
            }

            int IB(int j)
            {
                return 3 * ang + j;
            }

            // Outer wall
            for (var j = 0; j < ang; j++)
            {
                var jn = (j + 1) % ang;
                faces.Add(new[] { OT(j), OB(j), OT(jn) });
                faces.Add(new[] { OT(jn), OB(j), OB(jn) });
            }

            // Inner wall
            for (var j = 0; j < ang; j++)
            {
                var jn = (j + 1) % ang;
                faces.Add(new[] { IT(j), IT(jn), IB(j) });
                faces.Add(new[] { IT(jn), IB(jn), IB(j) });
            }

            // Top annulus
            for (var j = 0; j < ang; j++)
            {
                var jn = (j + 1) % ang;
                faces.Add(new[] { OT(j), IT(j), OT(jn) });
                faces.Add(new[] { OT(jn), IT(j), IT(jn) });
            }

            // Bottom annulus
            for (var j = 0; j < ang; j++)
            {
                var jn = (j + 1) % ang;
                faces.Add(new[] { OB(j), OB(jn), IB(j) });
                faces.Add(new[] { OB(jn), IB(jn), IB(j) });
            }

            var mesh = Mesh3DDataset.CreateFromData(
                name,
                Path.Combine(Path.GetTempPath(), $"{name}_litho_preview.obj"),
                verts, faces, 1f, "m");

            // Color
            for (var i = 0; i < mesh.Vertices.Count; i++) mesh.Colors.Add(color);
            return mesh;
        }

        static Vector3 HsvToRgb(float hDeg, float s, float v)
        {
            var h = (hDeg % 360f + 360f) % 360f / 60f;
            var i = (int)MathF.Floor(h);
            float f = h - i, p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
            return i switch
            {
                0 => new Vector3(v, t, p), 1 => new Vector3(q, v, p), 2 => new Vector3(p, v, t),
                3 => new Vector3(p, q, v), 4 => new Vector3(t, p, v), _ => new Vector3(v, p, q)
            };
        }

        var n = borehole.LithologyUnits.Count;
        for (var idx = 0; idx < n; idx++)
        {
            var u = borehole.LithologyUnits[idx];
            float zTop = -u.DepthFrom, zBot = -u.DepthTo;
            var hue = idx * 360f / Math.Max(1, n);
            var rgb = HsvToRgb(hue, 0.65f, 0.95f);
            var color = new Vector4(rgb.X, rgb.Y, rgb.Z, 0.95f);
            meshes.Add(MakeBand($"Lithology_{u.Name}", zTop, zBot, color));
        }

        return meshes;
    }

    private Mesh3DDataset CreateBoreholePreviewMesh(BoreholeDataset borehole, GeothermalSimulationOptions options,
        bool exaggerateSmall)
    {
        var depthTop = (float)options.DomainExtension;
        var depthBot = -(borehole.TotalDepth + (float)options.DomainExtension);

        var physicalR = (float)(borehole.WellDiameter / 2.0);
        var minVisR = 0.02f * (float)options.DomainRadius; // 2% of domain for visibility
        var r = exaggerateSmall ? MathF.Max(physicalR, minVisR) : physicalR;

        int ang = 32, zseg = 64;
        var verts = new List<Vector3>();
        var faces = new List<int[]>();

        for (var i = 0; i <= zseg; i++)
        {
            var t = i / (float)zseg;
            var z = depthTop + (depthBot - depthTop) * t;
            for (var j = 0; j < ang; j++)
            {
                var a = j * 2f * MathF.PI / ang;
                verts.Add(new Vector3(r * MathF.Cos(a), r * MathF.Sin(a), z));
            }
        }

        for (var i = 0; i < zseg; i++)
        for (var j = 0; j < ang; j++)
        {
            var jn = (j + 1) % ang;
            var v0 = i * ang + j;
            var v1 = i * ang + jn;
            var v2 = (i + 1) * ang + j;
            var v3 = (i + 1) * ang + jn;
            faces.Add(new[] { v0, v2, v1 });
            faces.Add(new[] { v1, v2, v3 });
        }

        var mesh = Mesh3DDataset.CreateFromData(
            "Borehole_Preview",
            Path.Combine(Path.GetTempPath(), "borehole_preview.obj"),
            verts, faces, 1f, "m");

        // Color (steel-ish)
        for (var i = 0; i < mesh.Vertices.Count; i++)
            mesh.Colors.Add(new Vector4(0.75f, 0.78f, 0.82f, 1f));

        return mesh;
    }

    /// <summary>
    ///     Helper method to convert HSV color space to RGB.
    /// </summary>
    private static Vector3 HsvToRgb(float h, float s, float v)
    {
        h = h % 360.0f;
        var c = v * s;
        var x = c * (1 - MathF.Abs(h / 60.0f % 2 - 1));
        var m = v - c;

        float r, g, b;
        if (h < 60)
        {
            r = c;
            g = x;
            b = 0;
        }
        else if (h < 120)
        {
            r = x;
            g = c;
            b = 0;
        }
        else if (h < 180)
        {
            r = 0;
            g = c;
            b = x;
        }
        else if (h < 240)
        {
            r = 0;
            g = x;
            b = c;
        }
        else if (h < 300)
        {
            r = x;
            g = 0;
            b = c;
        }
        else
        {
            r = c;
            g = 0;
            b = x;
        }

        return new Vector3(r + m, g + m, b + m);
    }
}