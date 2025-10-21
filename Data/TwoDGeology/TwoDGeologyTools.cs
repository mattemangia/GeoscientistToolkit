// GeoscientistToolkit/UI/GIS/TwoDGeologyTools.cs

using System.Numerics;
using GeoscientistToolkit.Business.GIS;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.TwoDGeology;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using StbImageWriteSharp;

namespace GeoscientistToolkit.UI.GIS;

/// <summary>
///     Provides a categorized tool panel for 2D Geology datasets.
/// </summary>
public class TwoDGeologyTools : IDatasetTools
{
    private readonly Dictionary<ToolCategory, string> _categoryNames;
    private readonly Dictionary<ToolCategory, List<ToolEntry>> _toolsByCategory;
    private ToolCategory _selectedCategory = ToolCategory.Editing;

    public TwoDGeologyTools()
    {
        _categoryNames = new Dictionary<ToolCategory, string>
        {
            { ToolCategory.Editing, "Editing" },
            { ToolCategory.Analysis, "Analysis" }
        };

        _toolsByCategory = new Dictionary<ToolCategory, List<ToolEntry>>
        {
            {
                ToolCategory.Editing, new List<ToolEntry>
                {
                    new() { Name = "Vertex Editor", Tool = new VertexEditorTool() },
                    new() { Name = "Advanced Editor", Tool = new TwoDGeologyEditorTools() }
                }
            },
            {
                ToolCategory.Analysis, new List<ToolEntry>
                {
                    new() { Name = "Restoration Tool", Tool = new RestorationTool() },
                    new() { Name = "Forward Modeling Tool", Tool = new ForwardModelingTool() },
                    new() { Name = "Export Tool", Tool = new ExportTool() }
                }
            }
        };
    }

    public void Draw(Dataset dataset)
    {
        if (dataset is not TwoDGeologyDataset twoDDataset)
        {
            ImGui.TextDisabled("Tools are only available for 2D Geology datasets.");
            return;
        }

        // Draw category selection
        ImGui.Text("Tool Category:");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##CategorySelector", _categoryNames[_selectedCategory]))
        {
            foreach (var category in _categoryNames.Keys)
                if (ImGui.Selectable(_categoryNames[category], _selectedCategory == category))
                    _selectedCategory = category;

            ImGui.EndCombo();
        }

        ImGui.Separator();

        // Draw tools in the selected category
        var tools = _toolsByCategory[_selectedCategory];
        if (ImGui.BeginTabBar("ToolsTabBar"))
        {
            foreach (var toolEntry in tools)
                if (ImGui.BeginTabItem(toolEntry.Name))
                {
                    toolEntry.Tool.Draw(twoDDataset);
                    ImGui.EndTabItem();
                }

            ImGui.EndTabBar();
        }
    }

    private enum ToolCategory
    {
        Editing,
        Analysis
    }

    private class ToolEntry
    {
        public string Name { get; set; }
        public IDatasetTools Tool { get; set; }
    }

    // --- NESTED TOOL CLASSES ---


    private class ExportTool : IDatasetTools
    {
        private static SvgExporter _svgExporter = new();

        private static readonly ImGuiExportFileDialog _svgExportDialog =
            new("SvgExportStandalone", "Export Current View as SVG");

        private static readonly AnimationExporter _animationExporter = new();

        // SVG export options
        private static bool _svgIncludeLabels = true;
        private static bool _svgIncludeGrid = true;
        private static bool _svgIncludeLegend = true;
        private static int _svgWidth = 1920;
        private static int _svgHeight = 1080;
        private static bool _show2DGeoExportInfo;

