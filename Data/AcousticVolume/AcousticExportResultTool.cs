// GeoscientistToolkit/UI/AcousticVolume/AcousticExportResultsTool.cs
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using System;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace GeoscientistToolkit.UI.AcousticVolume
{
    public class AcousticExportResultsTool : IDatasetTools
    {
        private readonly ImGuiExportFileDialog _exportDialog;
        private readonly ProgressBarDialog _progressDialog;

        private enum ExportFormat { CSV, TextReport }
        private int _formatSelection = (int)ExportFormat.CSV;
        private int _samplingStep = 1;
        private bool _isExporting = false;

        private static readonly string[] CsvHeader =
        {
            "X", "Y", "Z", "Density_kg_m3", "Vp_m_s", "Vs_m_s", "VpVs_Ratio", "YoungsModulus_GPa",
            "PoissonRatio", "BulkModulus_GPa", "ShearModulus_GPa", "LameLambda_GPa", "Damage"
        };

        public AcousticExportResultsTool()
        {
            _exportDialog = new ImGuiExportFileDialog(nameof(AcousticExportResultsTool), "Export Acoustic Properties");
            _progressDialog = new ProgressBarDialog("Exporting Data");
        }

        public void Draw(Dataset dataset)
        {
            if (dataset is not AcousticVolumeDataset ad)
            {
                ImGui.TextDisabled("This tool requires an Acoustic Volume Dataset.");
                return;
            }

            if (ad.DensityData == null)
            {
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "Warning: Density data has not been calibrated.");
                ImGui.TextWrapped("Please run the Density Calibration tool before exporting results. The exported file will be empty or contain default values.");
                return;
            }

            if (_isExporting)
            {
                ImGui.BeginDisabled();
            }

            ImGui.Text("Select export format and options:");

            ImGui.RadioButton("CSV (Comma-Separated Values)", ref _formatSelection, (int)ExportFormat.CSV);
            ImGui.RadioButton("Formatted Text Report", ref _formatSelection, (int)ExportFormat.TextReport);

            ImGui.Spacing();
            ImGui.SliderInt("Sampling Step", ref _samplingStep, 1, 50);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Exports data for every Nth voxel in each dimension to reduce file size.");
            }

            ImGui.Spacing();
            ImGui.Separator();

            if (ImGui.Button("Export Results...", new Vector2(-1, 0)))
            {
                var format = (ExportFormat)_formatSelection;
                string extension = format == ExportFormat.CSV ? ".csv" : ".txt";
                _exportDialog.SetExtensions(new ImGuiExportFileDialog.ExtensionOption(extension, format.ToString()));
                _exportDialog.Open($"{ad.Name}_properties");
            }

            if (_isExporting)
            {
                ImGui.EndDisabled();
            }

            HandleDialogs(ad);
        }

        private void HandleDialogs(AcousticVolumeDataset dataset)
        {
            if (_exportDialog.Submit())
            {
                string path = _exportDialog.SelectedPath;
                _isExporting = true;
                _progressDialog.Open($"Exporting to {Path.GetFileName(path)}...");
                Task.Run(() => ExportDataAsync(dataset, path, _progressDialog.CancellationToken), _progressDialog.CancellationToken);
            }

            if (_isExporting)
            {
                // The progress dialog's Submit method returns true if the user cancels.
                _progressDialog.Submit();
            }
        }

        private async Task ExportDataAsync(AcousticVolumeDataset ad, string path, CancellationToken token)
        {
            try
            {
                var densityVolume = ad.DensityData;
                var damageVolume = ad.DamageField;
                int width = densityVolume.Width;
                int height = densityVolume.Height;
                int depth = densityVolume.Depth;
                var exportFormat = (ExportFormat)_formatSelection;

                using (var writer = new StreamWriter(path))
                {
                    if (exportFormat == ExportFormat.CSV)
                    {
                        await writer.WriteLineAsync(string.Join(",", CsvHeader));
                    }
                    else
                    {
                        await writer.WriteLineAsync($"# Acoustic Properties Report for: {ad.Name}");
                        await writer.WriteLineAsync($"# Exported on: {DateTime.Now}");
                        await writer.WriteLineAsync($"# Dimensions: {width}x{height}x{depth}, Sampling Step: {_samplingStep}");
                        await writer.WriteLineAsync("# ---------------------------------------------------");
                    }

                    long totalVoxelsToProcess = (width / _samplingStep) * (height / _samplingStep) * (depth / _samplingStep);
                    if (totalVoxelsToProcess == 0) totalVoxelsToProcess = 1; // Avoid division by zero
                    long processedVoxels = 0;

                    for (int z = 0; z < depth; z += _samplingStep)
                    {
                        token.ThrowIfCancellationRequested();

                        for (int y = 0; y < height; y += _samplingStep)
                        {
                            for (int x = 0; x < width; x += _samplingStep)
                            {
                                float density = densityVolume.GetDensity(x, y, z);
                                float vp = densityVolume.GetPWaveVelocity(x, y, z);
                                float vs = densityVolume.GetSWaveVelocity(x, y, z);
                                float youngsModulusPa = densityVolume.GetYoungsModulus(x, y, z);
                                float poisson = densityVolume.GetPoissonRatio(x, y, z);
                                float bulkModulusPa = densityVolume.GetBulkModulus(x, y, z);
                                float shearModulusPa = densityVolume.GetShearModulus(x, y, z);
                                float damage = (damageVolume != null) ? damageVolume[x, y, z] / 255.0f : 0.0f;

                                float vpVs = (vs > 1e-6f) ? vp / vs : 0;
                                // More robust calculation for Lamé's first parameter (lambda) using K and μ (shear modulus).
                                // λ = K - (2/3)μ
                                float lambdaPa = bulkModulusPa - (2f / 3f) * shearModulusPa;

                                if (exportFormat == ExportFormat.CSV)
                                {
                                    await writer.WriteLineAsync($"{x},{y},{z},{density:F2},{vp:F2},{vs:F2},{vpVs:F4},{youngsModulusPa / 1e9:F4},{poisson:F4},{bulkModulusPa / 1e9:F4},{shearModulusPa / 1e9:F4},{lambdaPa / 1e9:F4},{damage:F4}");
                                }
                                else
                                {
                                    await writer.WriteLineAsync($"Voxel ({x}, {y}, {z}):");
                                    await writer.WriteLineAsync($"  Density: {density:F2} kg/m³");
                                    await writer.WriteLineAsync($"  Vp/Vs: {vp:F0} m/s / {vs:F0} m/s (Ratio: {vpVs:F3})");
                                    await writer.WriteLineAsync($"  Young's Modulus: {youngsModulusPa / 1e9:F2} GPa");
                                    await writer.WriteLineAsync($"  Poisson's Ratio: {poisson:F3}");
                                    await writer.WriteLineAsync($"  Bulk/Shear Modulus: {bulkModulusPa / 1e9:F2} / {shearModulusPa / 1e9:F2} GPa");
                                    await writer.WriteLineAsync($"  Lamé Parameters (λ,μ): {lambdaPa / 1e9:F2}, {shearModulusPa / 1e9:F2} GPa");
                                    await writer.WriteLineAsync($"  Damage: {damage:P1}");
                                    await writer.WriteLineAsync();
                                }
                                processedVoxels++;
                            }
                        }
                        _progressDialog.Update((float)processedVoxels / totalVoxelsToProcess, $"Processing slice {z + 1}/{depth}...");
                    }
                }
                Logger.Log($"[AcousticExportResultsTool] Successfully exported data to {path}");
            }
            catch (OperationCanceledException)
            {
                Logger.LogWarning($"[AcousticExportResultsTool] Export to '{path}' was cancelled by the user.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[AcousticExportResultsTool] Failed to export data: {ex.Message}");
            }
            finally
            {
                _isExporting = false;
                _progressDialog.Close();
            }
        }
    }
}