// GeoscientistToolkit/Data/CtImageStack/CtImageStackTools.cs
using GeoscientistToolkit.Data;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using GeoscientistToolkit.UI;
using ImGuiNET;
using System.Numerics;

namespace GeoscientistToolkit.Data.CtImageStack
{
    public class CtImageStackTools : IDatasetTools
    {
        private readonly ImGuiFileDialog _exportDialog;
        private readonly ProgressBarDialog _progressDialog;
        private CtImageStackDataset _currentDataset;
        private int _exportStartSlice = 0;
        private int _exportEndSlice = 0;
        private bool _exportAllSlices = true;
        
        public CtImageStackTools()
        {
            _exportDialog = new ImGuiFileDialog("CTExportDialog", FileDialogType.OpenDirectory, "Select Export Folder");
            _progressDialog = new ProgressBarDialog("Exporting Slices");
        }
        
        public void Draw(Dataset dataset)
        {
            if (dataset is not CtImageStackDataset ctDataset) return;
            
            _currentDataset = ctDataset;
            
            if (_exportEndSlice == 0 && ctDataset.Depth > 0)
            {
                _exportEndSlice = ctDataset.Depth - 1;
            }
            
            ImGui.Text("CT Stack Tools");
            ImGui.Separator();
            
            // Volume information
            if (ImGui.CollapsingHeader("Volume Information", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                ImGui.Text($"Dimensions: {ctDataset.Width} × {ctDataset.Height} × {ctDataset.Depth}");
                ImGui.Text($"Voxel Size: {ctDataset.PixelSize:F2} × {ctDataset.PixelSize:F2} × {ctDataset.SliceThickness:F2} {ctDataset.Unit}");
                
                if (ctDataset.BinningSize > 1)
                {
                    ImGui.Text($"Binning: {ctDataset.BinningSize}×{ctDataset.BinningSize}×{ctDataset.BinningSize}");
                }
                
                // Calculate volume size
                float volumeMm3 = (ctDataset.Width * ctDataset.PixelSize / 1000f) * 
                                 (ctDataset.Height * ctDataset.PixelSize / 1000f) * 
                                 (ctDataset.Depth * ctDataset.SliceThickness / 1000f);
                ImGui.Text($"Volume: {volumeMm3:F2} mm³");
                
                ImGui.Unindent();
            }
            
            // Export options
            if (ImGui.CollapsingHeader("Export Slices"))
            {
                ImGui.Indent();
                
                ImGui.Checkbox("Export all slices", ref _exportAllSlices);
                
                if (!_exportAllSlices)
                {
                    ImGui.SetNextItemWidth(100);
                    ImGui.InputInt("Start slice", ref _exportStartSlice);
                    _exportStartSlice = Math.Clamp(_exportStartSlice, 0, ctDataset.Depth - 1);
                    
                    ImGui.SetNextItemWidth(100);
                    ImGui.InputInt("End slice", ref _exportEndSlice);
                    _exportEndSlice = Math.Clamp(_exportEndSlice, _exportStartSlice, ctDataset.Depth - 1);
                    
                    ImGui.Text($"Will export {_exportEndSlice - _exportStartSlice + 1} slices");
                }
                
                if (ImGui.Button("Export as PNG sequence..."))
                {
                    _exportDialog.Open();
                }
                
                ImGui.Unindent();
            }
            
            // Histogram
            if (ImGui.CollapsingHeader("Histogram"))
            {
                ImGui.Indent();
                DrawHistogram(ctDataset);
                ImGui.Unindent();
            }
            
            // Handle dialogs
            HandleExportDialog();
            _progressDialog.Submit();
        }
        
        private void DrawHistogram(CtImageStackDataset dataset)
        {
            // For now, show a placeholder
            ImGui.Text("Histogram visualization coming soon...");
            ImGui.TextDisabled("Min value: 0");
            ImGui.TextDisabled("Max value: 255");
            ImGui.TextDisabled("Mean value: ~128");
        }
        
        private void HandleExportDialog()
        {
            if (_exportDialog.Submit())
            {
                string outputFolder = _exportDialog.SelectedPath;
                if (!string.IsNullOrEmpty(outputFolder) && Directory.Exists(outputFolder))
                {
                    StartExportSlices(outputFolder);
                }
            }
        }
        
        private void StartExportSlices(string outputFolder)
        {
            if (_currentDataset?.VolumeData == null)
            {
                Logger.Log("[CtImageStackTools] No volume data to export");
                return;
            }
            
            int startSlice = _exportAllSlices ? 0 : _exportStartSlice;
            int endSlice = _exportAllSlices ? _currentDataset.Depth - 1 : _exportEndSlice;
            int totalSlices = endSlice - startSlice + 1;
            
            _progressDialog.Open($"Exporting {totalSlices} slices...");
            
            Task.Run(async () =>
            {
                try
                {
                    for (int i = startSlice; i <= endSlice; i++)
                    {
                        if (_progressDialog.IsCancellationRequested)
                            break;
                        
                        // Export slice
                        await ExportSliceAsync(outputFolder, i);
                        
                        // Update progress
                        float progress = (float)(i - startSlice + 1) / totalSlices;
                        string status = $"Exported slice {i + 1} of {_currentDataset.Depth}";
                        
                        VeldridManager.ExecuteOnMainThread(() =>
                        {
                            _progressDialog.Update(progress, status);
                        });
                    }
                    
                    Logger.Log($"[CtImageStackTools] Exported {totalSlices} slices to {outputFolder}");
                }
                catch (Exception ex)
                {
                    Logger.Log($"[CtImageStackTools] Export error: {ex.Message}");
                }
                finally
                {
                    VeldridManager.ExecuteOnMainThread(() =>
                    {
                        _progressDialog.Close();
                    });
                }
            }, _progressDialog.CancellationToken);
        }
        
        private async Task ExportSliceAsync(string outputFolder, int sliceIndex)
        {
            await Task.Run(() =>
            {
                int width = _currentDataset.Width;
                int height = _currentDataset.Height;
                byte[] sliceData = new byte[width * height];
                
                // Read slice data
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        sliceData[y * width + x] = _currentDataset.VolumeData[x, y, sliceIndex];
                    }
                }
                
                // Save as PNG using StbImageWrite
                string filename = Path.Combine(outputFolder, $"slice_{sliceIndex:D5}.png");
                
                // Convert to RGBA for StbImageWrite
                byte[] rgbaData = new byte[width * height * 4];
                for (int i = 0; i < width * height; i++)
                {
                    byte gray = sliceData[i];
                    rgbaData[i * 4] = gray;
                    rgbaData[i * 4 + 1] = gray;
                    rgbaData[i * 4 + 2] = gray;
                    rgbaData[i * 4 + 3] = 255;
                }
                
                using (var stream = File.Create(filename))
                {
                    var writer = new StbImageWriteSharp.ImageWriter();
                    writer.WritePng(rgbaData, width, height, 
                        StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
                }
            });
        }
    }
}