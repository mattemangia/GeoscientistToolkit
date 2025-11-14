using System;
using System.Numerics;
using GeoscientistToolkit.Analysis.Seismology;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;

namespace GeoscientistToolkit.Data
{
    /// <summary>
    /// UI tools for earthquake simulation datasets
    /// </summary>
    public class EarthquakeSimulationTools : IDatasetTools
    {
        private bool _isRunning = false;
        private EarthquakeSimulationEngine? _engine;
        private float _progress = 0f;
        private string _progressMessage = "";
        private bool _parametersExpanded = true;
        private bool _resultsExpanded = true;
        private bool _visualizationExpanded = true;

        // Visualization options
        private int _visualizationMode = 0; // 0=PGA, 1=PGV, 2=Damage, 3=Fractures, 4=Arrivals
        private bool _showEpicenter = true;
        private float _colorScale = 1.0f;

        public void Draw(Dataset dataset)
        {
            if (dataset is not EarthquakeDataset eqDataset)
            {
                ImGui.TextDisabled("Earthquake tools are only available for Earthquake datasets.");
                return;
            }

            // Parameters Section
            if (ImGui.CollapsingHeader("Simulation Parameters", ref _parametersExpanded,
                ImGuiTreeNodeFlags.DefaultOpen))
            {
                DrawParametersUI(eqDataset);
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Run Simulation
            DrawSimulationControls(eqDataset);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Results Section
            if (eqDataset.HasResults)
            {
                if (ImGui.CollapsingHeader("Simulation Results", ref _resultsExpanded,
                    ImGuiTreeNodeFlags.DefaultOpen))
                {
                    DrawResultsUI(eqDataset);
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                // Visualization Section
                if (ImGui.CollapsingHeader("Visualization", ref _visualizationExpanded,
                    ImGuiTreeNodeFlags.DefaultOpen))
                {
                    DrawVisualizationUI(eqDataset);
                }
            }
        }

        private void DrawParametersUI(EarthquakeDataset dataset)
        {
            var parameters = dataset.Parameters;
            if (parameters == null)
            {
                if (ImGui.Button("Create Default Parameters"))
                {
                    dataset.SetParameters(EarthquakeSimulationParameters.CreateDefault());
                }
                return;
            }

            // Source Location
            ImGui.Text("Source Location");
            ImGui.Indent();

            var epicenterLat = (float)parameters.EpicenterLatitude;
            if (ImGui.DragFloat("Epicenter Latitude", ref epicenterLat, 0.01f, -90f, 90f))
                parameters.EpicenterLatitude = epicenterLat;

            var epicenterLon = (float)parameters.EpicenterLongitude;
            if (ImGui.DragFloat("Epicenter Longitude", ref epicenterLon, 0.01f, -180f, 180f))
                parameters.EpicenterLongitude = epicenterLon;

            var depth = (float)parameters.HypocenterDepthKm;
            if (ImGui.DragFloat("Hypocenter Depth (km)", ref depth, 0.1f, 0f, 100f))
                parameters.HypocenterDepthKm = depth;

            ImGui.Unindent();
            ImGui.Spacing();

            // Source Characteristics
            ImGui.Text("Source Characteristics");
            ImGui.Indent();

            var magnitude = (float)parameters.MomentMagnitude;
            if (ImGui.SliderFloat("Moment Magnitude", ref magnitude, 3.0f, 9.0f))
                parameters.MomentMagnitude = magnitude;

            int mechanismIdx = (int)parameters.Mechanism;
            string[] mechanisms = { "Strike-Slip", "Normal", "Reverse", "Oblique" };
            if (ImGui.Combo("Fault Mechanism", ref mechanismIdx, mechanisms, mechanisms.Length))
            {
                parameters.Mechanism = (FaultMechanism)mechanismIdx;
                parameters.SetMechanismFromType();
            }

            var strike = (float)parameters.StrikeDegrees;
            if (ImGui.DragFloat("Strike (°)", ref strike, 1f, 0f, 360f))
                parameters.StrikeDegrees = strike;

            var dip = (float)parameters.DipDegrees;
            if (ImGui.DragFloat("Dip (°)", ref dip, 1f, 0f, 90f))
                parameters.DipDegrees = dip;

            var rake = (float)parameters.RakeDegrees;
            if (ImGui.DragFloat("Rake (°)", ref rake, 1f, -180f, 180f))
                parameters.RakeDegrees = rake;

            ImGui.Unindent();
            ImGui.Spacing();

            // Domain
            ImGui.Text("Simulation Domain");
            ImGui.Indent();

            var minLat = (float)parameters.MinLatitude;
            if (ImGui.DragFloat("Min Latitude", ref minLat, 0.01f, -90f, 90f))
                parameters.MinLatitude = minLat;

            var maxLat = (float)parameters.MaxLatitude;
            if (ImGui.DragFloat("Max Latitude", ref maxLat, 0.01f, -90f, 90f))
                parameters.MaxLatitude = maxLat;

            var minLon = (float)parameters.MinLongitude;
            if (ImGui.DragFloat("Min Longitude", ref minLon, 0.01f, -180f, 180f))
                parameters.MinLongitude = minLon;

            var maxLon = (float)parameters.MaxLongitude;
            if (ImGui.DragFloat("Max Longitude", ref maxLon, 0.01f, -180f, 180f))
                parameters.MaxLongitude = maxLon;

            var maxDepth = (float)parameters.MaxDepthKm;
            if (ImGui.DragFloat("Max Depth (km)", ref maxDepth, 1f, 10f, 200f))
                parameters.MaxDepthKm = maxDepth;

            ImGui.Unindent();
            ImGui.Spacing();

            // Grid Resolution
            ImGui.Text("Grid Resolution");
            ImGui.Indent();

            int gridNX = parameters.GridNX;
            if (ImGui.SliderInt("Grid NX", ref gridNX, 20, 200))
                parameters.GridNX = gridNX;

            int gridNY = parameters.GridNY;
            if (ImGui.SliderInt("Grid NY", ref gridNY, 20, 200))
                parameters.GridNY = gridNY;

            int gridNZ = parameters.GridNZ;
            if (ImGui.SliderInt("Grid NZ", ref gridNZ, 10, 100))
                parameters.GridNZ = gridNZ;

            ImGui.Text($"Grid spacing: {parameters.GridDX:F3}° × {parameters.GridDY:F3}° × {parameters.GridDZ:F2} km");

            ImGui.Unindent();
            ImGui.Spacing();

            // Time Parameters
            ImGui.Text("Time Parameters");
            ImGui.Indent();

            var duration = (float)parameters.SimulationDurationSeconds;
            if (ImGui.DragFloat("Duration (s)", ref duration, 1f, 10f, 300f))
                parameters.SimulationDurationSeconds = duration;

            var timeStep = (float)parameters.TimeStepSeconds;
            if (ImGui.DragFloat("Time Step (s)", ref timeStep, 0.001f, 0.001f, 0.1f, "%.4f"))
                parameters.TimeStepSeconds = timeStep;

            ImGui.Text($"Total steps: {(int)(parameters.SimulationDurationSeconds / parameters.TimeStepSeconds):N0}");

            ImGui.Unindent();
            ImGui.Spacing();

            // Analysis Options
            ImGui.Text("Analysis Options");
            ImGui.Indent();

            bool calcDamage = parameters.CalculateDamage;
            if (ImGui.Checkbox("Calculate Damage", ref calcDamage))
                parameters.CalculateDamage = calcDamage;

            if (parameters.CalculateDamage)
            {
                int vulnIdx = (int)parameters.BuildingVulnerability - (int)VulnerabilityClass.A;
                string[] vulnClasses = { "A (Very Vulnerable)", "B (Vulnerable)", "C (Medium)",
                                        "D (Low)", "E (Very Low)", "F (Extremely Low)" };
                if (ImGui.Combo("Building Vulnerability", ref vulnIdx, vulnClasses, vulnClasses.Length))
                    parameters.BuildingVulnerability = (VulnerabilityClass)((int)VulnerabilityClass.A + vulnIdx);
            }

            bool calcFractures = parameters.CalculateFractures;
            if (ImGui.Checkbox("Calculate Fractures", ref calcFractures))
                parameters.CalculateFractures = calcFractures;

            bool trackWaves = parameters.TrackWaveTypes;
            if (ImGui.Checkbox("Track Wave Types", ref trackWaves))
                parameters.TrackWaveTypes = trackWaves;

            ImGui.Unindent();

            // Estimated Source Properties
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("Estimated Source Properties:");
            ImGui.Text($"  Seismic Moment: {parameters.SeismicMoment:E2} N·m");
            ImGui.Text($"  Rupture Area: {parameters.EstimateRuptureArea():F1} km²");
            ImGui.Text($"  Rupture Length: {parameters.EstimateRuptureLength():F1} km");
            ImGui.Text($"  Average Slip: {parameters.EstimateAverageSlip():F2} m");
            ImGui.Text($"  Rupture Duration: {parameters.EstimateRuptureDuration():F1} s");
        }

        private void DrawSimulationControls(EarthquakeDataset dataset)
        {
            if (_isRunning)
            {
                ImGui.Text("Simulation Running...");
                ImGui.ProgressBar(_progress, new Vector2(-1, 0), _progressMessage);

                if (ImGui.Button("Cancel"))
                {
                    _isRunning = false;
                }
            }
            else
            {
                if (dataset.Parameters == null)
                {
                    ImGui.TextDisabled("Set parameters first");
                    return;
                }

                // Validate
                bool isValid = dataset.Parameters.Validate(out string errorMessage);

                if (!isValid)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.3f, 0.3f, 1f));
                    ImGui.Text($"Invalid parameters: {errorMessage}");
                    ImGui.PopStyleColor();
                }

                ImGui.BeginDisabled(!isValid);

                if (ImGui.Button("Run Simulation", new Vector2(-1, 40)))
                {
                    RunSimulation(dataset);
                }

                ImGui.EndDisabled();

                if (dataset.HasResults)
                {
                    ImGui.SameLine();
                    if (ImGui.Button("Export Results"))
                    {
                        ExportResults(dataset);
                    }
                }
            }
        }

