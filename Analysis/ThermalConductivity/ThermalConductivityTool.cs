// GeoscientistToolkit/Analysis/ThermalConductivity/ThermalConductivityTool.cs

using System.Numerics;
using System.Text;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.UI;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.ThermalConductivity;

public class ThermalConductivityTool : IDatasetTools, IDisposable
{
    private readonly ThermalOptions _options = new();
    private CancellationTokenSource _cancellationTokenSource;
    private bool _isSimulationRunning;
    private Task _simulationTask;
    private ThermalResults _results;
    private readonly ProgressBarDialog _progressDialog = new("Thermal Simulation");

    private int _selectedSliceIndex;
    private int _selectedSliceDirectionInt;
    private int _colorMapIndex; // 0: Hot, 1: Rainbow
    private bool _showIsocontours = true;
    private int _numIsocontours = 10;
    private double _isosurfaceValue = 300.0;
    private int _numIsosurfaces = 5; // Moved from local static to class field
    
    private readonly ImGuiExportFileDialog _csvExportDialog = new("ExportThermalCsv", "Export Results to CSV");
    private readonly ImGuiExportFileDialog _sliceCsvExportDialog = new("ExportSliceCsv", "Export Slice to CSV");
    private readonly ImGuiExportFileDialog _pngExportDialog = new("ExportThermalPng", "Export Slice Image");
    private readonly ImGuiExportFileDialog _compositePngExportDialog = new("ExportCompositePng", "Export Composite Image");
    private readonly ImGuiExportFileDialog _txtReportExportDialog = new("ExportTxtReport", "Export Text Report");
    private readonly ImGuiExportFileDialog _rtfReportExportDialog = new("ExportRtfReport", "Export Rich Text Report");
    private readonly ImGuiExportFileDialog _stlExportDialog = new("ExportStl", "Export Mesh to STL");
    
    private static Vector3[,] _colormapData;

    public ThermalConductivityTool()
    {
        _csvExportDialog.SetExtensions((".csv", "Comma-separated values"));
        _sliceCsvExportDialog.SetExtensions((".csv", "Comma-separated values"));
        _pngExportDialog.SetExtensions((".png", "Portable Network Graphics"));
        _compositePngExportDialog.SetExtensions((".png", "Portable Network Graphics"));
        _txtReportExportDialog.SetExtensions((".txt", "Text Document"));
        _rtfReportExportDialog.SetExtensions((".rtf", "Rich Text Format"));
        _stlExportDialog.SetExtensions((".stl", "Stereolithography"));
        
        InitializeColormaps();
    }

