// GeoscientistToolkit/UI/AcousticVolume/AcousticToCtConverterDialog.cs
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using GeoscientistToolkit.UI; // Assuming ProgressBarDialog is in this namespace

namespace GeoscientistToolkit.UI.AcousticVolume
{
    /// <summary>
    /// A dialog for converting an AcousticVolumeDataset to a new grayscale CtImageStackDataset.
    /// </summary>
    public class AcousticToCtConverterDialog
    {
        private bool _isOpen = false;
        private AcousticVolumeDataset _sourceDataset;

        // UI State
        private int _selectedField = 2; // 0=P, 1=S, 2=Combined
        private bool _exportDamageAsMaterial = true;
        private string _newDatasetName = "";

        private readonly ImGuiExportFileDialog _exportDialog;
        private readonly ProgressBarDialog _progressDialog;

        private bool _isConverting = false;
        
        // This CancellationTokenSource is still needed for the Task
        private CancellationTokenSource _conversionCts;

        public AcousticToCtConverterDialog()
        {
            _exportDialog = new ImGuiExportFileDialog("AcousticToCtExport", "Select Output Location");
            _progressDialog = new ProgressBarDialog("Converting Volume");
        }

        /// <summary>
        /// Opens the dialog for a specific dataset.
        /// </summary>
        public void Open(AcousticVolumeDataset dataset)
        {
            _sourceDataset = dataset;
            _newDatasetName = $"{dataset.Name}_greyscale";
            _isOpen = true;
            _isConverting = false;
            _exportDamageAsMaterial = _sourceDataset.DamageField != null;
            _conversionCts?.Cancel(); // Cancel any previous operation
        }

        /// <summary>
        /// Draws the dialog window if it is open.
        /// </summary>
        public void Draw()
        {
            if (!_isOpen) return;

            ImGui.SetNextWindowSize(new Vector2(500, 280), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Convert Acoustic Volume to Greyscale", ref _isOpen, ImGuiWindowFlags.Modal | ImGuiWindowFlags.NoCollapse))
            {
                if (_sourceDataset == null)
                {
                    ImGui.Text("Error: No source dataset provided.");
                    ImGui.End();
                    return;
                }

                // Disable UI elements during conversion
                if (_isConverting)
                {
                    ImGui.BeginDisabled();
                }

                ImGui.Text("Select the velocity field to convert to grayscale:");
                string[] fields = { "P-Wave", "S-Wave", "Combined" };
                ImGui.Combo("Source Field", ref _selectedField, fields, fields.Length);

                ImGui.Separator();

                ImGui.Text("Segmentation Options:");
                if (_sourceDataset.DamageField == null)
                {
                    ImGui.BeginDisabled();
                }
                ImGui.Checkbox("Export Damage Field as a material", ref _exportDamageAsMaterial);
                if (_sourceDataset.DamageField == null)
                {
                    ImGui.EndDisabled();
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Damage field is not available in this dataset.");
                    }
                }

                ImGui.Separator();

                ImGui.Text("Output Dataset Name:");
                ImGui.InputText("##NewDatasetName", ref _newDatasetName, 256);

                if (ImGui.Button("Choose Output Location & Convert...", new Vector2(-1, 0)))
                {
                    _exportDialog.SetExtensions(new ImGuiExportFileDialog.ExtensionOption("", "GeoscientistToolkit Dataset Folder"));
                    _exportDialog.Open(_newDatasetName);
                }

                // Handle dialog submissions
                if (_exportDialog.Submit())
                {
                    string path = _exportDialog.SelectedPath; 
                    string directory = Path.GetDirectoryName(path);
                    string datasetName = Path.GetFileName(path);

                    if (!string.IsNullOrWhiteSpace(directory) && !string.IsNullOrWhiteSpace(datasetName))
                    {
                        _isConverting = true;
                        
                        // Use the CancellationTokenSource from the dialog
                        _progressDialog.Open($"Converting {datasetName}...");
                        _conversionCts = new CancellationTokenSource(); // Create a new CTS for this run
                        Task.Run(() => PerformConversion(directory, datasetName, _conversionCts.Token));
                    }
                }

                if (_isConverting)
                {
                    ImGui.EndDisabled();
                }
                
                // --- FIX START ---
                // First, draw the progress dialog if we are in the converting state.
                if (_isConverting)
                {
                    _progressDialog.Submit();

                    // Now, check the state *after* drawing.
                    // If we were converting, but the dialog is no longer active (meaning the user just closed it),
                    // then we need to cancel the background task.
                    if (!_progressDialog.IsActive)
                    {
                        _conversionCts?.Cancel();
                        // Update our own state flag immediately for better UI responsiveness
                        _isConverting = false; 
                    }
                }
                // --- FIX END ---
                
                ImGui.End();
            }
            else
            {
                // If main window is closed by clicking outside or its own 'X', cancel the conversion
                if (_isConverting)
                {
                    _conversionCts?.Cancel();
                }
            }
        }

