// GeoscientistToolkit/UI/AcousticVolume/AcousticExportResultsTool.cs
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace GeoscientistToolkit.UI.AcousticVolume
{
    public class AcousticExportResultsTool : IDatasetTools
    {
        private readonly ImGuiExportFileDialog _exportDialog;
        private readonly ProgressBarDialog _progressDialog;

        private enum ExportFormat { CSV, TextReport }
        private ExportFormat _format = ExportFormat.CSV;
        private int _samplingStep = 1;

        public AcousticExportResultsTool()
        {
            _exportDialog = new ImGuiExportFileDialog("AcousticResultsExport", "Export Acoustic Properties");
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
                ImGui.TextColored(new System.Numerics.Vector4(1, 1, 0, 1), "Warning: Density data has not been calibrated.");
                ImGui.TextWrapped("Please run the Density Calibration tool before exporting results. The exported file will be empty or contain default values.");
                return;
            }

            ImGui.Text("Select export format and options:");

            ImGui.RadioButton("CSV (Comma-Separated Values)", ref UnsafeCast.As<ExportFormat, int>(ref _format), (int)ExportFormat.CSV);
            ImGui.RadioButton("Formatted Text Report", ref UnsafeCast.As<ExportFormat, int>(ref _format), (int)ExportFormat.TextReport);

            ImGui.Spacing();
            ImGui.SliderInt("Sampling Step", ref _samplingStep, 1, 50);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Exports data for every Nth voxel in each dimension to reduce file size.");
            }

            ImGui.Spacing();
            ImGui.Separator();

            if (ImGui.Button("Export Results...", new System.Numerics.Vector2(-1, 0)))
            {
                string extension = _format == ExportFormat.CSV ? ".csv" : ".txt";
                _exportDialog.SetExtensions(new ImGuiExportFileDialog.ExtensionOption(extension, _format.ToString()));
                _exportDialog.Open($"{ad.Name}_properties");
            }

            HandleDialogs(ad);
        }

        private void HandleDialogs(AcousticVolumeDataset dataset)
        {
            if (_exportDialog.Submit())
            {
                string path = _exportDialog.SelectedPath;
                _progressDialog.Open($"Exporting to {Path.GetFileName(path)}...");
                Task.Run(() => ExportData(dataset, path));
            }
            _progressDialog.Submit();
        }

        private void ExportData(AcousticVolumeDataset ad, string path)
        {
            try
            {
                var densityVolume = ad.DensityData;
                var damageVolume = ad.DamageField;
                int width = densityVolume.Width;
                int height = densityVolume.Height;
                int depth = densityVolume.Depth;

                using (var writer = new StreamWriter(path))
                {
                    if (_format == ExportFormat.CSV)
                    {
                        writer.WriteLine("X,Y,Z,Density_kg_m3,Vp_m_s,Vs_m_s,VpVs_Ratio,YoungsModulus_GPa,PoissonRatio,BulkModulus_GPa,ShearModulus_GPa,LameLambda_GPa,Damage");
                    }
                    else
                    {
                        writer.WriteLine($"# Acoustic Properties Report for: {ad.Name}");
                        writer.WriteLine($"# Exported on: {DateTime.Now}");
                        writer.WriteLine($"# Dimensions: {width}x{height}x{depth}, Sampling Step: {_samplingStep}");
                        writer.WriteLine("# ---------------------------------------------------");
                    }

                    long totalVoxelsToProcess = (width / _samplingStep) * (height / _samplingStep) * (depth / _samplingStep);
                    long processedVoxels = 0;

                    for (int z = 0; z < depth; z += _samplingStep)
                    {
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

                                float vpVs = (vs > 0) ? vp / vs : 0;
                                float lambdaPa = youngsModulusPa * poisson / ((1 + poisson) * (1 - 2 * poisson));

                                if (_format == ExportFormat.CSV)
                                {
                                    writer.WriteLine($"{x},{y},{z},{density:F2},{vp:F2},{vs:F2},{vpVs:F4},{youngsModulusPa / 1e9:F4},{poisson:F4},{bulkModulusPa / 1e9:F4},{shearModulusPa / 1e9:F4},{lambdaPa / 1e9:F4},{damage:F4}");
                                }
                                else
                                {
                                    writer.WriteLine($"Voxel ({x}, {y}, {z}):");
                                    writer.WriteLine($"  Density: {density:F2} kg/m³");
                                    writer.WriteLine($"  Vp/Vs: {vp:F0} m/s / {vs:F0} m/s (Ratio: {vpVs:F3})");
                                    writer.WriteLine($"  Young's Modulus: {youngsModulusPa / 1e9:F2} GPa");
                                    writer.WriteLine($"  Poisson's Ratio: {poisson:F3}");
                                    writer.WriteLine($"  Bulk/Shear Modulus: {bulkModulusPa / 1e9:F2} / {shearModulusPa / 1e9:F2} GPa");
                                    writer.WriteLine($"  Lamé Parameters (λ,μ): {lambdaPa / 1e9:F2}, {shearModulusPa / 1e9:F2} GPa");
                                    writer.WriteLine($"  Damage: {damage:P1}");
                                    writer.WriteLine();
                                }
                                processedVoxels++;
                            }
                        }
                        _progressDialog.Update((float)processedVoxels / totalVoxelsToProcess, $"Processing slice {z + 1}/{depth}...");
                    }
                }
                Logger.Log($"[AcousticExportResultsTool] Successfully exported data to {path}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[AcousticExportResultsTool] Failed to export data: {ex.Message}");
            }
            finally
            {
                _progressDialog.Close();
            }
        }
    }

    internal static class UnsafeCast
    {
        public static ref TTo As<TFrom, TTo>(ref TFrom source) where TFrom : struct where TTo : struct
        {
            return ref Unsafe.As<TFrom, TTo>(ref source);
        }
    }
}