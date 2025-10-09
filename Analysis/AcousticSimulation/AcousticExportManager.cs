// GeoscientistToolkit/Analysis/AcousticSimulation/AcousticExportManager.cs

using System.Numerics;
using System.Text.Json;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.UI;
using GeoscientistToolkit.UI.AcousticVolume;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.AcousticSimulation;

/// <summary>
///     Manages exporting acoustic simulation results with progress tracking.
/// </summary>
public class AcousticExportManager : IDisposable
{
    private readonly ImGuiExportFileDialog _exportDialog;
    private readonly ProgressBarDialog _progressDialog;
    private CalibrationData _calibrationData;
    private float[,,] _damageField; // Store damage field from simulation
    private float[,,] _densityVolume; // ADDED
    private bool _isWaveformViewerOpen;
    private SimulationParameters _parameters;
    private float _pixelSize;
    private float[,,] _poissonRatioVolume; // ADDED
    private SimulationResults _results;
    private CtImageStackDataset _sourceDataset;
    private WaveformViewer _waveformViewer;
    private float[,,] _youngsModulusVolume; // ADDED

    public AcousticExportManager()
    {
        _exportDialog = new ImGuiExportFileDialog("AcousticExport", "Export Acoustic Volume");
        _exportDialog.SetExtensions((".acvol", "Acoustic Volume Package"));
        _progressDialog = new ProgressBarDialog("Exporting Acoustic Volume");
    }

    public void Dispose()
    {
        _waveformViewer?.Dispose();
    }

    public void SetDamageField(float[,,] damageField)
    {
        _damageField = damageField;
    }

    public void SetCalibrationData(CalibrationData calibrationData)
    {
        _calibrationData = calibrationData;
    }

