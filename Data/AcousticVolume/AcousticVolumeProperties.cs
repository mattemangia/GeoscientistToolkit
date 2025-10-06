// GeoscientistToolkit/UI/AcousticVolume/AcousticVolumeProperties.cs
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;
using System;

namespace GeoscientistToolkit.Data.AcousticVolume
{
    /// <summary>
    /// Properties renderer for acoustic volume datasets
    /// </summary>
    public class AcousticVolumeProperties : IDatasetPropertiesRenderer
    {
        public void Draw(Dataset dataset)
        {
            if (dataset is not AcousticVolumeDataset acousticDataset)
                return;
            
            ImGui.Text("Acoustic Volume Properties");
            ImGui.Separator();
            
            // Wave velocities
            if (ImGui.CollapsingHeader("Wave Properties", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Text($"P-Wave Velocity: {acousticDataset.PWaveVelocity:F2} m/s");
                ImGui.Text($"S-Wave Velocity: {acousticDataset.SWaveVelocity:F2} m/s");
                ImGui.Text($"Vp/Vs Ratio: {acousticDataset.VpVsRatio:F3}");
                
                // Show quality factor if calculated
                if (acousticDataset.VpVsRatio > 0)
                {
                    double quality = acousticDataset.PWaveVelocity / acousticDataset.SWaveVelocity;
                    ImGui.Text($"Quality Factor: {quality:F2}");
                }
                
                // Calculate wave speed ratio
                if (acousticDataset.PWaveVelocity > 0 && acousticDataset.SWaveVelocity > 0)
                {
                    ImGui.Spacing();
                    ImGui.TextDisabled($"P-wave is {acousticDataset.PWaveVelocity / acousticDataset.SWaveVelocity:F2}x faster than S-wave");
                }
            }
            
            // Material properties
            if (ImGui.CollapsingHeader("Material Properties"))
            {
                ImGui.Text($"Young's Modulus: {acousticDataset.YoungsModulusMPa:F0} MPa");
                ImGui.Text($"Poisson's Ratio: {acousticDataset.PoissonRatio:F3}");
                ImGui.Text($"Confining Pressure: {acousticDataset.ConfiningPressureMPa:F1} MPa");
                
                ImGui.Spacing();
                ImGui.Text("Derived Properties:");
                ImGui.Indent();
                
                // Calculate and show bulk modulus
                if (acousticDataset.PoissonRatio < 0.5)
                {
                    double bulk = acousticDataset.YoungsModulusMPa / (3 * (1 - 2 * acousticDataset.PoissonRatio));
                    ImGui.Text($"Bulk Modulus: {bulk:F0} MPa");
                }
                
                // Calculate and show shear modulus
                double shear = acousticDataset.YoungsModulusMPa / (2 * (1 + acousticDataset.PoissonRatio));
                ImGui.Text($"Shear Modulus: {shear:F0} MPa");
                
                // Calculate Lame parameters
                double lambda = acousticDataset.YoungsModulusMPa * acousticDataset.PoissonRatio / 
                               ((1 + acousticDataset.PoissonRatio) * (1 - 2 * acousticDataset.PoissonRatio));
                ImGui.Text($"Lamé λ: {lambda:F0} MPa");
                ImGui.Text($"Lamé μ: {shear:F0} MPa");
                
                ImGui.Unindent();
            }
            
            // Source parameters
            if (ImGui.CollapsingHeader("Source Parameters"))
            {
                ImGui.Text($"Frequency: {acousticDataset.SourceFrequencyKHz:F0} kHz");
                ImGui.Text($"Energy: {acousticDataset.SourceEnergyJ:F2} J");
                ImGui.Text($"Period: {1.0 / acousticDataset.SourceFrequencyKHz:F3} ms");
                
                ImGui.Spacing();
                ImGui.Text("Wavelengths:");
                ImGui.Indent();
                
                // Calculate wavelengths
                if (acousticDataset.PWaveVelocity > 0 && acousticDataset.SourceFrequencyKHz > 0)
                {
                    double pWavelength = acousticDataset.PWaveVelocity / (acousticDataset.SourceFrequencyKHz * 1000); // in meters
                    ImGui.Text($"P-Wave: {pWavelength * 1000:F2} mm");
                    
                    // Resolution info
                    if (acousticDataset.PWaveField != null && acousticDataset.VoxelSize > 0)
                    {
                        double resolution = pWavelength / acousticDataset.VoxelSize; // meters / meters
                        ImGui.TextDisabled($"  ({resolution:F1} voxels per wavelength)");
                    }
                }
                
                if (acousticDataset.SWaveVelocity > 0 && acousticDataset.SourceFrequencyKHz > 0)
                {
                    double sWavelength = acousticDataset.SWaveVelocity / (acousticDataset.SourceFrequencyKHz * 1000); // in meters
                    ImGui.Text($"S-Wave: {sWavelength * 1000:F2} mm");
                    
                    // Resolution info
                    if (acousticDataset.SWaveField != null && acousticDataset.VoxelSize > 0)
                    {
                        double resolution = sWavelength / acousticDataset.VoxelSize; // meters / meters
                        ImGui.TextDisabled($"  ({resolution:F1} voxels per wavelength)");
                    }
                }
                
                ImGui.Unindent();
            }
            
            // Simulation information
            if (ImGui.CollapsingHeader("Simulation Info"))
            {
                ImGui.Text($"Time Steps: {acousticDataset.TimeSteps:N0}");
                ImGui.Text($"Computation Time: {FormatTime(acousticDataset.ComputationTime)}");
                
                if (acousticDataset.ComputationTime.TotalSeconds > 0)
                {
                    double stepsPerSecond = acousticDataset.TimeSteps / acousticDataset.ComputationTime.TotalSeconds;
                    ImGui.Text($"Performance: {stepsPerSecond:F0} steps/sec");
                    
                    // Estimate time per voxel
                    if (acousticDataset.CombinedWaveField != null)
                    {
                        long totalVoxels = (long)acousticDataset.CombinedWaveField.Width * 
                                          acousticDataset.CombinedWaveField.Height * 
                                          acousticDataset.CombinedWaveField.Depth;
                        double timePerVoxel = acousticDataset.ComputationTime.TotalMilliseconds / (totalVoxels * acousticDataset.TimeSteps);
                        ImGui.Text($"Speed: {timePerVoxel * 1000000:F2} ns/voxel/step");
                    }
                }
                
                ImGui.Spacing();
                
                if (!string.IsNullOrEmpty(acousticDataset.SourceDatasetPath))
                {
                    ImGui.Text("Source Dataset:");
                    ImGui.Indent();
                    ImGui.TextWrapped(acousticDataset.SourceDatasetPath);
                    ImGui.Unindent();
                }
                
                if (!string.IsNullOrEmpty(acousticDataset.SourceMaterialName))
                {
                    ImGui.Text($"Material: {acousticDataset.SourceMaterialName}");
                }
            }
            
            // Volume data information
            if (ImGui.CollapsingHeader("Volume Data"))
            {
                bool hasData = false;
                
                if (acousticDataset.PWaveField != null)
                {
                    hasData = true;
                    DrawVolumeInfo("P-Wave Field", acousticDataset.PWaveField, acousticDataset);
                }
                
                if (acousticDataset.SWaveField != null)
                {
                    hasData = true;
                    DrawVolumeInfo("S-Wave Field", acousticDataset.SWaveField, acousticDataset);
                }
                
                if (acousticDataset.CombinedWaveField != null)
                {
                    hasData = true;
                    DrawVolumeInfo("Combined Field", acousticDataset.CombinedWaveField, acousticDataset);
                }
                
                if (!hasData)
                {
                    ImGui.TextDisabled("No volume data loaded");
                }
                
                // Time series information
                if (acousticDataset.TimeSeriesSnapshots != null && acousticDataset.TimeSeriesSnapshots.Count > 0)
                {
                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Text("Time Series Animation:");
                    ImGui.Indent();
                    ImGui.Text($"Frames: {acousticDataset.TimeSeriesSnapshots.Count}");
                    
                    var firstSnapshot = acousticDataset.TimeSeriesSnapshots[0];
                    var lastSnapshot = acousticDataset.TimeSeriesSnapshots[acousticDataset.TimeSeriesSnapshots.Count - 1];
                    ImGui.Text($"Time Range: {firstSnapshot.SimulationTime:F6} - {lastSnapshot.SimulationTime:F6} s");
                    ImGui.Text($"Duration: {(lastSnapshot.SimulationTime - firstSnapshot.SimulationTime) * 1000:F3} ms");
                    
                    if (acousticDataset.TimeSeriesSnapshots.Count > 1)
                    {
                        double frameDuration = (lastSnapshot.SimulationTime - firstSnapshot.SimulationTime) / 
                                              (acousticDataset.TimeSeriesSnapshots.Count - 1);
                        ImGui.Text($"Frame Interval: {frameDuration * 1000:F3} ms");
                        ImGui.Text($"Effective FPS: {1.0 / frameDuration:F1} Hz");
                    }
                    
                    // Memory usage for time series
                    long timeSeriesSize = 0;
                    foreach (var snapshot in acousticDataset.TimeSeriesSnapshots)
                    {
                        timeSeriesSize += snapshot.GetSizeInBytes();
                    }
                    ImGui.Text($"Memory Usage: {FormatBytes(timeSeriesSize)}");
                    
                    ImGui.Unindent();
                }
            }
            
            // Storage information
            if (ImGui.CollapsingHeader("Storage"))
            {
                long totalSize = acousticDataset.GetSizeInBytes();
                ImGui.Text($"Total Size: {FormatBytes(totalSize)}");
                
                // Breakdown by component
                ImGui.Indent();
                
                if (acousticDataset.PWaveField != null)
                {
                    long size = (long)acousticDataset.PWaveField.Width * 
                               acousticDataset.PWaveField.Height * 
                               acousticDataset.PWaveField.Depth;
                    ImGui.Text($"P-Wave: {FormatBytes(size)}");
                }
                
                if (acousticDataset.SWaveField != null)
                {
                    long size = (long)acousticDataset.SWaveField.Width * 
                               acousticDataset.SWaveField.Height * 
                               acousticDataset.SWaveField.Depth;
                    ImGui.Text($"S-Wave: {FormatBytes(size)}");
                }
                
                if (acousticDataset.CombinedWaveField != null)
                {
                    long size = (long)acousticDataset.CombinedWaveField.Width * 
                               acousticDataset.CombinedWaveField.Height * 
                               acousticDataset.CombinedWaveField.Depth;
                    ImGui.Text($"Combined: {FormatBytes(size)}");
                }
                
                if (acousticDataset.TimeSeriesSnapshots != null && acousticDataset.TimeSeriesSnapshots.Count > 0)
                {
                    long timeSeriesSize = 0;
                    foreach (var snapshot in acousticDataset.TimeSeriesSnapshots)
                    {
                        timeSeriesSize += snapshot.GetSizeInBytes();
                    }
                    ImGui.Text($"Time Series: {FormatBytes(timeSeriesSize)}");
                }
                
                ImGui.Unindent();
                
                ImGui.Spacing();
                
                if (!string.IsNullOrEmpty(acousticDataset.FilePath))
                {
                    ImGui.Text("Path:");
                    ImGui.Indent();
                    ImGui.TextWrapped(acousticDataset.FilePath);
                    ImGui.Unindent();
                }
                
                if (acousticDataset.IsMissing)
                {
                    ImGui.TextColored(new System.Numerics.Vector4(1, 0.5f, 0, 1), "Warning: Dataset files are missing!");
                }
            }
        }
        
        private void DrawVolumeInfo(string name, Data.VolumeData.ChunkedVolume volume, AcousticVolumeDataset acousticDataset)
        {
            ImGui.Text($"{name}:");
            ImGui.Indent();
            ImGui.Text($"Dimensions: {volume.Width} × {volume.Height} × {volume.Depth}");
            
            long voxelCount = (long)volume.Width * volume.Height * volume.Depth;
            ImGui.Text($"Voxels: {voxelCount:N0}");
            
            // Calculate and display physical size if voxel size is known
            if (acousticDataset.VoxelSize > 0)
            {
                double sizeX = volume.Width * acousticDataset.VoxelSize * 1000;  // in mm
                double sizeY = volume.Height * acousticDataset.VoxelSize * 1000; // in mm
                double sizeZ = volume.Depth * acousticDataset.VoxelSize * 1000;  // in mm
                ImGui.Text($"Physical Size: {sizeX:F1} × {sizeY:F1} × {sizeZ:F1} mm");
            }
            else
            {
                ImGui.TextDisabled("Physical size information not available");
            }
            
            ImGui.Unindent();
            ImGui.Spacing();
        }
        
        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }
        
        private string FormatTime(TimeSpan time)
        {
            if (time.TotalHours >= 1)
                return $"{time.TotalHours:F1} hours";
            else if (time.TotalMinutes >= 1)
                return $"{time.TotalMinutes:F1} minutes";
            else
                return $"{time.TotalSeconds:F1} seconds";
        }
    }
}