    public void Draw(Dataset dataset)
    {
        if (dataset is not CtImageStackDataset ctDataset)
        {
            ImGui.TextDisabled("This tool requires a CT Image Stack dataset.");
            return;
        }

        _options.Dataset = ctDataset;

        if (_isSimulationRunning)
        {
            _progressDialog.Submit();
            return;
        }
        
        if (ImGui.BeginTabBar("ThermalTabs"))
        {
            if (ImGui.BeginTabItem("Settings"))
            {
                DrawSettingsTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Results"))
            {
                DrawResultsTab();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }

    private void DrawSettingsTab()
    {
        // Quick presets
        if (ImGui.CollapsingHeader("Quick Presets", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Text("Apply common temperature configurations:");
            
            if (ImGui.Button("Room Temp → Boiling Water", new Vector2(-1, 0)))
            {
                _options.TemperatureHot = 373.15; // 100°C
                _options.TemperatureCold = 293.15; // 20°C
            }
            if (ImGui.Button("Freezing → Room Temp", new Vector2(-1, 0)))
            {
                _options.TemperatureHot = 293.15; // 20°C
                _options.TemperatureCold = 273.15; // 0°C
            }
            if (ImGui.Button("Geothermal Gradient (50°C)", new Vector2(-1, 0)))
            {
                _options.TemperatureHot = 323.15; // 50°C
                _options.TemperatureCold = 283.15; // 10°C
            }
            if (ImGui.Button("High Temperature (500°C)", new Vector2(-1, 0)))
            {
                _options.TemperatureHot = 773.15; // 500°C
                _options.TemperatureCold = 293.15; // 20°C
            }
        }
        
        ImGui.Spacing();
        
        // Material properties
        if (ImGui.CollapsingHeader("Material Properties", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (ImGui.BeginTable("MaterialsTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(0, 200)))
            {
                ImGui.TableSetupColumn("Material", ImGuiTableColumnFlags.WidthFixed, 120);
                ImGui.TableSetupColumn("Conductivity (W/m·K)", ImGuiTableColumnFlags.WidthFixed, 150);
                ImGui.TableSetupColumn("Library", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();

                foreach (var material in _options.Dataset.Materials)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    
                    // Material name with color indicator
                    var color = material.Color;
                    ImGui.ColorButton($"##color_{material.ID}", color, ImGuiColorEditFlags.NoTooltip, new Vector2(16, 16));
                    ImGui.SameLine();
                    ImGui.Text(material.Name);
                    
                    ImGui.TableNextColumn();
                    if (!_options.MaterialConductivities.ContainsKey(material.ID))
                    {
                        _options.MaterialConductivities[material.ID] = 1.0;
                    }
                    var conductivity = (float)_options.MaterialConductivities[material.ID];
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.InputFloat($"##cond_{material.ID}", ref conductivity, 0.01f, 0.1f, "%.4f"))
                    {
                        _options.MaterialConductivities[material.ID] = Math.Max(0.001, conductivity);
                    }
                    
                    // Validation indicator
                    if (conductivity <= 0)
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(new Vector4(1, 0, 0, 1), "!");
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip("Conductivity must be positive!");
                        }
                    }

                    ImGui.TableNextColumn();
                    PhysicalMaterial libMat = null;
                    if (!string.IsNullOrEmpty(material.PhysicalMaterialName))
                    {
                        libMat = MaterialLibrary.Instance.Find(material.PhysicalMaterialName);
                    }
                    
                    if (libMat?.ThermalConductivity_W_mK != null)
                    {
                        ImGui.Text($"{libMat.ThermalConductivity_W_mK:F3}");
                    }
                    else
                    {
                        ImGui.TextDisabled("N/A");
                    }
                    
                    ImGui.TableNextColumn();
                    if (libMat?.ThermalConductivity_W_mK != null)
                    {
                        if (ImGui.SmallButton($"Use##use_{material.ID}"))
                        {
                            _options.MaterialConductivities[material.ID] = libMat.ThermalConductivity_W_mK.Value;
                        }
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip($"Set to {libMat.ThermalConductivity_W_mK:F3} W/m·K from '{libMat.Name}'");
                        }
                    }
                }
                ImGui.EndTable();
            }
            
            // Common material presets
            ImGui.Spacing();
            ImGui.Text("Quick material assignments:");
            ImGui.Indent();
            if (ImGui.SmallButton("Set all to Air (0.026)"))
            {
                foreach (var mat in _options.Dataset.Materials)
                    _options.MaterialConductivities[mat.ID] = 0.026;
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Set all to Water (0.6)"))
            {
                foreach (var mat in _options.Dataset.Materials)
                    _options.MaterialConductivities[mat.ID] = 0.6;
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Set all to Rock (2.5)"))
            {
                foreach (var mat in _options.Dataset.Materials)
                    _options.MaterialConductivities[mat.ID] = 2.5;
            }
            ImGui.Unindent();
        }
        
        ImGui.Spacing();
        
        // Simulation parameters
        if (ImGui.CollapsingHeader("Simulation Parameters", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Text("Boundary Temperatures:");
            ImGui.Indent();
            
            var tempHot = (float)_options.TemperatureHot;
            if (ImGui.InputFloat("Hot Temperature (K)", ref tempHot, 1.0f, 10.0f, "%.2f"))
            {
                _options.TemperatureHot = Math.Max(tempHot, _options.TemperatureCold + 1);
            }
            var tempHotC = _options.TemperatureHot - 273.15;
            ImGui.SameLine();
            ImGui.TextDisabled($"({tempHotC:F1} °C)");
            
            var tempCold = (float)_options.TemperatureCold;
            if (ImGui.InputFloat("Cold Temperature (K)", ref tempCold, 1.0f, 10.0f, "%.2f"))
            {
                _options.TemperatureCold = Math.Min(tempCold, _options.TemperatureHot - 1);
            }
            var tempColdC = _options.TemperatureCold - 273.15;
            ImGui.SameLine();
            ImGui.TextDisabled($"({tempColdC:F1} °C)");
            
            ImGui.Unindent();
            
            ImGui.Spacing();
            ImGui.Text("Heat Flow Configuration:");
            ImGui.Indent();
            
            int directionIndex = (int)_options.HeatFlowDirection;
            if (ImGui.Combo("Heat Flow Direction", ref directionIndex, "X (Width)\0Y (Height)\0Z (Depth)\0"))
            {
                _options.HeatFlowDirection = (HeatFlowDirection)directionIndex;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Direction of the applied temperature gradient");
            }
            
            ImGui.Unindent();
            
            ImGui.Spacing();
            ImGui.Text("Solver Configuration:");
            ImGui.Indent();
            
            int backendIndex = (int)_options.SolverBackend;
            if (ImGui.Combo("Solver Backend", ref backendIndex, "CPU Parallel\0CPU SIMD (AVX2)\0GPU (OpenCL)\0"))
            {
                _options.SolverBackend = (SolverBackend)backendIndex;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("CPU Parallel: Best compatibility\nCPU SIMD: Fastest on modern CPUs\nGPU: Best for very large datasets");
            }
            
            var maxIter = _options.MaxIterations;
            if (ImGui.InputInt("Max Iterations", ref maxIter, 100, 1000))
            {
                _options.MaxIterations = Math.Max(100, Math.Min(100000, maxIter));
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Maximum number of solver iterations (100-100000)");
            }

            var tolerance = (float)_options.ConvergenceTolerance;
            if (ImGui.InputFloat("Tolerance", ref tolerance, 0, 0, "%.1e"))
            {
                _options.ConvergenceTolerance = Math.Max(1e-9, Math.Min(1e-3, tolerance));
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Convergence criterion - smaller values take longer but are more accurate");
            }
            
            ImGui.Unindent();
        }
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
        // Validation summary
        bool canRun = ValidateSettings(out var validationMessages);
        
        if (!canRun)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0.5f, 0, 1));
            ImGui.TextWrapped("Cannot run simulation:");
            foreach (var msg in validationMessages)
            {
                ImGui.BulletText(msg);
            }
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }
        
        // Run button
        ImGui.BeginDisabled(!canRun);
        if (ImGui.Button("Run Simulation", new Vector2(-1, 40)))
        {
            StartSimulation();
        }
        ImGui.EndDisabled();
        
        if (!canRun && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip("Fix validation errors before running");
        }
    }
    
    private bool ValidateSettings(out List<string> messages)
    {
        messages = new List<string>();
        
        // Check temperature gradient
        if (_options.TemperatureHot <= _options.TemperatureCold)
        {
            messages.Add("Hot temperature must be higher than cold temperature");
        }
        
        // Check material conductivities
        foreach (var kvp in _options.MaterialConductivities)
        {
            if (kvp.Value <= 0)
            {
                var mat = _options.Dataset.Materials.FirstOrDefault(m => m.ID == kvp.Key);
                var name = mat?.Name ?? $"Material {kvp.Key}";
                messages.Add($"{name} has invalid conductivity ({kvp.Value})");
            }
        }
        
        // Check if dataset has label data
        if (_options.Dataset.LabelData == null)
        {
            messages.Add("Dataset has no label/material data");
        }
        
        // Check dataset dimensions
        if (_options.Dataset.Width < 3 || _options.Dataset.Height < 3 || _options.Dataset.Depth < 3)
        {
            messages.Add("Dataset is too small (minimum 3x3x3 voxels)");
        }
        
        return messages.Count == 0;
    }

    private void DrawResultsTab()
    {
        if (_results == null)
        {
            ImGui.TextDisabled("No results to display. Run a simulation from the Settings tab.");
            return;
        }

        if (ImGui.CollapsingHeader("Summary & Reporting", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Text($"Effective Conductivity: {_results.EffectiveConductivity:F4} W/mK");
            ImGui.Text($"Computation Time: {_results.ComputationTime.TotalSeconds:F2} s");

            if (_results.AnalyticalEstimates.Count > 0)
            {
                ImGui.Text("Analytical Estimates:");
                foreach (var (name, value) in _results.AnalyticalEstimates)
                {
                    ImGui.Text($"  {name}: {value:F4} W/mK");
                }
            }
            
            ImGui.Separator();
            ImGui.Text("Export Simulation Data:");
            
            if (ImGui.Button("Export Summary to CSV", new Vector2(200, 0)))
            {
                _csvExportDialog.Open($"ThermalSummary_{_options.Dataset.Name}.csv");
            }
            ImGui.SameLine();
            if (ImGui.Button("Export Composite Image (PNG)", new Vector2(200, 0)))
            {
                 _compositePngExportDialog.Open($"ThermalComposite_{(HeatFlowDirection)_selectedSliceDirectionInt}{_selectedSliceIndex}.png");
            }
            
            if (ImGui.Button("Export Text Report (.txt)", new Vector2(200, 0)))
            {
                _txtReportExportDialog.Open($"ThermalReport_{_options.Dataset.Name}.txt");
            }
            ImGui.SameLine();
             if (ImGui.Button("Export Rich Report (.rtf)", new Vector2(200, 0)))
            {
                _rtfReportExportDialog.Open($"ThermalReport_{_options.Dataset.Name}.rtf");
            }

            if (_csvExportDialog.Submit()) { ExportSummaryToCsv(_csvExportDialog.SelectedPath); }
            if (_compositePngExportDialog.Submit()) { ExportCompositeImage(_compositePngExportDialog.SelectedPath); }
            if (_txtReportExportDialog.Submit()) { ExportTextReport(_txtReportExportDialog.SelectedPath, false); }
            if (_rtfReportExportDialog.Submit()) { ExportTextReport(_rtfReportExportDialog.SelectedPath, true); }
        }
        
        if (ImGui.CollapsingHeader("2D Temperature Field", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawSliceViewer();
        }
        
        if (ImGui.CollapsingHeader("Isosurface Generation", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawIsosurfaceGenerator();
        }
    }

    private void DrawSliceViewer()
    {
        int maxSlice = 0;
        var selectedDirection = (HeatFlowDirection)_selectedSliceDirectionInt;
        switch (selectedDirection)
        {
            case HeatFlowDirection.X: maxSlice = _options.Dataset.Width - 1; break;
            case HeatFlowDirection.Y: maxSlice = _options.Dataset.Height - 1; break;
            case HeatFlowDirection.Z: maxSlice = _options.Dataset.Depth - 1; break;
        }
        _selectedSliceIndex = Math.Clamp(_selectedSliceIndex, 0, maxSlice);

        if (ImGui.Combo("View Axis", ref _selectedSliceDirectionInt, "X\0Y\0Z\0"))
        {
            _selectedSliceIndex = 0; // Reset slice on axis change
        }

        ImGui.SliderInt("Slice", ref _selectedSliceIndex, 0, maxSlice);

        ImGui.Combo("Colormap", ref _colorMapIndex, "Hot\0Rainbow\0");
        ImGui.Checkbox("Show Isocontours", ref _showIsocontours);
        if (_showIsocontours)
        {
            ImGui.SliderInt("Contour Count", ref _numIsocontours, 2, 50);
        }

        var (slice, width, height) = GetSelectedSlice();
        if (slice == null) return;
        
        // Render slice visualization
        var available = ImGui.GetContentRegionAvail();
        var dl = ImGui.GetWindowDrawList();
        var canvasPos = ImGui.GetCursorScreenPos();
        
        // Calculate canvas size maintaining aspect ratio
        float aspectRatio = (float)width / height;
        var canvasSize = new Vector2(
            Math.Min(available.X - 20, available.Y * aspectRatio),
            Math.Min(available.Y - 120, available.X / aspectRatio)
        );

        dl.AddRectFilled(canvasPos, canvasPos + canvasSize, 0xFF202020); // Background
        
        // Render temperature field with improved quality
        int pixelSkip = Math.Max(1, Math.Max(width, height) / 512); // Adaptive sampling for performance
        
        for (int y = 0; y < height; y += pixelSkip)
        for (int x = 0; x < width; x += pixelSkip)
        {
            var temp = slice[x, y];
            var normalizedTemp = (temp - _options.TemperatureCold) / (_options.TemperatureHot - _options.TemperatureCold);
            normalizedTemp = Math.Clamp(normalizedTemp, 0.0, 1.0);
            var color = ApplyColorMap((float)normalizedTemp, _colorMapIndex);
            
            var px = canvasPos.X + (float)x / width * canvasSize.X;
            var py = canvasPos.Y + (float)y / height * canvasSize.Y;
            var pw = canvasSize.X / width * pixelSkip;
            var ph = canvasSize.Y / height * pixelSkip;
            
            dl.AddRectFilled(new Vector2(px, py), new Vector2(px + pw, py + ph), ImGui.GetColorU32(color));
        }

        // Draw isocontours
        if (_showIsocontours)
        {
            var tempRange = _options.TemperatureHot - _options.TemperatureCold;
            for (int i = 1; i <= _numIsocontours; i++)
            {
                var isovalue = _options.TemperatureCold + (i * tempRange / (_numIsocontours + 1));
                var lines = IsosurfaceGenerator.GenerateIsocontours(slice, (float)isovalue);
                
                // Use different colors for different contour levels
                float t = (float)i / (_numIsocontours + 1);
                var contourColor = new Vector4(1.0f, 1.0f, 1.0f, 0.8f);
                
                foreach(var (p1, p2) in lines)
                {
                     var sp1 = canvasPos + new Vector2(p1.X / width * canvasSize.X, p1.Y / height * canvasSize.Y);
                     var sp2 = canvasPos + new Vector2(p2.X / width * canvasSize.X, p2.Y / height * canvasSize.Y);
                     dl.AddLine(sp1, sp2, ImGui.GetColorU32(contourColor), 1.5f);
                }
            }
        }
        
        // Draw border
        dl.AddRect(canvasPos, canvasPos + canvasSize, 0xFFFFFFFF, 0, 0, 2.0f);
        
        ImGui.Dummy(canvasSize); // Reserve space
        
        // Mouse interaction - show temperature value on hover
        if (ImGui.IsItemHovered())
        {
            var mousePos = ImGui.GetMousePos();
            var relativePos = mousePos - canvasPos;
            int hoverX = (int)(relativePos.X / canvasSize.X * width);
            int hoverY = (int)(relativePos.Y / canvasSize.Y * height);
            
            if (hoverX >= 0 && hoverX < width && hoverY >= 0 && hoverY < height)
            {
                var temp = slice[hoverX, hoverY];
                var tempC = temp - 273.15;
                ImGui.SetTooltip($"Position: ({hoverX}, {hoverY})\nTemperature: {temp:F2} K ({tempC:F2} °C)");
            }
        }
        
        ImGui.Spacing();
        
        // Draw color scale legend
        DrawColorScaleLegend(canvasPos + new Vector2(canvasSize.X + 10, 0), new Vector2(30, canvasSize.Y));
        
        ImGui.Spacing();
        
        if (ImGui.Button("Export Slice as PNG", new Vector2(180, 0)))
        {
            _pngExportDialog.Open($"Slice_{(HeatFlowDirection)_selectedSliceDirectionInt}{_selectedSliceIndex}.png");
        }
        ImGui.SameLine();
        if (ImGui.Button("Export Slice to CSV", new Vector2(180, 0)))
        {
            _sliceCsvExportDialog.Open($"SliceData_{(HeatFlowDirection)_selectedSliceDirectionInt}{_selectedSliceIndex}.csv");
        }
        
        if (_pngExportDialog.Submit())
        {
            ExportSliceToPng(_pngExportDialog.SelectedPath, slice, width, height);
        }
        if (_sliceCsvExportDialog.Submit())
        {
            ExportSliceToCsv(_sliceCsvExportDialog.SelectedPath, slice);
        }
    }
    
    private void DrawColorScaleLegend(Vector2 pos, Vector2 size)
    {
        var dl = ImGui.GetWindowDrawList();
        
        // Draw color gradient
        int steps = 50;
        for (int i = 0; i < steps; i++)
        {
            float t = (float)i / (steps - 1);
            var color = ApplyColorMap(t, _colorMapIndex);
            
            var y1 = pos.Y + size.Y * (1.0f - t - 1.0f / steps);
            var y2 = pos.Y + size.Y * (1.0f - t);
            
            dl.AddRectFilled(
                new Vector2(pos.X, y1),
                new Vector2(pos.X + size.X, y2),
                ImGui.GetColorU32(color)
            );
        }
        
        // Draw border
        dl.AddRect(pos, pos + size, 0xFFFFFFFF);
        
        // Draw temperature labels
        var font = ImGui.GetFont();
        var tempHot = _options.TemperatureHot;
        var tempCold = _options.TemperatureCold;
        
        // Hot temperature (top)
        var labelHot = $"{tempHot:F0}K";
        dl.AddText(new Vector2(pos.X + size.X + 5, pos.Y - 5), 0xFFFFFFFF, labelHot);
        
        // Cold temperature (bottom)
        var labelCold = $"{tempCold:F0}K";
        dl.AddText(new Vector2(pos.X + size.X + 5, pos.Y + size.Y - 10), 0xFFFFFFFF, labelCold);
        
        // Middle temperature
        var tempMid = (tempHot + tempCold) / 2;
        var labelMid = $"{tempMid:F0}K";
        dl.AddText(new Vector2(pos.X + size.X + 5, pos.Y + size.Y / 2 - 5), 0xFFFFFFFF, labelMid);
    }

    private void DrawIsosurfaceGenerator()
    {
         if (_results?.TemperatureField == null) return;

        ImGui.Text("Generate 3D mesh of isothermal surface:");
        ImGui.Spacing();
        
        // Temperature range info
        ImGui.Text($"Temperature range: {_options.TemperatureCold:F1} K to {_options.TemperatureHot:F1} K");
        ImGui.Text($"                   {(_options.TemperatureCold - 273.15):F1} °C to {(_options.TemperatureHot - 273.15):F1} °C");
        ImGui.Spacing();
        
        // Single isosurface generation
        ImGui.InputDouble("Isosurface Temperature (K)", ref _isosurfaceValue);
        ImGui.SameLine();
        if (ImGui.SmallButton("Set to Mid"))
        {
            _isosurfaceValue = (_options.TemperatureHot + _options.TemperatureCold) / 2.0;
        }
        
        var tempC = _isosurfaceValue - 273.15;
        ImGui.Text($"= {tempC:F2} °C");

        if (ImGui.Button("Generate Single Isosurface", new Vector2(-1, 0)))
        {
            GenerateIsosurface(_isosurfaceValue);
        }
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
        // Batch generation
        ImGui.Text("Batch Generation:");
        
        ImGui.SliderInt("Number of surfaces", ref _numIsosurfaces, 2, 20);
        
        if (ImGui.Button("Generate Multiple Isosurfaces", new Vector2(-1, 0)))
        {
            GenerateMultipleIsosurfaces(_numIsosurfaces);
        }
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // List generated meshes
         if (_results.IsosurfaceMeshes.Count > 0)
        {
            ImGui.Text($"Generated Meshes ({_results.IsosurfaceMeshes.Count}):");
            
            if (ImGui.BeginTable("MeshTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
            {
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Vertices", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn("Faces", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 160);
                ImGui.TableHeadersRow();
                
                for (int i = _results.IsosurfaceMeshes.Count - 1; i >= 0; i--)
                {
                    var mesh = _results.IsosurfaceMeshes[i];
                    
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(mesh.Name);
                    
                    ImGui.TableNextColumn();
                    ImGui.Text(mesh.VertexCount.ToString());
                    
                    ImGui.TableNextColumn();
                    ImGui.Text(mesh.FaceCount.ToString());
                    
                    ImGui.TableNextColumn();
                    if (ImGui.SmallButton($"Export STL##{i}"))
                    {
                        _stlExportDialog.Open($"{mesh.Name}.stl");
                    }
                    if (_stlExportDialog.Submit())
                    {
                        MeshExporter.ExportToStl(mesh, _stlExportDialog.SelectedPath);
                    }
                    
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"Remove##{i}"))
                    {
                        ProjectManager.Instance.RemoveDataset(mesh);
                        _results.IsosurfaceMeshes.RemoveAt(i);
                    }
                }
                
                ImGui.EndTable();
            }
            
            if (ImGui.Button("Clear All Meshes", new Vector2(-1, 0)))
            {
                foreach (var mesh in _results.IsosurfaceMeshes)
                {
                    ProjectManager.Instance.RemoveDataset(mesh);
                }
                _results.IsosurfaceMeshes.Clear();
            }
        }
        else
        {
            ImGui.TextDisabled("No isosurface meshes generated yet.");
        }
    }
    private void ExportSliceToCsv(string path, float[,] slice)
    {
        if (slice == null) return;
        try
        {
            var width = slice.GetLength(0);
            var height = slice.GetLength(1);
            var sb = new StringBuilder();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    sb.Append(slice[x, y].ToString("F4"));
                    if (x < width - 1)
                    {
                        sb.Append(",");
                    }
                }
                sb.AppendLine();
            }

            File.WriteAllText(path, sb.ToString());
            Logger.Log($"[ThermalTool] Successfully exported slice data to {path}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[ThermalTool] Failed to export slice CSV: {ex.Message}");
        }
    }
    private void ExportCompositeImage(string filePath)
    {
        try
        {
            var (slice, sliceWidth, sliceHeight) = GetSelectedSlice();
            if (slice == null) return;

            // Define layout
            int padding = 20;
            int legendWidth = 60;
            int infoWidth = 250;
            int compositeWidth = sliceWidth + legendWidth + infoWidth + padding * 4;
            int compositeHeight = Math.Max(sliceHeight, 400) + padding * 2;
            
            var buffer = new byte[compositeWidth * compositeHeight * 4];
            // Fill with a dark gray background
            Array.Fill(buffer, (byte)50);
            for (int i = 3; i < buffer.Length; i += 4) buffer[i] = 255;

            // 1. Draw Temperature Slice
            for (int y = 0; y < sliceHeight; y++)
            {
                for (int x = 0; x < sliceWidth; x++)
                {
                    var temp = slice[x, y];
                    var norm = Math.Clamp((temp - _options.TemperatureCold) / (_options.TemperatureHot - _options.TemperatureCold), 0.0, 1.0);
                    var color = ApplyColorMap((float)norm, _colorMapIndex);
                    
                    int destIdx = ((y + padding) * compositeWidth + (x + padding)) * 4;
                    buffer[destIdx + 0] = (byte)(color.X * 255);
                    buffer[destIdx + 1] = (byte)(color.Y * 255);
                    buffer[destIdx + 2] = (byte)(color.Z * 255);
                    buffer[destIdx + 3] = 255;
                }
            }
            
            // 2. Draw Color Legend
            int legendX = sliceWidth + padding * 2;
            int legendY = padding;
            int barWidth = 30;
            for (int i = 0; i < sliceHeight; i++)
            {
                var color = ApplyColorMap((float)i / (sliceHeight - 1), _colorMapIndex);
                for (int j = 0; j < barWidth; j++)
                {
                    int destIdx = ((sliceHeight - 1 - i + legendY) * compositeWidth + (j + legendX)) * 4;
                    buffer[destIdx + 0] = (byte)(color.X * 255);
                    buffer[destIdx + 1] = (byte)(color.Y * 255);
                    buffer[destIdx + 2] = (byte)(color.Z * 255);
                }
            }
            uint white = 0xFFFFFFFF;
            SimpleFontRenderer.DrawText(buffer, compositeWidth, legendX + barWidth + 5, legendY, $"{_options.TemperatureHot:F0}K", white);
            SimpleFontRenderer.DrawText(buffer, compositeWidth, legendX + barWidth + 5, legendY + sliceHeight - 10, $"{_options.TemperatureCold:F0}K", white);

            // 3. Draw Scale Bar
            float pixelSizeUm = _options.Dataset.PixelSize;
            float scaleBarLengthMm = 0.1f; // 100 um
            int scaleBarLengthPx = (int)(scaleBarLengthMm * 1000 / pixelSizeUm);
            int scaleBarY = padding + sliceHeight + 10;
            for (int i = 0; i < scaleBarLengthPx; i++)
            {
                for (int j = 0; j < 5; j++)
                {
                    int destIdx = ((scaleBarY + j) * compositeWidth + (padding + i)) * 4;
                    buffer[destIdx + 0] = 255; buffer[destIdx + 1] = 255; buffer[destIdx + 2] = 255;
                }
            }
            SimpleFontRenderer.DrawText(buffer, compositeWidth, padding, scaleBarY + 8, $"{scaleBarLengthMm * 1000} UM", white);
            
            // 4. Draw Text Info
            int infoX = legendX + legendWidth + padding;
            int infoY = padding;
            int line = 0;
            var text = new List<string>
            {
                $"KEFF: {_results.EffectiveConductivity:F4} W/MK",
                $"AXIS: {(HeatFlowDirection)_selectedSliceDirectionInt}",
                $"SLICE: {_selectedSliceIndex}",
                $"T_HOT: {_options.TemperatureHot:F1}K",
                $"T_COLD: {_options.TemperatureCold:F1}K",
                $"DATASET: {_options.Dataset.Name}"
            };
            foreach(var str in text)
            {
                SimpleFontRenderer.DrawText(buffer, compositeWidth, infoX, infoY + line++ * 12, str, white);
            }

            ImageExporter.ExportColorSlice(buffer, compositeWidth, compositeHeight, filePath);
            Logger.Log($"[ThermalTool] Successfully exported composite image to {filePath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[ThermalTool] Failed to export composite image: {ex.Message}");
        }
    }
    private string GetReportTextContent()
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("========================================");
        sb.AppendLine("   Thermal Conductivity Analysis Report   ");
        sb.AppendLine("========================================");
        sb.AppendLine();
        sb.AppendLine($"Dataset: {_options.Dataset.Name}");
        sb.AppendLine($"Generated on: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        sb.AppendLine("--- Simulation Summary ---");
        sb.AppendLine($"Effective Thermal Conductivity: {_results.EffectiveConductivity:F6} W/mK");
        sb.AppendLine($"Total Computation Time: {_results.ComputationTime.TotalSeconds:F3} seconds");
        sb.AppendLine();

        sb.AppendLine("--- Simulation Parameters ---");
        sb.AppendLine($"Hot Boundary Temperature: {_options.TemperatureHot:F2} K ({_options.TemperatureHot - 273.15:F1} C)");
        sb.AppendLine($"Cold Boundary Temperature: {_options.TemperatureCold:F2} K ({_options.TemperatureCold - 273.15:F1} C)");
        sb.AppendLine($"Heat Flow Direction: {_options.HeatFlowDirection}");
        sb.AppendLine($"Solver Backend: {_options.SolverBackend}");
        sb.AppendLine($"Max Iterations: {_options.MaxIterations}");
        sb.AppendLine($"Convergence Tolerance: {_options.ConvergenceTolerance:E2}");
        sb.AppendLine();

        sb.AppendLine("--- Material Properties Used ---");
        sb.AppendLine("ID | Name                 | Conductivity (W/mK)");
        sb.AppendLine("---|----------------------|--------------------");
        foreach (var material in _options.Dataset.Materials.OrderBy(m => m.ID))
        {
            var conductivity = _results.MaterialConductivities.GetValueOrDefault(material.ID, 0.0);
            sb.AppendLine($"{material.ID,-3}| {material.Name,-20} | {conductivity,-18:F4}");
        }
        sb.AppendLine();

        if (_results.AnalyticalEstimates.Count > 0)
        {
            sb.AppendLine("--- Analytical Model Comparison ---");
            sb.AppendLine("Model Name               | Conductivity (W/mK) | Rel. Error (%)");
            sb.AppendLine("-------------------------|---------------------|---------------");
            foreach (var (name, value) in _results.AnalyticalEstimates.OrderBy(x => x.Value))
            {
                var relativeError = Math.Abs((_results.EffectiveConductivity - value) / _results.EffectiveConductivity * 100.0);
                sb.AppendLine($"{name,-24} | {value,-19:F6} | {relativeError,12:F2}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("--- Dataset Information ---");
        sb.AppendLine($"Dimensions: {_options.Dataset.Width} x {_options.Dataset.Height} x {_options.Dataset.Depth} voxels");
        sb.AppendLine($"Voxel Size: {_options.Dataset.PixelSize} um (in-plane), {_options.Dataset.SliceThickness} um (thickness)");
        sb.AppendLine();

        return sb.ToString();
    }
    private void ExportTextReport(string filePath, bool isRtf)
    {
        try
        {
            string content = GetReportTextContent();
            if (isRtf)
            {
                var rtfContent = new StringBuilder();
                rtfContent.AppendLine(@"{\rtf1\ansi\deff0");
                rtfContent.AppendLine(@"{\fonttbl{\f0 Arial;}}");
                rtfContent.AppendLine(@"\pard\sa200\sl276\slmult1\f0\fs24");

                var lines = content.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                foreach (var line in lines)
                {
                    string rtfLine = line.Replace(@"\", @"\\").Replace("{", @"\{").Replace("}", @"\}");
                    if (line.StartsWith("===") || line.StartsWith("---"))
                    {
                        rtfContent.Append(@"\b ");
                        rtfContent.Append(rtfLine);
                        rtfContent.Append(@"\b0");
                    }
                    else
                    {
                        rtfContent.Append(rtfLine);
                    }
                    rtfContent.AppendLine(@"\par");
                }
                rtfContent.AppendLine("}");
                content = rtfContent.ToString();
            }

            File.WriteAllText(filePath, content);
            Logger.Log($"[ThermalTool] Successfully exported report to {filePath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[ThermalTool] Failed to export report: {ex.Message}");
        }
    }
    private void GenerateIsosurface(double temperature)
    {
        try
        {
            Logger.Log($"[ThermalTool] Generating isosurface at {temperature:F2} K ({(temperature - 273.15):F2} °C)");
            
            var voxelSize = new Vector3(
                _options.Dataset.PixelSize * 1e-6f,
                _options.Dataset.PixelSize * 1e-6f, 
                _options.Dataset.SliceThickness * 1e-6f
            );
            
            var mesh = IsosurfaceGenerator.GenerateIsosurface(
                _results.TemperatureField, 
                (float)temperature, 
                voxelSize
            );
            
            if (mesh.VertexCount > 0)
            {
                ProjectManager.Instance.AddDataset(mesh);
                _results.IsosurfaceMeshes.Add(mesh);
                Logger.Log($"[ThermalTool] Generated mesh with {mesh.VertexCount} vertices and {mesh.FaceCount} faces");
            }
            else
            {
                Logger.LogWarning($"[ThermalTool] No surface found at temperature {temperature:F2} K");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[ThermalTool] Failed to generate isosurface: {ex.Message}");
        }
    }
    
    private void GenerateMultipleIsosurfaces(int count)
    {
        try
        {
            Logger.Log($"[ThermalTool] Generating {count} isosurfaces");
            
            var tempRange = _options.TemperatureHot - _options.TemperatureCold;
            var voxelSize = new Vector3(
                _options.Dataset.PixelSize * 1e-6f,
                _options.Dataset.PixelSize * 1e-6f,
                _options.Dataset.SliceThickness * 1e-6f
            );
            
            int generated = 0;
            for (int i = 1; i <= count; i++)
            {
                var temperature = _options.TemperatureCold + (i * tempRange / (count + 1));
                
                try
                {
                    var mesh = IsosurfaceGenerator.GenerateIsosurface(
                        _results.TemperatureField,
                        (float)temperature,
                        voxelSize
                    );
                    
                    if (mesh.VertexCount > 0)
                    {
                        ProjectManager.Instance.AddDataset(mesh);
                        _results.IsosurfaceMeshes.Add(mesh);
                        generated++;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"[ThermalTool] Failed to generate isosurface at {temperature:F2} K: {ex.Message}");
                }
            }
            
            Logger.Log($"[ThermalTool] Successfully generated {generated} out of {count} isosurfaces");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[ThermalTool] Batch generation failed: {ex.Message}");
        }
    }

    private void StartSimulation()
    {
        // Validate before starting
        if (!ValidateSettings(out var validationMessages))
        {
            Logger.LogError("[ThermalTool] Cannot start simulation - validation failed:");
            foreach (var msg in validationMessages)
            {
                Logger.LogError($"  - {msg}");
            }
            return;
        }
        
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();
        
        _progressDialog.Open("Initializing thermal simulation...");
        _isSimulationRunning = true;

        var startTime = DateTime.Now;
        Logger.Log($"[ThermalTool] Starting thermal simulation at {startTime:HH:mm:ss}");
        Logger.Log($"[ThermalTool] Dataset: {_options.Dataset.Name} ({_options.Dataset.Width}x{_options.Dataset.Height}x{_options.Dataset.Depth})");
        Logger.Log($"[ThermalTool] Temperature range: {_options.TemperatureCold:F2} K to {_options.TemperatureHot:F2} K");
        Logger.Log($"[ThermalTool] Solver: {_options.SolverBackend}");

        _simulationTask = Task.Run(() =>
        {
            try
            {
                var progress = new Progress<float>((p) =>
                {
                    var percent = (int)(p * 100);
                    var stage = p switch
                    {
                        < 0.05f => "Initializing...",
                        < 0.10f => "Loading material properties...",
                        < 0.15f => "Setting up solver...",
                        < 0.85f => $"Solving heat equation... {percent}%",
                        < 0.90f => "Computing effective conductivity...",
                        < 0.95f => "Calculating analytical estimates...",
                        _ => "Finalizing results..."
                    };
                    
                    _progressDialog.Update(p, stage);
                });
                
                _results = ThermalConductivitySolver.Solve(_options, progress, _cancellationTokenSource.Token);
                
                var endTime = DateTime.Now;
                var elapsed = endTime - startTime;
                
                Logger.Log($"[ThermalTool] Simulation completed at {endTime:HH:mm:ss}");
                Logger.Log($"[ThermalTool] Total time: {elapsed.TotalSeconds:F2} seconds");
                Logger.Log($"[ThermalTool] Effective conductivity: {_results.EffectiveConductivity:F6} W/m·K");
                
                // Log analytical comparison
                if (_results.AnalyticalEstimates.Count > 0)
                {
                    Logger.Log("[ThermalTool] Analytical model comparison:");
                    foreach (var (model, keff) in _results.AnalyticalEstimates.OrderBy(x => x.Value))
                    {
                        var error = Math.Abs(_results.EffectiveConductivity - keff) / _results.EffectiveConductivity * 100.0;
                        Logger.Log($"  {model}: {keff:F6} W/m·K (error: {error:F2}%)");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Log("[ThermalTool] Simulation was canceled by user.");
                _results = null;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ThermalTool] Simulation failed with error: {ex.Message}");
                Logger.LogError($"Stack trace: {ex.StackTrace}");
                _results = null;
            }
            finally
            {
                _isSimulationRunning = false;
                _progressDialog.Close();
            }
        });
    }

    private void ExportSummaryToCsv(string path)
    {
        try
        {
            using (var writer = new StreamWriter(path))
            {
                // Header
                writer.WriteLine("# Thermal Conductivity Analysis Results");
                writer.WriteLine($"# Dataset: {_options.Dataset.Name}");
                writer.WriteLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine($"# Voxel Size: {_options.Dataset.PixelSize} µm");
                writer.WriteLine("#");
                writer.WriteLine();
                
                // Summary statistics
                writer.WriteLine("## Summary Statistics");
                writer.WriteLine("Parameter,Value,Unit");
                writer.WriteLine($"Effective Thermal Conductivity,{_results.EffectiveConductivity:F6},W/mK");
                writer.WriteLine($"Computation Time,{_results.ComputationTime.TotalSeconds:F3},seconds");
                writer.WriteLine($"Hot Boundary Temperature,{_options.TemperatureHot:F2},K");
                writer.WriteLine($"Cold Boundary Temperature,{_options.TemperatureCold:F2},K");
                writer.WriteLine($"Temperature Gradient,{(_options.TemperatureHot - _options.TemperatureCold):F2},K");
                writer.WriteLine($"Heat Flow Direction,{_options.HeatFlowDirection},");
                writer.WriteLine($"Max Iterations,{_options.MaxIterations},");
                writer.WriteLine($"Convergence Tolerance,{_options.ConvergenceTolerance:E3},");
                writer.WriteLine();
                
                // Material properties
                writer.WriteLine("## Material Conductivities");
                writer.WriteLine("Material ID,Material Name,Conductivity (W/mK),Volume Fraction");
                
                var totalVoxels = _options.Dataset.Width * _options.Dataset.Height * _options.Dataset.Depth;
                var materialVoxelCounts = new Dictionary<byte, long>();
                
                // Count voxels per material
                for (int z = 0; z < _options.Dataset.Depth; z++)
                for (int y = 0; y < _options.Dataset.Height; y++)
                for (int x = 0; x < _options.Dataset.Width; x++)
                {
                    byte mat = _options.Dataset.LabelData[x, y, z];
                    if (!materialVoxelCounts.ContainsKey(mat))
                        materialVoxelCounts[mat] = 0;
                    materialVoxelCounts[mat]++;
                }
                
                foreach (var material in _options.Dataset.Materials.OrderBy(m => m.ID))
                {
                    var conductivity = _results.MaterialConductivities.ContainsKey(material.ID) 
                        ? _results.MaterialConductivities[material.ID] 
                        : 0.0;
                    
                    var voxelCount = materialVoxelCounts.ContainsKey(material.ID) 
                        ? materialVoxelCounts[material.ID] 
                        : 0;
                    
                    var volumeFraction = (double)voxelCount / totalVoxels;
                    
                    writer.WriteLine($"{material.ID},{material.Name},{conductivity:F6},{volumeFraction:F6}");
                }
                writer.WriteLine();
                
                // Analytical estimates
                if (_results.AnalyticalEstimates.Count > 0)
                {
                    writer.WriteLine("## Analytical Model Estimates");
                    writer.WriteLine("Model,Conductivity (W/mK),Relative Error (%)");
                    
                    foreach (var (name, value) in _results.AnalyticalEstimates.OrderBy(x => x.Value))
                    {
                        var relativeError = (_results.EffectiveConductivity - value) / _results.EffectiveConductivity * 100.0;
                        writer.WriteLine($"{name},{value:F6},{relativeError:F2}");
                    }
                    writer.WriteLine();
                }
                
                // Temperature field statistics
                writer.WriteLine("## Temperature Field Statistics");
                writer.WriteLine("Statistic,Value,Unit");
                
                double minTemp = double.MaxValue;
                double maxTemp = double.MinValue;
                double sumTemp = 0;
                long count = 0;
                
                for (int z = 0; z < _options.Dataset.Depth; z++)
                for (int y = 0; y < _options.Dataset.Height; y++)
                for (int x = 0; x < _options.Dataset.Width; x++)
                {
                    var temp = _results.TemperatureField[x, y, z];
                    minTemp = Math.Min(minTemp, temp);
                    maxTemp = Math.Max(maxTemp, temp);
                    sumTemp += temp;
                    count++;
                }
                
                var meanTemp = sumTemp / count;
                
                writer.WriteLine($"Minimum Temperature,{minTemp:F2},K");
                writer.WriteLine($"Maximum Temperature,{maxTemp:F2},K");
                writer.WriteLine($"Mean Temperature,{meanTemp:F2},K");
                writer.WriteLine($"Total Voxels,{count},");
                writer.WriteLine();
                
                // Dataset dimensions
                writer.WriteLine("## Dataset Information");
                writer.WriteLine("Parameter,Value");
                writer.WriteLine($"Width,{_options.Dataset.Width}");
                writer.WriteLine($"Height,{_options.Dataset.Height}");
                writer.WriteLine($"Depth,{_options.Dataset.Depth}");
                writer.WriteLine($"Pixel Size,{_options.Dataset.PixelSize} µm");
                writer.WriteLine($"Slice Thickness,{_options.Dataset.SliceThickness} µm");
            }
            
            Logger.Log($"[ThermalTool] Successfully exported comprehensive results to {path}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[ThermalTool] Failed to export CSV: {ex.Message}");
        }
    }
    
    private (float[,] slice, int width, int height) GetSelectedSlice()
    {
        if (_results?.TemperatureField == null) return (null, 0, 0);

        var W = _options.Dataset.Width;
        var H = _options.Dataset.Height;
        var D = _options.Dataset.Depth;
        var selectedDirection = (HeatFlowDirection)_selectedSliceDirectionInt;
        
        _selectedSliceIndex = Math.Clamp(_selectedSliceIndex, 0, 
            selectedDirection == HeatFlowDirection.X ? W - 1 :
            selectedDirection == HeatFlowDirection.Y ? H - 1 : D - 1);
        
        if (_results.TemperatureSlices.TryGetValue((selectedDirection.ToString()[0], _selectedSliceIndex), out var slice))
        {
            return (slice, slice.GetLength(0), slice.GetLength(1));
        }

        // Extract slice if not cached
        switch (selectedDirection)
        {
            case HeatFlowDirection.X:
                var sliceX = new float[H, D];
                Parallel.For(0, H, y => { for (int z = 0; z < D; z++) sliceX[y, z] = _results.TemperatureField[_selectedSliceIndex, y, z]; });
                _results.TemperatureSlices[('X', _selectedSliceIndex)] = sliceX;
                return (sliceX, H, D);
            case HeatFlowDirection.Y:
                var sliceY = new float[W, D];
                Parallel.For(0, W, x => { for (int z = 0; z < D; z++) sliceY[x, z] = _results.TemperatureField[x, _selectedSliceIndex, z]; });
                 _results.TemperatureSlices[('Y', _selectedSliceIndex)] = sliceY;
                return (sliceY, W, D);
            case HeatFlowDirection.Z:
                var sliceZ = new float[W, H];
                Parallel.For(0, W, x => { for (int y = 0; y < H; y++) sliceZ[x, y] = _results.TemperatureField[x, y, _selectedSliceIndex]; });
                _results.TemperatureSlices[('Z', _selectedSliceIndex)] = sliceZ;
                return (sliceZ, W, H);
        }
        return (null, 0, 0);
    }
    
    private static void InitializeColormaps()
    {
        if (_colormapData != null) return;
        const int size = 256;
        _colormapData = new Vector3[2, size];

        // Hot (map 0)
        for (var i = 0; i < size; i++)
        {
            var t = i / (float)(size - 1);
            var r = Math.Min(1.0f, 3.0f * t);
            var g = Math.Clamp(3.0f * t - 1.0f, 0.0f, 1.0f);
            var b = Math.Clamp(3.0f * t - 2.0f, 0.0f, 1.0f);
            _colormapData[0, i] = new Vector3(r, g, b);
        }
        // Rainbow (map 1)
        for (var i = 0; i < size; i++)
        {
            var h = i / (float)(size - 1) * 0.7f; 
            _colormapData[1, i] = HsvToRgb(h, 1.0f, 1.0f);
        }
    }
    
    private Vector4 ApplyColorMap(float normalizedIntensity, int colorMapIndex)
    {
        var mapIdx = Math.Clamp(colorMapIndex, 0, 1);
        var texelIdx = (int)(normalizedIntensity * 255);
        texelIdx = Math.Clamp(texelIdx, 0, 255);
        var rgb = _colormapData[mapIdx, texelIdx];
        return new Vector4(rgb.X, rgb.Y, rgb.Z, 1.0f);
    }
    
    private static Vector3 HsvToRgb(float h, float s, float v)
    {
        float r, g, b;
        int i = (int)(h * 6);
        float f = h * 6 - i;
        float p = v * (1 - s);
        float q = v * (1 - f * s);
        float t = v * (1 - (1 - f) * s);
        switch (i % 6)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }
        return new Vector3(r, g, b);
    }

    private void ExportSliceToPng(string filePath, float[,] slice, int width, int height)
    {
        try
        {
            // Create RGBA image data
            var imageData = new byte[width * height * 4];
            
            // Normalize and colormap the temperature data
            Parallel.For(0, height, y =>
            {
                for (int x = 0; x < width; x++)
                {
                    var temp = slice[x, y];
                    var normalizedTemp = (temp - _options.TemperatureCold) / (_options.TemperatureHot - _options.TemperatureCold);
                    normalizedTemp = Math.Clamp(normalizedTemp, 0.0, 1.0);
                    
                    var color = ApplyColorMap((float)normalizedTemp, _colorMapIndex);
                    
                    int idx = (y * width + x) * 4;
                    imageData[idx + 0] = (byte)(color.X * 255);     // R
                    imageData[idx + 1] = (byte)(color.Y * 255);     // G
                    imageData[idx + 2] = (byte)(color.Z * 255);     // B
                    imageData[idx + 3] = (byte)(color.W * 255);     // A
                }
            });
            
            // Draw isocontours if enabled
            if (_showIsocontours)
            {
                var tempRange = _options.TemperatureHot - _options.TemperatureCold;
                for (int i = 1; i <= _numIsocontours; i++)
                {
                    var isovalue = _options.TemperatureCold + (i * tempRange / (_numIsocontours + 1));
                    var lines = IsosurfaceGenerator.GenerateIsocontours(slice, (float)isovalue);
                    
                    // Draw lines in white
                    foreach (var (p1, p2) in lines)
                    {
                        DrawLineOnImage(imageData, width, height, 
                            (int)p1.X, (int)p1.Y, (int)p2.X, (int)p2.Y, 
                            255, 255, 255, 255);
                    }
                }
            }
            
            // Export using ImageExporter
            ImageExporter.ExportColorSlice(imageData, width, height, filePath);
            
            Logger.Log($"[ThermalTool] Successfully exported slice to {filePath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[ThermalTool] Failed to export PNG: {ex.Message}");
        }
    }
    
    private void DrawLineOnImage(byte[] imageData, int width, int height, 
        int x0, int y0, int x1, int y1, byte r, byte g, byte b, byte a)
    {
        // Bresenham's line algorithm
        int dx = Math.Abs(x1 - x0);
        int dy = Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;
        
        while (true)
        {
            // Plot pixel if within bounds
            if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < height)
            {
                int idx = (y0 * width + x0) * 4;
                imageData[idx + 0] = r;
                imageData[idx + 1] = g;
                imageData[idx + 2] = b;
                imageData[idx + 3] = a;
            }
            
            if (x0 == x1 && y0 == y1) break;
            
            int e2 = 2 * err;
            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }
            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }
}