    /// <summary>
    ///     Shows export UI controls.
    /// </summary>
    public void DrawExportControls(SimulationResults results, SimulationParameters parameters,
        CtImageStackDataset sourceDataset, float[,,] damageField = null, CalibrationData calibrationData = null)
    {
        if (results == null) return;

        _results = results;
        _parameters = parameters;
        _sourceDataset = sourceDataset;
        _damageField = damageField;
        _calibrationData = calibrationData;
        _pixelSize = sourceDataset.PixelSize;
        if (ImGui.Button("Export Acoustic Volume", new Vector2(-1, 0)))
        {
            var defaultName = $"{sourceDataset.Name}_AcousticVolume_{DateTime.Now:yyyyMMdd_HHmmss}";
            _exportDialog.Open(defaultName);
        }

        if (ImGui.Button("View Waveforms", new Vector2(-1, 0)))
        {
            if (_waveformViewer == null)
            {
                // Create temporary dataset for waveform viewer
                var tempDataset = CreateTemporaryDataset();
                _waveformViewer = new WaveformViewer(tempDataset);
            }

            _isWaveformViewerOpen = true;
        }

        // Handle dialogs
        HandleDialogs();

        // Draw waveform viewer if open
        if (_isWaveformViewerOpen)
        {
            ImGui.SetNextWindowSize(new Vector2(600, 400), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Waveform Viewer", ref _isWaveformViewerOpen, ImGuiWindowFlags.NoDocking))
                _waveformViewer?.Draw();
            ImGui.End();
        }

        if (!_isWaveformViewerOpen && _waveformViewer != null)
        {
            _waveformViewer.Dispose();
            _waveformViewer = null;
        }
    }

    public void SetMaterialPropertyVolumes(float[,,] density, float[,,] youngsModulus, float[,,] poissonRatio)
    {
        _densityVolume = density;
        _youngsModulusVolume = youngsModulus;
        _poissonRatioVolume = poissonRatio;
    }

    private void HandleDialogs()
    {
        _progressDialog.Submit();

        if (_exportDialog.Submit()) _ = ExportAcousticVolumeAsync(_exportDialog.SelectedPath);
    }

    private async Task ExportAcousticVolumeAsync(string basePath)
    {
        _progressDialog.Open("Preparing acoustic volume export...");

        try
        {
            await Task.Run(() => ExportAcousticVolume(basePath, _progressDialog.CancellationToken));
            Logger.Log($"[AcousticExportManager] Successfully exported acoustic volume to {basePath}");
        }
        catch (OperationCanceledException)
        {
            Logger.Log("[AcousticExportManager] Export cancelled by user");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[AcousticExportManager] Export failed: {ex.Message}");
        }
        finally
        {
            _progressDialog.Close();
        }
    }

    private void ExportAcousticVolume(string basePath, CancellationToken cancellationToken)
    {
        var volumeDir = Path.ChangeExtension(basePath, null);
        Directory.CreateDirectory(volumeDir);

        var timeSeriesDir = Path.Combine(volumeDir, "TimeSeries");
        if (_results.TimeSeriesSnapshots?.Count > 0) Directory.CreateDirectory(timeSeriesDir);

        UpdateProgress(0.05f, "Creating acoustic volume dataset...");

        var acousticDataset = new AcousticVolumeDataset(
            Path.GetFileName(volumeDir),
            volumeDir)
        {
            PWaveVelocity = _results.PWaveVelocity,
            SWaveVelocity = _results.SWaveVelocity,
            VpVsRatio = _results.VpVsRatio,
            TimeSteps = _results.TotalTimeSteps,
            ComputationTime = _results.ComputationTime,
            YoungsModulusMPa = _parameters.YoungsModulusMPa,
            PoissonRatio = _parameters.PoissonRatio,
            ConfiningPressureMPa = _parameters.ConfiningPressureMPa,
            SourceFrequencyKHz = _parameters.SourceFrequencyKHz,
            SourceEnergyJ = _parameters.SourceEnergyJ,
            SourceDatasetPath = _sourceDataset.FilePath,
            SourceMaterialName = GetMaterialName(),
            CohesionMPa = _parameters.CohesionMPa,
            FailureAngleDeg = _parameters.FailureAngleDeg,
            MaxDamage = 1.0f,
            Calibration = _calibrationData
        };

        var progressBase = 0.1f;
        var progressPerField = 0.12f;

        // FIX: Export with correct filenames that loader expects
        // Export P-Wave field (maximum P-wave magnitude)
        if (_results.WaveFieldVx != null)
        {
            UpdateProgress(progressBase, "Exporting P-Wave field (max longitudinal)...");
            cancellationToken.ThrowIfCancellationRequested();

            var pWavePath = Path.Combine(volumeDir, "PWaveField.bin");
            ExportWaveField(_results.WaveFieldVx, pWavePath, false); // unsigned

            Logger.Log("[Export] ✓ Exported PWaveField.bin (maximum P-wave/longitudinal velocity)");
            progressBase += progressPerField;
        }

        // Export S-Wave field (maximum S-wave magnitude)
        if (_results.WaveFieldVy != null)
        {
            UpdateProgress(progressBase, "Exporting S-Wave field (max transverse)...");
            cancellationToken.ThrowIfCancellationRequested();

            var sWavePath = Path.Combine(volumeDir, "SWaveField.bin");
            ExportWaveField(_results.WaveFieldVy, sWavePath, false); // unsigned

            Logger.Log("[Export] ✓ Exported SWaveField.bin (maximum S-wave/transverse velocity)");
            progressBase += progressPerField;
        }

        // Export Combined field (maximum total magnitude)
        if (_results.WaveFieldVz != null)
        {
            UpdateProgress(progressBase, "Exporting Combined field (max total magnitude)...");
            cancellationToken.ThrowIfCancellationRequested();

            var combinedPath = Path.Combine(volumeDir, "CombinedField.bin");
            ExportWaveField(_results.WaveFieldVz, combinedPath, false); // unsigned

            Logger.Log("[Export] ✓ Exported CombinedField.bin (maximum combined magnitude)");
            progressBase += progressPerField;
        }

        // Damage field
        if (_damageField != null)
        {
            UpdateProgress(progressBase, "Exporting damage field...");
            cancellationToken.ThrowIfCancellationRequested();

            var damagePath = Path.Combine(volumeDir, "DamageField.bin");
            ExportWaveField(_damageField, damagePath, false);
            Logger.Log("[Export] ✓ Exported DamageField.bin");
            progressBase += progressPerField;
        }

        // Material properties (unchanged)
        if (_densityVolume != null)
        {
            UpdateProgress(progressBase, "Exporting Density Volume...");
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(volumeDir, "Density.bin");
            ExportRawFloatField(_densityVolume, path);
            progressBase += progressPerField;
        }

        if (_youngsModulusVolume != null)
        {
            UpdateProgress(progressBase, "Exporting Young's Modulus Volume...");
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(volumeDir, "YoungsModulus.bin");
            ExportRawFloatField(_youngsModulusVolume, path);
            progressBase += progressPerField;
        }

        if (_poissonRatioVolume != null)
        {
            UpdateProgress(progressBase, "Exporting Poisson's Ratio Volume...");
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(volumeDir, "PoissonRatio.bin");
            ExportRawFloatField(_poissonRatioVolume, path);
            progressBase += progressPerField;
        }

        // Time series (unchanged)
        if (_results.TimeSeriesSnapshots?.Count > 0)
        {
            UpdateProgress(0.8f, "Exporting time series snapshots...");
            ExportTimeSeries(timeSeriesDir, cancellationToken);
        }

        UpdateProgress(0.95f, "Saving metadata...");
        SaveMetadata(acousticDataset, volumeDir);

        if (_calibrationData != null && _calibrationData.Points.Count > 0)
        {
            UpdateProgress(0.97f, "Saving calibration data...");
            SaveCalibration(_calibrationData, volumeDir);
        }

        UpdateProgress(0.99f, "Adding to project...");
        acousticDataset.Load();
        ProjectManager.Instance.AddDataset(acousticDataset);

        UpdateProgress(1.0f, "Export complete!");

        Logger.Log("[Export] ═══════════════════════════════════════");
        Logger.Log("[Export] ✓ Acoustic volume exported successfully");
        Logger.Log("[Export] PWaveField.bin    = Maximum P-wave (longitudinal) velocity");
        Logger.Log("[Export] SWaveField.bin    = Maximum S-wave (transverse) velocity");
        Logger.Log("[Export] CombinedField.bin = Maximum combined magnitude");
        Logger.Log("[Export] These fields allow post-simulation Vp/Vs analysis in viewer");
        Logger.Log("[Export] ═══════════════════════════════════════");
    }

    private void ExportWaveField(float[,,] field, string path, bool isSigned)
    {
        if (field == null) return;

        // Create a ChunkedVolume from the float data. This normalizes it correctly.
        using (var volume = CreateVolumeFromField(field, isSigned))
        {
            // Use the built-in save method which writes a correct header.
            volume?.SaveAsBin(path);
        }
    }

    private float[,,] CreateCombinedField()
    {
        if (_results.WaveFieldVx == null) return null;

        var width = _results.WaveFieldVx.GetLength(0);
        var height = _results.WaveFieldVx.GetLength(1);
        var depth = _results.WaveFieldVx.GetLength(2);

        var combined = new float[width, height, depth];

        for (var z = 0; z < depth; z++)
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var vx = _results.WaveFieldVx?[x, y, z] ?? 0;
            var vy = _results.WaveFieldVy?[x, y, z] ?? 0;
            var vz = _results.WaveFieldVz?[x, y, z] ?? 0;
            combined[x, y, z] = (float)Math.Sqrt(vx * vx + vy * vy + vz * vz);
        }

        return combined;
    }

