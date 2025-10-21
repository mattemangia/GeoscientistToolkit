// GeoscientistToolkit/Business/GIS/RasterTools/RasterCalculatorTool.cs

using System.Numerics;
using System.Text.RegularExpressions;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Business.GIS.RasterTools;

public class RasterCalculatorTool : IDatasetTools
{
    private readonly GeoScriptEngine _geoScriptEngine = new();
    private string _expression = "A * 2";
    private GISRasterLayer _rasterA;
    private GISRasterLayer _rasterB;

    public void Draw(Dataset dataset)
    {
        if (dataset is not GISDataset gisDataset) return;

        ImGui.TextWrapped(
            "Perform map algebra using a raster calculator. Use 'A' and 'B' to refer to the selected raster layers.");

        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35.0f);
            ImGui.TextUnformatted(
                @"How to use the Raster Calculator:

1. Select source rasters for 'A' and (optionally) 'B'.
   The rasters should ideally have the same dimensions.

2. Write a mathematical expression in the box below.
   - Use 'A' and 'B' to refer to the pixel values of the
     selected rasters.
   - Standard operators are supported: +, -, *, /

3. The calculation is performed for each pixel using a GeoScript
   command in the background.

4. A new raster layer will be created and added to a
   new dataset in the project.

Examples:
- ""A * 100"" -> Multiply every pixel in Raster A by 100.
- ""(A - B) / (A + B)"" -> Calculate a Normalized Difference Index.
- ""(A + B) / 2"" -> Average of the two rasters."
            );
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }

        var rasterLayers = gisDataset.Layers.OfType<GISRasterLayer>().ToList();
        if (rasterLayers.Count == 0)
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "No raster layers available in this dataset.");
            return;
        }

        // Initialize selections
        _rasterA ??= rasterLayers.FirstOrDefault();
        _rasterB ??= rasterLayers.Count > 1 ? rasterLayers[1] : rasterLayers.FirstOrDefault();

        // Raster A selector
        ImGui.Text("Raster A:");
        ImGui.SameLine();
        if (ImGui.BeginCombo("##RasterA", _rasterA?.Name ?? "Select..."))
        {
            foreach (var layer in rasterLayers)
                if (ImGui.Selectable(layer.Name, layer == _rasterA))
                    _rasterA = layer;

            ImGui.EndCombo();
        }

        // Raster B selector
        ImGui.Text("Raster B:");
        ImGui.SameLine();
        if (ImGui.BeginCombo("##RasterB", _rasterB?.Name ?? "Select..."))
        {
            foreach (var layer in rasterLayers)
                if (ImGui.Selectable(layer.Name, layer == _rasterB))
                    _rasterB = layer;

            ImGui.EndCombo();
        }

        ImGui.Separator();

        // Expression input
        ImGui.Text("Expression:");
        ImGui.InputText("##Expression", ref _expression, 256);
        ImGui.TextDisabled("Examples: A + B, (A - B) / (A + B), A * 100");

        ImGui.Separator();

        if (ImGui.Button("Calculate", new Vector2(120, 0))) PerformCalculation(gisDataset);
    }

    private async void PerformCalculation(GISDataset parentDataset)
    {
        if (_rasterA == null)
        {
            Logger.LogError("Raster A is not selected.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_expression))
        {
            Logger.LogError("Expression cannot be empty.");
            return;
        }

        // Using Regex to avoid replacing 'B' in words like 'ABS'. \b is a word boundary.
        if (Regex.IsMatch(_expression, @"\bB\b", RegexOptions.IgnoreCase) && _rasterB == null)
        {
            Logger.LogError("Expression uses Raster B, but it is not selected.");
            return;
        }

        if (_rasterB != null && (_rasterA.Width != _rasterB.Width || _rasterA.Height != _rasterB.Height))
            Logger.LogWarning(
                "Rasters have different dimensions. Calculation will proceed but may produce unexpected results.");

        try
        {
            // Prepare the GeoScript expression. Layer names must be in double quotes.
            // Use Regex.Replace with word boundaries (\b) to avoid partial replacements.
            var geoScriptExpression =
                Regex.Replace(_expression, @"\bA\b", $"\"{_rasterA.Name}\"", RegexOptions.IgnoreCase);
            if (_rasterB != null)
                geoScriptExpression = Regex.Replace(geoScriptExpression, @"\bB\b", $"\"{_rasterB.Name}\"",
                    RegexOptions.IgnoreCase);

            // Create a unique name for the new layer
            var safeExpressionName = _expression
                .Replace("\"", "")
                .Replace("'", "")
                .Replace("*", "mul")
                .Replace("/", "div")
                .Replace("+", "add")
                .Replace("-", "sub")
                .Replace(" ", "_");
            var newLayerName = $"Calc_{safeExpressionName}";

            var command = $"RASTERCALC '{newLayerName}' = {geoScriptExpression}";

            Logger.Log($"Executing GeoScript: {command}");

            // The engine call is async, so we await it.
            var resultDataset = await _geoScriptEngine.ExecuteAsync(command, parentDataset);

            if (resultDataset != null)
            {
                ProjectManager.Instance.AddDataset(resultDataset);
                Logger.Log($"Successfully created new raster dataset from calculation: {resultDataset.Name}");
            }
            else
            {
                Logger.LogError("Raster calculation script did not produce a result dataset.");
            }
        }
        catch (Exception ex)
        {
            // Catch exceptions from both parsing and execution
            Logger.LogError($"Raster calculation failed: {ex.Message}");
        }
    }
}