        public void Draw(Dataset dataset)
        {
            var twoDDataset = dataset as TwoDGeologyDataset;
            if (twoDDataset?.ProfileData == null) return;

            ImGui.TextWrapped("Export the current cross-section view to various formats.");
            ImGui.Separator();

            // === SVG EXPORT ===
            ImGui.TextColored(new Vector4(0.2f, 0.8f, 0.3f, 1f), "📄 SVG Export (Vector Graphics)");
            ImGui.TextWrapped(
                "Export as scalable vector graphics - perfect for publications, presentations, and further editing in Inkscape/Illustrator.");
            ImGui.Spacing();

            ImGui.Text("SVG Settings:");
            ImGui.Indent();

            ImGui.SetNextItemWidth(150);
            ImGui.InputInt("Width (px)", ref _svgWidth);
            _svgWidth = Math.Clamp(_svgWidth, 800, 4096);

            ImGui.SetNextItemWidth(150);
            ImGui.InputInt("Height (px)", ref _svgHeight);
            _svgHeight = Math.Clamp(_svgHeight, 600, 4096);

            ImGui.Checkbox("Include Labels & Title", ref _svgIncludeLabels);
            ImGui.Checkbox("Include Grid Lines", ref _svgIncludeGrid);
            ImGui.Checkbox("Include Legend", ref _svgIncludeLegend);

            ImGui.Unindent();
            ImGui.Spacing();

            if (ImGui.Button("📄 Export Current View as SVG...", new Vector2(-1, 0)))
            {
                _svgExportDialog.SetExtensions((".svg", "Scalable Vector Graphics"));
                _svgExportDialog.Open($"cross_section_{DateTime.Now:yyyyMMdd_HHmmss}");
            }

            // Handle SVG export dialog
            if (_svgExportDialog.Submit())
            {
                var section = twoDDataset.ProfileData;
                _svgExporter = new SvgExporter(_svgWidth, _svgHeight);
                _svgExporter.SaveToFile(_svgExportDialog.SelectedPath, section,
                    _svgIncludeLabels, _svgIncludeGrid, _svgIncludeLegend);
            }

            ImGui.Separator();

            // === RASTER EXPORT ===
            ImGui.TextColored(new Vector4(1f, 0.6f, 0.2f, 1f), "🖼️ Raster Export (PNG/BMP)");
            ImGui.TextWrapped("Export a single frame as PNG or BMP image.");
            ImGui.Spacing();

            _animationExporter.DrawUI();

            if (ImGui.Button("🖼️ Export Single Frame as PNG/BMP...", new Vector2(-1, 0)))
                ExportSingleFrame(twoDDataset);

            ImGui.Separator();

            // === BINARY EXPORT ===
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 1f, 1f), "💾 2D Geology Format");
            ImGui.TextWrapped("Export as native binary format for re-opening later.");
            ImGui.Spacing();

            if (ImGui.Button("💾 Save as 2D Geology Dataset...", new Vector2(-1, 0)))
            {
                // This would trigger the same export as in GeologicalMappingViewer
                Logger.Log("Use 'Export as 2D Geology Dataset' from the profile generation window.");
                _show2DGeoExportInfo = true;
                ImGui.OpenPopup("Info##2DGeoExport");
            }

            if (ImGui.BeginPopupModal("Info##2DGeoExport", ref _show2DGeoExportInfo, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text("To export in 2D Geology format:");
                ImGui.BulletText("Generate the profile from the Geological Mapping Viewer");
                ImGui.BulletText("Click 'Export as 2D Geology Dataset' in the cross-section window");
                ImGui.Spacing();
                if (ImGui.Button("OK", new Vector2(120, 0)))
                {
                    _show2DGeoExportInfo = false;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }

            ImGui.Separator();

            // === EXPORT INFO ===
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "Export Format Guide:");
            ImGui.Spacing();
            ImGui.BulletText("SVG: Best for publications, infinite zoom, editable");
            ImGui.BulletText("PNG: Good quality images for presentations");
            ImGui.BulletText("BMP: Uncompressed, fast, large files");
            ImGui.BulletText("2D Geology: Native format, preserves all data");
        }

        private void ExportSingleFrame(TwoDGeologyDataset dataset)
        {
            // Create a temporary restoration object just to use the rendering
            var restoration = new StructuralRestoration(dataset.ProfileData);

            var outputPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                $"cross_section_{DateTime.Now:yyyyMMdd_HHmmss}.png");

            // Use the animation exporter to create a single frame
            var exporter = new AnimationExporter();