    private void ExportTimeSeries(string timeSeriesDir, CancellationToken cancellationToken)
    {
        if (_results.TimeSeriesSnapshots == null) return;

        for (var i = 0; i < _results.TimeSeriesSnapshots.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (i % 5 == 0)
            {
                var progress = 0.8f + 0.15f * i / _results.TimeSeriesSnapshots.Count;
                UpdateProgress(progress, $"Exporting snapshot {i + 1}/{_results.TimeSeriesSnapshots.Count}...");
            }

            var simSnapshot = _results.TimeSeriesSnapshots[i];
            var path = Path.Combine(timeSeriesDir, $"snapshot_{i:D6}.bin");

            // Convert simulation snapshot to data layer format
            var dataSnapshot = new Data.AcousticVolume.WaveFieldSnapshot
            {
                TimeStep = simSnapshot.TimeStep,
                SimulationTime = simSnapshot.SimulationTime,
                Width = _results.WaveFieldVx.GetLength(0),
                Height = _results.WaveFieldVx.GetLength(1),
                Depth = _results.WaveFieldVx.GetLength(2)
            };

            // For huge datasets, we only have max velocity (combined), so we need to split it
            // For normal datasets, we have the full Vx, Vy, Vz fields
            if (simSnapshot.VelocityField != null)
            {
                // Use the combined velocity field as Vx, leave Vy and Vz as zero
                // This is acceptable for visualization purposes
                var vx = simSnapshot.VelocityField;
                var vy = new float[dataSnapshot.Width, dataSnapshot.Height, dataSnapshot.Depth];
                var vz = new float[dataSnapshot.Width, dataSnapshot.Height, dataSnapshot.Depth];
                dataSnapshot.SetVelocityFields(vx, vy, vz);
            }
            else if (simSnapshot.MaxVelocityField != null)
            {
                // Same approach for max velocity field
                var vx = simSnapshot.MaxVelocityField;
                var vy = new float[dataSnapshot.Width, dataSnapshot.Height, dataSnapshot.Depth];
                var vz = new float[dataSnapshot.Width, dataSnapshot.Height, dataSnapshot.Depth];
                dataSnapshot.SetVelocityFields(vx, vy, vz);
            }

            // Save in the data layer format
            dataSnapshot.SaveToFile(path);
        }
    }

