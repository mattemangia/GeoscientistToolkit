// GeoscientistToolkit/Data/PointCloud/PointCloudTools.cs

using System.Numerics;
using GeoscientistToolkit.Analysis.SlopeStability;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Data.PointCloud;

/// <summary>
/// Provides tools for PointCloudDataset in the Tools panel.
/// </summary>
public class PointCloudTools : IDatasetTools
{
    // Per-dataset state
    private static readonly Dictionary<PointCloudDataset, ToolState> _stateByDataset = new();

    // Dialogs
    private readonly ImGuiExportFileDialog _exportDialog;
    private readonly ImGuiFileDialog _folderDialog;

    public PointCloudTools()
    {
        _exportDialog = new ImGuiExportFileDialog("ExportPointCloudMesh", "Export Mesh");
        _folderDialog = new ImGuiFileDialog("SelectOutputFolder", FileDialogType.OpenDirectory, "Select Output Folder");
    }

    public void Draw(Dataset dataset)
    {
        if (dataset is not PointCloudDataset pc) return;

        if (!_stateByDataset.ContainsKey(pc))
            _stateByDataset[pc] = new ToolState();

        var state = _stateByDataset[pc];

        // Transform Tools
        if (ImGui.CollapsingHeader("Transform Tools", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawTransformTools(pc, state);
        }

        ImGui.Separator();

        // Mesh Generation
        if (ImGui.CollapsingHeader("Generate Mesh", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawMeshGenerationTools(pc, state);
        }

        ImGui.Separator();

        // Downsampling
        if (ImGui.CollapsingHeader("Downsampling"))
        {
            DrawDownsamplingTools(pc, state);
        }

        ImGui.Separator();

        // Export
        if (ImGui.CollapsingHeader("Export"))
        {
            DrawExportTools(pc, state);
        }

        // Handle dialog submissions
        if (_exportDialog.Submit())
        {
            if (!string.IsNullOrEmpty(_exportDialog.SelectedPath))
            {
                state.ExportPath = _exportDialog.SelectedPath;
            }
        }

        if (_folderDialog.Submit())
        {
            if (!string.IsNullOrEmpty(_folderDialog.SelectedPath))
            {
                state.OutputFolder = _folderDialog.SelectedPath;
            }
        }
    }

    private void DrawTransformTools(PointCloudDataset pc, ToolState state)
    {
        ImGui.Indent();

        // Scale
        var scale = pc.Scale;
        if (ImGui.SliderFloat("Scale", ref scale, 0.01f, 10f, "%.2fx"))
            pc.Scale = scale;

        // Rotation (visual only for now)
        ImGui.SliderFloat3("Rotation (X/Y/Z)", ref state.Rotation, -180f, 180f, "%.0f");

        ImGui.Spacing();

        if (ImGui.Button("Center at Origin"))
        {
            pc.CenterAtOrigin();
            Logger.Log($"Centered point cloud at origin: {pc.Name}");
        }

        ImGui.SameLine();

        if (ImGui.Button("Reset Transform"))
        {
            pc.Scale = 1.0f;
            state.Rotation = Vector3.Zero;
        }

        ImGui.Unindent();
    }

    private void DrawMeshGenerationTools(PointCloudDataset pc, ToolState state)
    {
        ImGui.Indent();

        if (!pc.IsLoaded)
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "Point cloud must be loaded first");
            if (ImGui.Button("Load Point Cloud"))
                pc.Load();
            ImGui.Unindent();
            return;
        }

        if (state.IsProcessing)
        {
            ImGui.Text(state.ProgressMessage);
            ImGui.ProgressBar(state.Progress, new Vector2(-1, 0));
            ImGui.Unindent();
            return;
        }

        ImGui.Text("Mesh Generation Parameters:");
        ImGui.Spacing();

        // Grid step
        ImGui.SetNextItemWidth(150);
        ImGui.InputFloat("Grid Step", ref state.GridStep, 0.1f, 1.0f, "%.2f");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Grid cell size for downsampling (larger = fewer triangles)");

        // Max edge length
        ImGui.SetNextItemWidth(150);
        ImGui.InputFloat("Max Edge Length", ref state.MaxEdgeLength, 0.5f, 2.0f, "%.2f");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Maximum triangle edge length (filters boundary triangles)");

        // Z depth for solid mesh
        ImGui.SetNextItemWidth(150);
        ImGui.InputFloat("Base Depth", ref state.ZDeep, 1.0f, 10.0f, "%.1f");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Depth of solid mesh base below surface");

        ImGui.Spacing();

        // Checkboxes
        ImGui.Checkbox("Create Solid Mesh", ref state.CreateSolidMesh);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Creates watertight mesh with bottom and sides");

        ImGui.Checkbox("Translate to Origin", ref state.TranslateToOrigin);

        ImGui.Spacing();

        // Output path
        ImGui.Text("Output Folder:");
        if (!string.IsNullOrEmpty(state.OutputFolder))
            ImGui.TextWrapped(state.OutputFolder);
        else
            ImGui.TextDisabled("No folder selected");

        if (ImGui.Button("Select Output Folder", new Vector2(-1, 0)))
            _folderDialog.Open(state.OutputFolder);

        ImGui.Spacing();

        // Generate button
        var canGenerate = !string.IsNullOrEmpty(state.OutputFolder) && Directory.Exists(state.OutputFolder);

        if (!canGenerate) ImGui.BeginDisabled();

        if (ImGui.Button("Generate 3D Mesh", new Vector2(-1, 30)))
        {
            _ = GenerateMeshAsync(pc, state);
        }

        if (!canGenerate) ImGui.EndDisabled();

        if (!canGenerate && !string.IsNullOrEmpty(state.OutputFolder))
            ImGui.TextColored(new Vector4(1, 0, 0, 1), "Selected folder does not exist!");

        ImGui.Unindent();
    }

