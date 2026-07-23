// GAIA/UI/Tools/MeshExtractionTool.cs

using System.Numerics;
using GAIA.Business;
using GAIA.Data;
using GAIA.Data.CtImageStack;
using GAIA.Data.Mesh3D;
using GAIA.UI.Interfaces;
using GAIA.Util;
using ImGuiNET;

namespace GAIA.UI.Tools;

public class MeshExtractionTool : IDatasetTools, IDisposable
{
    private readonly int[] _downsamplingOptions = { 1, 2, 4, 8 };
    private CancellationTokenSource _cancellationTokenSource;
    private int _downsamplingFactor = 2;
    private bool _shellOnly;
    private int _smoothingIterations = 3;
    private int _keepPercent = 100;
    private bool _isProcessing;

    private Task _meshingTask;
    private string _outputMeshName = "";
    private float _progress;
    private int _selectedMaterialIndex;
    private string _statusText;

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
        var materialNames = materials.Select(m => m.Name).ToArray();
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("Target Material", ref _selectedMaterialIndex, materialNames, materialNames.Length))
            if (_selectedMaterialIndex < materials.Count)
                _outputMeshName = $"{ctDataset.Name}_{materials[_selectedMaterialIndex].Name}";

        if (string.IsNullOrEmpty(_outputMeshName) && materials.Any())
            _outputMeshName = $"{ctDataset.Name}_{materials[_selectedMaterialIndex].Name}";

        // Output Name Input
        ImGui.Text("Output Mesh Name:");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##MeshName", ref _outputMeshName, 256);

        // Quality/Downsampling Slider
        ImGui.Text("Mesh Quality (Lower is Better):");
        ImGui.SetNextItemWidth(-1);
        var currentDownsamplingIndex = Array.IndexOf(_downsamplingOptions, _downsamplingFactor);
        if (ImGui.SliderInt("##Downsampling", ref currentDownsamplingIndex, 0, _downsamplingOptions.Length - 1,
                $"Factor: {_downsamplingOptions[currentDownsamplingIndex]}x"))
            _downsamplingFactor = _downsamplingOptions[currentDownsamplingIndex];
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "Controls mesh resolution. A factor of 2 processes an 8x smaller volume, creating a lower quality but smaller mesh much faster.");

        ImGui.Spacing();

        // Mesh type: full surface (keeps internal cavities) vs. external shell only (fills them).
        ImGui.Checkbox("External shell only (3D printing)", ref _shellOnly);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "Fills internal cavities and keeps only the outer watertight surface.\n" +
                "Use it when internal parts were also segmented but you want a solid print.");

        ImGui.Text("Surface Smoothing:");
        ImGui.SetNextItemWidth(-1);
        ImGui.SliderInt("##Smoothing", ref _smoothingIterations, 0, 10, "%d iterations");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Taubin smoothing removes the voxel staircase without shrinking the model.");

        ImGui.Text("Simplify (keep % of triangles):");
        ImGui.SetNextItemWidth(-1);
        ImGui.SliderInt("##Simplify", ref _keepPercent, 5, 100, "%d%%");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Quadric-error (QEM) decimation. Lower = fewer triangles and smaller file,\n" +
                             "while preserving the overall shape.");

        ImGui.Spacing();

        if (_isProcessing) ImGui.BeginDisabled();

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
                _cancellationTokenSource?.Cancel();
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
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
        _cancellationTokenSource = new CancellationTokenSource();
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
                var options = new MeshingOptions
                {
                    Downsampling = _downsamplingFactor,
                    ShellOnly = _shellOnly,
                    SmoothingIterations = _smoothingIterations,
                    DecimateKeepRatio = _keepPercent / 100f
                };
                var (vertices, faces) = await mesher.GenerateMeshAsync(ctDataset.LabelData, material.ID,
                    options, progressInterface, token);

                token.ThrowIfCancellationRequested();

                if (vertices == null || vertices.Count < 3 || faces == null || faces.Count == 0)
                    throw new InvalidOperationException(
                        "Meshing resulted in an empty or invalid mesh. Ensure the selected material contains voxels that form a surface.");

                progressInterface.Report((0.95f, "Saving mesh to file..."));

                var projectDirectory = Path.GetDirectoryName(ProjectManager.Instance.ProjectPath);
                var meshFolder = Path.Combine(projectDirectory, $"{ProjectManager.Instance.ProjectName}_Meshes");
                Directory.CreateDirectory(meshFolder);

                var safeMeshName = string.Join("_", _outputMeshName.Split(Path.GetInvalidFileNameChars()));
                var meshFileName = $"{safeMeshName}_{DateTime.Now:yyyyMMddHHmmss}.obj";
                var meshFilePath = Path.Combine(meshFolder, meshFileName);

                // Create, scale, and save the dataset
                var meshDataset = Mesh3DDataset.CreateFromData(_outputMeshName, meshFilePath, vertices, faces,
                    ctDataset.PixelSize, ctDataset.Unit);

                progressInterface.Report((1.0f, "Adding mesh to project..."));

                // Note: UI updates should be dispatched to the main thread in a real application
                ProjectManager.Instance.AddDataset(meshDataset);

                Logger.Log(
                    $"Successfully generated and added mesh '{_outputMeshName}' with {vertices.Count} vertices and {faces.Count} faces.");
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
}