            // Create temporary folder for single frame
            var tempFolder = Path.Combine(Path.GetTempPath(), $"geotk_export_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempFolder);

            try
            {
                // Export one frame at current state (0% = original)
                restoration.Restore(0f); // This just copies the original

                // Render frame manually
                var frameData = RenderFrameForExport(dataset.ProfileData);
                var fileName = "frame_00000.png";
                var filePath = Path.Combine(tempFolder, fileName);

                SaveFrameAsPng(frameData, filePath, 1920, 1080);

                // Move to final location
                var finalPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    $"cross_section_{DateTime.Now:yyyyMMdd_HHmmss}.png");

                File.Move(filePath, finalPath);

                Logger.Log($"Exported single frame to: {finalPath}");
            }
            finally
            {
                // Cleanup temp folder
                try
                {
                    Directory.Delete(tempFolder, true);
                }
                catch
                {
                }
            }
        }

        private byte[] RenderFrameForExport(GeologicalMapping.CrossSectionGenerator.CrossSection section)
        {
            // This would use the same rendering logic as AnimationExporter
            // For now, just return a placeholder
            Logger.LogWarning("Single frame export not fully implemented yet - use animation export with 1 frame");
            return new byte[1920 * 1080 * 4]; // Placeholder
        }

        private void SaveFrameAsPng(byte[] imageData, string filePath, int width, int height)
        {
            using var stream = File.OpenWrite(filePath);
            var writer = new ImageWriter();
            writer.WritePng(imageData, width, height, ColorComponents.RedGreenBlueAlpha, stream);
        }
    }

    private class VertexEditorTool : IDatasetTools
    {
        public void Draw(Dataset dataset)
        {
            var twoDDataset = dataset as TwoDGeologyDataset;
            if (twoDDataset?.GetViewer() is not TwoDGeologyViewer viewer)
            {
                ImGui.TextDisabled("Viewer not available for editing.");
                return;
            }

            ImGui.TextWrapped("Activate vertex editing mode in the viewer.");

            var isEditing = viewer.CurrentEditMode == TwoDGeologyViewer.EditMode.EditVertices;
            if (ImGui.Checkbox("Enable Vertex Editing", ref isEditing))
                viewer.CurrentEditMode =
                    isEditing ? TwoDGeologyViewer.EditMode.EditVertices : TwoDGeologyViewer.EditMode.None;

            ImGui.Separator();

            ImGui.BeginDisabled(!viewer.UndoRedo.CanUndo);
            if (ImGui.Button("Undo")) viewer.UndoRedo.Undo();
            ImGui.EndDisabled();

            ImGui.SameLine();

            ImGui.BeginDisabled(!viewer.UndoRedo.CanRedo);
            if (ImGui.Button("Redo")) viewer.UndoRedo.Redo();
            ImGui.EndDisabled();

            ImGui.Separator();
            ImGui.TextWrapped(
                "Instructions:\n- Click on a line to select it for editing.\n- Drag square handles to move vertices.\n- Drag circular handles on line segments to add new vertices.");
        }
    }

    private class RestorationTool : IDatasetTools
    {
        private static StructuralRestoration _restoration;
        private static float _restorationPercentage;
        private static readonly AnimationExporter _animationExporter = new();
        private static SvgExporter _svgExporter = new();
        private static readonly ImGuiExportFileDialog _svgExportDialog = new("SvgExportDialog", "Export as SVG");

        // SVG export options
        private static bool _svgIncludeLabels = true;
        private static bool _svgIncludeGrid = true;
        private static bool _svgIncludeLegend = true;
        private static int _svgWidth = 1920;
        private static int _svgHeight = 1080;

        public void Draw(Dataset dataset)
        {
            var twoDDataset = dataset as TwoDGeologyDataset;
            if (twoDDataset?.ProfileData == null) return;

            if (_restoration == null || _restoration.RestoredSection.Profile != twoDDataset.ProfileData.Profile)
                _restoration = new StructuralRestoration(twoDDataset.ProfileData);

            ImGui.TextWrapped(
                "Structural Restoration: Reverse faulting and folding to validate your cross-section interpretation.");
            ImGui.Separator();

            ImGui.TextWrapped("This process:");
            ImGui.BulletText("Removes fault displacements (unfaulting)");
            ImGui.BulletText("Flattens folds using flexural slip mechanics");
            ImGui.BulletText("Preserves bed length during unfolding");
            ImGui.BulletText("Tests if the interpretation is geologically valid");

            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.3f, 0.8f, 1f, 1f), "Restoration Progress");

            if (ImGui.SliderFloat("Restore %", ref _restorationPercentage, 0f, 100f, "%.0f%%"))
            {
                _restoration.Restore(_restorationPercentage);
                twoDDataset.SetRestorationData(_restoration.RestoredSection);
            }

            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text("0%% = Original deformed state");
                ImGui.Text("100%% = Fully restored (flat)");
                ImGui.EndTooltip();
            }

            ImGui.Spacing();

            if (ImGui.Button("Reset to Original", new Vector2(-1, 0)))
            {
                _restorationPercentage = 0;
                twoDDataset.ClearRestorationData();
            }

            ImGui.Separator();
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), "🎬 Animation Export");

            _animationExporter.DrawUI();

            if (ImGui.Button("📹 Export Restoration Animation", new Vector2(-1, 0)))
                _animationExporter.ExportAnimation(twoDDataset, _restoration,
                    AnimationExporter.AnimationType.Restoration, "restoration_animation");

            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.2f, 0.8f, 0.3f, 1f), "📄 Vector Export (SVG)");

            // SVG export options
            ImGui.Text("SVG Settings:");
            ImGui.Indent();
            ImGui.SetNextItemWidth(150);
            ImGui.InputInt("Width", ref _svgWidth);
            _svgWidth = Math.Clamp(_svgWidth, 800, 4096);

            ImGui.SetNextItemWidth(150);
            ImGui.InputInt("Height", ref _svgHeight);
            _svgHeight = Math.Clamp(_svgHeight, 600, 4096);

            ImGui.Checkbox("Include Labels", ref _svgIncludeLabels);
            ImGui.Checkbox("Include Grid", ref _svgIncludeGrid);
            ImGui.Checkbox("Include Legend", ref _svgIncludeLegend);
            ImGui.Unindent();

            if (ImGui.Button("📄 Export as SVG...", new Vector2(-1, 0)))
            {
                _svgExportDialog.SetExtensions((".svg", "Scalable Vector Graphics"));
                _svgExportDialog.Open($"restoration_{DateTime.Now:yyyyMMdd_HHmmss}");
            }

            // Handle SVG export dialog
            if (_svgExportDialog.Submit())
            {
                var section = _restoration.RestoredSection ?? twoDDataset.ProfileData;
                _svgExporter = new SvgExporter(_svgWidth, _svgHeight);
                _svgExporter.SaveToFile(_svgExportDialog.SelectedPath, section,
                    _svgIncludeLabels, _svgIncludeGrid, _svgIncludeLegend);
            }

            ImGui.Separator();
            ImGui.TextWrapped("Validation Criteria:");
            ImGui.BulletText("Restored layers should be flat and continuous");
            ImGui.BulletText("No gaps or overlaps in formations");
            ImGui.BulletText("Bed lengths should be conserved");
            ImGui.BulletText("If restoration fails, the interpretation may need revision");
        }
    }

    private class ForwardModelingTool : IDatasetTools
    {
        private static StructuralRestoration _restoration;
        private static float _deformationPercentage;
        private static bool _useOriginalAsBase = true;
        private static readonly AnimationExporter _animationExporter = new();
        private static SvgExporter _svgExporter = new();

        private static readonly ImGuiExportFileDialog _svgExportDialog =
            new("SvgExportForwardDialog", "Export as SVG");

        // SVG export options
        private static bool _svgIncludeLabels = true;
        private static bool _svgIncludeGrid = true;
        private static bool _svgIncludeLegend = true;
        private static int _svgWidth = 1920;
        private static int _svgHeight = 1080;

        public void Draw(Dataset dataset)
        {
            var twoDDataset = dataset as TwoDGeologyDataset;
            if (twoDDataset?.ProfileData == null) return;

            if (_restoration == null || _restoration.RestoredSection.Profile != twoDDataset.ProfileData.Profile)
                _restoration = new StructuralRestoration(twoDDataset.ProfileData);

            ImGui.TextWrapped(
                "Forward Modeling: Simulate future or alternative deformation states by applying folding and faulting.");
            ImGui.Separator();

            ImGui.Text("Starting State:");
            if (ImGui.RadioButton("From Current Section", _useOriginalAsBase))
                if (!_useOriginalAsBase)
                {
                    _useOriginalAsBase = true;
                    _deformationPercentage = 0;
                    twoDDataset.ClearRestorationData();
                }

            ImGui.SameLine();
            if (ImGui.RadioButton("From Flat Reference", !_useOriginalAsBase))
                if (_useOriginalAsBase)
                {
                    _useOriginalAsBase = false;
                    _deformationPercentage = 0;
                    _restoration.CreateFlatReference();
                    twoDDataset.SetRestorationData(_restoration.RestoredSection);
                }

            ImGui.Separator();
            ImGui.TextColored(new Vector4(1f, 0.6f, 0.3f, 1f), "Deformation Progress");

            if (ImGui.SliderFloat("Deform %", ref _deformationPercentage, 0f, 100f, "%.0f%%"))
            {
                _restoration.Deform(_deformationPercentage);
                twoDDataset.SetRestorationData(_restoration.RestoredSection);
            }

            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                if (_useOriginalAsBase)
                {
                    ImGui.Text("0%% = Current section state");
                    ImGui.Text("100%% = More deformed version");
                }
                else
                {
                    ImGui.Text("0%% = Flat undeformed state");
                    ImGui.Text("100%% = Deformed (matching original geometry)");
                }

                ImGui.EndTooltip();
            }

            ImGui.Spacing();

            if (ImGui.Button("Reset", new Vector2(-1, 0)))
            {
                _deformationPercentage = 0;
                if (!_useOriginalAsBase)
                {
                    _restoration.CreateFlatReference();
                    twoDDataset.SetRestorationData(_restoration.RestoredSection);
                }
                else
                {
                    twoDDataset.ClearRestorationData();
                }
            }

            ImGui.Separator();
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), "🎬 Animation Export");

            _animationExporter.DrawUI();

            if (ImGui.Button("📹 Export Forward Modeling Animation", new Vector2(-1, 0)))
                _animationExporter.ExportAnimation(twoDDataset, _restoration,
                    AnimationExporter.AnimationType.ForwardModeling, "forward_modeling_animation");

            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.2f, 0.8f, 0.3f, 1f), "📄 Vector Export (SVG)");

            // SVG export options
            ImGui.Text("SVG Settings:");
            ImGui.Indent();
            ImGui.SetNextItemWidth(150);
            ImGui.InputInt("Width", ref _svgWidth);
            _svgWidth = Math.Clamp(_svgWidth, 800, 4096);

            ImGui.SetNextItemWidth(150);
            ImGui.InputInt("Height", ref _svgHeight);
            _svgHeight = Math.Clamp(_svgHeight, 600, 4096);

            ImGui.Checkbox("Include Labels", ref _svgIncludeLabels);
            ImGui.Checkbox("Include Grid", ref _svgIncludeGrid);
            ImGui.Checkbox("Include Legend", ref _svgIncludeLegend);
            ImGui.Unindent();

            if (ImGui.Button("📄 Export as SVG...", new Vector2(-1, 0)))
            {
                _svgExportDialog.SetExtensions((".svg", "Scalable Vector Graphics"));
                _svgExportDialog.Open($"forward_model_{DateTime.Now:yyyyMMdd_HHmmss}");
            }

            // Handle SVG export dialog
            if (_svgExportDialog.Submit())
            {
                var section = _restoration.RestoredSection ?? twoDDataset.ProfileData;
                _svgExporter = new SvgExporter(_svgWidth, _svgHeight);
                _svgExporter.SaveToFile(_svgExportDialog.SelectedPath, section,
                    _svgIncludeLabels, _svgIncludeGrid, _svgIncludeLegend);
            }

            ImGui.Separator();
            ImGui.TextWrapped("Forward Modeling Applications:");
            ImGui.BulletText("Predict future deformation under continued stress");
            ImGui.BulletText("Test alternative structural scenarios");
            ImGui.BulletText("Understand deformation history by building it forward");
            ImGui.BulletText("Verify that restoration and forward modeling are consistent");

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.2f, 1f), "Consistency Check:");
            ImGui.TextWrapped(
                "After restoring to flat, try forward modeling back to 100%%. The result should closely match your original section, validating the interpretation.");
        }
    }
}