    private void SaveMetadata(AcousticVolumeDataset dataset, string volumeDir)
    {
        var metadata = new AcousticMetadata
        {
            PWaveVelocity = dataset.PWaveVelocity,
            SWaveVelocity = dataset.SWaveVelocity,
            VpVsRatio = dataset.VpVsRatio,
            TimeSteps = dataset.TimeSteps,
            ComputationTimeSeconds = dataset.ComputationTime.TotalSeconds,
            YoungsModulusMPa = dataset.YoungsModulusMPa,
            PoissonRatio = dataset.PoissonRatio,
            ConfiningPressureMPa = dataset.ConfiningPressureMPa,
            SourceFrequencyKHz = dataset.SourceFrequencyKHz,
            SourceEnergyJ = dataset.SourceEnergyJ,
            SourceDatasetPath = dataset.SourceDatasetPath,
            SourceMaterialName = dataset.SourceMaterialName,
            TensileStrengthMPa = dataset.TensileStrengthMPa,
            CohesionMPa = dataset.CohesionMPa,
            FailureAngleDeg = dataset.FailureAngleDeg,
            MaxDamage = dataset.MaxDamage
        };

        var json = JsonSerializer.Serialize(metadata,
            new JsonSerializerOptions { WriteIndented = true });
        var metadataPath = Path.Combine(volumeDir, "metadata.json");
        File.WriteAllText(metadataPath, json);
    }

    private void SaveCalibration(CalibrationData calibrationData, string volumeDir)
    {
        var json = JsonSerializer.Serialize(calibrationData,
            new JsonSerializerOptions { WriteIndented = true });
        var calibrationPath = Path.Combine(volumeDir, "calibration.json");
        File.WriteAllText(calibrationPath, json);
    }

    private AcousticVolumeDataset CreateTemporaryDataset()
    {
        var dataset = new AcousticVolumeDataset("Temp", Path.GetTempPath())
        {
            PWaveVelocity = _results.PWaveVelocity,
            SWaveVelocity = _results.SWaveVelocity,
            VpVsRatio = _results.VpVsRatio,
            TimeSteps = _results.TotalTimeSteps,
            ComputationTime = _results.ComputationTime,
            YoungsModulusMPa = _parameters.YoungsModulusMPa,
            PoissonRatio = _parameters.PoissonRatio,
            ConfiningPressureMPa = _parameters.ConfiningPressureMPa,
            SourceFrequencyKHz = _parameters.SourceFrequencyKHz,
            SourceEnergyJ = _parameters.SourceEnergyJ,
            TimeSeriesSnapshots = ConvertTimeSeriesForViewer(_results.TimeSeriesSnapshots)
        };

        // FIX: Map correctly to dataset fields
        if (_results.WaveFieldVx != null)
            dataset.PWaveField = CreateVolumeFromField(_results.WaveFieldVx, false); // Max P-wave
        if (_results.WaveFieldVy != null)
            dataset.SWaveField = CreateVolumeFromField(_results.WaveFieldVy, false); // Max S-wave
        if (_results.WaveFieldVz != null)
            dataset.CombinedWaveField = CreateVolumeFromField(_results.WaveFieldVz, false); // Max combined
        if (_damageField != null)
            dataset.DamageField = CreateVolumeFromField(_damageField, false);

        dataset.Calibration = _calibrationData;

        return dataset;
    }

