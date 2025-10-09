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
    private float _combinedFieldMaxVelocity;
    private float[,,] _damageField;
    private float _damageFieldMaxValue; // NEW: Store max value for denormalization
    private float[,,] _densityVolume;
    private bool _isWaveformViewerOpen;
    private SimulationParameters _parameters;
    private float _pixelSize;
    private float[,,] _poissonRatioVolume;

    // NEW: Store max velocity values for denormalization
    private float _pWaveFieldMaxVelocity;

    private SimulationResults _results;
    private CtImageStackDataset _sourceDataset;
    private float _sWaveFieldMaxVelocity;
    private WaveformViewer _waveformViewer;
    private float[,,] _youngsModulusVolume;

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

    public void SetMaterialPropertyVolumes(float[,,] density, float[,,] youngsModulus, float[,,] poissonRatio)
    {
        _densityVolume = density;
        _youngsModulusVolume = youngsModulus;
        _poissonRatioVolume = poissonRatio;
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
                var tempDataset = CreateTemporaryDataset();
                _waveformViewer = new WaveformViewer(tempDataset);
            }

            _isWaveformViewerOpen = true;
        }

        HandleDialogs();

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

    private void HandleDialogs()
    {
        _progressDialog.Submit();
        if (_exportDialog.Submit())
            _ = ExportAcousticVolumeAsync(_exportDialog.SelectedPath);
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
        if (_results.TimeSeriesSnapshots?.Count > 0)
            Directory.CreateDirectory(timeSeriesDir);

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

        // Export P-Wave field
        if (_results.WaveFieldVx != null)
        {
            UpdateProgress(progressBase, "Exporting P-Wave field...");
            cancellationToken.ThrowIfCancellationRequested();

            var pWavePath = Path.Combine(volumeDir, "PWaveField.bin");
            ExportWaveField(_results.WaveFieldVx, pWavePath, false, out _pWaveFieldMaxVelocity);
            acousticDataset.PWaveFieldMaxVelocity = _pWaveFieldMaxVelocity;

            Logger.Log($"[Export] ✓ PWaveField.bin (max: {_pWaveFieldMaxVelocity:F2} m/s)");
            progressBase += progressPerField;
        }

        // Export S-Wave field
        if (_results.WaveFieldVy != null)
        {
            UpdateProgress(progressBase, "Exporting S-Wave field...");
            cancellationToken.ThrowIfCancellationRequested();

            var sWavePath = Path.Combine(volumeDir, "SWaveField.bin");
            ExportWaveField(_results.WaveFieldVy, sWavePath, false, out _sWaveFieldMaxVelocity);
            acousticDataset.SWaveFieldMaxVelocity = _sWaveFieldMaxVelocity;

            Logger.Log($"[Export] ✓ SWaveField.bin (max: {_sWaveFieldMaxVelocity:F2} m/s)");
            progressBase += progressPerField;
        }

        // Export Combined field
        if (_results.WaveFieldVz != null)
        {
            UpdateProgress(progressBase, "Exporting Combined field...");
            cancellationToken.ThrowIfCancellationRequested();

            var combinedPath = Path.Combine(volumeDir, "CombinedField.bin");
            ExportWaveField(_results.WaveFieldVz, combinedPath, false, out _combinedFieldMaxVelocity);
            acousticDataset.CombinedFieldMaxVelocity = _combinedFieldMaxVelocity;

            Logger.Log($"[Export] ✓ CombinedField.bin (max: {_combinedFieldMaxVelocity:F2} m/s)");
            progressBase += progressPerField;
        }

        // Damage field
        if (_damageField != null)
        {
            UpdateProgress(progressBase, "Exporting damage field...");
            cancellationToken.ThrowIfCancellationRequested();

            var damagePath = Path.Combine(volumeDir, "DamageField.bin");
            ExportWaveField(_damageField, damagePath, false, out _damageFieldMaxValue);
            acousticDataset.DamageFieldMaxValue = _damageFieldMaxValue;

            Logger.Log($"[Export] ✓ DamageField.bin (max: {_damageFieldMaxValue:F3})");
            progressBase += progressPerField;
        }

        // Material properties
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

        // Time series
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
        Logger.Log($"[Export] P-Wave max: {_pWaveFieldMaxVelocity:F2} m/s");
        Logger.Log($"[Export] S-Wave max: {_sWaveFieldMaxVelocity:F2} m/s");
        Logger.Log($"[Export] Combined max: {_combinedFieldMaxVelocity:F2} m/s");
        Logger.Log("[Export] Velocity profile tool can now extract simulated velocities");
        Logger.Log("[Export] ═══════════════════════════════════════");
    }

    /// <summary>
    ///     Exports a wave field and returns the max value used for normalization.
    /// </summary>
    private void ExportWaveField(float[,,] field, string path, bool isSigned, out float maxValue)
    {
        if (field == null)
        {
            maxValue = 0;
            return;
        }

        // Create volume and get the max value used for normalization
        using (var volume = CreateVolumeFromField(field, isSigned, out maxValue))
        {
            volume?.SaveAsBin(path);
        }
    }

    /// <summary>
    ///     Creates a ChunkedVolume from a float field, normalizing to byte range.
    ///     Returns the max value used for normalization (for later denormalization).
    /// </summary>
    private ChunkedVolume CreateVolumeFromField(float[,,] field, bool isSigned, out float maxValue)
    {
        if (field == null)
        {
            maxValue = 0;
            return null;
        }

        var width = field.GetLength(0);
        var height = field.GetLength(1);
        var depth = field.GetLength(2);

        var volume = new ChunkedVolume(width, height, depth);
        volume.PixelSize = _pixelSize;

        // Find max for normalization using percentile to avoid source saturation
        var allValues = new List<float>();
        for (var z = 0; z < depth; z++)
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            allValues.Add(Math.Abs(field[x, y, z]));

        allValues.Sort();

        // Use 99.5th percentile as max to exclude extreme outliers (source voxels)
        var percentileIndex = (int)(allValues.Count * 0.995);
        maxValue = allValues[Math.Min(percentileIndex, allValues.Count - 1)];

        // Ensure we have a valid range
        if (maxValue < 1e-10f) maxValue = 1e-10f;

        Logger.Log($"[Export] Normalization: 0 to {maxValue:E3} (99.5th percentile)");
        Logger.Log($"[Export] Actual max: {allValues[allValues.Count - 1]:E3}");

        // Apply logarithmic compression for better visualization
        var useLogCompression = true;

        if (useLogCompression)
        {
            Logger.Log("[Export] Applying logarithmic compression");
            for (var z = 0; z < depth; z++)
            {
                var slice = new byte[width * height];
                var idx = 0;
                for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var value = Math.Abs(field[x, y, z]);

                    // Logarithmic compression: log10(1 + value/maxValue)
                    var normalized = (float)Math.Log10(1 + value / maxValue) / (float)Math.Log10(2);
                    normalized = Math.Clamp(normalized, 0f, 1f);

                    slice[idx++] = (byte)(normalized * 255);
                }

                volume.WriteSliceZ(z, slice);
            }
        }
        else
        {
            // Linear normalization
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

            var dataSnapshot = new Data.AcousticVolume.WaveFieldSnapshot
            {
                TimeStep = simSnapshot.TimeStep,
                SimulationTime = simSnapshot.SimulationTime,
                Width = _results.WaveFieldVx.GetLength(0),
                Height = _results.WaveFieldVx.GetLength(1),
                Depth = _results.WaveFieldVx.GetLength(2)
            };

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
            MaxDamage = dataset.MaxDamage,
            // NEW: Store scaling factors for denormalization
            PWaveFieldMaxVelocity = dataset.PWaveFieldMaxVelocity,
            SWaveFieldMaxVelocity = dataset.SWaveFieldMaxVelocity,
            CombinedFieldMaxVelocity = dataset.CombinedFieldMaxVelocity,
            DamageFieldMaxValue = dataset.DamageFieldMaxValue
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
            TimeSeriesSnapshots = ConvertTimeSeriesForViewer(_results.TimeSeriesSnapshots),
            Calibration = _calibrationData,
            PWaveFieldMaxVelocity = _pWaveFieldMaxVelocity,
            SWaveFieldMaxVelocity = _sWaveFieldMaxVelocity,
            CombinedFieldMaxVelocity = _combinedFieldMaxVelocity,
            DamageFieldMaxValue = _damageFieldMaxValue
        };

        if (_results.WaveFieldVx != null)
            dataset.PWaveField = CreateVolumeFromField(_results.WaveFieldVx, false, out _);
        if (_results.WaveFieldVy != null)
            dataset.SWaveField = CreateVolumeFromField(_results.WaveFieldVy, false, out _);
        if (_results.WaveFieldVz != null)
            dataset.CombinedWaveField = CreateVolumeFromField(_results.WaveFieldVz, false, out _);
        if (_damageField != null)
            dataset.DamageField = CreateVolumeFromField(_damageField, false, out _);

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