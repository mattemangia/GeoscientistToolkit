// GeoscientistToolkit/UI/GIS/Tools/RasterCalculatorTool.cs

using System.Numerics;
using System.Text.RegularExpressions;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Business.GIS;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using NCalc;

namespace GeoscientistToolkit.UI.GIS.Tools;

/// <summary>
/// Raster calculator tool that allows mathematical operations on raster layers using GeoScript
/// </summary>
public class RasterCalculatorTool : IDatasetTools
{
    private readonly GeoScriptEngine _scriptEngine = new();
    private string _calculatorExpression = "";
    private string _outputLayerName = "Calculated";
    private int _selectedLayerIndex = 0;
    private string _lastError = "";
    private bool _isCalculating = false;
    private List<string> _quickFormulas = new()
    {
        "A + 100",
        "A * 2",
        "A - B",
        "(A + B) / 2",
        "sqrt(A^2 + B^2)",
        "log(A + 1)",
        "A > 100 ? 255 : 0",
        "sin(A * 3.14159 / 180)",
        "abs(A)"
    };
    
    private string _selectedQuickFormula = "";

    public void Draw(Dataset dataset)
    {
        if (dataset is not GISDataset gisDataset)
        {
            ImGui.TextDisabled("Raster calculator is only available for GIS datasets.");
            return;
        }

        ImGui.TextWrapped("Perform mathematical operations on raster layers using expressions.");
        
        // Add helpful tooltip
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.4f, 0.7f, 1.0f, 1.0f), "(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(400);
            ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.4f, 1.0f), "How to Use Raster Calculator:");
            ImGui.Separator();
            ImGui.Text("");
            ImGui.Text("1. Your raster layers are labeled A, B, C, etc.");
            ImGui.Text("2. Write a math expression using these letters");
            ImGui.Text("3. Use operators: + - * / ^ ( )");
            ImGui.Text("4. Use functions: sqrt() log() sin() cos() abs()");
            ImGui.Text("5. Click Calculate to create a new raster layer");
            ImGui.Text("");
            ImGui.TextColored(new Vector4(1.0f, 1.0f, 0.4f, 1.0f), "Quick Examples:");
            ImGui.BulletText("A + 100   (add 100 to all values)");
            ImGui.BulletText("A * 2     (double all values)");
            ImGui.BulletText("A - B     (difference between layers)");
            ImGui.BulletText("(A + B) / 2   (average two layers)");
            ImGui.BulletText("sqrt(A^2 + B^2)   (magnitude)");
            ImGui.BulletText("A > 100 ? 255 : 0   (threshold)");
            ImGui.Text("");
            ImGui.TextColored(new Vector4(1.0f, 0.5f, 0.5f, 1.0f), "Note:");
            ImGui.Text("All rasters must have the same dimensions!");
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Get available raster layers
        var rasterLayers = gisDataset.Layers
            .OfType<GISRasterLayer>()
            .ToList();

        if (rasterLayers.Count == 0)
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "No raster layers available.");
            ImGui.TextWrapped("This tool requires at least one raster layer. Load a GeoTIFF or create a raster layer first.");
            return;
        }

        // Layer selection
        ImGui.Text("Available Raster Layers:");
        ImGui.Spacing();
        
        if (ImGui.BeginChild("RasterLayersList", new Vector2(0, 120), ImGuiChildFlags.Border))
        {
            for (int i = 0; i < rasterLayers.Count; i++)
            {
                var layer = rasterLayers[i];
                var isSelected = _selectedLayerIndex == i;
                
                if (ImGui.Selectable($"[{(char)('A' + i)}] {layer.Name}", isSelected))
                {
                    _selectedLayerIndex = i;
                }
                
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"Reference as '{(char)('A' + i)}' in expressions\n" +
                                   $"Size: {layer.Width}x{layer.Height}\n" +
                                   $"Bounds: {layer.Bounds.Min} to {layer.Bounds.Max}");
                }
            }
            ImGui.EndChild();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Expression input
        ImGui.Text("Expression:");
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.4f, 0.7f, 1.0f, 1.0f), "(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(350);
            ImGui.TextColored(new Vector4(1.0f, 1.0f, 0.4f, 1.0f), "Expression Tips:");
            ImGui.Separator();
            ImGui.BulletText("Reference layers as A, B, C, etc.");
            ImGui.BulletText("Use spaces for readability");
            ImGui.BulletText("Check layer list below for assignments");
            ImGui.BulletText("Test with simple expressions first");
            ImGui.Text("");
            ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.4f, 1.0f), "Common patterns:");
            ImGui.BulletText("Arithmetic: A + B, A * 2");
            ImGui.BulletText("Conditions: A > 50 ? 100 : 0");
            ImGui.BulletText("Math: sqrt(A), log(A + 1)");
            ImGui.BulletText("Multi-layer: (A + B + C) / 3");
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
        
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextMultiline("##Expression", ref _calculatorExpression, 1000, 
            new Vector2(-1, 80)))
        {
            _lastError = "";
        }

        ImGui.Spacing();

        // Quick formulas
        ImGui.Text("Quick Formulas:");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##QuickFormulas", _selectedQuickFormula))
        {
            foreach (var formula in _quickFormulas)
            {
                if (ImGui.Selectable(formula))
                {
                    _calculatorExpression = formula;
                    _selectedQuickFormula = formula;
                    _lastError = "";
                }
            }
            ImGui.EndCombo();
        }

        ImGui.Spacing();

        // Help text
        if (ImGui.CollapsingHeader("Expression Syntax Help"))
        {
            ImGui.Indent();
            ImGui.Text("Available operators:");
            ImGui.BulletText("+, -, *, / (arithmetic)");
            ImGui.BulletText("^, sqrt(), log(), exp() (math)");
            ImGui.BulletText("sin(), cos(), tan() (trigonometry)");
            ImGui.BulletText("abs(), floor(), ceil(), round()");
            ImGui.BulletText(">, <, >=, <=, ==, != (comparison)");
            ImGui.BulletText("condition ? value_if_true : value_if_false (ternary)");
            
            ImGui.Spacing();
            ImGui.Text("Layer references:");
            for (int i = 0; i < Math.Min(rasterLayers.Count, 26); i++)
            {
                ImGui.BulletText($"{(char)('A' + i)} = {rasterLayers[i].Name}");
            }
            
            ImGui.Spacing();
            ImGui.TextWrapped("Example: (A + B) / 2  -  Average of two layers");
            ImGui.TextWrapped("Example: A > 100 ? 255 : 0  -  Binary threshold");
            ImGui.TextWrapped("Example: sqrt(A^2 + B^2)  -  Euclidean distance");
            ImGui.Unindent();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Output settings
        ImGui.Text("Output Layer Name:");
        ImGui.SetNextItemWidth(200);
        ImGui.InputText("##OutputName", ref _outputLayerName, 100);

        ImGui.Spacing();

        // Error display
        if (!string.IsNullOrEmpty(_lastError))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0.2f, 0.2f, 1));
            ImGui.TextWrapped($"Error: {_lastError}");
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }

        // Calculate button
        if (_isCalculating)
        {
            ImGui.TextColored(new Vector4(0, 1, 1, 1), "Calculating...");
        }
        else
        {
            if (ImGui.Button("Calculate", new Vector2(120, 0)))
            {
                PerformCalculation(gisDataset, rasterLayers);
            }
            
            ImGui.SameLine();
            
            if (ImGui.Button("Clear", new Vector2(120, 0)))
            {
                _calculatorExpression = "";
                _lastError = "";
            }
        }
    }

    private async void PerformCalculation(GISDataset gisDataset, List<GISRasterLayer> rasterLayers)
    {
        if (string.IsNullOrWhiteSpace(_calculatorExpression))
        {
            _lastError = "Expression cannot be empty.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_outputLayerName))
        {
            _lastError = "Output layer name cannot be empty.";
            return;
        }

        _isCalculating = true;
        _lastError = "";

        try
        {
            // Validate that we have all referenced layers
            var referencedLetters = Regex.Matches(_calculatorExpression, @"\b[A-Z]\b")
                .Select(m => m.Value[0])
                .Distinct()
                .ToList();

            foreach (var letter in referencedLetters)
            {
                int index = letter - 'A';
                if (index >= rasterLayers.Count)
                {
                    _lastError = $"Layer '{letter}' not found. Only {rasterLayers.Count} layers available (A-{(char)('A' + rasterLayers.Count - 1)}).";
                    _isCalculating = false;
                    return;
                }
            }

            // Get dimensions from first layer
            var firstLayer = rasterLayers[0];
            int width = firstLayer.Width;
            int height = firstLayer.Height;

            // Check all layers have same dimensions
            foreach (var layer in rasterLayers)
            {
                if (layer.Width != width || layer.Height != height)
                {
                    _lastError = "All raster layers must have the same dimensions.";
                    _isCalculating = false;
                    return;
                }
            }

            // Calculate result
            var resultData = new float[width, height];
            var expression = new Expression(_calculatorExpression);

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    // Set parameter values for this pixel
                    for (int i = 0; i < rasterLayers.Count; i++)
                    {
                        char letter = (char)('A' + i);
                        float value = rasterLayers[i].GetPixelData()[x, y];
                        expression.Parameters[letter.ToString()] = value;
                    }

                    try
                    {
                        var result = expression.Evaluate();
                        resultData[x, y] = Convert.ToSingle(result);
                        
                        // Handle special values
                        if (float.IsNaN(resultData[x, y]) || float.IsInfinity(resultData[x, y]))
                        {
                            resultData[x, y] = 0;
                        }
                    }
                    catch (Exception ex)
                    {
                        _lastError = $"Error evaluating expression at pixel ({x},{y}): {ex.Message}";
                        _isCalculating = false;
                        return;
                    }
                }
            }

            // Create new raster layer with result
            var newLayer = new GISRasterLayer(resultData, firstLayer.Bounds)
            {
                Name = _outputLayerName,
                IsVisible = true,
                Color = new Vector4(1, 1, 1, 1)
            };

            newLayer.Properties["Expression"] = _calculatorExpression;
            newLayer.Properties["CreatedFrom"] = string.Join(", ", rasterLayers.Select(l => l.Name));
            newLayer.Properties["CreationDate"] = DateTime.Now.ToString();

            gisDataset.Layers.Add(newLayer);
            gisDataset.AddTag(GISTag.Generated);
            
            Logger.Log($"Raster calculation complete: '{_outputLayerName}' created from expression '{_calculatorExpression}'");
            
            // Update bounds
            gisDataset.UpdateBounds();
            
            ProjectManager.Instance.HasUnsavedChanges = true;
        }
        catch (Exception ex)
        {
            _lastError = $"Calculation failed: {ex.Message}";
            Logger.LogError($"Raster calculation error: {ex.Message}");
        }
        finally
        {
            _isCalculating = false;
        }
    }
}