    private void DrawDownsamplingTools(PointCloudDataset pc, ToolState state)
    {
        ImGui.Indent();

        if (!pc.IsLoaded)
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "Load the point cloud first");
            ImGui.Unindent();
            return;
        }

        ImGui.Text($"Current points: {pc.PointCount:N0}");

        ImGui.SetNextItemWidth(150);
        ImGui.InputFloat("Voxel Grid Size", ref state.DownsampleGridSize, 0.1f, 1.0f, "%.2f");

        // Estimate resulting point count
        var size = pc.Size;
        var estimatedBuckets = (int)((size.X / state.DownsampleGridSize) *
                                      (size.Y / state.DownsampleGridSize) *
                                      (size.Z / state.DownsampleGridSize));
        estimatedBuckets = Math.Min(estimatedBuckets, pc.PointCount);
        ImGui.Text($"Estimated result: ~{estimatedBuckets:N0} points");

        ImGui.Spacing();

        if (ImGui.Button("Downsample", new Vector2(-1, 0)))
        {
            pc.Downsample(state.DownsampleGridSize);
        }

        ImGui.TextColored(new Vector4(1, 0.8f, 0, 1), "Warning: This operation cannot be undone!");

        ImGui.Unindent();
    }

    private void DrawExportTools(PointCloudDataset pc, ToolState state)
    {
        ImGui.Indent();

        if (!pc.IsLoaded)
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "Load the point cloud first");
            ImGui.Unindent();
            return;
        }

        ImGui.Text("Export Path:");
        if (!string.IsNullOrEmpty(state.ExportPath))
            ImGui.TextWrapped(state.ExportPath);
        else
            ImGui.TextDisabled("No file selected");

        if (ImGui.Button("Select Export File", new Vector2(-1, 0)))
        {
            var defaultName = Path.GetFileNameWithoutExtension(pc.FilePath) + "_export";
            _exportDialog.SetExtensions(
                (".xyz", "XYZ Point Cloud"),
                (".txt", "Text File"),
                (".csv", "CSV File"));
            _exportDialog.Open(defaultName);
        }

        ImGui.Spacing();

        var canExport = !string.IsNullOrEmpty(state.ExportPath);

        if (!canExport) ImGui.BeginDisabled();

        if (ImGui.Button("Export Point Cloud", new Vector2(-1, 0)))
        {
            try
            {
                pc.Save(state.ExportPath);
                Logger.Log($"Exported point cloud to: {state.ExportPath}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to export: {ex.Message}");
            }
        }

        if (!canExport) ImGui.EndDisabled();

        ImGui.Unindent();
    }

    private async Task GenerateMeshAsync(PointCloudDataset pc, ToolState state)
    {
        state.IsProcessing = true;
        state.Progress = 0.0f;
        state.ProgressMessage = "Initializing mesh generation...";

        try
        {
            var parameters = new PointCloudMeshGenerator.MeshGenerationParameters
            {
                GridStep = state.GridStep,
                MaxEdgeLength = state.MaxEdgeLength,
                ZDeep = state.ZDeep,
                CreateSolidMesh = state.CreateSolidMesh,
                TranslateToOrigin = state.TranslateToOrigin,
                EnableDownsampling = true
            };

            var generator = new PointCloudMeshGenerator(parameters);
            generator.SetProgressCallback((message, progress) =>
            {
                state.Progress = progress * 0.8f;
                state.ProgressMessage = message;
            });

            state.ProgressMessage = "Generating mesh from point cloud...";

            var result = await Task.Run(() => generator.GenerateFromPoints(pc.Points));

            if (!result.Success)
            {
                state.ProgressMessage = $"Error: {result.StatusMessage}";
                return;
            }

            state.Progress = 0.85f;
            state.ProgressMessage = "Saving mesh file...";

            // Create output file path
            var outputPath = Path.Combine(state.OutputFolder,
                Path.GetFileNameWithoutExtension(pc.FilePath) + "_mesh.obj");

            // Write OBJ file
            await Task.Run(() => WriteMeshToOBJ(result, outputPath));

            state.Progress = 0.95f;
            state.ProgressMessage = "Loading mesh dataset...";

            // Create and add Mesh3DDataset
            var meshDataset = new Mesh3DDataset(pc.Name + "_mesh", outputPath);
            meshDataset.Load();

            ProjectManager.Instance.AddDataset(meshDataset);

            state.Progress = 1.0f;
            state.ProgressMessage = $"Success! Created mesh with {result.Vertices.Count:N0} vertices";

            Logger.Log($"Generated mesh from point cloud: {result.Vertices.Count} vertices, {result.Faces.Count} faces");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Mesh generation failed: {ex.Message}");
            state.ProgressMessage = $"Error: {ex.Message}";
        }
        finally
        {
            state.IsProcessing = false;
        }
    }

    private void WriteMeshToOBJ(PointCloudMeshGenerator.MeshGenerationResult result, string path)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Generated from point cloud by GeoscientistToolkit");
        sb.AppendLine($"# Vertices: {result.Vertices.Count}");
        sb.AppendLine($"# Faces: {result.Faces.Count}");
        sb.AppendLine();

        foreach (var v in result.Vertices)
        {
            sb.AppendLine($"v {v.X:F6} {v.Y:F6} {v.Z:F6}");
        }

        sb.AppendLine();

        foreach (var face in result.Faces)
        {
            sb.AppendLine($"f {face[0] + 1} {face[1] + 1} {face[2] + 1}");
        }

        File.WriteAllText(path, sb.ToString());
    }

    private class ToolState
    {
        // Transform
        public Vector3 Rotation = Vector3.Zero;

        // Mesh generation
        public float GridStep = 2.0f;
        public float MaxEdgeLength = 4.0f;
        public float ZDeep = 20.0f;
        public bool CreateSolidMesh = true;
        public bool TranslateToOrigin = false;
        public string OutputFolder = "";

        // Downsampling
        public float DownsampleGridSize = 1.0f;

        // Export
        public string ExportPath = "";

        // Processing state
        public bool IsProcessing;
        public float Progress;
        public string ProgressMessage = "";
    }
}
