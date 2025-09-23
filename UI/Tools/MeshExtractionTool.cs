// GeoscientistToolkit/UI/Tools/MeshExtractionTool.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Tools
{
    public class MeshExtractionTool : IDatasetTools, IDisposable
    {
        private int _selectedMaterialIndex = 0;
        private string _outputMeshName = "";
        private int _downsamplingFactor = 2;
        private readonly int[] _downsamplingOptions = { 1, 2, 4, 8 };

        private Task _meshingTask;
        private float _progress;
        private string _statusText;
        private bool _isProcessing;
        private System.Threading.CancellationTokenSource _cancellationTokenSource;

        public void Draw(Dataset dataset)
        {
            if (dataset is not CtImageStackDataset ctDataset) return;

            ImGui.Text("Create 3D Mesh from Material");
            ImGui.Separator();

            var materials = ctDataset.Materials.Where(m => m.ID != 0).ToList();
            if (materials.Count == 0)
            {
                ImGui.TextDisabled("No materials defined. Segment the dataset first.");
                return;
            }

            // Target Material Dropdown
            string[] materialNames = materials.Select(m => m.Name).ToArray();
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo("Target Material", ref _selectedMaterialIndex, materialNames, materialNames.Length))
            {
                if (_selectedMaterialIndex < materials.Count)
                    _outputMeshName = $"{ctDataset.Name}_{materials[_selectedMaterialIndex].Name}";
            }

            if (string.IsNullOrEmpty(_outputMeshName) && materials.Any())
            {
                _outputMeshName = $"{ctDataset.Name}_{materials[_selectedMaterialIndex].Name}";
            }

            // Output Name Input
            ImGui.Text("Output Mesh Name:");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("##MeshName", ref _outputMeshName, 256);

            // Quality/Downsampling Slider
            ImGui.Text("Mesh Quality (Lower is Better):");
            ImGui.SetNextItemWidth(-1);
            int currentDownsamplingIndex = Array.IndexOf(_downsamplingOptions, _downsamplingFactor);
            if (ImGui.SliderInt("##Downsampling", ref currentDownsamplingIndex, 0, _downsamplingOptions.Length - 1, $"Factor: {_downsamplingOptions[currentDownsamplingIndex]}x"))
            {
                _downsamplingFactor = _downsamplingOptions[currentDownsamplingIndex];
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Controls mesh resolution. A factor of 2 processes an 8x smaller volume, creating a lower quality but smaller mesh much faster.");
            }

            ImGui.Spacing();

            if (_isProcessing)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Button("Generate Mesh", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
            {
                var selectedMaterial = materials[_selectedMaterialIndex];
                StartMeshingProcess(ctDataset, selectedMaterial);
            }

            if (_isProcessing)
            {
                ImGui.EndDisabled();
                ImGui.Spacing();
                ImGui.ProgressBar(_progress, new Vector2(-1, 0), $"{_progress * 100:0}%");
                ImGui.Text(_statusText);

                if (ImGui.Button("Cancel", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
                {
                    _cancellationTokenSource?.Cancel();
                }
            }
        }

        private void StartMeshingProcess(CtImageStackDataset ctDataset, Material material)
        {
            if (string.IsNullOrWhiteSpace(ProjectManager.Instance.ProjectPath))
            {
                Logger.LogError("Project must be saved before generating a mesh.");
                _statusText = "Error: Please save the project first.";
                return;
            }

            if (ctDataset.LabelData == null)
            {
                Logger.LogError("Label data is not loaded. Cannot generate mesh.");
                _statusText = "Error: Label data not found.";
                return;
            }

            _isProcessing = true;
            _progress = 0f;
            _statusText = "Initializing...";
            _cancellationTokenSource = new System.Threading.CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            var progressReporter = new Progress<(float, string)>(p =>
            {
                _progress = p.Item1;
                _statusText = p.Item2;
            });
            
            // Cast IProgress<T> for reporting
            var progressInterface = (IProgress<(float, string)>)progressReporter;

            // Explicitly cast the async lambda to Func<Task> to resolve ambiguity
            _meshingTask = Task.Run(async () =>
            {
                try
                {
                    var mesher = new SurfaceNetsMesher();
                    var (vertices, faces) = await mesher.GenerateMeshAsync(ctDataset.LabelData, material.ID, _downsamplingFactor, progressInterface, token);

                    token.ThrowIfCancellationRequested();

                    if (vertices == null || vertices.Count < 3 || faces == null || faces.Count == 0)
                    {
                        throw new InvalidOperationException("Meshing resulted in an empty or invalid mesh. Ensure the selected material contains voxels that form a surface.");
                    }
                    
                    progressInterface.Report((0.95f, "Saving mesh to file..."));

                    string projectDirectory = Path.GetDirectoryName(ProjectManager.Instance.ProjectPath);
                    string meshFolder = Path.Combine(projectDirectory, $"{ProjectManager.Instance.ProjectName}_Meshes");
                    Directory.CreateDirectory(meshFolder);

                    string safeMeshName = string.Join("_", _outputMeshName.Split(Path.GetInvalidFileNameChars()));
                    string meshFileName = $"{safeMeshName}_{DateTime.Now:yyyyMMddHHmmss}.obj";
                    string meshFilePath = Path.Combine(meshFolder, meshFileName);
                    
                    // Create, scale, and save the dataset
                    var meshDataset = Mesh3DDataset.CreateFromData(_outputMeshName, meshFilePath, vertices, faces, ctDataset.PixelSize, ctDataset.Unit);
                    
                    progressInterface.Report((1.0f, "Adding mesh to project..."));
                    
                    // Note: UI updates should be dispatched to the main thread in a real application
                    ProjectManager.Instance.AddDataset(meshDataset);

                    Logger.Log($"Successfully generated and added mesh '{_outputMeshName}' with {vertices.Count} vertices and {faces.Count} faces.");
                }
                catch (OperationCanceledException)
                {
                    Logger.LogWarning("Mesh generation was canceled.");
                    _statusText = "Canceled.";
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to generate mesh: {ex.Message}");
                    _statusText = $"Error: {ex.Message}";
                }
                finally
                {
                    _isProcessing = false;
                }
            }, token);
        }

        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
        }
    }
}