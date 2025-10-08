// GeoscientistToolkit/AddIns/AcousticSimulation/AcousticExportManager.cs

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

namespace GeoscientistToolkit.AddIns.AcousticSimulation;

/// <summary>
///     Manages exporting acoustic simulation results with progress tracking
/// </summary>
public class AcousticExportManager : IDisposable
{
    private readonly ImGuiExportFileDialog _exportDialog;
    private readonly ProgressBarDialog _progressDialog;
    private CalibrationData _calibrationData;
    private float[,,] _damageField; // Store damage field from simulation
    private SimulationParameters _parameters;
    private SimulationResults _results;
    private CtImageStackDataset _sourceDataset;
    private WaveformViewer _waveformViewer;

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
    ///     Shows export UI controls
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

        if (ImGui.Button("Export Acoustic Volume", new Vector2(-1, 0)))
        {
            var defaultName = $"{sourceDataset.Name}_AcousticVolume_{DateTime.Now:yyyyMMdd_HHmmss}";
            _exportDialog.Open(defaultName);
        }

        if (ImGui.Button("View Waveforms", new Vector2(-1, 0)))
            if (_waveformViewer == null)
            {
                // Create temporary dataset for waveform viewer
                var tempDataset = CreateTemporaryDataset();
                _waveformViewer = new WaveformViewer(tempDataset);
            }

        // Handle dialogs
        HandleDialogs();

        // Draw waveform viewer if open
        _waveformViewer?.Draw();
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
        // Create directory structure
        var volumeDir = Path.ChangeExtension(basePath, null);
        Directory.CreateDirectory(volumeDir);

        var timeSeriesDir = Path.Combine(volumeDir, "TimeSeries");
        if (_results.TimeSeriesSnapshots?.Count > 0) Directory.CreateDirectory(timeSeriesDir);

        UpdateProgress(0.1f, "Creating acoustic volume dataset...");

        // Create dataset
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
            TensileStrengthMPa = _parameters.TensileStrengthMPa,
            CohesionMPa = _parameters.CohesionMPa,
            FailureAngleDeg = _parameters.FailureAngleDeg,
            MaxDamage = 1.0f,
            Calibration = _calibrationData
        };

        var progressBase = 0.2f;
        var progressPerField = 0.15f;

        // Export P-Wave field
        if (_results.WaveFieldVx != null)
        {
            UpdateProgress(progressBase, "Exporting P-Wave field...");
            cancellationToken.ThrowIfCancellationRequested();

            var pWavePath = Path.Combine(volumeDir, "PWaveField.bin");
            ExportWaveField(_results.WaveFieldVx, pWavePath, cancellationToken,
                progressBase, progressPerField);
            progressBase += progressPerField;
        }

        // Export S-Wave field
        if (_results.WaveFieldVy != null)
        {
            UpdateProgress(progressBase, "Exporting S-Wave field...");
            cancellationToken.ThrowIfCancellationRequested();

            var sWavePath = Path.Combine(volumeDir, "SWaveField.bin");
            ExportWaveField(_results.WaveFieldVy, sWavePath, cancellationToken,
                progressBase, progressPerField);
            progressBase += progressPerField;
        }

        // Export Combined field
        if (_results.WaveFieldVz != null)
        {
            UpdateProgress(progressBase, "Creating combined field...");
            cancellationToken.ThrowIfCancellationRequested();

            var combined = CreateCombinedField();
            var combinedPath = Path.Combine(volumeDir, "CombinedField.bin");
            ExportWaveField(combined, combinedPath, cancellationToken,
                progressBase, progressPerField);
            progressBase += progressPerField;
        }

        // Export Damage field
        if (_damageField != null)
        {
            UpdateProgress(progressBase, "Exporting damage field...");
            cancellationToken.ThrowIfCancellationRequested();

            var damagePath = Path.Combine(volumeDir, "DamageField.bin");
            ExportWaveField(_damageField, damagePath, cancellationToken,
                progressBase, progressPerField);
            progressBase += progressPerField;
        }

        // Export time series
        if (_results.TimeSeriesSnapshots?.Count > 0)
        {
            UpdateProgress(0.8f, "Exporting time series snapshots...");
            ExportTimeSeries(timeSeriesDir, cancellationToken);
        }

        // Save metadata
        UpdateProgress(0.95f, "Saving metadata...");
        SaveMetadata(acousticDataset, volumeDir);

        // Save calibration if available
        if (_calibrationData != null && _calibrationData.Points.Count > 0)
        {
            UpdateProgress(0.97f, "Saving calibration data...");
            SaveCalibration(_calibrationData, volumeDir);
        }

        // Add to project
        UpdateProgress(0.99f, "Adding to project...");
        acousticDataset.Load();
        ProjectManager.Instance.AddDataset(acousticDataset);

        UpdateProgress(1.0f, "Export complete!");
    }

    private void ExportWaveField(float[,,] field, string path, CancellationToken cancellationToken,
        float baseProgress, float progressRange)
    {
        var width = field.GetLength(0);
        var height = field.GetLength(1);
        var depth = field.GetLength(2);

        using (var writer = new BinaryWriter(File.Create(path)))
        {
            // Write dimensions
            writer.Write(width);
            writer.Write(height);
            writer.Write(depth);

            // Normalize and write data
            float maxValue = 0;
            for (var z = 0; z < depth; z++)
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                maxValue = Math.Max(maxValue, Math.Abs(field[x, y, z]));

            if (maxValue > 0)
                for (var z = 0; z < depth; z++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (z % 10 == 0)
                    {
                        var progress = baseProgress + progressRange * z / depth;
                        UpdateProgress(progress, $"Writing slice {z + 1}/{depth}...");
                    }

                    for (var y = 0; y < height; y++)
                    for (var x = 0; x < width; x++)
                    {
                        var normalized = (field[x, y, z] + maxValue) / (2 * maxValue);
                        var value = (byte)(normalized * 255);
                        writer.Write(value);
                    }
                }
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

            var path = Path.Combine(timeSeriesDir, $"snapshot_{i:D6}.bin");
            _results.TimeSeriesSnapshots[i].SaveToFile(path);
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
        // Create a temporary dataset for the waveform viewer
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
            TimeSeriesSnapshots = _results.TimeSeriesSnapshots
        };

        // Create volumes from results
        if (_results.WaveFieldVx != null) dataset.PWaveField = CreateVolumeFromField(_results.WaveFieldVx);
        if (_results.WaveFieldVy != null) dataset.SWaveField = CreateVolumeFromField(_results.WaveFieldVy);
        if (_results.WaveFieldVz != null) dataset.CombinedWaveField = CreateVolumeFromField(CreateCombinedField());
        if (_damageField != null) dataset.DamageField = CreateVolumeFromField(_damageField);

        dataset.Calibration = _calibrationData;

        return dataset;
    }

    private ChunkedVolume CreateVolumeFromField(float[,,] field)
    {
        if (field == null) return null;

        var width = field.GetLength(0);
        var height = field.GetLength(1);
        var depth = field.GetLength(2);

        var volume = new ChunkedVolume(width, height, depth);

        // Normalize and convert to byte
        float maxValue = 0;
        for (var z = 0; z < depth; z++)
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            maxValue = Math.Max(maxValue, Math.Abs(field[x, y, z]));

        if (maxValue > 0)
            for (var z = 0; z < depth; z++)
            {
                var slice = new byte[width * height];
                var idx = 0;
                for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var normalized = (field[x, y, z] + maxValue) / (2 * maxValue);
                    slice[idx++] = (byte)(normalized * 255);
                }

                volume.WriteSliceZ(z, slice);
            }

        return volume;
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