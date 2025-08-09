// GeoscientistToolkit/Data/Mesh3D/Mesh3DTools.cs
using GeoscientistToolkit.Data;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Business;
using ImGuiNET;
using System.Collections.Generic;
using System.Numerics;
using GeoscientistToolkit.Util;
using System;
using System.Threading.Tasks;

namespace GeoscientistToolkit.Data.Mesh3D
{
    /// <summary>
    /// Provides transformation tools and voxelization for Mesh3DDataset in the Tools panel.
    /// </summary>
    public class Mesh3DTools : IDatasetTools
    {
        private static readonly Dictionary<Mesh3DDataset, Vector3> _rotationByDataset = new();
        private static readonly Dictionary<Mesh3DDataset, ScanToVolumeState> _scanStateByDataset = new();

        private ImGuiFileDialog _folderDialog;

        private class ScanToVolumeState
        {
            public float VoxelSize = 1.0f; // in mm
            public string OutputFolder = "";
            public bool IsProcessing = false;
            public float Progress = 0.0f;
            public string ProgressMessage = "";
        }

        public Mesh3DTools()
        {
            _folderDialog = new ImGuiFileDialog("SelectOutputFolder", FileDialogType.OpenDirectory, "Select Output Folder");
        }

        public void Draw(Dataset dataset)
        {
            if (dataset is not Mesh3DDataset mesh) return;

            if (!_rotationByDataset.ContainsKey(mesh))
                _rotationByDataset[mesh] = Vector3.Zero;

            if (!_scanStateByDataset.ContainsKey(mesh))
                _scanStateByDataset[mesh] = new ScanToVolumeState();

            Vector3 rot = _rotationByDataset[mesh];
            float scale = mesh.Scale;

            if (ImGui.CollapsingHeader("Transform Tools", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();

                if (ImGui.SliderFloat("Scale", ref scale, 0.01f, 10f, "%.2f×"))
                    mesh.Scale = scale;

                if (ImGui.SliderFloat3("Rotation (X/Y/Z)", ref rot, -180f, 180f, "%.0f°"))
                    _rotationByDataset[mesh] = rot;

                if (ImGui.Button("Reset Transform"))
                {
                    mesh.Scale = 1.0f;
                    _rotationByDataset[mesh] = Vector3.Zero;
                }

                ImGui.Unindent();
            }

            ImGui.Separator();

            // Scan to Volume Tool
            if (ImGui.CollapsingHeader("Scan to Volume", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();

                var scanState = _scanStateByDataset[mesh];

                if (!mesh.IsLoaded)
                {
                    ImGui.TextColored(new Vector4(1, 1, 0, 1), "Model must be loaded first");
                    if (ImGui.Button("Load Model"))
                    {
                        mesh.Load();
                    }
                }
                else if (scanState.IsProcessing)
                {
                    // Show progress
                    ImGui.Text(scanState.ProgressMessage);
                    ImGui.ProgressBar(scanState.Progress, new Vector2(-1, 0));
                }
                else
                {
                    // Voxel size control
                    ImGui.Text("Voxel Size (Quality):");
                    ImGui.SetNextItemWidth(-1);

                    if (ImGui.SliderFloat("##VoxelSize", ref scanState.VoxelSize, 0.1f, 10.0f, "%.1f mm"))
                    {
                        scanState.VoxelSize = Math.Max(0.1f, scanState.VoxelSize);
                    }

                    ImGui.TextDisabled("Smaller = Higher Quality, Larger File");

                    // Calculate estimated volume dimensions
                    var size = mesh.BoundingBoxMax - mesh.BoundingBoxMin;
                    var scaledSize = size * mesh.Scale;
                    int estimatedWidth = (int)Math.Ceiling(scaledSize.X / scanState.VoxelSize);
                    int estimatedHeight = (int)Math.Ceiling(scaledSize.Y / scanState.VoxelSize);
                    int estimatedDepth = (int)Math.Ceiling(scaledSize.Z / scanState.VoxelSize);

                    ImGui.Separator();
                    ImGui.Text($"Estimated Volume Size:");
                    ImGui.Text($"  {estimatedWidth} × {estimatedHeight} × {estimatedDepth} voxels");
                    long estimatedBytes = (long)estimatedWidth * estimatedHeight * estimatedDepth;
                    ImGui.Text($"  ~{FormatBytes(estimatedBytes)} uncompressed");

                    // Output folder selection
                    ImGui.Separator();
                    ImGui.Text("Output Folder:");

                    if (!string.IsNullOrEmpty(scanState.OutputFolder))
                    {
                        ImGui.TextWrapped(scanState.OutputFolder);
                    }
                    else
                    {
                        ImGui.TextDisabled("No folder selected");
                    }

                    if (ImGui.Button("Select Output Folder", new Vector2(-1, 0)))
                    {
                        _folderDialog.Open(scanState.OutputFolder);
                    }

                    // Start conversion button
                    ImGui.Separator();
                    bool canStart = !string.IsNullOrEmpty(scanState.OutputFolder) &&
                                   System.IO.Directory.Exists(scanState.OutputFolder);

                    if (!canStart) ImGui.BeginDisabled();

                    if (ImGui.Button("Start Scan to Volume", new Vector2(-1, 30)))
                    {
                        _ = StartScanToVolumeAsync(mesh, scanState);
                    }

                    if (!canStart) ImGui.EndDisabled();

                    if (!canStart && !string.IsNullOrEmpty(scanState.OutputFolder))
                    {
                        ImGui.TextColored(new Vector4(1, 0, 0, 1), "Selected folder does not exist!");
                    }
                }

                ImGui.Unindent();
            }

            if (!mesh.IsLoaded && ImGui.CollapsingHeader("Model Status"))
            {
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "Model not loaded");
                if (ImGui.Button("Load Model"))
                {
                    mesh.Load();
                }
            }

            // Handle folder dialog
            if (_folderDialog.Submit())
            {
                if (!string.IsNullOrEmpty(_folderDialog.SelectedPath))
                {
                    var scanState = _scanStateByDataset[mesh];
                    scanState.OutputFolder = _folderDialog.SelectedPath;
                }
            }
        }

        private async Task StartScanToVolumeAsync(Mesh3DDataset mesh, ScanToVolumeState state)
        {
            state.IsProcessing = true;
            state.Progress = 0.0f;
            state.ProgressMessage = "Initializing voxelization...";

            try
            {
                // Create progress reporter
                var progress = new Progress<(float progress, string message)>(report =>
                {
                    state.Progress = report.progress;
                    state.ProgressMessage = report.message;
                });

                // Run voxelization
                var voxelizer = new MeshVoxelizer();
                var outputPath = await voxelizer.VoxelizeToImageStackAsync(
                    mesh,
                    state.OutputFolder,
                    state.VoxelSize,
                    _rotationByDataset.GetValueOrDefault(mesh, Vector3.Zero),
                    progress);

                if (!string.IsNullOrEmpty(outputPath))
                {
                    state.ProgressMessage = "Loading voxelized volume...";

                    // Load the created stack as a CT dataset
                    var loader = new Data.Loaders.CTStackLoaderWrapper
                    {
                        Mode = Data.Loaders.CTStackLoaderWrapper.LoadMode.LegacyFor2D,
                        SourcePath = outputPath,
                        IsMultiPageTiff = false,
                        PixelSize = state.VoxelSize,
                        Unit = Data.Loaders.PixelSizeUnit.Millimeters,
                        BinningFactor = 1
                    };

                    var loadProgress = new Progress<(float progress, string message)>(report =>
                    {
                        state.Progress = 0.9f + (report.progress * 0.1f);
                        state.ProgressMessage = "Loading CT stack: " + report.message;
                    });

                    var newDataset = await loader.LoadAsync(loadProgress);

                    if (newDataset != null)
                    {
                        // Add the new dataset to the project
                        ProjectManager.Instance.AddDataset(newDataset);

                        // Remove and dispose the old mesh dataset
                        ProjectManager.Instance.RemoveDataset(mesh);
                        mesh.Unload();

                        // Clean up state
                        _rotationByDataset.Remove(mesh);
                        _scanStateByDataset.Remove(mesh);

                        Logger.Log($"[Mesh3DTools] Successfully converted mesh to volume and loaded as CT stack");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Mesh3DTools] Scan to volume failed: {ex.Message}");
                state.ProgressMessage = $"Error: {ex.Message}";
            }
            finally
            {
                state.IsProcessing = false;
            }
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;

            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }

            return $"{size:0.##} {sizes[order]}";
        }

        public static Vector3 GetRotation(Mesh3DDataset dataset)
        {
            if (_rotationByDataset.TryGetValue(dataset, out var rot))
                return rot;
            return Vector3.Zero;
        }
    }
}