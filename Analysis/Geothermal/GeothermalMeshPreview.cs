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

            // Generate borehole mesh
            _boreholeMesh = GeothermalMeshGenerator.CreateBoreholeMesh(borehole, options);

            // Generate domain visualization mesh
            _domainMesh = CreateDomainMesh(borehole, options);

            // Generate lithology layers mesh
            var lithologyMeshes = CreateLithologyLayersMeshes(borehole, options);

            // Add meshes to visualization
            if (_showBorehole && _boreholeMesh != null)
                _visualization3D.AddMesh(_boreholeMesh);

            if (_showDomain && _domainMesh != null)
                _visualization3D.AddMesh(_domainMesh);

            if (_showLithologyLayers)
                foreach (var mesh in lithologyMeshes)
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

        // Left side: 3D visualization
        ImGui.BeginChild("3DPreviewView", new Vector2(availRegion.X - overlayWidth, availRegion.Y),
            ImGuiChildFlags.Border);

        if (_visualization3D != null)
        {
            // Render controls
            RenderVisualizationControls();
            ImGui.Separator();

            // Render 3D view
            _visualization3D.Render();
        }

        ImGui.EndChild();

        // Right side: Parameter overlays
        ImGui.SameLine();
        ImGui.BeginChild("ParameterOverlay", new Vector2(overlayWidth, availRegion.Y),
            ImGuiChildFlags.Border);

        RenderParameterOverlays(borehole, options);

        ImGui.EndChild();
    }

    private void RenderVisualizationControls()
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

    private void RenderParameterOverlays(BoreholeDataset borehole, GeothermalSimulationOptions options)
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
            // FluidMassFlowRate is in kg/s, convert to L/min (assuming water density ~1000 kg/mÃ‚Â³)
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

                if (ImGui.Selectable($"{unit.Name}##Layer", isSelected)) _selectedLithologyLayer = isSelected ? -1 : i;

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
                        RenderInfoRow("Specific Heat:", $"{specificHeat:F0} J/(kgÃ‚Â·K)");

                    if (options.LayerDensities.TryGetValue(layerName, out var density))
                        RenderInfoRow("Density:", $"{density:F0} kg/mÃ‚Â³");

                    if (options.LayerPorosities.TryGetValue(layerName, out var porosity))
                        RenderInfoRow("Porosity:", $"{porosity * 100:F1} %");

                    if (options.LayerPermeabilities.TryGetValue(layerName, out var permeability))
                        RenderInfoRow("Permeability:", $"{permeability:E2} mÃ‚Â²");

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

            if (totalCells > 100000) ImGui.TextWrapped("Ã¢Å¡Â  High cell count may result in long simulation times.");

            if (options.DomainRadius < borehole.TotalDepth * 0.5)
            {
                ImGui.Spacing();
                ImGui.TextWrapped("Ã¢Å¡Â  Domain radius seems small relative to borehole depth. Consider increasing it.");
            }

            if (borehole.LithologyUnits.Count == 0)
            {
                ImGui.Spacing();
                ImGui.TextWrapped("Ã¢â€žÂ¹ No lithology units defined. Default properties will be used.");
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

    private void RenderInfoRow(string label, string value)
    {
        ImGui.Text(label);
        ImGui.SameLine(ImGui.GetWindowWidth() - ImGui.CalcTextSize(value).X - 20);
        ImGui.TextColored(new Vector4(0.7f, 0.9f, 1f, 1f), value);
    }

    private void UpdateVisibility()
    {
        if (!_isInitialized || _visualization3D == null)
            return;

        _visualization3D.ClearDynamicMeshes();

        if (_showBorehole && _boreholeMesh != null)
            _visualization3D.AddMesh(_boreholeMesh);

        if (_showDomain && _domainMesh != null)
            _visualization3D.AddMesh(_domainMesh);
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

        return Mesh3DDataset.CreateFromData(
            "Domain_Boundary",
            tempPath,
            vertices,
            faces,
            1.0f,
            "m"
        );
    }

    /// <summary>
    ///     Creates cylindrical meshes for each lithology layer.
    /// </summary>
    private List<Mesh3DDataset> CreateLithologyLayersMeshes(BoreholeDataset borehole,
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

            meshes.Add(mesh);
        }

        return meshes;
    }
}