        private void DrawResultsUI(EarthquakeDataset dataset)
        {
            var results = dataset.Results;
            if (results == null) return;

            ImGui.Text(results.GetSummary());

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Statistics
            ImGui.Text("Ground Motion Statistics:");
            ImGui.BulletText($"Max PGA: {results.MaxPGA:F3} g");
            ImGui.BulletText($"Max PGV: {results.MaxPGV:F1} cm/s");
            ImGui.BulletText($"Max PGD: {results.MaxPGD:F1} cm");

            ImGui.Spacing();

            ImGui.Text("Impact Estimate:");
            ImGui.BulletText($"Affected Area: {results.AffectedAreaKm2:F1} km²");
            ImGui.BulletText($"Buildings Affected: ~{results.EstimatedBuildingsAffected:N0}");

            ImGui.Spacing();

            ImGui.Text("Computation:");
            ImGui.BulletText($"Simulation Time: {results.SimulationDurationSeconds:F1} s");
            ImGui.BulletText($"Computation Time: {results.ComputationTimeSeconds:F2} s");
            ImGui.BulletText($"Time Steps: {results.TotalTimeSteps:N0}");
        }

        private void DrawVisualizationUI(EarthquakeDataset dataset)
        {
            string[] modes = { "Peak Ground Acceleration (PGA)", "Peak Ground Velocity (PGV)",
                             "Damage Map", "Fracture Density", "Wave Arrivals" };

            ImGui.Combo("Visualization Mode", ref _visualizationMode, modes, modes.Length);

            ImGui.SliderFloat("Color Scale", ref _colorScale, 0.1f, 10f);

            ImGui.Checkbox("Show Epicenter", ref _showEpicenter);

            ImGui.Spacing();
            ImGui.Text("(3D visualization will be rendered in the main viewport)");
        }