        private async Task PerformConversion(string outputDirectory, string datasetName, CancellationToken token)
        {
            try
            {
                var sourceGrayscaleVolume = _selectedField switch
                {
                    0 => _sourceDataset.PWaveField,
                    1 => _sourceDataset.SWaveField,
                    _ => _sourceDataset.CombinedWaveField
                };

                if (sourceGrayscaleVolume == null)
                {
                    throw new InvalidOperationException("Selected source wave field is not available.");
                }

                int width = sourceGrayscaleVolume.Width;
                int height = sourceGrayscaleVolume.Height;
                int depth = sourceGrayscaleVolume.Depth;

                string datasetFolderPath = Path.Combine(outputDirectory, datasetName);
                Directory.CreateDirectory(datasetFolderPath);

                _progressDialog.Update(0.1f, "Creating grayscale volume...");
                var newGrayscaleVolume = new ChunkedVolume(width, height, depth);
                var sliceBuffer = new byte[width * height];

                for (int z = 0; z < depth; z++)
                {
                    token.ThrowIfCancellationRequested(); // Check for cancellation
                    sourceGrayscaleVolume.ReadSliceZ(z, sliceBuffer);
                    newGrayscaleVolume.WriteSliceZ(z, sliceBuffer);
                    _progressDialog.Update(0.1f + 0.6f * ((float)z / depth), $"Processing grayscale slice {z + 1}/{depth}");
                }

                string volumePath = Path.Combine(datasetFolderPath, $"{datasetName}.Volume.bin");
                await newGrayscaleVolume.SaveAsBinAsync(volumePath);
                newGrayscaleVolume.Dispose();

                if (_exportDamageAsMaterial && _sourceDataset.DamageField != null)
                {
                    _progressDialog.Update(0.7f, "Creating label volume...");
                    string labelVolumePath = Path.Combine(datasetFolderPath, $"{datasetName}.Labels.bin");
                    var newLabelVolume = new ChunkedLabelVolume(width, height, depth, ChunkedVolume.DEFAULT_CHUNK_DIM, false, labelVolumePath);
                    var materials = new List<Material>
                    {
                        new Material(0, "Exterior", new Vector4(0, 0, 0, 0)),
                        new Material(1, "Damage", new Vector4(1, 0, 0, 1)) // Red for damage
                    };

                    var damageSliceBuffer = new byte[width * height];
                    var labelSliceBuffer = new byte[width * height];

                    for (int z = 0; z < depth; z++)
                    {
                        token.ThrowIfCancellationRequested(); // Check for cancellation
                        _sourceDataset.DamageField.ReadSliceZ(z, damageSliceBuffer);
                        for (int i = 0; i < damageSliceBuffer.Length; i++)
                        {
                            labelSliceBuffer[i] = damageSliceBuffer[i] > 10 ? (byte)1 : (byte)0;
                        }
                        newLabelVolume.WriteSliceZ(z, labelSliceBuffer);
                        _progressDialog.Update(0.7f + 0.2f * ((float)z / depth), $"Processing damage slice {z + 1}/{depth}");
                    }
                    
                    newLabelVolume.SaveAsBin(newLabelVolume.FilePath);
                    newLabelVolume.Dispose();

                    var ctDatasetForMaterials = new CtImageStackDataset(datasetName, datasetFolderPath) { Materials = materials };
                    ctDatasetForMaterials.SaveMaterials();
                }

                _progressDialog.Update(0.95f, "Finalizing...");
                var newCtDataset = new CtImageStackDataset(datasetName, datasetFolderPath);
                newCtDataset.Load();
                ProjectManager.Instance.AddDataset(newCtDataset);

                Logger.Log($"Successfully converted '{_sourceDataset.Name}' to '{datasetName}'");
            }
            catch (OperationCanceledException)
            {
                Logger.LogWarning("Acoustic volume conversion was cancelled by the user.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to convert acoustic volume: {ex.Message}");
            }
            finally
            {
                _isConverting = false;
                _isOpen = false;
                _progressDialog.Close();
                _conversionCts?.Dispose();
                _conversionCts = null;
            }
        }
    }
}