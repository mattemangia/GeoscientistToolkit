// GeoscientistToolkit/Analysis/Geomechanics/GeomechanicalCalibrationManager.cs

using System.Numerics;
using System.Text.Json;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.Geomechanics;

public class CalibrationPoint
{
    public string TestName { get; set; }
    public DateTime TestDate { get; set; }
    public string MaterialName { get; set; }
    public float ConfiningPressure { get; set; } // MPa
    public float FailureStress { get; set; } // MPa
    public float YoungModulus { get; set; } // MPa
    public float PoissonRatio { get; set; }
    public string Notes { get; set; }
}

public class GeomechanicalCalibrationManager
{
    private readonly List<CalibrationPoint> _calibrationData = new();
    private readonly string _calibrationFilePath;
    private float _newConfiningPressure;
    private float _newFailureStress;
    private string _newMaterialName = "";
    private string _newNotes = "";
    private float _newPoissonRatio = 0.25f;

    // UI state
    private string _newTestName = "";
    private float _newYoungModulus = 30000f;
    private int _selectedCalibrationIndex = -1;

    public GeomechanicalCalibrationManager()
    {
        _calibrationFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GeoscientistToolkit", "GeomechanicalCalibration.json");

        LoadCalibrationData();
    }

    public void DrawCalibrationUI(ref float youngModulus, ref float poissonRatio,
        ref float cohesion, ref float frictionAngle, ref float tensileStrength)
    {
        ImGui.TextWrapped("Calibrate material properties using laboratory test data:");
        ImGui.Spacing();

        if (ImGui.Button("Add Lab Test Result", new Vector2(-1, 0))) ImGui.OpenPopup("AddCalibration");

        DrawAddCalibrationPopup();

        ImGui.Spacing();

        if (_calibrationData.Any())
        {
            ImGui.Text($"Calibration Database: {_calibrationData.Count} tests");

            ImGui.BeginChild("CalibrationList", new Vector2(-1, 150), ImGuiChildFlags.Border);

            for (var i = 0; i < _calibrationData.Count; i++)
            {
                var point = _calibrationData[i];
                var label = $"{point.TestName} - {point.MaterialName} ({point.TestDate:yyyy-MM-dd})";

                if (ImGui.Selectable(label, _selectedCalibrationIndex == i)) _selectedCalibrationIndex = i;

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip($"σ3: {point.ConfiningPressure:F1} MPa\n" +
                                     $"σ1f: {point.FailureStress:F1} MPa\n" +
                                     $"E: {point.YoungModulus:F0} MPa\n" +
                                     $"ν: {point.PoissonRatio:F3}");
            }

            ImGui.EndChild();

            if (_selectedCalibrationIndex >= 0 && _selectedCalibrationIndex < _calibrationData.Count)
            {
                ImGui.Spacing();
                var selected = _calibrationData[_selectedCalibrationIndex];

                ImGui.Text("Selected Test Details:");
                ImGui.Indent();
                ImGui.Text($"Material: {selected.MaterialName}");
                ImGui.Text($"Confining Pressure: {selected.ConfiningPressure:F1} MPa");
                ImGui.Text($"Failure Stress: {selected.FailureStress:F1} MPa");
                ImGui.Text($"Young's Modulus: {selected.YoungModulus:F0} MPa");
                ImGui.Text($"Poisson's Ratio: {selected.PoissonRatio:F3}");
                if (!string.IsNullOrEmpty(selected.Notes))
                    ImGui.TextWrapped($"Notes: {selected.Notes}");
                ImGui.Unindent();

                ImGui.Spacing();

                if (ImGui.Button("Apply to Current Material"))
                {
                    youngModulus = selected.YoungModulus;
                    poissonRatio = selected.PoissonRatio;
                }

                ImGui.SameLine();

                if (ImGui.Button("Delete"))
                {
                    _calibrationData.RemoveAt(_selectedCalibrationIndex);
                    _selectedCalibrationIndex = -1;
                    SaveCalibrationData();
                }
            }

            ImGui.Spacing();

            if (_calibrationData.Count >= 2)
                if (ImGui.Button("Fit Mohr-Coulomb Parameters", new Vector2(-1, 0)))
                {
                    var (c, phi) = FitMohrCoulombParameters();
                    cohesion = c;
                    frictionAngle = phi;

                    Logger.Log($"[Calibration] Fitted parameters: c = {c:F2} MPa, φ = {phi:F1}°");
                }
        }
        else
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1),
                "No calibration data. Add lab test results to calibrate.");
        }
    }

    private void DrawAddCalibrationPopup()
    {
        if (ImGui.BeginPopupModal("AddCalibration", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Add Laboratory Test Result");
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.InputText("Test Name", ref _newTestName, 256);
            ImGui.InputText("Material Name", ref _newMaterialName, 256);
            ImGui.DragFloat("Confining Pressure (MPa)", ref _newConfiningPressure, 0.5f, 0f, 200f);
            ImGui.DragFloat("Failure Stress σ1 (MPa)", ref _newFailureStress, 1f, 0f, 1000f);
            ImGui.DragFloat("Young's Modulus (MPa)", ref _newYoungModulus, 100f, 100f, 200000f);
            ImGui.DragFloat("Poisson's Ratio", ref _newPoissonRatio, 0.01f, 0f, 0.5f);
            ImGui.InputTextMultiline("Notes", ref _newNotes, 1024, new Vector2(400, 60));

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.Button("Add", new Vector2(120, 0)))
            {
                var point = new CalibrationPoint
                {
                    TestName = _newTestName,
                    TestDate = DateTime.Now,
                    MaterialName = _newMaterialName,
                    ConfiningPressure = _newConfiningPressure,
                    FailureStress = _newFailureStress,
                    YoungModulus = _newYoungModulus,
                    PoissonRatio = _newPoissonRatio,
                    Notes = _newNotes
                };

                _calibrationData.Add(point);
                SaveCalibrationData();

                // Reset form
                _newTestName = "";
                _newMaterialName = "";
                _newConfiningPressure = 0f;
                _newFailureStress = 0f;
                _newNotes = "";

                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(120, 0))) ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }
    }

    private (float cohesion, float frictionAngle) FitMohrCoulombParameters()
    {
        // Fit Mohr-Coulomb failure envelope: σ1 = σ3·Nφ + 2c·√Nφ
        // Where Nφ = (1 + sin φ) / (1 - sin φ)

        var points = _calibrationData
            .Select(p => (sigma3: p.ConfiningPressure, sigma1: p.FailureStress))
            .ToList();

        if (points.Count < 2)
            return (10f, 30f); // Default values

        // Linear regression: σ1 = A·σ3 + B
        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
        var n = points.Count;

        foreach (var (sigma3, sigma1) in points)
        {
            sumX += sigma3;
            sumY += sigma1;
            sumXY += sigma3 * sigma1;
            sumX2 += sigma3 * sigma3;
        }

        var A = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
        var B = (sumY - A * sumX) / n;

        // Convert to Mohr-Coulomb parameters
        var Nphi = A;
        var phi = MathF.Asin((float)((Nphi - 1) / (Nphi + 1))) * 180f / MathF.PI;
        var c = (float)(B / (2 * Math.Sqrt(Nphi)));

        // Clamp to reasonable ranges
        phi = Math.Clamp(phi, 10f, 70f);
        c = Math.Clamp(c, 0.1f, 100f);

        return (c, phi);
    }

    private void LoadCalibrationData()
    {
        try
        {
            if (File.Exists(_calibrationFilePath))
            {
                var json = File.ReadAllText(_calibrationFilePath);
                var data = JsonSerializer.Deserialize<List<CalibrationPoint>>(json);
                if (data != null)
                {
                    _calibrationData.Clear();
                    _calibrationData.AddRange(data);
                    Logger.Log($"[Calibration] Loaded {_calibrationData.Count} calibration points");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[Calibration] Failed to load: {ex.Message}");
        }
    }

    private void SaveCalibrationData()
    {
        try
        {
            var directory = Path.GetDirectoryName(_calibrationFilePath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(_calibrationData,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_calibrationFilePath, json);

            Logger.Log($"[Calibration] Saved {_calibrationData.Count} calibration points");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[Calibration] Failed to save: {ex.Message}");
        }
    }
}