// GeoscientistToolkit/Analysis/Geomechanics/GeomechanicalExportManager.cs

using System.Numerics;
using System.Security;
using System.Text;
using System.Text.Json;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.UI;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using SkiaSharp;

namespace GeoscientistToolkit.Analysis.Geomechanics;

public class GeomechanicalExportManager : IDisposable
{
    private readonly ImGuiExportFileDialog _csvDialog;
    private readonly ImGuiExportFileDialog _exportDialog;
    private readonly ImGuiExportFileDialog _mohrDialog;
    private readonly ProgressBarDialog _progressDialog;

    public GeomechanicalExportManager()
    {
        _exportDialog = new ImGuiExportFileDialog("GeomechExport", "Export Geomechanical Results");
        _exportDialog.SetExtensions((".geomech", "Geomechanical Dataset"));

        _csvDialog = new ImGuiExportFileDialog("GeomechCSV", "Export CSV Data");
        _csvDialog.SetExtensions((".csv", "CSV File"));

        _mohrDialog = new ImGuiExportFileDialog("MohrExport", "Export Mohr Circle Plot");
        _mohrDialog.SetExtensions((".png", "PNG Image"), (".svg", "SVG Vector"));

        _progressDialog = new ProgressBarDialog("Exporting Results");
    }


    public void Dispose()
    {
        // No resources to dispose
    }

