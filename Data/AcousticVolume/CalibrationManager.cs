// GeoscientistToolkit/Data/AcousticVolume/CalibrationManagerUI.cs

using System.Globalization;
using System.Numerics;
using System.Text.Json;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.AcousticVolume;

/// <summary>
///     Shared calibration UI component for acoustic simulations
/// </summary>
public class CalibrationManagerUI
{
    private const string DefaultExportBaseName = "calibration";
    private static readonly string[] ImportExtensions = new[] { ".json" };
    private static readonly string[] CsvImportExtensions = new[] { ".csv" };
    private readonly CtImageStackDataset _dataset;
    private readonly ImGuiExportFileDialog _exportDialog;

    // File dialogs
    private readonly ImGuiFileDialog _importDialog;
    private readonly ImGuiFileDialog _csvImportDialog;

    // Comparison mode
    private float _comparisonDensity = 2700f;
    private float _comparisonPressure = 1.0f;
    private float _labConfiningPressure = 1.0f;
    private float _labDensity = 2700f;

    // Lab measurement input
    private string _labMaterialName = "New Material";
    private float _labMeasuredVp = 5000f;
    private float _labMeasuredVs = 3000f;
    private string _labNotes = "";
    private string _lastExportDir = "";

    // File dialog helpers
    private string _lastImportDir = "";
    private int _selectedCalibrationIndex = -1;

    // UI State
    private bool _showCalibrationWindow;

    public CalibrationManagerUI(CtImageStackDataset dataset)
    {
        _dataset = dataset;
        CalibrationData = new CalibrationData();

        _importDialog = new ImGuiFileDialog("ImportCalibration", FileDialogType.OpenFile, "Import Calibration");
        _csvImportDialog = new ImGuiFileDialog("ImportCalibrationCsv", FileDialogType.OpenFile, "Import Calibration CSV");
        _exportDialog = new ImGuiExportFileDialog("ExportCalibration", "Export Calibration");

        // Reasonable defaults
        _lastImportDir = Directory.GetCurrentDirectory();
        _lastExportDir = Directory.GetCurrentDirectory();
    }

    public CalibrationData CalibrationData { get; private set; }

    public bool HasCalibration => CalibrationData?.Points.Count > 0;

    public void DrawCalibrationSection(ref float youngsModulus, ref float poissonRatio)
    {
        if (ImGui.CollapsingHeader("Calibration System"))
        {
            ImGui.Indent();

            // Status
            if (HasCalibration)
                ImGui.TextColored(new Vector4(0, 1, 0, 1),
                    $"{CalibrationData.Points.Count} calibration points loaded");
            else
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "No calibration data");

            // Buttons
            if (ImGui.Button("Manage Calibration"))
                _showCalibrationWindow = true;

            ImGui.SameLine();
            if (ImGui.Button("Import"))
                // Open: restrict to JSON
                _importDialog.Open(
                    _lastImportDir,
                    ImportExtensions);

            ImGui.SameLine();
            if (HasCalibration)
            {
                if (ImGui.Button("Export"))
                {
                    // Configure export dialog (extensions dropdown)
                    _exportDialog.SetExtensions(
                        new ImGuiExportFileDialog.ExtensionOption(".json", "Calibration JSON")
                    );
                    _exportDialog.Open(
                        DefaultExportBaseName,
                        _lastExportDir
                    );
                }
            }
            else
            {
                ImGui.BeginDisabled();
                ImGui.Button("Export");
                ImGui.EndDisabled();
            }

            ImGui.SameLine();
            if (HasCalibration && ImGui.Button("Apply Auto-Calibration"))
            {
                var material = _dataset.Materials.FirstOrDefault(m => m.ID != 0);
                if (material != null)
                {
                    var (E, nu) = CalibrationData.InterpolateParameters(
                        (float)material.Density,
                        1.0f);
                    youngsModulus = E;
                    poissonRatio = nu;
                    Logger.Log($"[CalibrationUI] Applied calibration: E={E:F2} MPa, ν={nu:F4}");
                }
            }

            // Quick summary
            if (HasCalibration)
            {
                ImGui.Spacing();
                ImGui.Text("Recent Calibrations:");
                ImGui.Indent();
                var count = Math.Min(3, CalibrationData.Points.Count);
                for (var i = CalibrationData.Points.Count - count; i < CalibrationData.Points.Count; i++)
                {
                    if (i < 0) continue;
                    var point = CalibrationData.Points[i];
                    ImGui.BulletText($"{point.MaterialName}: Vp/Vs={point.MeasuredVpVsRatio:F3}");
                }

                ImGui.Unindent();
            }

            ImGui.Unindent();
        }

