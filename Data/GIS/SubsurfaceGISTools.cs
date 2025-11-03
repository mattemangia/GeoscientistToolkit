// GeoscientistToolkit/UI/Tools/SubsurfaceGISTools.cs

using System.Numerics;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Tools;

/// <summary>
/// Tools for working with SubsurfaceGISDataset
/// </summary>
public class SubsurfaceGISTools : IDatasetTools
{
    private readonly ImGuiFileDialog _saveDialog;
    private readonly ImGuiFileDialog _exportVoxelsDialog;
    private readonly ImGuiFileDialog _exportLayersDialog;
    
    private bool _showStatistics = false;
    private bool _showExportOptions = false;

    public SubsurfaceGISTools()
    {
        _saveDialog = new ImGuiFileDialog("SaveSubsurfaceGISDialog", FileDialogType.SaveFile, "Save Subsurface GIS Model");
        _exportVoxelsDialog = new ImGuiFileDialog("ExportVoxelsDialog", FileDialogType.SaveFile, "Export Voxels to CSV");
        _exportLayersDialog = new ImGuiFileDialog("ExportLayersDialog", FileDialogType.SaveFile, "Export Layer Boundaries to CSV");
    }

    public void Draw(Data.Dataset dataset)
    {
        if (dataset is not SubsurfaceGISDataset subsurfaceDataset)
        {
            ImGui.TextDisabled("Invalid dataset type for subsurface GIS tools.");
            return;
        }

        ImGui.Text("Subsurface GIS Model Tools");
        ImGui.Separator();
        ImGui.Spacing();

        // Save/Export Section
        DrawSaveExportSection(subsurfaceDataset);
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Statistics Section
        DrawStatisticsSection(subsurfaceDataset);
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Model Information Section
        DrawModelInfoSection(subsurfaceDataset);
        
        // Handle dialog submissions
        HandleDialogs(subsurfaceDataset);
    }

    private void DrawSaveExportSection(SubsurfaceGISDataset dataset)
    {
        ImGui.Text("💾 Save & Export");
        ImGui.Spacing();

        // Save full model button
        if (ImGui.Button("Save Model (.subgis)", new Vector2(-1, 30)))
        {
            string[] extensions = { ".subgis" };
            _saveDialog.Open(dataset.Name, extensions);
        }
        
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text("Save the complete subsurface GIS model");
            ImGui.Text("including voxels, layer boundaries, and all properties.");
            ImGui.EndTooltip();
        }

        ImGui.Spacing();

        // Export options toggle
        if (ImGui.CollapsingHeader("Export Options", ImGuiTreeNodeFlags.None))
        {
            ImGui.Indent();
            
            // Export voxels to CSV
            if (ImGui.Button("Export Voxels to CSV", new Vector2(-1, 25)))
            {
                string[] extensions = { ".csv" };
                _exportVoxelsDialog.Open($"{dataset.Name}_voxels", extensions);
            }
            
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text("Export all voxel data (position, lithology, parameters)");
                ImGui.Text("to CSV format for external analysis.");
                ImGui.EndTooltip();
            }

            ImGui.Spacing();