    public void DrawExportControls(GeomechanicalResults results, CtImageStackDataset sourceDataset)
    {
        if (results == null) return;

        ImGui.Text("Export Options:");
        ImGui.Spacing();

        if (ImGui.Button("Export Complete Dataset", new Vector2(-1, 0)))
        {
            var defaultName = $"{sourceDataset.Name}_Geomech_{DateTime.Now:yyyyMMdd_HHmmss}";
            _exportDialog.Open(defaultName);
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Export stress fields, damage, and all analysis data");

        if (ImGui.Button("Export Statistics CSV", new Vector2(-1, 0)))
        {
            var defaultName = $"{sourceDataset.Name}_Geomech_Stats_{DateTime.Now:yyyyMMdd_HHmmss}";
            _csvDialog.Open(defaultName);
        }

        if (ImGui.Button("Export Mohr Circle Plot", new Vector2(-1, 0)))
        {
            var defaultName = $"{sourceDataset.Name}_MohrCircle_{DateTime.Now:yyyyMMdd_HHmmss}";
            _mohrDialog.Open(defaultName);
        }

        _exportDialog.Submit();
        _csvDialog.Submit();
        _mohrDialog.Submit();


        if (_exportDialog.Submit()) _ = ExportCompleteDatasetAsync(results, sourceDataset, _exportDialog.SelectedPath);

        if (_csvDialog.Submit()) ExportStatisticsCSV(results, _csvDialog.SelectedPath);

        if (_mohrDialog.Submit()) ExportMohrCirclePlot(results, _mohrDialog.SelectedPath);

        // The progress dialog is handled separately as it doesn't return a path.
        _progressDialog.Submit();
    }

    private async Task ExportCompleteDatasetAsync(GeomechanicalResults results,
        CtImageStackDataset sourceDataset, string path)
    {
        _progressDialog.Open("Exporting geomechanical dataset...");

        try
        {
            await Task.Run(() => ExportCompleteDataset(results, sourceDataset, path));
            Logger.Log($"[GeomechExport] Successfully exported to {path}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[GeomechExport] Export failed: {ex.Message}");
        }
        finally
        {
            _progressDialog.Close();
        }
    }

    private void ExportCompleteDataset(GeomechanicalResults results,
    CtImageStackDataset sourceDataset, string basePath)
{
    var datasetDir = Path.ChangeExtension(basePath, null);
    Directory.CreateDirectory(datasetDir);

    _progressDialog.Update(0.1f, "Exporting stress fields...");

    // Export stress tensor components
    ExportField(results.StressXX, Path.Combine(datasetDir, "StressXX.bin"));
    ExportField(results.StressYY, Path.Combine(datasetDir, "StressYY.bin"));
    ExportField(results.StressZZ, Path.Combine(datasetDir, "StressZZ.bin"));
    ExportField(results.StressXY, Path.Combine(datasetDir, "StressXY.bin"));
    ExportField(results.StressXZ, Path.Combine(datasetDir, "StressXZ.bin"));
    ExportField(results.StressYZ, Path.Combine(datasetDir, "StressYZ.bin"));

    _progressDialog.Update(0.3f, "Exporting principal stresses...");

    ExportField(results.Sigma1, Path.Combine(datasetDir, "Sigma1.bin"));
    ExportField(results.Sigma2, Path.Combine(datasetDir, "Sigma2.bin"));
    ExportField(results.Sigma3, Path.Combine(datasetDir, "Sigma3.bin"));

    _progressDialog.Update(0.5f, "Exporting failure data...");

    ExportField(results.FailureIndex, Path.Combine(datasetDir, "FailureIndex.bin"));
    ExportByteField(results.DamageField, Path.Combine(datasetDir, "DamageField.bin"));

    // Export geothermal fields if present
    if (results.TemperatureField != null)
    {
        _progressDialog.Update(0.6f, "Exporting geothermal data...");
        ExportField(results.TemperatureField, Path.Combine(datasetDir, "Temperature.bin"));
    }

    // Export fluid fields if present
    if (results.PressureField != null)
    {
        _progressDialog.Update(0.65f, "Exporting fluid pressure data...");
        ExportField(results.PressureField, Path.Combine(datasetDir, "Pressure.bin"));
        
        if (results.FluidVelocityX != null)
        {
            ExportField(results.FluidVelocityX, Path.Combine(datasetDir, "VelocityX.bin"));
            ExportField(results.FluidVelocityY, Path.Combine(datasetDir, "VelocityY.bin"));
            ExportField(results.FluidVelocityZ, Path.Combine(datasetDir, "VelocityZ.bin"));
        }
        
        if (results.FractureAperture != null)
        {
            ExportField(results.FractureAperture, Path.Combine(datasetDir, "FractureAperture.bin"));
        }
        
        if (results.EffectiveStressXX != null)
        {
            ExportField(results.EffectiveStressXX, Path.Combine(datasetDir, "EffectiveStressXX.bin"));
            ExportField(results.EffectiveStressYY, Path.Combine(datasetDir, "EffectiveStressYY.bin"));
            ExportField(results.EffectiveStressZZ, Path.Combine(datasetDir, "EffectiveStressZZ.bin"));
        }
    }

    _progressDialog.Update(0.7f, "Exporting metadata...");

    ExportMetadata(results, Path.Combine(datasetDir, "metadata.json"));

    _progressDialog.Update(0.9f, "Exporting statistics...");

    ExportStatisticsCSV(results, Path.Combine(datasetDir, "statistics.csv"));
    ExportMohrData(results, Path.Combine(datasetDir, "mohr_circles.csv"));
    
    // Export time series if present
    if (results.TimePoints != null && results.TimePoints.Count > 0)
    {
        ExportTimeSeriesData(results, Path.Combine(datasetDir, "fluid_timeseries.csv"));
    }
    
    // Export fracture network if present
    if (results.FractureNetwork != null && results.FractureNetwork.Count > 0)
    {
        ExportFractureNetwork(results, Path.Combine(datasetDir, "fracture_network.csv"));
    }

    _progressDialog.Update(1.0f, "Export complete!");
}
    private void ExportTimeSeriesData(GeomechanicalResults results, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Time_s,InjectionPressure_MPa,FlowRate_m3ps,FractureVolume_m3,EnergyExtraction_MW");
    
        for (var i = 0; i < results.TimePoints.Count; i++)
        {
            sb.Append($"{results.TimePoints[i]:F2},");
            sb.Append($"{(i < results.InjectionPressureHistory.Count ? results.InjectionPressureHistory[i] : 0):F4},");
            sb.Append($"{(i < results.FlowRateHistory.Count ? results.FlowRateHistory[i] : 0):F6},");
            sb.Append($"{(i < results.FractureVolumeHistory.Count ? results.FractureVolumeHistory[i] : 0):F6},");
            sb.AppendLine($"{(i < results.EnergyExtractionHistory.Count ? results.EnergyExtractionHistory[i] : 0):F4}");
        }
    
        File.WriteAllText(path, sb.ToString());
        Logger.Log($"[GeomechExport] Exported time series data to {path}");
    }

    private void ExportFractureNetwork(GeomechanicalResults results, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("StartX,StartY,StartZ,EndX,EndY,EndZ,Aperture_m,Permeability_m2,Pressure_Pa,Temperature_C,Connected");
    
        foreach (var segment in results.FractureNetwork)
        {
            sb.AppendLine($"{segment.Start.X:F6},{segment.Start.Y:F6},{segment.Start.Z:F6}," +
                          $"{segment.End.X:F6},{segment.End.Y:F6},{segment.End.Z:F6}," +
                          $"{segment.Aperture:E4},{segment.Permeability:E4}," +
                          $"{segment.Pressure:F2},{segment.Temperature:F2}," +
                          $"{segment.IsConnectedToInjection}");
        }
    
        File.WriteAllText(path, sb.ToString());
        Logger.Log($"[GeomechExport] Exported fracture network ({results.FractureNetwork.Count} segments) to {path}");
    }
    private void ExportField(float[,,] field, string path)
    {
        var w = field.GetLength(0);
        var h = field.GetLength(1);
        var d = field.GetLength(2);

        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(w);
        writer.Write(h);
        writer.Write(d);

        var buffer = new byte[field.Length * sizeof(float)];
        Buffer.BlockCopy(field, 0, buffer, 0, buffer.Length);
        writer.Write(buffer);
    }

    private void ExportByteField(byte[,,] field, string path)
    {
        var w = field.GetLength(0);
        var h = field.GetLength(1);
        var d = field.GetLength(2);

        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(w);
        writer.Write(h);
        writer.Write(d);

        var buffer = new byte[field.Length];
        Buffer.BlockCopy(field, 0, buffer, 0, buffer.Length);
        writer.Write(buffer);
    }

    private void ExportMetadata(GeomechanicalResults results, string path)
    {
        var metadata = new
        {
            ExportDate = DateTime.Now,
            ComputationTime = results.ComputationTime.TotalSeconds,
            Iterations = results.IterationsPerformed,
            results.Converged,
            MeanStress_MPa = results.MeanStress / 1e6f,
            MaxShearStress_MPa = results.MaxShearStress / 1e6f,
            results.TotalVoxels,
            results.FailedVoxels,
            FailurePercentage = results.FailedVoxelPercentage,
            Parameters = new
            {
                results.Parameters.LoadingMode,
                Sigma1_MPa = results.Parameters.Sigma1,
                Sigma2_MPa = results.Parameters.Sigma2,
                Sigma3_MPa = results.Parameters.Sigma3,
                YoungModulus_MPa = results.Parameters.YoungModulus,
                results.Parameters.PoissonRatio,
                Cohesion_MPa = results.Parameters.Cohesion,
                FrictionAngle_deg = results.Parameters.FrictionAngle,
                TensileStrength_MPa = results.Parameters.TensileStrength,
                results.Parameters.FailureCriterion
            }
        };

        var json = JsonSerializer.Serialize(metadata,
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

   private void ExportStatisticsCSV(GeomechanicalResults results, string path)
{
    var sb = new StringBuilder();
    sb.AppendLine("Property,Value,Unit");
    sb.AppendLine($"Computation Time,{results.ComputationTime.TotalSeconds:F2},seconds");
    sb.AppendLine($"Iterations,{results.IterationsPerformed},");
    sb.AppendLine($"Converged,{results.Converged},");
    sb.AppendLine($"Mean Stress,{results.MeanStress / 1e6f:F4},MPa");
    sb.AppendLine($"Max Shear Stress,{results.MaxShearStress / 1e6f:F4},MPa");
    sb.AppendLine($"Von Mises Stress (Mean),{results.VonMisesStress_Mean / 1e6f:F4},MPa");
    sb.AppendLine($"Von Mises Stress (Max),{results.VonMisesStress_Max / 1e6f:F4},MPa");
    sb.AppendLine($"Volumetric Strain,{results.VolumetricStrain:E4},");
    sb.AppendLine($"Total Voxels,{results.TotalVoxels},");
    sb.AppendLine($"Failed Voxels,{results.FailedVoxels},");
    sb.AppendLine($"Failure Percentage,{results.FailedVoxelPercentage:F2},%");

    // Geothermal statistics
    if (results.TemperatureField != null)
    {
        sb.AppendLine();
        sb.AppendLine("Geothermal Results");
        sb.AppendLine($"Average Thermal Gradient,{results.AverageThermalGradient:F2},°C/km");
        sb.AppendLine($"Geothermal Energy Potential,{results.GeothermalEnergyPotential:F2},MWh");
    }

    // Fluid injection statistics
    if (results.PressureField != null)
    {
        sb.AppendLine();
        sb.AppendLine("Hydraulic Fracturing Results");
        sb.AppendLine($"Breakdown Pressure,{results.BreakdownPressure:F2},MPa");
        sb.AppendLine($"Propagation Pressure,{results.PropagationPressure:F2},MPa");
        sb.AppendLine($"Peak Injection Pressure,{results.PeakInjectionPressure:F2},MPa");
        sb.AppendLine($"Min Fluid Pressure,{results.MinFluidPressure/1e6f:F2},MPa");
        sb.AppendLine($"Max Fluid Pressure,{results.MaxFluidPressure/1e6f:F2},MPa");
        sb.AppendLine($"Total Fluid Injected,{results.TotalFluidInjected:F4},m³");
        sb.AppendLine($"Total Fracture Volume,{results.TotalFractureVolume:F6},m³");
        sb.AppendLine($"Fracture Voxel Count,{results.FractureVoxelCount},");
        sb.AppendLine($"Fracture Network Segments,{results.FractureNetwork?.Count ?? 0},");
    }

    sb.AppendLine();
    sb.AppendLine("Applied Loading");
    sb.AppendLine($"Loading Mode,{results.Parameters.LoadingMode},");
    sb.AppendLine($"Sigma 1,{results.Parameters.Sigma1:F2},MPa");
    sb.AppendLine($"Sigma 2,{results.Parameters.Sigma2:F2},MPa");
    sb.AppendLine($"Sigma 3,{results.Parameters.Sigma3:F2},MPa");

    sb.AppendLine();
    sb.AppendLine("Material Properties");
    sb.AppendLine($"Young's Modulus,{results.Parameters.YoungModulus:F0},MPa");
    sb.AppendLine($"Poisson's Ratio,{results.Parameters.PoissonRatio:F4},");
    sb.AppendLine($"Cohesion,{results.Parameters.Cohesion:F2},MPa");
    sb.AppendLine($"Friction Angle,{results.Parameters.FrictionAngle:F2},degrees");
    sb.AppendLine($"Tensile Strength,{results.Parameters.TensileStrength:F2},MPa");
    sb.AppendLine($"Density,{results.Parameters.Density:F0},kg/m³");

    File.WriteAllText(path, sb.ToString());
    Logger.Log($"[GeomechExport] Exported statistics to {path}");
}
    private void ExportMohrData(GeomechanicalResults results, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Location,Position_X,Position_Y,Position_Z,Sigma1_MPa,Sigma2_MPa,Sigma3_MPa," +
                      "MaxShear_MPa,HasFailed,FailureAngle_deg");

        foreach (var circle in results.MohrCircles)
            sb.AppendLine($"{circle.Location},{circle.Position.X:F1},{circle.Position.Y:F1}," +
                          $"{circle.Position.Z:F1},{circle.Sigma1:F4},{circle.Sigma2:F4}," +
                          $"{circle.Sigma3:F4},{circle.MaxShearStress:F4},{circle.HasFailed}," +
                          $"{circle.FailureAngle:F2}");

        File.WriteAllText(path, sb.ToString());
        Logger.Log($"[GeomechExport] Exported Mohr circle data to {path}");
    }

    private void ExportMohrCirclePlot(GeomechanicalResults results, string path)
    {
        var extension = Path.GetExtension(path).ToLower();

        if (extension == ".png")
            ExportMohrCirclePNG(results, path);
        else if (extension == ".svg") ExportMohrCircleSVG(results, path);
    }

    private void ExportMohrCirclePNG(GeomechanicalResults results, string path)
    {
        const int width = 1200;
        const int height = 900;

        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        // Draw Mohr circles for each location
        var gridCols = Math.Min(3, results.MohrCircles.Count);
        var gridRows = (results.MohrCircles.Count + gridCols - 1) / gridCols;

        var cellWidth = width / gridCols;
        var cellHeight = height / gridRows;

        for (var i = 0; i < results.MohrCircles.Count; i++)
        {
            var circle = results.MohrCircles[i];
            var col = i % gridCols;
            var row = i / gridCols;

            var x = col * cellWidth;
            var y = row * cellHeight;

            DrawMohrCircleToCanvas(canvas, circle, results.Parameters,
                new SKRect(x, y, x + cellWidth, y + cellHeight));
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(path);
        data.SaveTo(stream);

        Logger.Log($"[GeomechExport] Exported Mohr circle plot to {path}");
    }

    private void DrawMohrCircleToCanvas(SKCanvas canvas, MohrCircleData circle,
        GeomechanicalParameters parameters, SKRect bounds)
    {
        var margin = 40f;
        var plotBounds = new SKRect(
            bounds.Left + margin,
            bounds.Top + margin,
            bounds.Right - margin,
            bounds.Bottom - margin);

        // Background
        canvas.DrawRect(plotBounds, new SKPaint { Color = SKColors.WhiteSmoke });

        // Axes
        var axisPaint = new SKPaint { Color = SKColors.Black, StrokeWidth = 2 };
        canvas.DrawLine(plotBounds.Left, plotBounds.Bottom, plotBounds.Right, plotBounds.Bottom, axisPaint);
        canvas.DrawLine(plotBounds.Left, plotBounds.Bottom, plotBounds.Left, plotBounds.Top, axisPaint);

        // Title
        var titlePaint = new SKPaint
            { Color = SKColors.Black, TextSize = 14, IsAntialias = true, TextAlign = SKTextAlign.Center };
        canvas.DrawText(circle.Location, plotBounds.MidX, bounds.Top + 25, titlePaint);

        // Draw Mohr circle
        var maxStress = Math.Max(Math.Abs(circle.Sigma1), Math.Abs(circle.Sigma3)) * 1.2f;
        if (maxStress < 1f) maxStress = 1f;

        float ToScreenX(float sigma)
        {
            return plotBounds.Left + (sigma + maxStress) / (2 * maxStress) * plotBounds.Width;
        }

        float ToScreenY(float tau)
        {
            // The Y-axis (shear) should scale relative to the X-axis to maintain a circular shape
            var stressRange = 2 * maxStress;
            var scale = plotBounds.Width / stressRange;
            return plotBounds.Bottom - tau * scale;
        }

        var center = new SKPoint(ToScreenX((circle.Sigma1 + circle.Sigma3) / 2), plotBounds.Bottom);
        var radius = ToScreenX(circle.Sigma1) - center.X;

        var circlePaint = new SKPaint
        {
            Color = SKColors.Blue,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            IsAntialias = true
        };

        canvas.DrawCircle(center.X, center.Y, radius, circlePaint);

        // Principal stress points
        var pointPaint = new SKPaint { Color = SKColors.Red, Style = SKPaintStyle.Fill };
        canvas.DrawCircle(ToScreenX(circle.Sigma1), plotBounds.Bottom, 5, pointPaint);
        canvas.DrawCircle(ToScreenX(circle.Sigma3), plotBounds.Bottom, 5, pointPaint);

        // Failure indicator
        if (circle.HasFailed)
        {
            var failPaint = new SKPaint { Color = SKColors.Red, TextSize = 12 };
            canvas.DrawText("FAILED", plotBounds.Left + 10, plotBounds.Top + 20, failPaint);
        }
    }

    private void ExportMohrCircleSVG(GeomechanicalResults results, string path)
    {
        const int width = 1200;
        const int height = 900;

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" " +
                      "font-family=\"sans-serif\" font-size=\"12px\">");
        sb.AppendLine($"<rect width=\"{width}\" height=\"{height}\" fill=\"white\"/>");

        // Layout grid for circles
        var gridCols = Math.Min(3, results.MohrCircles.Count);
        var gridRows = (results.MohrCircles.Count + gridCols - 1) / gridCols;
        var cellWidth = width / (float)gridCols;
        var cellHeight = height / (float)gridRows;

        for (var i = 0; i < results.MohrCircles.Count; i++)
        {
            var circle = results.MohrCircles[i];
            var col = i % gridCols;
            var row = i / gridCols;

            var x = col * cellWidth;
            var y = row * cellHeight;

            // Use a group transform to position each plot
            sb.AppendLine($"<g transform=\"translate({x:F1}, {y:F1})\">");
            DrawMohrCircleToSVG(sb, circle, results.Parameters, new SKRect(0, 0, cellWidth, cellHeight));
            sb.AppendLine("</g>");
        }

        sb.AppendLine("</svg>");

        File.WriteAllText(path, sb.ToString());
        Logger.Log($"[GeomechExport] Exported Mohr circle SVG to {path}");
    }

    private void DrawMohrCircleToSVG(StringBuilder sb, MohrCircleData circle,
        GeomechanicalParameters parameters, SKRect bounds)
    {
        var margin = 40f;
        var plotBounds = new SKRect(
            bounds.Left + margin,
            bounds.Top + margin,
            bounds.Right - margin,
            bounds.Bottom - margin);

        // Background and border
        sb.AppendLine($"<rect x=\"{plotBounds.Left}\" y=\"{plotBounds.Top}\" " +
                      $"width=\"{plotBounds.Width}\" height=\"{plotBounds.Height}\" " +
                      "fill=\"#f5f5f5\" stroke=\"#ccc\"/>");

        // Axes
        sb.AppendLine($"<line x1=\"{plotBounds.Left}\" y1=\"{plotBounds.Bottom}\" " +
                      $"x2=\"{plotBounds.Right}\" y2=\"{plotBounds.Bottom}\" stroke=\"black\" stroke-width=\"1.5\"/>");
        sb.AppendLine($"<line x1=\"{plotBounds.Left}\" y1=\"{plotBounds.Bottom}\" " +
                      $"x2=\"{plotBounds.Left}\" y2=\"{plotBounds.Top}\" stroke=\"black\" stroke-width=\"1.5\"/>");

        // Title
        sb.AppendLine($"<text x=\"{plotBounds.MidX}\" y=\"{bounds.Top + 25}\" " +
                      "text-anchor=\"middle\" font-size=\"14px\" font-weight=\"bold\">" +
                      $"{SecurityElement.Escape(circle.Location)}</text>");

        // Calculate coordinate transformation
        var maxNormalStress = Math.Max(Math.Abs(circle.Sigma1), Math.Abs(circle.Sigma3)) * 1.2f;
        if (maxNormalStress < 1f) maxNormalStress = 1f;
        var stressRange = 2 * maxNormalStress;
        var scale = plotBounds.Width / stressRange;

        float ToScreenX(float sigma)
        {
            return plotBounds.Left + (sigma - -maxNormalStress) / stressRange * plotBounds.Width;
        }

        float ToScreenY(float tau)
        {
            return plotBounds.Bottom - tau * scale;
        }

        // Draw Mohr circle
        var centerX = ToScreenX((circle.Sigma1 + circle.Sigma3) / 2);
        var centerY = plotBounds.Bottom;
        var radiusPixels = (circle.Sigma1 - circle.Sigma3) / 2 * scale;

        sb.AppendLine($"<circle cx=\"{centerX:F2}\" cy=\"{centerY:F2}\" r=\"{radiusPixels:F2}\" " +
                      "fill=\"none\" stroke=\"blue\" stroke-width=\"2\"/>");

        // Draw principal stress points
        sb.AppendLine($"<circle cx=\"{ToScreenX(circle.Sigma1):F2}\" cy=\"{centerY:F2}\" r=\"3\" fill=\"red\"/>");
        sb.AppendLine($"<text x=\"{ToScreenX(circle.Sigma1) + 5:F2}\" y=\"{centerY - 5:F2}\" fill=\"black\">σ1</text>");
        sb.AppendLine($"<circle cx=\"{ToScreenX(circle.Sigma3):F2}\" cy=\"{centerY:F2}\" r=\"3\" fill=\"red\"/>");
        sb.AppendLine(
            $"<text x=\"{ToScreenX(circle.Sigma3) - 15:F2}\" y=\"{centerY - 5:F2}\" fill=\"black\">σ3</text>");

        // Draw failure envelope (Mohr-Coulomb)
        var c = parameters.Cohesion;
        var phi = parameters.FrictionAngle * MathF.PI / 180f;
        var tanPhi = MathF.Tan(phi);

        var startSigma = -maxNormalStress;
        var endSigma = maxNormalStress;

        var startTau = c + startSigma * tanPhi;
        var endTau = c + endSigma * tanPhi;

        sb.AppendLine($"<line x1=\"{ToScreenX(startSigma):F2}\" y1=\"{ToScreenY(startTau):F2}\" " +
                      $"x2=\"{ToScreenX(endSigma):F2}\" y2=\"{ToScreenY(endTau):F2}\" " +
                      "stroke=\"red\" stroke-width=\"1.5\" stroke-dasharray=\"4\"/>");

        // Failure indicator
        if (circle.HasFailed)
            sb.AppendLine($"<text x=\"{plotBounds.Left + 10}\" y=\"{plotBounds.Top + 20}\" " +
                          "fill=\"red\" font-size=\"14px\" font-weight=\"bold\">FAILED</text>");
    }
}