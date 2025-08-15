// GeoscientistToolkit/AddIns/AcousticSimulation/UnifiedCalibrationManager.cs
using System;
using System.Collections.Generic;
using System.Linq;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.AddIns.AcousticSimulation
{
    /// <summary>
    /// Unified calibration manager for acoustic simulations
    /// </summary>
    public class UnifiedCalibrationManager
    {
        private CalibrationData _calibrationData;
        private bool _showCalibrationWindow = false;
        
        // Lab measurement input
        private string _labMaterialName = "New Material";
        private float _labDensity = 2700f;
        private float _labConfiningPressure = 1.0f;
        private float _labMeasuredVp = 5000f;
        private float _labMeasuredVs = 3000f;
        private string _labNotes = "";
        
        public CalibrationData CalibrationData => _calibrationData;
        public bool HasCalibration => _calibrationData?.Points.Count > 0;
        
        public UnifiedCalibrationManager()
        {
            _calibrationData = new CalibrationData();
        }
        
        public void DrawCalibrationControls(ref float youngsModulus, ref float poissonRatio)
        {
            if (ImGui.CollapsingHeader("Calibration System"))
            {
                ImGui.Indent();
                
                // Status
                if (HasCalibration)
                {
                    ImGui.TextColored(new System.Numerics.Vector4(0, 1, 0, 1), 
                        $"✓ {_calibrationData.Points.Count} calibration points loaded");
                }
                else
                {
                    ImGui.TextColored(new System.Numerics.Vector4(1, 1, 0, 1), 
                        "⚠ No calibration data");
                }
                
                if (ImGui.Button("Manage Calibration"))
                {
                    _showCalibrationWindow = true;
                }
                
                ImGui.SameLine();
                
                if (HasCalibration && ImGui.Button("Apply Auto-Calibration"))
                {
                    var (E, nu) = _calibrationData.InterpolateParameters(_labDensity, _labConfiningPressure);
                    youngsModulus = E;
                    poissonRatio = nu;
                    Logger.Log($"[Calibration] Applied: E={E:F2} MPa, ν={nu:F4}");
                }
                
                ImGui.Unindent();
            }
            
            if (_showCalibrationWindow)
            {
                DrawCalibrationWindow(ref youngsModulus, ref poissonRatio);
            }
        }
        
        private void DrawCalibrationWindow(ref float youngsModulus, ref float poissonRatio)
        {
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(600, 400), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Calibration Manager", ref _showCalibrationWindow))
            {
                ImGui.Text("Add Lab Measurement:");
                ImGui.Separator();
                
                ImGui.InputText("Material Name", ref _labMaterialName, 256);
                ImGui.InputFloat("Density (kg/m³)", ref _labDensity, 10, 100);
                ImGui.InputFloat("Confining Pressure (MPa)", ref _labConfiningPressure, 0.1f, 1.0f);
                ImGui.InputFloat("Measured Vp (m/s)", ref _labMeasuredVp, 10, 100);
                ImGui.InputFloat("Measured Vs (m/s)", ref _labMeasuredVs, 10, 100);
                
                float vpVsRatio = _labMeasuredVs > 0 ? _labMeasuredVp / _labMeasuredVs : 0;
                ImGui.Text($"Vp/Vs Ratio: {vpVsRatio:F3}");
                
                ImGui.InputTextMultiline("Notes", ref _labNotes, 1024, new System.Numerics.Vector2(-1, 60));
                
                if (ImGui.Button("Add Measurement"))
                {
                    AddLabMeasurement();
                }
                
                ImGui.Separator();
                
                // Display existing points
                if (_calibrationData.Points.Count > 0)
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
                        
                        for (int i = 0; i < _calibrationData.Points.Count; i++)
                        {
                            var point = _calibrationData.Points[i];
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
                                _calibrationData.Points.RemoveAt(i);
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
            float rho = _labDensity;
            float vp = (float)_labMeasuredVp;
            float vs = (float)_labMeasuredVs;
            
            float mu = vs * vs * rho;
            float lambda = vp * vp * rho - 2 * mu;
            
            point.YoungsModulusMPa = mu * (3 * lambda + 2 * mu) / (lambda + mu) / 1e6f;
            point.PoissonRatio = lambda / (2 * (lambda + mu));
            
            _calibrationData.AddPoint(point);
            Logger.Log($"[Calibration] Added lab measurement: {_labMaterialName}");
        }
        
        public void AddSimulationResult(string materialName, byte materialID, float density,
            float confiningPressure, float youngsModulus, float poissonRatio,
            double simulatedVp, double simulatedVs)
        {
            var existingPoint = _calibrationData.Points.FirstOrDefault(p =>
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
                
                _calibrationData.AddPoint(point);
                Logger.Log($"[Calibration] Added simulation calibration point: {materialName}");
            }
        }
        
        public (float YoungsModulus, float PoissonRatio) GetCalibratedParameters(float density, float confiningPressure)
        {
            return _calibrationData.InterpolateParameters(density, confiningPressure);
        }
    }
}