        private async void RunSimulation(EarthquakeDataset dataset)
        {
            _isRunning = true;
            _progress = 0f;
            _progressMessage = "Initializing...";

            try
            {
                _engine = new EarthquakeSimulationEngine(dataset.Parameters!);

                _engine.ProgressChanged += (sender, e) =>
                {
                    _progress = e.Percentage / 100f;
                    _progressMessage = e.Message;
                };

                await System.Threading.Tasks.Task.Run(() =>
                {
                    _engine.Initialize();
                    var results = _engine.Run();
                    dataset.SetResults(results);
                    dataset.Save();
                });

                _progressMessage = "Complete!";
            }
            catch (Exception ex)
            {
                _progressMessage = $"Error: {ex.Message}";
                Console.WriteLine($"Earthquake simulation error: {ex}");
            }
            finally
            {
                _isRunning = false;
            }
        }

        private void ExportResults(EarthquakeDataset dataset)
        {
            if (dataset.Results == null) return;

            try
            {
                string outputDir = Path.Combine(
                    Path.GetDirectoryName(dataset.FilePath) ?? "",
                    "earthquake_results"
                );

                dataset.ExportToGIS(outputDir);

                Console.WriteLine($"Results exported to: {outputDir}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Export error: {ex.Message}");
            }
        }
    }
}
