// GeoscientistToolkit/AddIns/AcousticSimulation/UnifiedCalibrationManager.cs

using System.Numerics;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.AddIns.AcousticSimulation;

/// <summary>
///     Unified calibration manager for acoustic simulations
/// </summary>
public class UnifiedCalibrationManager
{
    private float _labConfiningPressure = 1.0f;
    private float _labDensity = 2700f;

    // Lab measurement input
    private string _labMaterialName = "New Material";
    private float _labMeasuredVp = 5000f;
    private float _labMeasuredVs = 3000f;
    private string _labNotes = "";
    private bool _showCalibrationWindow;

    public UnifiedCalibrationManager()
    {
        CalibrationData = new CalibrationData();
    }

    public CalibrationData CalibrationData { get; }

    public bool HasCalibration => CalibrationData?.Points.Count > 0;

    public void DrawCalibrationControls(ref float youngsModulus, ref float poissonRatio)
    {
        if (ImGui.CollapsingHeader("Calibration System"))
        {
            ImGui.Indent();

            // Status
            if (HasCalibration)
                ImGui.TextColored(new Vector4(0, 1, 0, 1),
                    $"{CalibrationData.Points.Count} calibration points loaded");
            else
                ImGui.TextColored(new Vector4(1, 1, 0, 1),
                    "No calibration data");

            if (ImGui.Button("Manage Calibration")) _showCalibrationWindow = true;

            ImGui.SameLine();

            if (HasCalibration && ImGui.Button("Apply Auto-Calibration"))
            {
                var (E, nu) = CalibrationData.InterpolateParameters(_labDensity, _labConfiningPressure);
                youngsModulus = E;
                poissonRatio = nu;
                Logger.Log($"[Calibration] Applied: E={E:F2} MPa, ν={nu:F4}");
            }

            ImGui.Unindent();
        }

        if (_showCalibrationWindow) DrawCalibrationWindow(ref youngsModulus, ref poissonRatio);
    }

    private void DrawCalibrationWindow(ref float youngsModulus, ref float poissonRatio)
    {
        ImGui.SetNextWindowSize(new Vector2(600, 400), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Calibration Manager", ref _showCalibrationWindow))
        {
            ImGui.Text("Add Lab Measurement:");
            ImGui.Separator();

            ImGui.InputText("Material Name", ref _labMaterialName, 256);
            ImGui.InputFloat("Density (kg/m³)", ref _labDensity, 10, 100);
            ImGui.InputFloat("Confining Pressure (MPa)", ref _labConfiningPressure, 0.1f, 1.0f);
            ImGui.InputFloat("Measured Vp (m/s)", ref _labMeasuredVp, 10, 100);
            ImGui.InputFloat("Measured Vs (m/s)", ref _labMeasuredVs, 10, 100);

            var vpVsRatio = _labMeasuredVs > 0 ? _labMeasuredVp / _labMeasuredVs : 0;
            ImGui.Text($"Vp/Vs Ratio: {vpVsRatio:F3}");

            ImGui.InputTextMultiline("Notes", ref _labNotes, 1024, new Vector2(-1, 60));

            if (ImGui.Button("Add Measurement")) AddLabMeasurement();

            ImGui.Separator();

            // Display existing points
            if (CalibrationData.Points.Count > 0)
            {
                ImGui.Text("Calibration Points:");
                if (ImGui.BeginTable("CalibrationTable", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders))
                {
                    ImGui.TableSetupColumn("Material");
                    ImGui.TableSetupColumn("Density");
                    ImGui.TableSetupColumn("Vp");
                    ImGui.TableSetupColumn("Vs");
                    ImGui.TableSetupColumn("Vp/Vs");
                    ImGui.TableSetupColumn("Actions");
                    ImGui.TableHeadersRow();

                    for (var i = 0; i < CalibrationData.Points.Count; i++)
                    {
                        var point = CalibrationData.Points[i];
                        ImGui.TableNextRow();

                        ImGui.TableSetColumnIndex(0);
                        ImGui.Text(point.MaterialName);

                        ImGui.TableSetColumnIndex(1);
                        ImGui.Text($"{point.Density:F0}");

                        ImGui.TableSetColumnIndex(2);
                        ImGui.Text($"{point.MeasuredVp:F0}");

                        ImGui.TableSetColumnIndex(3);
                        ImGui.Text($"{point.MeasuredVs:F0}");

                        ImGui.TableSetColumnIndex(4);
                        ImGui.Text($"{point.MeasuredVpVsRatio:F3}");

                        ImGui.TableSetColumnIndex(5);
                        if (ImGui.SmallButton($"Delete##{i}"))
                        {
                            CalibrationData.Points.RemoveAt(i);
                            break;
                        }
                    }

                    ImGui.EndTable();
                }
            }
        }

        ImGui.End();
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
        Logger.Log($"[Calibration] Added lab measurement: {_labMaterialName}");
    }

    public void AddSimulationResult(string materialName, byte materialID, float density,
        float confiningPressure, float youngsModulus, float poissonRatio,
        double simulatedVp, double simulatedVs)
    {
        var existingPoint = CalibrationData.Points.FirstOrDefault(p =>
            Math.Abs(p.Density - density) < 10 &&
            Math.Abs(p.ConfiningPressureMPa - confiningPressure) < 0.1f &&
            !string.IsNullOrEmpty(p.MaterialName));

        if (existingPoint != null)
        {
            existingPoint.SimulatedVp = simulatedVp;
            existingPoint.SimulatedVs = simulatedVs;
            existingPoint.SimulatedVpVsRatio = simulatedVs > 0 ? simulatedVp / simulatedVs : 0;
            existingPoint.YoungsModulusMPa = youngsModulus;
            existingPoint.PoissonRatio = poissonRatio;

            Logger.Log($"[Calibration] Updated calibration point with simulation: {materialName}");
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
            Logger.Log($"[Calibration] Added simulation calibration point: {materialName}");
        }
    }

    public (float YoungsModulus, float PoissonRatio) GetCalibratedParameters(float density, float confiningPressure)
    {
        return CalibrationData.InterpolateParameters(density, confiningPressure);
    }
}
