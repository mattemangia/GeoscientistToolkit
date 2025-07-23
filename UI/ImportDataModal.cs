// GeoscientistToolkit/UI/ImportDataModal.cs
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI
{
    public class ImportDataModal
    {
        private enum ImportState { Idle, ShowingOptions, Processing }
        private ImportState _currentState = ImportState.Idle;
        
        private readonly ImGuiFileDialog _folderDialog;
        
        private int _selectedDatasetTypeIndex = 1;
        private readonly string[] _datasetTypeNames = { "Single Image (Not Implemented)", "CT Image Stack (Optimized for 3D Streaming)", "CT Image Stack (Legacy for 2D Editing)" };
        
        private string _ctFolderPath = "";
        private float _ctPixelSize = 1.0f;
        private int _ctPixelSizeUnit = 0;
        private readonly string[] _pixelSizeUnits = { "µm", "mm" };
        
        // --- BINNING RESTORED ---
        private int _ctBinningFactor = 1;

        private float _progress;
        private string _statusText = "";
        private Task _importTask;
        
        public ImportDataModal()
        {
            _folderDialog = new ImGuiFileDialog("ImportFolderDialog", FileDialogType.OpenDirectory, "Select Image Folder");
        }

        public void Open()
        {
            ResetState();
            _currentState = ImportState.ShowingOptions;
        }

        public void Submit()
        {
            if (_currentState == ImportState.Idle) return;
            if (_folderDialog.Submit()) _ctFolderPath = _folderDialog.SelectedPath;
            
            ImGui.SetNextWindowSize(new Vector2(600, 550), ImGuiCond.FirstUseEver);
            var center = ImGui.GetMainViewport().GetCenter();
            ImGui.SetNextWindowPos(center, ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));
            
            bool modalOpen = true;
            if (ImGui.Begin("Import Data", ref modalOpen, ImGuiWindowFlags.NoDocking))
            {
                if (_currentState == ImportState.Processing)
                {
                    DrawProgress();
                }
                else
                {
                    DrawOptions();
                }
                ImGui.End();
            }
            if (!modalOpen) _currentState = ImportState.Idle;
        }

        private void DrawOptions()
        {
            ImGui.Text("Select the type of data to import:");
            ImGui.Combo("##DatasetType", ref _selectedDatasetTypeIndex, _datasetTypeNames, _datasetTypeNames.Length);
            ImGui.Separator();
            ImGui.Spacing();
            DrawCTImageStackOptions();
            ImGui.SetCursorPosY(ImGui.GetWindowHeight() - ImGui.GetFrameHeightWithSpacing() * 1.5f);
            ImGui.Separator();
            DrawButtons();
        }

        private void DrawCTImageStackOptions()
        {
            ImGui.Text("Image Folder:");
            ImGui.InputText("##CTPath", ref _ctFolderPath, 260, ImGuiInputTextFlags.ReadOnly);
            ImGui.SameLine();
            if (ImGui.Button("Browse...##CTFolder")) _folderDialog.Open();
            
            ImGui.Spacing();
            ImGui.Text("Voxel Size:");
            ImGui.SetNextItemWidth(150);
            ImGui.InputFloat("##PixelSizeCT", ref _ctPixelSize, 0.1f, 1.0f, "%.3f");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(80);
            ImGui.Combo("##Unit", ref _ctPixelSizeUnit, _pixelSizeUnits, _pixelSizeUnits.Length);
            
            ImGui.Spacing();

            // --- BINNING UI RESTORED ---
            ImGui.Text("3D Binning Factor:");
            ImGui.SetNextItemWidth(230);
            int[] binningOptions = { 1, 2, 4, 8 };
            string[] binningLabels = { "1×1×1 (None)", "2×2×2 (8x smaller)", "4×4×4 (64x smaller)", "8×8×8 (512x smaller)" };
            int binningIndex = Array.IndexOf(binningOptions, _ctBinningFactor);
            if (ImGui.Combo("##Binning", ref binningIndex, binningLabels, binningLabels.Length))
            {
                _ctBinningFactor = binningOptions[binningIndex];
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Reduces dataset size by averaging voxels. A factor of 2 makes the data 8 times smaller. This is a pre-processing step.");
            }
        }

        private void DrawButtons()
        {
            if (ImGui.Button("Cancel", new Vector2(120, 0))) _currentState = ImportState.Idle;
            ImGui.SameLine();
            bool canImport = !string.IsNullOrEmpty(_ctFolderPath);
            if (!canImport) ImGui.BeginDisabled();
            if (ImGui.Button("Import", new Vector2(120, 0))) HandleImportClick();
            if (!canImport) ImGui.EndDisabled();
        }

        private void HandleImportClick()
        {
            _currentState = ImportState.Processing;
            if (_selectedDatasetTypeIndex == 2) // Legacy
            {
                _importTask = ImportLegacyCTStack();
            }
            else // Optimized
            {
                _importTask = ConvertAndImportOptimizedCTStack();
            }
        }

        private void DrawProgress()
        {
            ImGui.Text(_statusText);
            ImGui.ProgressBar(_progress, new Vector2(-1, 0), $"{(_progress * 100):0}%");
            if (_importTask.IsFaulted)
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), "Error during import:");
                ImGui.TextWrapped(_importTask.Exception?.GetBaseException().Message);
                if (ImGui.Button("Close")) ResetState();
            }
            else if (_importTask.IsCompletedSuccessfully)
            {
                ImGui.TextColored(new Vector4(0, 1, 0, 1), "Import successful!");
                if (ImGui.Button("Close")) _currentState = ImportState.Idle;
            }
        }

        private async Task ConvertAndImportOptimizedCTStack()
        {
            var folderName = Path.GetFileName(_ctFolderPath);
            // Include binning factor in the output filename to avoid conflicts
            string gvtFileName = _ctBinningFactor > 1 ? $"{folderName}_bin{_ctBinningFactor}.gvt" : $"{folderName}.gvt";
            string gvtPath = Path.Combine(_ctFolderPath, gvtFileName);

            // --- BINNING IS NOW THE FIRST STEP OF THE PROCESS ---
            var binnedVolume = await CTStackLoader.LoadCTStackAsync(_ctFolderPath, 0, _ctBinningFactor, false,
                new Progress<float>(p => { _progress = p * 0.5f; _statusText = $"Step 1/2: Binning and loading images... {(int)(p*100)}%"; }), folderName);
            
            // Now convert the binned volume
            if (!File.Exists(gvtPath))
            {
                await CtStackConverter.ConvertToStreamableFormat(binnedVolume, gvtPath, 
                    (p, s) => { _progress = 0.5f + (p * 0.5f); _statusText = $"Step 2/2: {s}"; });
            }
            else
            {
                _statusText = "Found existing optimized file. Loading..."; _progress = 1.0f;
            }
            
            var legacyDataset = await CreateLegacyDatasetForEditing(binnedVolume);
            
            var streamingDataset = new StreamingCtVolumeDataset($"{folderName} (3D View)", gvtPath)
            {
                EditablePartner = legacyDataset
            };
            
            ProjectManager.Instance.AddDataset(streamingDataset);
            ProjectManager.Instance.AddDataset(legacyDataset);
            
            _statusText = "Optimized dataset and editable partner added to project!";
        }
        
        private async Task ImportLegacyCTStack()
        {
            var legacyDataset = await CreateLegacyDatasetForEditing(null); // Pass null to force loading from disk
            ProjectManager.Instance.AddDataset(legacyDataset);
            _statusText = "Legacy dataset added to project!";
        }

        private async Task<CtImageStackDataset> CreateLegacyDatasetForEditing(ChunkedVolume preloadedVolume)
        {
            var folderName = Path.GetFileName(_ctFolderPath);
            CtImageStackDataset dataset = null;

            await Task.Run(async () =>
            {
                // If a volume is already loaded (from the optimized path), use it. Otherwise, load from disk.
                var volume = preloadedVolume;
                if (volume == null)
                {
                     var progressReporter = new Progress<float>(p => 
                     {
                         _progress = p;
                         _statusText = $"Processing images... {(int)(p * 100)}%";
                     });
                     volume = await CTStackLoader.LoadCTStackAsync(_ctFolderPath, 0, _ctBinningFactor, false, progressReporter, folderName);
                }

                double pixelSizeMicrons = (_ctPixelSizeUnit == 0 ? _ctPixelSize : _ctPixelSize * 1000) * _ctBinningFactor;

                dataset = new CtImageStackDataset($"{folderName} (2D Edit & Segment)", _ctFolderPath)
                {
                    Width = volume.Width,
                    Height = volume.Height,
                    Depth = volume.Depth,
                    PixelSize = (float)pixelSizeMicrons,
                    SliceThickness = (float)pixelSizeMicrons,
                    Unit = "µm",
                    BinningSize = _ctBinningFactor
                };
            });
            return dataset;
        }

        private (int count, long totalSize) CountImagesInFolder(string folderPath)
        {
            try { return (Directory.GetFiles(folderPath).Count(ImageLoader.IsSupportedImageFile), Directory.GetFiles(folderPath).Where(ImageLoader.IsSupportedImageFile).Sum(f => new FileInfo(f).Length)); }
            catch { return (0, 0); }
        }
        
        private void ResetState()
        {
            _ctFolderPath = "";
            _ctPixelSize = 1.0f;
            _ctPixelSizeUnit = 0;
            _ctBinningFactor = 1; // Reset binning
            _importTask = null;
            _progress = 0;
            _statusText = "";
            _currentState = ImportState.Idle;
        }
    }
}