            // Export layer boundaries to CSV
            if (ImGui.Button("Export Layer Boundaries to CSV", new Vector2(-1, 25)))
            {
                string[] extensions = { ".csv" };
                _exportLayersDialog.Open($"{dataset.Name}_layers", extensions);
            }
            
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text("Export layer boundary points to CSV format.");
                ImGui.EndTooltip();
            }
            
            ImGui.Unindent();
        }
    }

    private void DrawStatisticsSection(SubsurfaceGISDataset dataset)
    {
        if (ImGui.CollapsingHeader("📊 Model Statistics", ImGuiTreeNodeFlags.None))
        {
            ImGui.Indent();
            
            // Voxel statistics
            ImGui.Text("Voxel Grid:");
            ImGui.BulletText($"Total Voxels: {dataset.VoxelGrid.Count:N0}");
            ImGui.BulletText($"Resolution: {dataset.GridResolutionX} × {dataset.GridResolutionY} × {dataset.GridResolutionZ}");
            ImGui.BulletText($"Voxel Size: {dataset.VoxelSize.X:F2} × {dataset.VoxelSize.Y:F2} × {dataset.VoxelSize.Z:F2} m");
            
            ImGui.Spacing();
            
            // Lithology distribution
            var lithologyGroups = dataset.VoxelGrid
                .GroupBy(v => v.LithologyType)
                .OrderByDescending(g => g.Count())
                .ToList();
            
            if (lithologyGroups.Any())
            {
                ImGui.Text("Lithology Distribution:");
                foreach (var group in lithologyGroups.Take(5))
                {
                    float percentage = (float)group.Count() / dataset.VoxelGrid.Count * 100f;
                    ImGui.BulletText($"{group.Key}: {group.Count():N0} voxels ({percentage:F1}%)");
                }
                
                if (lithologyGroups.Count > 5)
                {
                    ImGui.BulletText($"... and {lithologyGroups.Count - 5} more types");
                }
            }
            
            ImGui.Spacing();
            
            // Parameter statistics
            var allParameters = dataset.VoxelGrid
                .SelectMany(v => v.Parameters.Keys)
                .Distinct()
                .ToList();
            
            if (allParameters.Any())
            {
                ImGui.Text("Available Parameters:");
                foreach (var param in allParameters)
                {
                    var values = dataset.VoxelGrid
                        .Where(v => v.Parameters.ContainsKey(param))
                        .Select(v => v.Parameters[param])
                        .ToList();
                    
                    if (values.Any())
                    {
                        float min = values.Min();
                        float max = values.Max();
                        float avg = values.Average();
                        
                        ImGui.BulletText($"{param}: [{min:F2}, {max:F2}] avg: {avg:F2}");
                    }
                }
            }
            
            ImGui.Spacing();
            
            // Confidence statistics
            if (dataset.VoxelGrid.Any())
            {
                float avgConfidence = dataset.VoxelGrid.Average(v => v.Confidence);
                float minConfidence = dataset.VoxelGrid.Min(v => v.Confidence);
                float maxConfidence = dataset.VoxelGrid.Max(v => v.Confidence);
                
                ImGui.Text("Confidence:");
                ImGui.BulletText($"Average: {avgConfidence:P1}");
                ImGui.BulletText($"Range: [{minConfidence:P1}, {maxConfidence:P1}]");
            }
            
            ImGui.Unindent();
        }
    }

    private void DrawModelInfoSection(SubsurfaceGISDataset dataset)
    {
        if (ImGui.CollapsingHeader("ℹ️ Model Information", ImGuiTreeNodeFlags.None))
        {
            ImGui.Indent();
            
            // Grid bounds
            ImGui.Text("Grid Bounds:");
            ImGui.BulletText($"Origin: ({dataset.GridOrigin.X:F2}, {dataset.GridOrigin.Y:F2}, {dataset.GridOrigin.Z:F2})");
            ImGui.BulletText($"Size: {dataset.GridSize.X:F2} × {dataset.GridSize.Y:F2} × {dataset.GridSize.Z:F2} m");
            
            ImGui.Spacing();
            
            // Source boreholes
            ImGui.Text($"Source Boreholes ({dataset.SourceBoreholeNames.Count}):");
            if (dataset.SourceBoreholeNames.Count > 0)
            {
                foreach (var boreholeName in dataset.SourceBoreholeNames.Take(10))
                {
                    ImGui.BulletText(boreholeName);
                }
                
                if (dataset.SourceBoreholeNames.Count > 10)
                {
                    ImGui.BulletText($"... and {dataset.SourceBoreholeNames.Count - 10} more");
                }
            }
            
            ImGui.Spacing();
            
            // Layer boundaries
            ImGui.Text($"Layer Boundaries ({dataset.LayerBoundaries.Count}):");
            foreach (var boundary in dataset.LayerBoundaries.Take(10))
            {
                ImGui.BulletText($"{boundary.LayerName} ({boundary.Points.Count} points)");
            }
            
            if (dataset.LayerBoundaries.Count > 10)
            {
                ImGui.BulletText($"... and {dataset.LayerBoundaries.Count - 10} more");
            }
            
            ImGui.Spacing();
            
            // Interpolation settings
            ImGui.Text("Interpolation Settings:");
            ImGui.BulletText($"Method: {dataset.Method}");
            ImGui.BulletText($"Radius: {dataset.InterpolationRadius:F2} m");
            ImGui.BulletText($"IDW Power: {dataset.IDWPower:F2}");
            
            if (!string.IsNullOrEmpty(dataset.HeightmapDatasetName))
            {
                ImGui.BulletText($"Heightmap: {dataset.HeightmapDatasetName}");
            }
            
            ImGui.Unindent();
        }
    }

    private void HandleDialogs(SubsurfaceGISDataset dataset)
    {
        // Handle save dialog
        if (_saveDialog.Submit())
        {
            try
            {
                var filePath = _saveDialog.SelectedPath;
                
                // Ensure .subgis extension
                if (!filePath.EndsWith(".subgis", StringComparison.OrdinalIgnoreCase))
                {
                    filePath += ".subgis";
                }
                
                dataset.SaveToFile(filePath);
                Logger.Log($"Successfully saved subsurface GIS model to: {filePath}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to save subsurface GIS model: {ex.Message}");
            }
        }

        // Handle export voxels dialog
        if (_exportVoxelsDialog.Submit())
        {
            try
            {
                var filePath = _exportVoxelsDialog.SelectedPath;
                
                // Ensure .csv extension
                if (!filePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    filePath += ".csv";
                }
                
                dataset.ExportVoxelsToCsv(filePath);
                Logger.Log($"Successfully exported voxels to: {filePath}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to export voxels: {ex.Message}");
            }
        }

        // Handle export layers dialog
        if (_exportLayersDialog.Submit())
        {
            try
            {
                var filePath = _exportLayersDialog.SelectedPath;
                
                // Ensure .csv extension
                if (!filePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    filePath += ".csv";
                }
                
                dataset.ExportLayerBoundariesToCsv(filePath);
                Logger.Log($"Successfully exported layer boundaries to: {filePath}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to export layer boundaries: {ex.Message}");
            }
        }
    }
}