    private List<Data.AcousticVolume.WaveFieldSnapshot> ConvertTimeSeriesForViewer(
        List<WaveFieldSnapshot> simSnapshots)
    {
        if (simSnapshots == null || simSnapshots.Count == 0)
            return new List<Data.AcousticVolume.WaveFieldSnapshot>();

        var dataSnapshots = new List<Data.AcousticVolume.WaveFieldSnapshot>();

        foreach (var simSnapshot in simSnapshots)
        {
            var dataSnapshot = new Data.AcousticVolume.WaveFieldSnapshot
            {
                TimeStep = simSnapshot.TimeStep,
                SimulationTime = simSnapshot.SimulationTime,
                Width = _results.WaveFieldVx.GetLength(0),
                Height = _results.WaveFieldVx.GetLength(1),
                Depth = _results.WaveFieldVx.GetLength(2)
            };

            // Convert velocity fields
            if (simSnapshot.VelocityField != null)
            {
                var vx = simSnapshot.VelocityField;
                var vy = new float[dataSnapshot.Width, dataSnapshot.Height, dataSnapshot.Depth];
                var vz = new float[dataSnapshot.Width, dataSnapshot.Height, dataSnapshot.Depth];
                dataSnapshot.SetVelocityFields(vx, vy, vz);
            }
            else if (simSnapshot.MaxVelocityField != null)
            {
                var vx = simSnapshot.MaxVelocityField;
                var vy = new float[dataSnapshot.Width, dataSnapshot.Height, dataSnapshot.Depth];
                var vz = new float[dataSnapshot.Width, dataSnapshot.Height, dataSnapshot.Depth];
                dataSnapshot.SetVelocityFields(vx, vy, vz);
            }

            dataSnapshots.Add(dataSnapshot);
        }

        return dataSnapshots;
    }

    private ChunkedVolume CreateVolumeFromField(float[,,] field, bool isSigned)
{
    if (field == null) return null;

    var width = field.GetLength(0);
    var height = field.GetLength(1);
    var depth = field.GetLength(2);

    var volume = new ChunkedVolume(width, height, depth);
    volume.PixelSize = _pixelSize;

    // FIX: Use percentile-based normalization instead of global max
    // This prevents source saturation from dominating the visualization
    var allValues = new List<float>();
    for (var z = 0; z < depth; z++)
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                allValues.Add(Math.Abs(field[x, y, z]));

    allValues.Sort();
    
    // Use 99.5th percentile as max to exclude extreme outliers (source voxels)
    var percentileIndex = (int)(allValues.Count * 0.995);
    var maxValue = allValues[Math.Min(percentileIndex, allValues.Count - 1)];
    
    // Ensure we have a valid range
    if (maxValue < 1e-10f) maxValue = 1e-10f;

    Logger.Log($"[Export] Normalization range: 0 to {maxValue:E3} (99.5th percentile)");
    Logger.Log($"[Export] Actual max value: {allValues[allValues.Count - 1]:E3}");

    // Optional: Apply logarithmic compression for better dynamic range
    var useLogCompression = true; // Make this a parameter if needed
    
    if (useLogCompression)
    {
        Logger.Log("[Export] Applying logarithmic compression for better visualization");
        for (var z = 0; z < depth; z++)
        {
            var slice = new byte[width * height];
            var idx = 0;
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var value = Math.Abs(field[x, y, z]);
                    
                    // Logarithmic compression: log10(1 + value/maxValue)
                    // This compresses the high values while preserving low-amplitude signals
                    var normalized = (float)Math.Log10(1 + value / maxValue) / (float)Math.Log10(2);
                    normalized = Math.Clamp(normalized, 0f, 1f);
                    
                    slice[idx++] = (byte)(normalized * 255);
                }
            volume.WriteSliceZ(z, slice);
        }
    }
    else
    {
        // Linear normalization with percentile-based max
        for (var z = 0; z < depth; z++)
        {
            var slice = new byte[width * height];
            var idx = 0;
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var value = field[x, y, z];
                    float normalized;

                    if (isSigned)
                        normalized = (value + maxValue) / (2 * maxValue);
                    else
                        normalized = value / maxValue;
                        
                    slice[idx++] = (byte)(Math.Clamp(normalized, 0f, 1f) * 255);
                }
            volume.WriteSliceZ(z, slice);
        }
    }

    return volume;
}

    private void ExportRawFloatField(float[,,] field, string path)
    {
        if (field == null) return;
        var width = field.GetLength(0);
        var height = field.GetLength(1);
        var depth = field.GetLength(2);

        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write(width);
            writer.Write(height);
            writer.Write(depth);

            // Buffer the entire array for a single fast write
            var buffer = new byte[field.Length * sizeof(float)];
            Buffer.BlockCopy(field, 0, buffer, 0, buffer.Length);
            writer.Write(buffer);
        }
    }

    private string GetMaterialName()
    {
        if (_sourceDataset?.Materials == null) return "Unknown";

        var material = _sourceDataset.Materials.FirstOrDefault(m => m.ID == _parameters.SelectedMaterialID);
        return material?.Name ?? "Unknown";
    }

    private void UpdateProgress(float progress, string message)
    {
        _progressDialog.Update(progress, message);
    }
}