        // Window
        if (_showCalibrationWindow) DrawCalibrationWindow(ref youngsModulus, ref poissonRatio);

        // Handle file dialogs each frame
        HandleFileDialogs();
    }

    private void DrawCalibrationWindow(ref float youngsModulus, ref float poissonRatio)
    {
        ImGui.SetNextWindowSize(new Vector2(800, 600), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Calibration Manager", ref _showCalibrationWindow))
            if (ImGui.BeginTabBar("CalibrationTabs"))
            {
                if (ImGui.BeginTabItem("Lab Measurements"))
                {
                    DrawLabMeasurementsTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Calibration Points"))
                {
                    DrawCalibrationPointsTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Parameter Estimation"))
                {
                    DrawParameterEstimationTab(ref youngsModulus, ref poissonRatio);
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Validation"))
                {
                    DrawValidationTab();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }

        ImGui.End();
    }

    private void DrawLabMeasurementsTab()
    {
        ImGui.Text("Add Lab Measurement Results:");
        ImGui.Separator();

        ImGui.InputText("Material Name", ref _labMaterialName, 256);
        ImGui.InputFloat("Density (kg/m³)", ref _labDensity, 10, 100);
        ImGui.InputFloat("Confining Pressure (MPa)", ref _labConfiningPressure, 0.1f, 1.0f);

        ImGui.Spacing();
        ImGui.Text("Measured Velocities:");
        ImGui.InputFloat("P-Wave Velocity (m/s)", ref _labMeasuredVp, 10, 100);
        ImGui.InputFloat("S-Wave Velocity (m/s)", ref _labMeasuredVs, 10, 100);

        var vpVsRatio = _labMeasuredVs > 0 ? _labMeasuredVp / _labMeasuredVs : 0;
        ImGui.Text($"Vp/Vs Ratio: {vpVsRatio:F3}");

        ImGui.Spacing();
        ImGui.Text("Notes:");
        ImGui.InputTextMultiline("##Notes", ref _labNotes, 1024, new Vector2(-1, 100));

        ImGui.Spacing();

        if (ImGui.Button("Add Measurement")) AddLabMeasurement();

        ImGui.SameLine();
        if (ImGui.Button("Import from CSV"))
            _csvImportDialog.Open(
                _lastImportDir,
                CsvImportExtensions);
    }

    private void DrawCalibrationPointsTab()
    {
        if (CalibrationData.Points.Count == 0)
        {
            ImGui.Text("No calibration points available.");
            ImGui.Text("Add lab measurements or run simulations to create calibration points.");
            return;
        }

        if (ImGui.BeginTable("CalibrationTable", 9,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("Material");
            ImGui.TableSetupColumn("Density");
            ImGui.TableSetupColumn("Pressure");
            ImGui.TableSetupColumn("Lab Vp");
            ImGui.TableSetupColumn("Lab Vs");
            ImGui.TableSetupColumn("Sim Vp");
            ImGui.TableSetupColumn("Sim Vs");
            ImGui.TableSetupColumn("Error %");
            ImGui.TableSetupColumn("Actions");
            ImGui.TableHeadersRow();

            for (var i = 0; i < CalibrationData.Points.Count; i++)
            {
                var point = CalibrationData.Points[i];
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                if (ImGui.Selectable($"{point.MaterialName}##{i}", _selectedCalibrationIndex == i))
                    _selectedCalibrationIndex = i;

                ImGui.TableSetColumnIndex(1);
                ImGui.Text($"{point.Density:F0}");

                ImGui.TableSetColumnIndex(2);
                ImGui.Text($"{point.ConfiningPressureMPa:F1}");

                ImGui.TableSetColumnIndex(3);
                ImGui.Text($"{point.MeasuredVp:F0}");

                ImGui.TableSetColumnIndex(4);
                ImGui.Text($"{point.MeasuredVs:F0}");

                ImGui.TableSetColumnIndex(5);
                if (point.SimulatedVp > 0) ImGui.Text($"{point.SimulatedVp:F0}");
                else ImGui.TextDisabled("N/A");

                ImGui.TableSetColumnIndex(6);
                if (point.SimulatedVs > 0) ImGui.Text($"{point.SimulatedVs:F0}");
                else ImGui.TextDisabled("N/A");

                ImGui.TableSetColumnIndex(7);
                if (point.SimulatedVp > 0 && point.MeasuredVp > 0)
                {
                    var error = Math.Abs((float)(point.SimulatedVp - point.MeasuredVp) / (float)point.MeasuredVp) * 100;
                    var color = error < 5 ? new Vector4(0, 1, 0, 1) :
                        error < 10 ? new Vector4(1, 1, 0, 1) :
                        new Vector4(1, 0, 0, 1);
                    ImGui.TextColored(color, $"{error:F1}%");
                }
                else
                {
                    ImGui.TextDisabled("N/A");
                }

                ImGui.TableSetColumnIndex(8);
                if (ImGui.SmallButton($"Delete##{i}"))
                {
                    CalibrationData.Points.RemoveAt(i);
                    if (_selectedCalibrationIndex >= i) _selectedCalibrationIndex--;
                    break;
                }
            }

            ImGui.EndTable();
        }

        ImGui.Spacing();

        if (CalibrationData.Points.Any(p => p.SimulatedVp > 0))
        {
            var validPoints = CalibrationData.Points.Where(p => p.SimulatedVp > 0 && p.MeasuredVp > 0).ToList();
            if (validPoints.Count > 0)
            {
                var avgError = validPoints.Average(p =>
                    Math.Abs((float)(p.SimulatedVp - p.MeasuredVp) / (float)p.MeasuredVp) * 100);

                ImGui.Text($"Average Error: {avgError:F2}%");
                ImGui.Text($"Validated Points: {validPoints.Count}/{CalibrationData.Points.Count}");
            }
        }
    }

    private void DrawParameterEstimationTab(ref float youngsModulus, ref float poissonRatio)
    {
        ImGui.Text("Parameter Estimation from Calibration:");
        ImGui.Separator();

        if (!HasCalibration)
        {
            ImGui.TextWrapped("No calibration data available. Add calibration points first.");
            return;
        }

        ImGui.Text("Input Conditions:");
        ImGui.InputFloat("Density (kg/m³)", ref _comparisonDensity, 10, 100);
        ImGui.InputFloat("Confining Pressure (MPa)", ref _comparisonPressure, 0.1f, 1.0f);

        ImGui.Spacing();

        if (ImGui.Button("Estimate Parameters"))
        {
            var (E, nu) = CalibrationData.InterpolateParameters(_comparisonDensity, _comparisonPressure);
            youngsModulus = E;
            poissonRatio = nu;
            ImGui.OpenPopup("EstimationResult");
        }

        if (ImGui.BeginPopup("EstimationResult"))
        {
            ImGui.Text("Estimated Parameters:");
            ImGui.Separator();
            ImGui.Text($"Young's Modulus: {youngsModulus:F0} MPa");
            ImGui.Text($"Poisson's Ratio: {poissonRatio:F4}");

            // Expected velocities
            var E = youngsModulus * 1e6f;
            var nu = poissonRatio;
            var rho = _comparisonDensity;
            var mu = E / (2 * (1 + nu));
            var lambda = E * nu / ((1 + nu) * (1 - 2 * nu));
            var vp = MathF.Sqrt((lambda + 2 * mu) / rho);
            var vs = MathF.Sqrt(mu / rho);

            ImGui.Spacing();
            ImGui.Text("Expected Velocities:");
            ImGui.Text($"Vp: {vp:F0} m/s");
            ImGui.Text($"Vs: {vs:F0} m/s");
            ImGui.Text($"Vp/Vs: {vp / vs:F3}");

            if (ImGui.Button("Apply")) ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }

        ImGui.Spacing();
        ImGui.Separator();
        DrawInterpolationVisualization();
    }

    private void DrawValidationTab()
    {
        ImGui.Text("Calibration Validation:");
        ImGui.Separator();

        if (!HasCalibration)
        {
            ImGui.TextWrapped("No calibration data available for validation.");
            return;
        }

        ImGui.Text("Cross-Validation Results:");
        ImGui.Spacing();

        if (ImGui.Button("Run Leave-One-Out Cross-Validation")) RunCrossValidation();

        ImGui.Text("Measured vs Predicted Plot:");
        ImGui.Text("[Visualization would be displayed here]");

        if (CalibrationData.Points.Any(p => p.SimulatedVp > 0))
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("Error Statistics:");

            var validPoints = CalibrationData.Points.Where(p => p.SimulatedVp > 0 && p.MeasuredVp > 0).ToList();
            if (validPoints.Count > 0)
            {
                var vpErrors = validPoints.Select(p =>
                    Math.Abs((float)(p.SimulatedVp - p.MeasuredVp) / (float)p.MeasuredVp) * 100).ToList();
                var vsErrors = validPoints.Select(p =>
                    Math.Abs((float)(p.SimulatedVs - p.MeasuredVs) / (float)p.MeasuredVs) * 100).ToList();

                ImGui.Text($"Vp Error: {vpErrors.Average():F2}% ± {StandardDeviation(vpErrors):F2}%");
                ImGui.Text($"Vs Error: {vsErrors.Average():F2}% ± {StandardDeviation(vsErrors):F2}%");
                ImGui.Text($"Max Vp Error: {vpErrors.Max():F2}%");
                ImGui.Text($"Max Vs Error: {vsErrors.Max():F2}%");
            }
        }
    }

    private void DrawInterpolationVisualization()
    {
        ImGui.Text("Interpolation Surface:");
        ImGui.Text("[3D surface plot would be displayed here]");

        ImGui.Text("Calibration Points:");
        ImGui.Indent();

        var closest = CalibrationData.GetClosestPoint(_comparisonDensity, _comparisonPressure);
        if (closest != null)
        {
            ImGui.Text($"Closest: {closest.MaterialName}");
            ImGui.Text(
                $"Distance: {Math.Abs(closest.Density - _comparisonDensity) + Math.Abs(closest.ConfiningPressureMPa - _comparisonPressure):F2}");
        }

        ImGui.Unindent();
    }

    private void AddLabMeasurement()
    {
        var point = new CalibrationPoint
        {
            MaterialName = _labMaterialName,
            MaterialID = 0,
            Density = _labDensity,
            ConfiningPressureMPa = _labConfiningPressure,
            MeasuredVp = _labMeasuredVp,
            MeasuredVs = _labMeasuredVs,
            MeasuredVpVsRatio = _labMeasuredVs > 0 ? _labMeasuredVp / _labMeasuredVs : 0,
            Notes = _labNotes,
            Timestamp = DateTime.Now
        };

        // Estimate E and nu from velocities
        var rho = _labDensity;
        var vp = _labMeasuredVp;
        var vs = _labMeasuredVs;

        var mu = vs * vs * rho;
        var lambda = vp * vp * rho - 2 * mu;

        point.YoungsModulusMPa = mu * (3 * lambda + 2 * mu) / (lambda + mu) / 1e6f;
        point.PoissonRatio = lambda / (2 * (lambda + mu));

        CalibrationData.AddPoint(point);
        Logger.Log($"[CalibrationUI] Added lab measurement: {_labMaterialName}");
    }

    public void AddSimulationResult(string materialName, byte materialID, float density,
        float confiningPressure, float youngsModulus, float poissonRatio,
        double simulatedVp, double simulatedVs)
    {
        var existingPoint = CalibrationData.Points.FirstOrDefault(p =>
            Math.Abs(p.Density - density) < 10 &&
            Math.Abs(p.ConfiningPressureMPa - confiningPressure) < 0.1f &&
            string.IsNullOrEmpty(p.MaterialName) == false);

        if (existingPoint != null)
        {
            existingPoint.SimulatedVp = simulatedVp;
            existingPoint.SimulatedVs = simulatedVs;
            existingPoint.SimulatedVpVsRatio = simulatedVs > 0 ? simulatedVp / simulatedVs : 0;
            existingPoint.YoungsModulusMPa = youngsModulus;
            existingPoint.PoissonRatio = poissonRatio;

            Logger.Log($"[CalibrationUI] Updated calibration point with simulation: {materialName}");
        }
        else
        {
            var point = new CalibrationPoint
            {
                MaterialName = materialName,
                MaterialID = materialID,
                Density = density,
                ConfiningPressureMPa = confiningPressure,
                YoungsModulusMPa = youngsModulus,
                PoissonRatio = poissonRatio,
                SimulatedVp = simulatedVp,
                SimulatedVs = simulatedVs,
                SimulatedVpVsRatio = simulatedVs > 0 ? simulatedVp / simulatedVs : 0,
                Timestamp = DateTime.Now
            };

            CalibrationData.AddPoint(point);
            Logger.Log($"[CalibrationUI] Added simulation calibration point: {materialName}");
        }
    }

    private void RunCrossValidation()
    {
        if (CalibrationData.Points.Count < 2)
        {
            Logger.Log("[CalibrationUI] Need at least 2 points for cross-validation");
            return;
        }

        float totalError = 0;
        var validCount = 0;

        foreach (var testPoint in CalibrationData.Points)
        {
            var tempCalibration = new CalibrationData();
            foreach (var p in CalibrationData.Points)
                if (p != testPoint)
                    tempCalibration.Points.Add(p);

            var (E, nu) = tempCalibration.InterpolateParameters(testPoint.Density, testPoint.ConfiningPressureMPa);

            var rho = testPoint.Density;
            var mu = E * 1e6f / (2 * (1 + nu));
            var lambda = E * 1e6f * nu / ((1 + nu) * (1 - 2 * nu));
            var predictedVp = MathF.Sqrt((lambda + 2 * mu) / rho);

            if (testPoint.MeasuredVp > 0)
            {
                var error = Math.Abs(predictedVp - (float)testPoint.MeasuredVp) / (float)testPoint.MeasuredVp;
                totalError += error;
                validCount++;
            }
        }

        if (validCount > 0)
        {
            var avgError = totalError / validCount * 100;
            Logger.Log($"[CalibrationUI] Cross-validation average error: {avgError:F2}%");
        }
    }

    private float StandardDeviation(List<float> values)
    {
        if (values.Count == 0) return 0;
        var avg = values.Average();
        var sumSquares = values.Sum(v => (v - avg) * (v - avg));
        return MathF.Sqrt(sumSquares / values.Count);
    }

    private void HandleFileDialogs()
    {
        // IMPORT
        if (_importDialog.Submit())
        {
            var path = _importDialog.SelectedPath;
            if (!string.IsNullOrEmpty(path))
            {
                _lastImportDir = Path.GetDirectoryName(path) ?? _lastImportDir;
                ImportCalibration(path);
            }
        }

        if (_csvImportDialog.Submit())
        {
            var path = _csvImportDialog.SelectedPath;
            if (!string.IsNullOrEmpty(path))
            {
                _lastImportDir = Path.GetDirectoryName(path) ?? _lastImportDir;
                ImportCalibrationCsv(path);
            }
        }

        // EXPORT
        if (_exportDialog.Submit())
        {
            var path = _exportDialog.SelectedPath;
            if (!string.IsNullOrEmpty(path))
            {
                _lastExportDir = Path.GetDirectoryName(path) ?? _lastExportDir;
                ExportCalibration(path);
            }
        }
    }

    private void ImportCalibration(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Logger.LogWarning($"[CalibrationUI] Import file not found: {path}");
                return;
            }

            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<CalibrationData>(json);
            CalibrationData = loaded ?? new CalibrationData();

            Logger.Log(
                $"[CalibrationUI] Imported {CalibrationData.Points.Count} calibration points from '{Path.GetFileName(path)}'");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[CalibrationUI] Failed to import calibration: {ex.Message}");
        }
    }

    private void ExportCalibration(string path)
    {
        try
        {
            // Ensure target folder exists
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(
                CalibrationData,
                new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(path, json);

            Logger.Log(
                $"[CalibrationUI] Exported {CalibrationData.Points.Count} calibration points to '{Path.GetFileName(path)}'");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[CalibrationUI] Failed to export calibration: {ex.Message}");
        }
    }

    public void LoadCalibration(CalibrationData calibrationData)
    {
        CalibrationData = calibrationData ?? new CalibrationData();
    }

    private void ImportCalibrationCsv(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Logger.LogWarning($"[CalibrationUI] CSV import file not found: {path}");
                return;
            }

            var lines = File.ReadAllLines(path);
            if (lines.Length < 2)
            {
                Logger.LogWarning("[CalibrationUI] CSV import file is empty.");
                return;
            }

            var header = ParseCsvLine(lines[0]);
            var headerLookup = header
                .Select((name, idx) => new { Name = name.Trim(), Index = idx })
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                .ToDictionary(item => item.Name, item => item.Index, StringComparer.OrdinalIgnoreCase);

            var points = new List<CalibrationPoint>();
            for (var i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                    continue;

                var values = ParseCsvLine(lines[i]);
                var point = new CalibrationPoint
                {
                    MaterialName = GetStringValue(values, headerLookup, "MaterialName") ?? "Unknown",
                    MaterialID = (byte)GetIntValue(values, headerLookup, "MaterialID"),
                    Density = GetFloatValue(values, headerLookup, "Density"),
                    ConfiningPressureMPa = GetFloatValue(values, headerLookup, "ConfiningPressureMPa", "Pressure"),
                    YoungsModulusMPa = GetFloatValue(values, headerLookup, "YoungsModulusMPa", "YoungsModulus"),
                    PoissonRatio = GetFloatValue(values, headerLookup, "PoissonRatio"),
                    MeasuredVp = GetDoubleValue(values, headerLookup, "MeasuredVp", "LabVp"),
                    MeasuredVs = GetDoubleValue(values, headerLookup, "MeasuredVs", "LabVs"),
                    SimulatedVp = GetDoubleValue(values, headerLookup, "SimulatedVp", "SimVp"),
                    SimulatedVs = GetDoubleValue(values, headerLookup, "SimulatedVs", "SimVs"),
                    Notes = GetStringValue(values, headerLookup, "Notes") ?? string.Empty,
                    Timestamp = GetDateValue(values, headerLookup, "Timestamp")
                };

                if (point.MeasuredVs > 0)
                    point.MeasuredVpVsRatio = point.MeasuredVp / point.MeasuredVs;
                if (point.SimulatedVs > 0)
                    point.SimulatedVpVsRatio = point.SimulatedVp / point.SimulatedVs;

                points.Add(point);
            }

            CalibrationData.Points = points;
            CalibrationData.LastUpdated = DateTime.Now;
            CalibrationData.CalibrationMethod = "CSV Import";

            Logger.Log($"[CalibrationUI] Imported {points.Count} calibration points from CSV.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[CalibrationUI] Failed to import calibration CSV: {ex.Message}");
        }
    }

    private static string[] ParseCsvLine(string line)
    {
        var values = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var ch in line)
        {
            if (ch == '"' )
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        values.Add(current.ToString());
        return values.ToArray();
    }

    private static string GetStringValue(IReadOnlyList<string> values, Dictionary<string, int> headers,
        params string[] keys)
    {
        foreach (var key in keys)
            if (headers.TryGetValue(key, out var index) && index < values.Count)
                return values[index];
        return null;
    }

    private static float GetFloatValue(IReadOnlyList<string> values, Dictionary<string, int> headers,
        params string[] keys)
    {
        var text = GetStringValue(values, headers, keys);
        return float.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0f;
    }

    private static int GetIntValue(IReadOnlyList<string> values, Dictionary<string, int> headers,
        params string[] keys)
    {
        var text = GetStringValue(values, headers, keys);
        return int.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0;
    }

    private static double GetDoubleValue(IReadOnlyList<string> values, Dictionary<string, int> headers,
        params string[] keys)
    {
        var text = GetStringValue(values, headers, keys);
        return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0d;
    }

    private static DateTime GetDateValue(IReadOnlyList<string> values, Dictionary<string, int> headers,
        params string[] keys)
    {
        var text = GetStringValue(values, headers, keys);
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var result)
            ? result
            : DateTime.Now;
    }
}
