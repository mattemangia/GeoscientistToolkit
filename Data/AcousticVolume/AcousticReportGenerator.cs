// GeoscientistToolkit/UI/AcousticVolume/AcousticReportGenerator.cs
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using StbImageWriteSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GeoscientistToolkit.UI.AcousticVolume
{
    /// <summary>
    /// A tool for generating comprehensive analysis reports for Acoustic Volume datasets.
    /// Supports both plain text and rich text (Markdown with embedded images).
    /// </summary>
    public class AcousticReportGeneratorTool : IDatasetTools
    {
        // --- UI State ---
        private enum ReportFormat { PlainText, Markdown }
        private int _format = (int)ReportFormat.Markdown;

        // Report Sections
        private bool _includeSummary = true;
        private bool _includeWaveStats = true;
        private bool _includeDamageAnalysis = true;
        private bool _includeVelocityProfile = true;
        private bool _includeWaveform = true;
        private bool _renderImages = true;

        // User-defined data
        private static bool _isVelocityLineSet = false;
        private static bool _isWaveformPointSet = false;

        // System Dialogs
        private readonly ImGuiExportFileDialog _exportDialog;
        private readonly ProgressBarDialog _progressDialog;

        private bool _isGenerating = false;

        public AcousticReportGeneratorTool()
        {
            _exportDialog = new ImGuiExportFileDialog("AcousticReportExport", "Export Analysis Report");
            _progressDialog = new ProgressBarDialog("Generating Report");
        }

        public void Draw(Dataset dataset)
        {
            if (dataset is not AcousticVolumeDataset ad)
            {
                ImGui.TextDisabled("This tool requires an Acoustic Volume Dataset.");
                return;
            }

            ImGui.TextWrapped("Generate a comprehensive analysis report including metadata, statistics, and visualizations.");
            ImGui.Separator();

            // --- Report Options ---
            ImGui.Text("Report Format:");
            ImGui.RadioButton("Plain Text (.txt)", ref _format, (int)ReportFormat.PlainText);
            ImGui.SameLine();
            ImGui.RadioButton("Rich Text / Markdown (.md)", ref _format, (int)ReportFormat.Markdown);

            ImGui.Spacing();
            ImGui.Text("Include Sections:");
            ImGui.Checkbox("Dataset Summary", ref _includeSummary);
            ImGui.Checkbox("Wave Field Statistics", ref _includeWaveStats);

            // Damage Analysis Section
            bool canAnalyzeDamage = ad.DamageField != null;
            if (!canAnalyzeDamage) ImGui.BeginDisabled();
            ImGui.Checkbox("Damage Field Analysis", ref _includeDamageAnalysis);
            if (!canAnalyzeDamage)
            {
                ImGui.EndDisabled();
                if(ImGui.IsItemHovered()) ImGui.SetTooltip("Damage field not available in this dataset.");
            }

            // Velocity Profile Section
            bool canDoVelocity = ad.DensityData != null;
            if (!canDoVelocity) ImGui.BeginDisabled();
            ImGui.Checkbox("Velocity Profile", ref _includeVelocityProfile);
            if (!canDoVelocity)
            {
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Density data must be calibrated first.");
            }

            // Waveform Section
            bool canDoWaveform = ad.TimeSeriesSnapshots != null && ad.TimeSeriesSnapshots.Count > 0;
            if (!canDoWaveform) ImGui.BeginDisabled();
            ImGui.Checkbox("Waveform at a Point", ref _includeWaveform);
            if (!canDoWaveform)
            {
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Time series data is required for waveform analysis.");
            }
            
            // Image Rendering Option
            if ((ReportFormat)_format == ReportFormat.PlainText) ImGui.BeginDisabled();
            ImGui.Checkbox("Render and Embed Images", ref _renderImages);
            if ((ReportFormat)_format == ReportFormat.PlainText) ImGui.EndDisabled();

            ImGui.Spacing();
            ImGui.Separator();

            // --- Data Selection ---
            ImGui.Text("Data Selection for Report:");

            // Velocity Profile Line Selection
            if (_includeVelocityProfile && canDoVelocity)
            {
                string status = _isVelocityLineSet ? "✓ Line Set" : "✗ Line Not Set";
                Vector4 color = _isVelocityLineSet ? new Vector4(0,1,0,1) : new Vector4(1,1,0,1);
                ImGui.Text("Velocity Profile Line:");
                ImGui.SameLine();
                ImGui.TextColored(color, status);
                ImGui.SameLine();
                if (ImGui.Button("Select Line in Viewer...##Vel"))
                {
                    AcousticInteractionManager.StartLineDrawing();
                }
            }

            // Waveform Point Selection
            if (_includeWaveform && canDoWaveform)
            {
                string status = _isWaveformPointSet ? "✓ Point Set" : "✗ Point Not Set";
                Vector4 color = _isWaveformPointSet ? new Vector4(0, 1, 0, 1) : new Vector4(1, 1, 0, 1);
                ImGui.Text("Waveform Point:");
                ImGui.SameLine();
                ImGui.TextColored(color, status);
                ImGui.SameLine();
                if (ImGui.Button("Select Point in Viewer...##Wf"))
                {
                    AcousticInteractionManager.StartPointSelection();
                }
            }
            
            // Update state based on interaction manager
            if (AcousticInteractionManager.HasNewLine)
            {
                _isVelocityLineSet = true;
                AcousticInteractionManager.HasNewLine = false; // Consume event
            }
            if (AcousticInteractionManager.HasNewPoint)
            {
                _isWaveformPointSet = true;
                AcousticInteractionManager.HasNewPoint = false; // Consume event
            }

            ImGui.Spacing();
            ImGui.Separator();

            // --- Generation Button ---
            bool canGenerate = IsReadyToGenerate();
            if (!canGenerate) ImGui.BeginDisabled();
            if (ImGui.Button("Generate and Export Report...", new Vector2(-1, 0)))
            {
                string extension = (ReportFormat)_format == ReportFormat.PlainText ? ".txt" : ".md";
                string desc = (ReportFormat)_format == ReportFormat.PlainText ? "Plain Text" : "Markdown Document";
                _exportDialog.SetExtensions(new ImGuiExportFileDialog.ExtensionOption(extension, desc));
                _exportDialog.Open($"{ad.Name}_Report");
            }
            if (!canGenerate)
            {
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(GetGenerationTooltip());
            }

            HandleDialogs(ad);
        }

        private bool IsReadyToGenerate()
        {
            if (_isGenerating) return false;
            if (_includeVelocityProfile && !_isVelocityLineSet) return false;
            if (_includeWaveform && !_isWaveformPointSet) return false;
            return true;
        }

        private string GetGenerationTooltip()
        {
            if (_isGenerating) return "Report generation is already in progress.";
            if (_includeVelocityProfile && !_isVelocityLineSet) return "A line for the velocity profile must be selected in the viewer.";
            if (_includeWaveform && !_isWaveformPointSet) return "A point for the waveform analysis must be selected in the viewer.";
            return "Ready to generate.";
        }

        private void HandleDialogs(AcousticVolumeDataset dataset)
        {
            if (_exportDialog.Submit())
            {
                string path = _exportDialog.SelectedPath;
                _isGenerating = true;
                _progressDialog.Open($"Generating Report: {Path.GetFileName(path)}...");
                Task.Run(() => GenerateReportAsync(dataset, path));
            }
            
            if (_isGenerating)
            {
                 _progressDialog.Submit();
            }
        }
        
        private void AppendSectionHeader(StringBuilder sb, string title)
        {
            sb.AppendLine();
            sb.AppendLine("//////////////////////////////////////////////////////////////////////");
            sb.AppendLine($"/// {title.ToUpper()}");
            sb.AppendLine("//////////////////////////////////////////////////////////////////////");
            sb.AppendLine();
        }

        private async Task GenerateReportAsync(AcousticVolumeDataset ad, string exportPath)
        {
            try
            {
                var sb = new StringBuilder();
                string imageSubFolder = $"{Path.GetFileNameWithoutExtension(exportPath)}_images";
                string imageDir = Path.Combine(Path.GetDirectoryName(exportPath), imageSubFolder);
                bool useImages = (ReportFormat)_format == ReportFormat.Markdown && _renderImages;
                ReportImageRenderer renderer = null;

                if (useImages)
                {
                    Directory.CreateDirectory(imageDir);
                    var config = ReportImageRenderer.RenderConfig.Default;
                    renderer = new ReportImageRenderer(ad, imageDir, config);
                }

                // --- HEADER ---
                sb.AppendLine($"# Acoustic Analysis Report: {ad.Name}");
                sb.AppendLine($"*Generated on: {DateTime.Now}*");

                // --- SUMMARY ---
                if (_includeSummary)
                {
                    _progressDialog.Update(0.05f, "Writing summary...");
                    await Task.Delay(50, _progressDialog.CancellationToken); 
                    AppendSectionHeader(sb, "1. Dataset Summary");
                    sb.AppendLine(AcousticAnalysisLogic.GetSummaryReport(ad));
                    if (useImages)
                    {
                        var sliceDef = new ReportImageRenderer.SliceDefinition
                        {
                            FieldName = "Combined", 
                            SliceAxis = 'Z', 
                            SliceIndex = ad.CombinedWaveField != null ? ad.CombinedWaveField.Depth / 2 : 0,
                            Title = "Central Slice of Combined Wave Field"
                        };
                        string imagePath = await renderer.RenderSliceViewAsync(sliceDef, _progressDialog.CancellationToken);
                        sb.AppendLine($"![Combined Field Slice]({Path.Combine(imageSubFolder, Path.GetFileName(imagePath))})");
                        sb.AppendLine("*Fig 1: Axial slice (XY view) of the combined wave field at Z = " + sliceDef.SliceIndex + "*");
                        sb.AppendLine();
                    }
                }

                // --- WAVE FIELD STATISTICS ---
                if (_includeWaveStats)
                {
                    _progressDialog.Update(0.20f, "Analyzing wave fields...");
                    await Task.Delay(50, _progressDialog.CancellationToken);
                    AppendSectionHeader(sb, "2. Wave Field Statistics");
                    sb.AppendLine(AcousticAnalysisLogic.GetWaveStatisticsReport(ad));
                }

                // --- DAMAGE ANALYSIS ---
                if (_includeDamageAnalysis && ad.DamageField != null)
                {
                    _progressDialog.Update(0.40f, "Analyzing damage field...");
                    AppendSectionHeader(sb, "3. Damage Field Analysis");
                    sb.AppendLine(AcousticAnalysisLogic.GetDamageStatisticsReport(ad));
                    if (useImages)
                    {
                        var sliceDef = new ReportImageRenderer.SliceDefinition
                        {
                            FieldName = "Damage", 
                            SliceAxis = 'Z', 
                            SliceIndex = ad.DamageField.Depth / 2,
                            Title = "Central Slice of Damage Field"
                        };
                        string imagePath = await renderer.RenderSliceViewAsync(sliceDef, _progressDialog.CancellationToken);
                        sb.AppendLine($"![Damage Field Slice]({Path.Combine(imageSubFolder, Path.GetFileName(imagePath))})");
                        sb.AppendLine("*Fig 2: Axial slice of the damage field, showing areas of high material failure.*");
                        sb.AppendLine();
                    }
                    _progressDialog.Update(0.55f, "Detecting fracture planes...");
                    sb.AppendLine(await AcousticAnalysisLogic.GetFractureOrientationReport(ad));
                }

                // --- VELOCITY PROFILE ---
                if (_includeVelocityProfile && ad.DensityData != null)
                {
                    _progressDialog.Update(0.70f, "Calculating velocity profile...");
                    var (report, vpData, vsData) = await AcousticAnalysisLogic.GetVelocityProfileReport(ad);
                    AppendSectionHeader(sb, "4. Velocity Profile Analysis");
                    sb.AppendLine(report);
                    if (useImages && vpData != null)
                    {
                        string imagePath = await renderer.RenderVelocityProfileAsync(vpData, vsData, _progressDialog.CancellationToken);
                         sb.AppendLine($"![Velocity Profile Plot]({Path.Combine(imageSubFolder, Path.GetFileName(imagePath))})");
                         sb.AppendLine("*Fig 3: Vp and Vs velocities along the user-defined profile line.*");
                         sb.AppendLine();
                    }
                }
                
                // --- WAVEFORM ANALYSIS ---
                if (_includeWaveform && ad.TimeSeriesSnapshots?.Count > 0)
                {
                    _progressDialog.Update(0.85f, "Extracting waveform...");
                    var (report, waveformData) = await AcousticAnalysisLogic.GetWaveformReport(ad);
                    AppendSectionHeader(sb, "5. Waveform Analysis");
                    sb.AppendLine(report);
                    if (useImages && waveformData != null)
                    {
                        string imagePath = await renderer.RenderWaveformAsync(waveformData, ad.TimeSeriesSnapshots.Last().SimulationTime, _progressDialog.CancellationToken);
                        sb.AppendLine($"![Waveform Plot]({Path.Combine(imageSubFolder, Path.GetFileName(imagePath))})");
                        sb.AppendLine("*Fig 4: Velocity magnitude over time at the user-selected point.*");
                        sb.AppendLine();
                    }
                }

                _progressDialog.Update(0.99f, "Finalizing report...");
                await File.WriteAllTextAsync(exportPath, sb.ToString(), _progressDialog.CancellationToken);
                Logger.Log($"[AcousticReportGenerator] Successfully created report at {exportPath}");
            }
            catch (OperationCanceledException)
            {
                 Logger.LogWarning("[AcousticReportGenerator] Report generation was cancelled.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[AcousticReportGenerator] Failed to generate report: {ex.Message}");
            }
            finally
            {
                _isGenerating = false;
                _progressDialog.Close();
            }
        }

        #region Nested Renderer Class
        /// <summary>
        /// Handles off-screen rendering of images for reports.
        /// This class is self-contained and does not depend on live viewer state.
        /// </summary>
        private class ReportImageRenderer
        {
            public struct RenderConfig
            {
                public int ImageWidth { get; set; }
                public int PlotHeight { get; set; }
                public int PaddingTop { get; set; }
                public int PaddingBottom { get; set; }
                public int PaddingLeft { get; set; }
                public int PaddingRight { get; set; }
                public int LineThickness { get; set; }
                public int FontScale { get; set; }
                
                public static RenderConfig Default => new RenderConfig
                {
                    ImageWidth = 800,
                    PlotHeight = 400,
                    PaddingTop = 40,
                    PaddingBottom = 50,
                    PaddingLeft = 70,
                    PaddingRight = 30,
                    LineThickness = 2,
                    FontScale = 1
                };
            }

            private readonly AcousticVolumeDataset _dataset;
            private readonly string _outputDirectory;
            private readonly RenderConfig _config;

            // Plot Styling
            private static readonly Color PlotBgColor = new Color(20, 20, 25, 255);
            private static readonly Color PlotGridColor = new Color(70, 70, 70, 255);
            private static readonly Color PlotTextColor = new Color(220, 220, 220, 255);
            private static readonly Color VpPlotColor = new Color(60, 180, 255, 255);
            private static readonly Color VsPlotColor = new Color(255, 180, 60, 255);
            private static readonly Color WaveformPlotColor = new Color(100, 255, 100, 255);

            public struct SliceDefinition
            {
                public string FieldName;
                public char SliceAxis;
                public int SliceIndex;
                public string Title;
            }

            public ReportImageRenderer(AcousticVolumeDataset dataset, string outputDirectory, RenderConfig config)
            {
                _dataset = dataset;
                _outputDirectory = outputDirectory;
                _config = config;
            }

            public async Task<string> RenderSliceViewAsync(SliceDefinition def, CancellationToken token)
            {
                return await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    
                    ChunkedVolume volume = null;
                    try
                    {
                        volume = def.FieldName switch
                        {
                            "PWave" => _dataset.PWaveField,
                            "SWave" => _dataset.SWaveField,
                            "Combined" => _dataset.CombinedWaveField,
                            "Damage" => _dataset.DamageField,
                            _ => _dataset.CombinedWaveField
                        };

                        if (volume == null) 
                        {
                            Logger.LogWarning($"[ReportImageRenderer] {def.FieldName} field is not available.");
                            return CreateErrorImage("Field not available");
                        }

                        var (width, height) = GetSliceDimensions(volume, def.SliceAxis);
                        var sliceData = ExtractSlice(volume, def.SliceAxis, def.SliceIndex, width, height);

                        byte[] rgbaData = new byte[width * height * 4];
                        for (int i = 0; i < sliceData.Length; i++)
                        {
                            float val = sliceData[i] / 255f;
                            Vector4 colorVec = (def.FieldName == "Damage") ? GetHotColor(val) : GetJetColor(val);
                            rgbaData[i * 4 + 0] = (byte)(colorVec.X * 255);
                            rgbaData[i * 4 + 1] = (byte)(colorVec.Y * 255);
                            rgbaData[i * 4 + 2] = (byte)(colorVec.Z * 255);
                            rgbaData[i * 4 + 3] = 255;
                        }

                        string path = Path.Combine(_outputDirectory, $"slice_{def.FieldName}_{def.SliceAxis}{def.SliceIndex}.png");
                        SavePng(path, width, height, rgbaData);
                        return path;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"[ReportImageRenderer] Failed to render slice: {ex.Message}");
                        return CreateErrorImage($"Error: {ex.Message}");
                    }
                }, token);
            }

            public async Task<string> RenderVelocityProfileAsync(List<float> vp, List<float> vs, CancellationToken token)
            {
                return await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    
                    var rgbaBuffer = new byte[_config.ImageWidth * _config.PlotHeight * 4];
                    DrawFilledRect(rgbaBuffer, _config.ImageWidth, 0, 0, _config.ImageWidth, _config.PlotHeight, PlotBgColor);

                    if (vp == null || vp.Count == 0 || vs == null || vs.Count == 0)
                    {
                        DrawText(rgbaBuffer, _config.ImageWidth, _config.ImageWidth / 2 - 100, _config.PlotHeight / 2, 
                            "NO DATA TO DISPLAY", PlotTextColor, 2);
                        string errorPath = Path.Combine(_outputDirectory, "velocity_profile_error.png");
                        SavePng(errorPath, _config.ImageWidth, _config.PlotHeight, rgbaBuffer);
                        return errorPath;
                    }
                    
                    float yMin = Math.Min(vs.Min(), 0);
                    float yMax = Math.Max(vp.Max(), vs.Max());
                    if (yMax <= yMin) yMax = yMin + 1;

                    // Draw grid and axes
                    DrawPlotAxesAndGrid(rgbaBuffer, _config.ImageWidth, _config.PlotHeight, vp.Count, 
                        yMin, yMax, "Distance (points)", "Velocity (m/s)", "Velocity Profile");

                    // Draw lines with configured thickness
                    DrawLinePlot(rgbaBuffer, _config.ImageWidth, _config.PlotHeight, vp, yMin, yMax, VpPlotColor);
                    DrawLinePlot(rgbaBuffer, _config.ImageWidth, _config.PlotHeight, vs, yMin, yMax, VsPlotColor);
                    
                    // Draw legend
                    DrawFilledRect(rgbaBuffer, _config.ImageWidth, 
                        _config.PaddingLeft + 20, _config.PaddingTop + 10, 100, 50, 
                        new Color(40, 40, 45, 200));
                    DrawLine(rgbaBuffer, _config.ImageWidth, 
                        _config.PaddingLeft + 30, _config.PaddingTop + 25, 
                        _config.PaddingLeft + 50, _config.PaddingTop + 25, VpPlotColor, 2);
                    DrawText(rgbaBuffer, _config.ImageWidth, 
                        _config.PaddingLeft + 55, _config.PaddingTop + 20, "VP", PlotTextColor, 1);
                    DrawLine(rgbaBuffer, _config.ImageWidth, 
                        _config.PaddingLeft + 30, _config.PaddingTop + 40, 
                        _config.PaddingLeft + 50, _config.PaddingTop + 40, VsPlotColor, 2);
                    DrawText(rgbaBuffer, _config.ImageWidth, 
                        _config.PaddingLeft + 55, _config.PaddingTop + 35, "VS", PlotTextColor, 1);

                    string path = Path.Combine(_outputDirectory, "velocity_profile.png");
                    SavePng(path, _config.ImageWidth, _config.PlotHeight, rgbaBuffer);
                    return path;
                }, token);
            }
            
            public async Task<string> RenderWaveformAsync(float[] waveform, float duration, CancellationToken token)
            {
                return await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var rgbaBuffer = new byte[_config.ImageWidth * _config.PlotHeight * 4];
                    DrawFilledRect(rgbaBuffer, _config.ImageWidth, 0, 0, _config.ImageWidth, _config.PlotHeight, PlotBgColor);

                    if (waveform == null || waveform.Length == 0)
                    {
                        DrawText(rgbaBuffer, _config.ImageWidth, _config.ImageWidth / 2 - 100, _config.PlotHeight / 2, 
                            "NO DATA TO DISPLAY", PlotTextColor, 2);
                        string errorPath = Path.Combine(_outputDirectory, "waveform_error.png");
                        SavePng(errorPath, _config.ImageWidth, _config.PlotHeight, rgbaBuffer);
                        return errorPath;
                    }

                    float yMin = waveform.Min();
                    float yMax = waveform.Max();
                    if (Math.Abs(yMax - yMin) < 1e-9f) 
                    {
                        yMax = yMin + 1e-9f;
                    }

                    // Draw grid and axes
                    DrawPlotAxesAndGrid(rgbaBuffer, _config.ImageWidth, _config.PlotHeight, waveform.Length, 
                        yMin, yMax, $"Time (0-{duration*1000:F2} ms)", "Amplitude", "Waveform");

                    // Draw line
                    DrawLinePlot(rgbaBuffer, _config.ImageWidth, _config.PlotHeight, waveform.ToList(), 
                        yMin, yMax, WaveformPlotColor);

                    string path = Path.Combine(_outputDirectory, "waveform.png");
                    SavePng(path, _config.ImageWidth, _config.PlotHeight, rgbaBuffer);
                    return path;
                }, token);
            }

            #region Graphics Primitives
            private struct Color 
            {
                public byte R, G, B, A;
                public Color(byte r, byte g, byte b, byte a) { R = r; G = g; B = b; A = a; }
            }

            private void SetPixel(byte[] rgbaBuffer, int imgWidth, int x, int y, Color color)
            {
                if (x < 0 || x >= imgWidth || y < 0 || y >= _config.PlotHeight) return;
                int index = (y * imgWidth + x) * 4;
                if (index < 0 || index >= rgbaBuffer.Length - 3) return;
                rgbaBuffer[index] = color.R;
                rgbaBuffer[index + 1] = color.G;
                rgbaBuffer[index + 2] = color.B;
                rgbaBuffer[index + 3] = color.A;
            }

            private void DrawLine(byte[] rgbaBuffer, int imgWidth, int x0, int y0, int x1, int y1, Color color, int thickness = 1)
            {
                if (thickness == 1)
                {
                    DrawBresenhamLine(rgbaBuffer, imgWidth, x0, y0, x1, y1, color);
                }
                else
                {
                    // Draw multiple lines for thickness
                    for (int t = 0; t < thickness; t++)
                    {
                        int offset = t - thickness / 2;
                        // Determine if line is more horizontal or vertical
                        if (Math.Abs(x1 - x0) > Math.Abs(y1 - y0))
                        {
                            // More horizontal - offset vertically
                            DrawBresenhamLine(rgbaBuffer, imgWidth, x0, y0 + offset, x1, y1 + offset, color);
                        }
                        else
                        {
                            // More vertical - offset horizontally
                            DrawBresenhamLine(rgbaBuffer, imgWidth, x0 + offset, y0, x1 + offset, y1, color);
                        }
                    }
                }
            }

            private void DrawBresenhamLine(byte[] rgbaBuffer, int imgWidth, int x0, int y0, int x1, int y1, Color color)
            {
                int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
                int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
                int err = dx + dy, e2;
                
                while (true) 
                {
                    SetPixel(rgbaBuffer, imgWidth, x0, y0, color);
                    if (x0 == x1 && y0 == y1) break;
                    e2 = 2 * err;
                    if (e2 >= dy) { err += dy; x0 += sx; }
                    if (e2 <= dx) { err += dx; y0 += sy; }
                }
            }

            private void DrawFilledRect(byte[] rgbaBuffer, int imgWidth, int x, int y, int w, int h, Color color)
            {
                for (int j = y; j < y + h && j < _config.PlotHeight; j++)
                {
                    for (int i = x; i < x + w && i < imgWidth; i++)
                    {
                        SetPixel(rgbaBuffer, imgWidth, i, j, color);
                    }
                }
            }

            private void DrawText(byte[] rgbaBuffer, int imgWidth, int x, int y, string text, Color color, int scale = 1)
            {
                int currentX = x;
                foreach (char c in text)
                {
                    char upperC = char.ToUpper(c);
                    if (SimpleFont.FontMap.TryGetValue(upperC, out var pattern))
                    {
                        for (int py = 0; py < SimpleFont.CharHeight; py++)
                        {
                            for (int px = 0; px < SimpleFont.CharWidth; px++)
                            {
                                if (pattern[py, px])
                                {
                                    if (scale == 1) 
                                    {
                                        SetPixel(rgbaBuffer, imgWidth, currentX + px, y + py, color);
                                    }
                                    else 
                                    {
                                        DrawFilledRect(rgbaBuffer, imgWidth, currentX + px * scale, y + py * scale, scale, scale, color);
                                    }
                                }
                            }
                        }
                        currentX += (SimpleFont.CharWidth + SimpleFont.CharSpacing) * scale;
                    }
                    else if (c == ' ')
                    {
                        currentX += (SimpleFont.CharWidth + SimpleFont.CharSpacing) * scale;
                    }
                }
            }

            private void DrawTextVertical(byte[] rgbaBuffer, int imgWidth, int x, int y, string text, Color color, int scale = 1)
            {
                int currentY = y;
                foreach (char c in text)
                {
                    char upperC = char.ToUpper(c);
                    if (SimpleFont.FontMap.TryGetValue(upperC, out var pattern))
                    {
                        for (int py = 0; py < SimpleFont.CharHeight; py++)
                        {
                            for (int px = 0; px < SimpleFont.CharWidth; px++)
                            {
                                if (pattern[py, px])
                                {
                                    if (scale == 1) 
                                    {
                                        SetPixel(rgbaBuffer, imgWidth, x + px, currentY + py, color);
                                    }
                                    else 
                                    {
                                        DrawFilledRect(rgbaBuffer, imgWidth, x + px * scale, currentY + py * scale, scale, scale, color);
                                    }
                                }
                            }
                        }
                        currentY += (SimpleFont.CharHeight + SimpleFont.CharSpacing) * scale;
                    }
                    else if (c == ' ')
                    {
                        currentY += (SimpleFont.CharHeight + SimpleFont.CharSpacing) * scale;
                    }
                }
            }

            private void DrawPlotAxesAndGrid(byte[] rgbaBuffer, int imgWidth, int imgHeight, int xDataCount, 
                float yMin, float yMax, string xLabel, string yLabel, string title)
            {
                int plotWidth = imgWidth - _config.PaddingLeft - _config.PaddingRight;
                int plotHeight = imgHeight - _config.PaddingTop - _config.PaddingBottom;

                // Title
                int titleX = imgWidth / 2 - (title.Length * 6 * 2) / 2;
                DrawText(rgbaBuffer, imgWidth, titleX, 10, title, PlotTextColor, 2);

                // Y Axis
                DrawLine(rgbaBuffer, imgWidth, _config.PaddingLeft, _config.PaddingTop, 
                    _config.PaddingLeft, _config.PaddingTop + plotHeight, PlotGridColor);
                DrawTextVertical(rgbaBuffer, imgWidth, 10, imgHeight / 2 - (yLabel.Length * 7) / 2, yLabel, PlotTextColor, 1);
                
                for (int i = 0; i <= 5; i++)
                {
                    float val = yMin + (yMax - yMin) * (i / 5.0f);
                    int y = _config.PaddingTop + plotHeight - (int)(i / 5.0f * plotHeight);
                    DrawLine(rgbaBuffer, imgWidth, _config.PaddingLeft - 5, y, _config.PaddingLeft, y, PlotGridColor);
                    DrawLine(rgbaBuffer, imgWidth, _config.PaddingLeft, y, _config.PaddingLeft + plotWidth, y, 
                        new Color(50, 50, 50, 255));
                    
                    string label = $"{val:G3}";
                    DrawText(rgbaBuffer, imgWidth, _config.PaddingLeft - 60, y - 4, label, PlotTextColor, 1);
                }

                // X Axis
                DrawLine(rgbaBuffer, imgWidth, _config.PaddingLeft, _config.PaddingTop + plotHeight, 
                    _config.PaddingLeft + plotWidth, _config.PaddingTop + plotHeight, PlotGridColor);
                int xLabelX = imgWidth / 2 - (xLabel.Length * 6) / 2;
                DrawText(rgbaBuffer, imgWidth, xLabelX, imgHeight - 20, xLabel, PlotTextColor, 1);
                
                for (int i = 0; i <= 5; i++)
                {
                    int x = _config.PaddingLeft + (int)(i / 5.0f * plotWidth);
                    float val = xDataCount * (i / 5.0f);
                    DrawLine(rgbaBuffer, imgWidth, x, _config.PaddingTop + plotHeight, x, 
                        _config.PaddingTop + plotHeight + 5, PlotGridColor);
                    DrawLine(rgbaBuffer, imgWidth, x, _config.PaddingTop, x, _config.PaddingTop + plotHeight, 
                        new Color(50, 50, 50, 255));
                    
                    string label = $"{val:F0}";
                    DrawText(rgbaBuffer, imgWidth, x - 10, _config.PaddingTop + plotHeight + 10, label, PlotTextColor, 1);
                }
            }

            private void DrawLinePlot(byte[] rgbaBuffer, int imgWidth, int imgHeight, List<float> data, 
                float yMin, float yMax, Color color)
            {
                if (data == null || data.Count < 2) return;
                
                int plotWidth = imgWidth - _config.PaddingLeft - _config.PaddingRight;
                int plotHeight = imgHeight - _config.PaddingTop - _config.PaddingBottom;
                float yRange = yMax - yMin;
                if (Math.Abs(yRange) < 1e-9f) yRange = 1e-9f;

                int lastX = -1, lastY = -1;
                for (int i = 0; i < data.Count; i++)
                {
                    float x_norm = (float)i / Math.Max(1, data.Count - 1);
                    float y_norm = (data[i] - yMin) / yRange;
                    y_norm = Math.Max(0, Math.Min(1, y_norm)); // Clamp
                    
                    int x = _config.PaddingLeft + (int)(x_norm * plotWidth);
                    int y = _config.PaddingTop + plotHeight - (int)(y_norm * plotHeight);
                    
                    if (lastX != -1)
                    {
                        DrawLine(rgbaBuffer, imgWidth, lastX, lastY, x, y, color, _config.LineThickness);
                    }
                    lastX = x;
                    lastY = y;
                }
            }
            #endregion

            #region Data and I/O Helpers
            private (int width, int height) GetSliceDimensions(ChunkedVolume vol, char axis) 
            {
                return axis switch 
                {
                    'X' => (vol.Height, vol.Depth), 
                    'Y' => (vol.Width, vol.Depth), 
                    _ => (vol.Width, vol.Height)
                };
            }

            private byte[] ExtractSlice(ChunkedVolume vol, char axis, int index, int w, int h) 
            {
                var data = new byte[w * h];
                try 
                {
                    if (axis == 'Z') 
                    {
                        vol.ReadSliceZ(index, data);
                    }
                    else if (axis == 'Y') 
                    {
                        for (int z = 0; z < h; z++)
                        {
                            for(int x = 0; x < w; x++)
                            {
                                data[z * w + x] = vol[x, index, z];
                            }
                        }
                    }
                    else if (axis == 'X') 
                    {
                        for (int z = 0; z < h; z++)
                        {
                            for(int y = 0; y < w; y++)
                            {
                                data[z * w + y] = vol[index, y, z];
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[ReportImageRenderer] Failed to extract slice: {ex.Message}");
                    // Return gray pattern on error
                    for (int i = 0; i < data.Length; i++)
                    {
                        data[i] = 128;
                    }
                }
                return data;
            }

            private void SavePng(string path, int w, int h, byte[] rgbaData) 
            {
                try
                {
                    using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write)) 
                    {
                        var writer = new ImageWriter();
                        writer.WritePng(rgbaData, w, h, ColorComponents.RedGreenBlueAlpha, stream);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[ReportImageRenderer] Failed to save PNG: {ex.Message}");
                }
            }

            private string CreateErrorImage(string message)
            {
                int w = 400, h = 100;
                byte[] rgbaData = new byte[w * h * 4];
                DrawFilledRect(rgbaData, w, 0, 0, w, h, new Color(50, 50, 50, 255));
                DrawText(rgbaData, w, 10, h/2 - 5, message, new Color(255, 100, 100, 255), 1);
                string path = Path.Combine(_outputDirectory, $"error_{DateTime.Now.Ticks}.png");
                SavePng(path, w, h, rgbaData);
                return path;
            }
            #endregion
            
            #region Colormaps
            private Vector4 GetJetColor(float v) 
            {
                v = Math.Clamp(v, 0.0f, 1.0f);
                if (v < 0.125f) return new Vector4(0, 0, 0.5f + 4 * v, 1);
                else if (v < 0.375f) return new Vector4(0, 4 * (v - 0.125f), 1, 1);
                else if (v < 0.625f) return new Vector4(4 * (v - 0.375f), 1, 1 - 4 * (v - 0.375f), 1);
                else if (v < 0.875f) return new Vector4(1, 1 - 4 * (v - 0.625f), 0, 1);
                else return new Vector4(1 - 4 * (v - 0.875f), 0, 0, 1);
            }
            
            private Vector4 GetHotColor(float v) 
            {
                v = Math.Clamp(v, 0.0f, 1.0f);
                float r = Math.Clamp(v / 0.4f, 0, 1);
                float g = Math.Clamp((v - 0.4f) / 0.4f, 0, 1);
                float b = Math.Clamp((v - 0.8f) / 0.2f, 0, 1);
                return new Vector4(r, g, b, 1);
            }
            #endregion

            #region Enhanced Font System
            private static class SimpleFont 
            {
                public const int CharWidth = 5, CharHeight = 7, CharSpacing = 1;
                public static readonly Dictionary<char, bool[,]> FontMap = new Dictionary<char, bool[,]> 
                {
                    // Uppercase letters
                    {'A',new bool[7,5]{{false,true,true,true,false},{true,false,false,false,true},{true,true,true,true,true},{true,false,false,false,true},{true,false,false,false,true},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'B',new bool[7,5]{{true,true,true,true,false},{true,false,false,false,true},{true,true,true,true,false},{true,false,false,false,true},{true,true,true,true,false},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'C',new bool[7,5]{{false,true,true,true,false},{true,false,false,false,true},{true,false,false,false,false},{true,false,false,false,true},{false,true,true,true,false},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'D',new bool[7,5]{{true,true,true,true,false},{true,false,false,false,true},{true,false,false,false,true},{true,false,false,false,true},{true,true,true,true,false},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'E',new bool[7,5]{{true,true,true,true,true},{true,false,false,false,false},{true,true,true,true,false},{true,false,false,false,false},{true,true,true,true,true},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'F',new bool[7,5]{{true,true,true,true,true},{true,false,false,false,false},{true,true,true,true,false},{true,false,false,false,false},{true,false,false,false,false},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'G',new bool[7,5]{{false,true,true,true,false},{true,false,false,false,false},{true,false,true,true,true},{true,false,false,false,true},{false,true,true,true,false},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'H',new bool[7,5]{{true,false,false,false,true},{true,false,false,false,true},{true,true,true,true,true},{true,false,false,false,true},{true,false,false,false,true},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'I',new bool[7,5]{{false,true,true,true,false},{false,false,true,false,false},{false,false,true,false,false},{false,false,true,false,false},{false,true,true,true,false},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'J',new bool[7,5]{{false,false,false,true,true},{false,false,false,false,true},{false,false,false,false,true},{true,false,false,false,true},{false,true,true,true,false},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'K',new bool[7,5]{{true,false,false,true,false},{true,false,true,false,false},{true,true,false,false,false},{true,false,true,false,false},{true,false,false,true,false},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'L',new bool[7,5]{{true,false,false,false,false},{true,false,false,false,false},{true,false,false,false,false},{true,false,false,false,false},{true,true,true,true,true},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'M',new bool[7,5]{{true,false,false,false,true},{true,true,false,true,true},{true,false,true,false,true},{true,false,false,false,true},{true,false,false,false,true},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'N',new bool[7,5]{{true,false,false,false,true},{true,true,false,false,true},{true,false,true,false,true},{true,false,false,true,true},{true,false,false,false,true},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'O',new bool[7,5]{{false,true,true,true,false},{true,false,false,false,true},{true,false,false,false,true},{true,false,false,false,true},{false,true,true,true,false},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'P',new bool[7,5]{{true,true,true,true,false},{true,false,false,false,true},{true,true,true,true,false},{true,false,false,false,false},{true,false,false,false,false},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'Q',new bool[7,5]{{false,true,true,true,false},{true,false,false,false,true},{true,false,true,false,true},{true,false,false,true,false},{false,true,true,false,true},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'R',new bool[7,5]{{true,true,true,true,false},{true,false,false,false,true},{true,true,true,true,false},{true,false,false,true,false},{true,false,false,false,true},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'S',new bool[7,5]{{false,true,true,true,true},{true,false,false,false,false},{false,true,true,true,false},{false,false,false,false,true},{true,true,true,true,false},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'T',new bool[7,5]{{true,true,true,true,true},{false,false,true,false,false},{false,false,true,false,false},{false,false,true,false,false},{false,false,true,false,false},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'U',new bool[7,5]{{true,false,false,false,true},{true,false,false,false,true},{true,false,false,false,true},{true,false,false,false,true},{false,true,true,true,false},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'V',new bool[7,5]{{true,false,false,false,true},{true,false,false,false,true},{false,true,false,true,false},{false,true,false,true,false},{false,false,true,false,false},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'W',new bool[7,5]{{true,false,false,false,true},{true,false,false,false,true},{true,false,true,false,true},{true,true,false,true,true},{true,false,false,false,true},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'X',new bool[7,5]{{true,false,false,false,true},{false,true,false,true,false},{false,false,true,false,false},{false,true,false,true,false},{true,false,false,false,true},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'Y',new bool[7,5]{{true,false,false,false,true},{false,true,false,true,false},{false,false,true,false,false},{false,false,true,false,false},{false,false,true,false,false},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'Z',new bool[7,5]{{true,true,true,true,true},{false,false,false,true,false},{false,false,true,false,false},{false,true,false,false,false},{true,true,true,true,true},{false,false,false,false,false},{false,false,false,false,false}}},
                    
                    // Numbers
                    {'0',new bool[7,5]{{false,true,true,true,false},{true,false,false,true,true},{true,false,true,false,true},{true,true,false,false,true},{false,true,true,true,false},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'1',new bool[7,5]{{false,false,true,false,false},{false,true,true,false,false},{false,false,true,false,false},{false,false,true,false,false},{false,true,true,true,false},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'2',new bool[7,5]{{false,true,true,true,false},{true,false,false,false,true},{false,false,false,true,false},{false,false,true,false,false},{true,true,true,true,true},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'3',new bool[7,5]{{false,true,true,true,false},{false,false,false,false,true},{false,false,true,true,false},{false,false,false,false,true},{false,true,true,true,false},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'4',new bool[7,5]{{false,false,true,true,false},{false,true,false,true,false},{true,false,false,true,false},{true,true,true,true,true},{false,false,false,true,false},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'5',new bool[7,5]{{true,true,true,true,true},{true,false,false,false,false},{true,true,true,true,false},{false,false,false,false,true},{true,true,true,true,false},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'6',new bool[7,5]{{false,true,true,true,false},{true,false,false,false,false},{true,true,true,true,false},{true,false,false,false,true},{false,true,true,true,false},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'7',new bool[7,5]{{true,true,true,true,true},{false,false,false,true,false},{false,false,true,false,false},{false,true,false,false,false},{false,true,false,false,false},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'8',new bool[7,5]{{false,true,true,true,false},{true,false,false,false,true},{false,true,true,true,false},{true,false,false,false,true},{false,true,true,true,false},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'9',new bool[7,5]{{false,true,true,true,false},{true,false,false,false,true},{false,true,true,true,true},{false,false,false,false,true},{false,true,true,true,false},{false,false,false,false,false},{false,false,false,false,false}}},
                    
                    // Special characters
                    {'.',new bool[7,5]{{false,false,false,false,false},{false,false,false,false,false},{false,false,false,false,false},{false,false,false,false,false},{false,true,true,false,false},{false,true,true,false,false},{false,false,false,false,false}}},
                    {':',new bool[7,5]{{false,false,false,false,false},{false,true,true,false,false},{false,true,true,false,false},{false,false,false,false,false},{false,true,true,false,false},{false,true,true,false,false},{false,false,false,false,false}}},
                    {',',new bool[7,5]{{false,false,false,false,false},{false,false,false,false,false},{false,false,false,false,false},{false,false,false,false,false},{false,false,true,false,false},{false,false,true,false,false},{false,true,false,false,false}}},
                    {';',new bool[7,5]{{false,false,false,false,false},{false,false,true,false,false},{false,false,true,false,false},{false,false,false,false,false},{false,false,true,false,false},{false,false,true,false,false},{false,true,false,false,false}}},
                    {'!',new bool[7,5]{{false,false,true,false,false},{false,false,true,false,false},{false,false,true,false,false},{false,false,true,false,false},{false,false,false,false,false},{false,false,true,false,false},{false,false,false,false,false}}},
                    {'?',new bool[7,5]{{false,true,true,true,false},{true,false,false,false,true},{false,false,false,true,false},{false,false,true,false,false},{false,false,false,false,false},{false,false,true,false,false},{false,false,false,false,false}}},
                    {'-',new bool[7,5]{{false,false,false,false,false},{false,false,false,false,false},{true,true,true,true,true},{false,false,false,false,false},{false,false,false,false,false},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'+',new bool[7,5]{{false,false,false,false,false},{false,false,true,false,false},{false,false,true,false,false},{true,true,true,true,true},{false,false,true,false,false},{false,false,true,false,false},{false,false,false,false,false}}},
                    {'=',new bool[7,5]{{false,false,false,false,false},{false,false,false,false,false},{true,true,true,true,true},{false,false,false,false,false},{true,true,true,true,true},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'*',new bool[7,5]{{false,false,false,false,false},{false,true,false,true,false},{false,false,true,false,false},{true,true,true,true,true},{false,false,true,false,false},{false,true,false,true,false},{false,false,false,false,false}}},
                    {'/',new bool[7,5]{{false,false,false,false,true},{false,false,false,true,false},{false,false,true,false,false},{false,true,false,false,false},{true,false,false,false,false},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'(',new bool[7,5]{{false,false,true,true,false},{false,true,false,false,false},{false,true,false,false,false},{false,true,false,false,false},{false,false,true,true,false},{false,false,false,false,false},{false,false,false,false,false}}},
                    {')',new bool[7,5]{{false,true,true,false,false},{false,false,false,true,false},{false,false,false,true,false},{false,false,false,true,false},{false,true,true,false,false},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'[',new bool[7,5]{{false,true,true,true,false},{false,true,false,false,false},{false,true,false,false,false},{false,true,false,false,false},{false,true,true,true,false},{false,false,false,false,false},{false,false,false,false,false}}},
                    {']',new bool[7,5]{{false,true,true,true,false},{false,false,false,true,false},{false,false,false,true,false},{false,false,false,true,false},{false,true,true,true,false},{false,false,false,false,false},{false,false,false,false,false}}},
                    {'_',new bool[7,5]{{false,false,false,false,false},{false,false,false,false,false},{false,false,false,false,false},{false,false,false,false,false},{false,false,false,false,false},{true,true,true,true,true},{false,false,false,false,false}}},
                    {' ',new bool[7,5]{{false,false,false,false,false},{false,false,false,false,false},{false,false,false,false,false},{false,false,false,false,false},{false,false,false,false,false},{false,false,false,false,false},{false,false,false,false,false}}}
                };

                static SimpleFont()
                {
                    // Add lowercase letters (mapped to uppercase for simplicity)
                    for (char c = 'a'; c <= 'z'; c++)
                    {
                        char upperC = char.ToUpper(c);
                        if (FontMap.ContainsKey(upperC))
                        {
                            FontMap[c] = FontMap[upperC];
                        }
                    }
                }
            }
            #endregion
        }
        #endregion
    }
    
    /// <summary>
    /// Contains pure, static methods for performing analysis calculations.
    /// Decoupled from UI state for reusability.
    /// </summary>
    public static class AcousticAnalysisLogic
    {
        public static string GetSummaryReport(AcousticVolumeDataset ad)
        {
            var sb = new StringBuilder();
            sb.AppendLine("### Simulation Parameters");
            sb.AppendLine("| Parameter | Value |");
            sb.AppendLine("|---|---|");
            sb.AppendLine($"| P-Wave Velocity | {ad.PWaveVelocity:F2} m/s |");
            sb.AppendLine($"| S-Wave Velocity | {ad.SWaveVelocity:F2} m/s |");
            sb.AppendLine($"| Vp/Vs Ratio | {ad.VpVsRatio:F3} |");
            sb.AppendLine($"| Source Frequency | {ad.SourceFrequencyKHz:F1} kHz |");
            sb.AppendLine($"| Computation Time | {ad.ComputationTime:g} |");
            sb.AppendLine();
            sb.AppendLine("### Material Properties (Input)");
            sb.AppendLine("| Property | Value |");
            sb.AppendLine("|---|---|");
            sb.AppendLine($"| Young's Modulus | {ad.YoungsModulusMPa:F0} MPa |");
            sb.AppendLine($"| Poisson's Ratio | {ad.PoissonRatio:F3} |");
            sb.AppendLine($"| Confining Pressure | {ad.ConfiningPressureMPa:F1} MPa |");
            if (ad.DensityData != null)
            {
                var avgDensity = ad.DensityData.GetMeanDensity();
                sb.AppendLine($"| Avg. Calibrated Density | {avgDensity:F0} kg/m³ |");
            }
            sb.AppendLine();
            return sb.ToString();
        }

        public static string GetWaveStatisticsReport(AcousticVolumeDataset ad)
        {
            var sb = new StringBuilder();
            sb.AppendLine("| Wave Field | Min | Max | Mean | Std. Dev. |");
            sb.AppendLine("|---|---|---|---|---|");
            if (ad.PWaveField != null) sb.AppendLine(GetStatsTableRow("P-Wave", ad.PWaveField));
            if (ad.SWaveField != null) sb.AppendLine(GetStatsTableRow("S-Wave", ad.SWaveField));
            if (ad.CombinedWaveField != null) sb.AppendLine(GetStatsTableRow("Combined", ad.CombinedWaveField));
            sb.AppendLine();
            return sb.ToString();
        }
        
        public static string GetDamageStatisticsReport(AcousticVolumeDataset ad)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Damage field values range from 0 (no damage) to 255 (max damage).");
            sb.AppendLine();
            sb.AppendLine("| Statistic | Value |");
            sb.AppendLine("|---|---|");
            
            var (min, max, mean, stdDev, count) = CalculateVolumeStats(ad.DamageField);
            float damagedVolume = (float)count.countDamaged / count.totalVoxels;

            sb.AppendLine($"| Minimum Damage | {min} |");
            sb.AppendLine($"| Maximum Damage | {max} |");
            sb.AppendLine($"| Mean Damage | {mean:F2} |");
            sb.AppendLine($"| Damaged Volume (>50%) | {damagedVolume:P2} |");
            sb.AppendLine();
            return sb.ToString();
        }

        public static async Task<string> GetFractureOrientationReport(AcousticVolumeDataset ad, int threshold = 128, int minClusterSize = 100)
        {
             var planes = await Task.Run(() =>
             {
                 var analyzer = new DamageAnalysisTool();
                 return analyzer.AnalyzeFractureOrientations_Internal(ad.DamageField, threshold, minClusterSize, null);
             });

            if (planes == null || planes.Count == 0)
            {
                return "No significant fracture clusters were identified with the current settings (Threshold > 128, Min. Size > 100 voxels).\n";
            }

            var sb = new StringBuilder();
            sb.AppendLine("### Major Fracture Planes");
            sb.AppendLine("The following table lists the largest identified fracture clusters and their principal orientation, calculated via PCA.");
            sb.AppendLine();
            sb.AppendLine("| Cluster Size (voxels) | Centroid (X,Y,Z) | Dip (°) | Azimuth (°) |");
            sb.AppendLine("|---|---|---|---|");

            foreach (var plane in planes.Take(10)) // Report top 10
            {
                sb.AppendLine($"| {plane.Size} | ({plane.Centroid.X:F0}, {plane.Centroid.Y:F0}, {plane.Centroid.Z:F0}) | {plane.Dip:F1} | {plane.Azimuth:F1} |");
            }
            sb.AppendLine();
            return sb.ToString();
        }

        public static async Task<(string report, List<float> vpData, List<float> vsData)> GetVelocityProfileReport(AcousticVolumeDataset ad)
        {
            var (vpData, vsData) = await Task.Run(() =>
            {
                var tool = new VelocityProfileTool();
                return tool.CalculateProfile_Internal(ad.DensityData);
            });

            if (vpData == null || vpData.Count == 0)
            {
                return ("Could not extract velocity data from the selected line.\n", null, null);
            }

            float avgVp = vpData.Average();
            float avgVs = vsData.Average();
            float avgVpVs = avgVs > 0 ? avgVp / avgVs : 0;
            
            var sb = new StringBuilder();
            sb.AppendLine("Velocity profile calculated along the user-defined line from the calibrated density volume.");
            sb.AppendLine();
            sb.AppendLine("| Statistic | Value |");
            sb.AppendLine("|---|---|");
            sb.AppendLine($"| Points Sampled | {vpData.Count} |");
            sb.AppendLine($"| Average Vp | {avgVp:F2} m/s |");
            sb.AppendLine($"| Average Vs | {avgVs:F2} m/s |");
            sb.AppendLine($"| Average Vp/Vs Ratio | {avgVpVs:F3} |");
            sb.AppendLine();
            
            return (sb.ToString(), vpData, vsData);
        }

        public static async Task<(string report, float[] waveform)> GetWaveformReport(AcousticVolumeDataset ad)
        {
            var waveform = await Task.Run(() => AcousticAnalysisLogic.ExtractWaveform_Internal(ad));
            if (waveform == null || waveform.Length == 0)
            {
                return ("Could not extract waveform data from the selected point.\n", null);
            }

            float maxAmp = 0;
            double sumOfSquares = 0;
            for(int i = 0; i < waveform.Length; i++)
            {
                float absVal = Math.Abs(waveform[i]);
                if (absVal > maxAmp) maxAmp = absVal;
                sumOfSquares += waveform[i] * waveform[i];
            }
            float rms = (float)Math.Sqrt(sumOfSquares / waveform.Length);
            float duration = ad.TimeSeriesSnapshots.Last().SimulationTime;

            var sb = new StringBuilder();
            sb.AppendLine("Waveform extracted from the user-selected point over the full simulation time.");
            sb.AppendLine();
            sb.AppendLine("| Statistic | Value |");
            sb.AppendLine("|---|---|");
            sb.AppendLine($"| Time Steps | {waveform.Length} |");
            sb.AppendLine($"| Duration | {duration * 1000:F3} ms |");
            sb.AppendLine($"| Peak Amplitude | {maxAmp:E3} |");
            sb.AppendLine($"| RMS Amplitude | {rms:E3} |");
            sb.AppendLine();

            return (sb.ToString(), waveform);
        }

        private static string GetStatsTableRow(string name, ChunkedVolume volume)
        {
            var (min, max, mean, stdDev, _) = CalculateVolumeStats(volume);
            return $"| {name} | {min} | {max} | {mean:F2} | {stdDev:F2} |";
        }

        private static (byte min, byte max, double mean, double stdDev, (long totalVoxels, long countDamaged) counts) CalculateVolumeStats(ChunkedVolume volume)
        {
            long count = 0;
            double sum = 0;
            double sumOfSquares = 0;
            byte min = 255;
            byte max = 0;
            long damagedCount = 0;

            for (int z = 0; z < volume.Depth; z++)
            {
                byte[] slice = new byte[volume.Width * volume.Height];
                volume.ReadSliceZ(z, slice);
                for (int i = 0; i < slice.Length; i++)
                {
                    byte val = slice[i];
                    sum += val;
                    sumOfSquares += val * val;
                    if (val < min) min = val;
                    if (val > max) max = val;
                    if (val > 128) damagedCount++;
                }
                count += slice.Length;
            }

            if (count == 0) return (0, 0, 0, 0, (0, 0));

            double mean = sum / count;
            double variance = (sumOfSquares / count) - (mean * mean);
            double stdDev = Math.Sqrt(variance);

            return (min, max, mean, stdDev, (count, damagedCount));
        }
        
        public static float[] ExtractWaveform_Internal(AcousticVolumeDataset ad)
        {
            var pt = AcousticInteractionManager.SelectedPoint;
            int x = (int)pt.X;
            int y = (int)pt.Y;
            int z = (int)pt.Z;

            var volume = ad.PWaveField ?? ad.CombinedWaveField;
            if (volume == null) return null;
            if (x < 0 || x >= volume.Width || y < 0 || y >= volume.Height || z < 0 || z >= volume.Depth) return null;

            int numTimeSteps = ad.TimeSeriesSnapshots.Count;
            float[] waveform = new float[numTimeSteps];

            for (int t = 0; t < numTimeSteps; t++)
            {
                var snapshot = ad.TimeSeriesSnapshots[t];
                var vx = snapshot.GetVelocityField(0);
                var vy = snapshot.GetVelocityField(1);
                var vz = snapshot.GetVelocityField(2);

                if (vx == null || vy == null || vz == null) continue;

                float valX = vx[x, y, z];
                float valY = vy[x, y, z];
                float valZ = vz[x, y, z];
                waveform[t] = MathF.Sqrt(valX * valX + valY * valY + valZ * valZ);
            }
            return waveform;
        }
    }
}