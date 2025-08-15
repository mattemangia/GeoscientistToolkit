// GeoscientistToolkit/Data/AcousticVolume/CalibrationManagerUI.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.AcousticVolume
{
    /// <summary>
    /// Shared calibration UI component for acoustic simulations
    /// </summary>
    public class CalibrationManagerUI
    {
        private CalibrationData _calibrationData;
        private readonly CtImageStackDataset _dataset;

        // UI State
        private bool _showCalibrationWindow = false;
        private int _selectedCalibrationIndex = -1;

        // Lab measurement input
        private string _labMaterialName = "New Material";
        private float _labDensity = 2700f;
        private float _labConfiningPressure = 1.0f;
        private float _labMeasuredVp = 5000f;
        private float _labMeasuredVs = 3000f;
        private string _labNotes = "";

        // Comparison mode
        private float _comparisonDensity = 2700f;
        private float _comparisonPressure = 1.0f;

        // File dialogs
        private readonly ImGuiFileDialog _importDialog;
        private readonly ImGuiExportFileDialog _exportDialog;

        // File dialog helpers
        private string _lastImportDir = "";
        private string _lastExportDir = "";
        private const string DefaultExportBaseName = "calibration";
        private static readonly string[] ImportExtensions = new[] { ".json" };

        public CalibrationData CalibrationData => _calibrationData;
        public bool HasCalibration => _calibrationData?.Points.Count > 0;

        public CalibrationManagerUI(CtImageStackDataset dataset)
        {
            _dataset = dataset;
            _calibrationData = new CalibrationData();

            _importDialog = new ImGuiFileDialog("ImportCalibration", FileDialogType.OpenFile, "Import Calibration");
            _exportDialog = new ImGuiExportFileDialog("ExportCalibration", "Export Calibration");

            // Reasonable defaults
            _lastImportDir = Directory.GetCurrentDirectory();
            _lastExportDir = Directory.GetCurrentDirectory();
        }

        public void DrawCalibrationSection(ref float youngsModulus, ref float poissonRatio)
        {
            if (ImGui.CollapsingHeader("Calibration System"))
            {
                ImGui.Indent();

                // Status
                if (HasCalibration)
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), $"✓ {_calibrationData.Points.Count} calibration points loaded");
                else
                    ImGui.TextColored(new Vector4(1, 1, 0, 1), "⚠ No calibration data");

                // Buttons
                if (ImGui.Button("Manage Calibration"))
                    _showCalibrationWindow = true;

                ImGui.SameLine();
                if (ImGui.Button("Import"))
                {
                    // Open: restrict to JSON
                    _importDialog.Open(
                        startingPath: _lastImportDir,
                        allowedExtensions: ImportExtensions,
                        defaultFileName: null);
                }

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
                            defaultFileName: DefaultExportBaseName,
                            startingPath: _lastExportDir
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
                        var (E, nu) = _calibrationData.InterpolateParameters(
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
                    int count = Math.Min(3, _calibrationData.Points.Count);
                    for (int i = _calibrationData.Points.Count - count; i < _calibrationData.Points.Count; i++)
                    {
                        if (i < 0) continue;
                        var point = _calibrationData.Points[i];
                        ImGui.BulletText($"{point.MaterialName}: Vp/Vs={point.MeasuredVpVsRatio:F3}");
                    }
                    ImGui.Unindent();
                }

                ImGui.Unindent();
            }

            // Window
            if (_showCalibrationWindow)
            {
                DrawCalibrationWindow(ref youngsModulus, ref poissonRatio);
            }

            // Handle file dialogs each frame
            HandleFileDialogs();
        }

        private void DrawCalibrationWindow(ref float youngsModulus, ref float poissonRatio)
        {
            ImGui.SetNextWindowSize(new Vector2(800, 600), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Calibration Manager", ref _showCalibrationWindow))
            {
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

            float vpVsRatio = _labMeasuredVs > 0 ? _labMeasuredVp / _labMeasuredVs : 0;
            ImGui.Text($"Vp/Vs Ratio: {vpVsRatio:F3}");

            ImGui.Spacing();
            ImGui.Text("Notes:");
            ImGui.InputTextMultiline("##Notes", ref _labNotes, 1024, new Vector2(-1, 100));

            ImGui.Spacing();

            if (ImGui.Button("Add Measurement"))
            {
                AddLabMeasurement();
            }

            ImGui.SameLine();
            if (ImGui.Button("Import from CSV"))
            {
                // (Optional future work) CSV import could go here.
                Logger.Log("[CalibrationUI] CSV import not yet implemented");
            }
        }

        private void DrawCalibrationPointsTab()
        {
            if (_calibrationData.Points.Count == 0)
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

                for (int i = 0; i < _calibrationData.Points.Count; i++)
                {
                    var point = _calibrationData.Points[i];
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
                    if (point.SimulatedVp > 0) ImGui.Text($"{point.SimulatedVp:F0}"); else ImGui.TextDisabled("N/A");

                    ImGui.TableSetColumnIndex(6);
                    if (point.SimulatedVs > 0) ImGui.Text($"{point.SimulatedVs:F0}"); else ImGui.TextDisabled("N/A");

                    ImGui.TableSetColumnIndex(7);
                    if (point.SimulatedVp > 0 && point.MeasuredVp > 0)
                    {
                        float error = Math.Abs((float)(point.SimulatedVp - point.MeasuredVp) / (float)point.MeasuredVp) * 100;
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
                        _calibrationData.Points.RemoveAt(i);
                        if (_selectedCalibrationIndex >= i) _selectedCalibrationIndex--;
                        break;
                    }
                }

                ImGui.EndTable();
            }

            ImGui.Spacing();

            if (_calibrationData.Points.Any(p => p.SimulatedVp > 0))
            {
                var validPoints = _calibrationData.Points.Where(p => p.SimulatedVp > 0 && p.MeasuredVp > 0).ToList();
                if (validPoints.Count > 0)
                {
                    float avgError = validPoints.Average(p =>
                        Math.Abs((float)(p.SimulatedVp - p.MeasuredVp) / (float)p.MeasuredVp) * 100);

                    ImGui.Text($"Average Error: {avgError:F2}%");
                    ImGui.Text($"Validated Points: {validPoints.Count}/{_calibrationData.Points.Count}");
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
                var (E, nu) = _calibrationData.InterpolateParameters(_comparisonDensity, _comparisonPressure);
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
                float E = youngsModulus * 1e6f;
                float nu = poissonRatio;
                float rho = _comparisonDensity;
                float mu = E / (2 * (1 + nu));
                float lambda = E * nu / ((1 + nu) * (1 - 2 * nu));
                float vp = MathF.Sqrt((lambda + 2 * mu) / rho);
                float vs = MathF.Sqrt(mu / rho);

                ImGui.Spacing();
                ImGui.Text("Expected Velocities:");
                ImGui.Text($"Vp: {vp:F0} m/s");
                ImGui.Text($"Vs: {vs:F0} m/s");
                ImGui.Text($"Vp/Vs: {vp / vs:F3}");

                if (ImGui.Button("Apply"))
                {
                    ImGui.CloseCurrentPopup();
                }

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

            if (ImGui.Button("Run Leave-One-Out Cross-Validation"))
            {
                RunCrossValidation();
            }

            ImGui.Text("Measured vs Predicted Plot:");
            ImGui.Text("[Visualization would be displayed here]");

            if (_calibrationData.Points.Any(p => p.SimulatedVp > 0))
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Text("Error Statistics:");

                var validPoints = _calibrationData.Points.Where(p => p.SimulatedVp > 0 && p.MeasuredVp > 0).ToList();
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

            var closest = _calibrationData.GetClosestPoint(_comparisonDensity, _comparisonPressure);
            if (closest != null)
            {
                ImGui.Text($"Closest: {closest.MaterialName}");
                ImGui.Text($"Distance: {Math.Abs(closest.Density - _comparisonDensity) + Math.Abs(closest.ConfiningPressureMPa - _comparisonPressure):F2}");
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
            float rho = _labDensity;
            float vp = (float)_labMeasuredVp;
            float vs = (float)_labMeasuredVs;

            float mu = vs * vs * rho;
            float lambda = vp * vp * rho - 2 * mu;

            point.YoungsModulusMPa = mu * (3 * lambda + 2 * mu) / (lambda + mu) / 1e6f;
            point.PoissonRatio = lambda / (2 * (lambda + mu));

            _calibrationData.AddPoint(point);
            Logger.Log($"[CalibrationUI] Added lab measurement: {_labMaterialName}");
        }

        public void AddSimulationResult(string materialName, byte materialID, float density,
            float confiningPressure, float youngsModulus, float poissonRatio,
            double simulatedVp, double simulatedVs)
        {
            var existingPoint = _calibrationData.Points.FirstOrDefault(p =>
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

                _calibrationData.AddPoint(point);
                Logger.Log($"[CalibrationUI] Added simulation calibration point: {materialName}");
            }
        }

        private void RunCrossValidation()
        {
            if (_calibrationData.Points.Count < 2)
            {
                Logger.Log("[CalibrationUI] Need at least 2 points for cross-validation");
                return;
            }

            float totalError = 0;
            int validCount = 0;

            foreach (var testPoint in _calibrationData.Points)
            {
                var tempCalibration = new CalibrationData();
                foreach (var p in _calibrationData.Points)
                {
                    if (p != testPoint)
                        tempCalibration.Points.Add(p);
                }

                var (E, nu) = tempCalibration.InterpolateParameters(testPoint.Density, testPoint.ConfiningPressureMPa);

                float rho = testPoint.Density;
                float mu = E * 1e6f / (2 * (1 + nu));
                float lambda = E * 1e6f * nu / ((1 + nu) * (1 - 2 * nu));
                float predictedVp = MathF.Sqrt((lambda + 2 * mu) / rho);

                if (testPoint.MeasuredVp > 0)
                {
                    float error = Math.Abs(predictedVp - (float)testPoint.MeasuredVp) / (float)testPoint.MeasuredVp;
                    totalError += error;
                    validCount++;
                }
            }

            if (validCount > 0)
            {
                float avgError = totalError / validCount * 100;
                Logger.Log($"[CalibrationUI] Cross-validation average error: {avgError:F2}%");
            }
        }

        private float StandardDeviation(List<float> values)
        {
            if (values.Count == 0) return 0;
            float avg = values.Average();
            float sumSquares = values.Sum(v => (v - avg) * (v - avg));
            return MathF.Sqrt(sumSquares / values.Count);
        }

        private void HandleFileDialogs()
        {
            // IMPORT
            if (_importDialog.Submit())
            {
                string path = _importDialog.SelectedPath;
                if (!string.IsNullOrEmpty(path))
                {
                    _lastImportDir = Path.GetDirectoryName(path) ?? _lastImportDir;
                    ImportCalibration(path);
                }
            }

            // EXPORT
            if (_exportDialog.Submit())
            {
                string path = _exportDialog.SelectedPath;
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

                string json = File.ReadAllText(path);
                var loaded = System.Text.Json.JsonSerializer.Deserialize<CalibrationData>(json);
                _calibrationData = loaded ?? new CalibrationData();

                Logger.Log($"[CalibrationUI] Imported {_calibrationData.Points.Count} calibration points from '{Path.GetFileName(path)}'");
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

                string json = System.Text.Json.JsonSerializer.Serialize(
                    _calibrationData,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(path, json);

                Logger.Log($"[CalibrationUI] Exported {_calibrationData.Points.Count} calibration points to '{Path.GetFileName(path)}'");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[CalibrationUI] Failed to export calibration: {ex.Message}");
            }
        }

        public void LoadCalibration(CalibrationData calibrationData)
        {
            _calibrationData = calibrationData ?? new CalibrationData();
        }
    }
}
