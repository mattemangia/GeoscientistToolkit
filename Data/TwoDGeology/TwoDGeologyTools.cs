// GeoscientistToolkit/UI/GIS/TwoDGeologyTools.cs

using System.Numerics;
using System.Reflection;
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
            { ToolCategory.Creation, "Creation" },
            { ToolCategory.Editing, "Editing" },
            { ToolCategory.Analysis, "Analysis" }
        };

        _toolsByCategory = new Dictionary<ToolCategory, List<ToolEntry>>
        {
            // ADD THIS ENTIRE BLOCK:
            {
                ToolCategory.Creation, new List<ToolEntry>
                {
                    new() { Name = "Profile Setup", Tool = new TwoDGeologyCreationTools() }
                }
            },
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
        Creation,
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

        private static readonly ImGuiExportFileDialog _singleFrameExportDialog =
            new("SingleFrameExportDialog", "Export Single Frame");

        private static readonly AnimationExporter _animationExporter = new();

        // SVG export options
        private static bool _svgIncludeLabels = true;
        private static bool _svgIncludeGrid = true;
        private static bool _svgIncludeLegend = true;
        private static int _svgWidth = 1920;
        private static int _svgHeight = 1080;

        private static readonly ImGuiExportFileDialog _2dGeoExportDialog =
            new("2DGeoExportDialog", "Save 2D Geology Profile");

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
            {
                _singleFrameExportDialog.SetExtensions(
                    (".png", "PNG Image"),
                    (".bmp", "BMP Image")
                );
                _singleFrameExportDialog.Open($"cross_section_{DateTime.Now:yyyyMMdd_HHmmss}");
            }

            if (_singleFrameExportDialog.Submit())
                ExportSingleFrame(twoDDataset, _singleFrameExportDialog.SelectedPath);


            ImGui.Separator();

            // === BINARY EXPORT ===
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 1f, 1f), "💾 2D Geology Format");
            ImGui.TextWrapped("Export as native binary format for re-opening later.");
            ImGui.Spacing();

            if (ImGui.Button("💾 Save as 2D Geology Dataset...", new Vector2(-1, 0)))
            {
                _2dGeoExportDialog.SetExtensions((".2dgeo", "2D Geology Profile"));
                _2dGeoExportDialog.Open($"{twoDDataset.Name}_{DateTime.Now:yyyyMMdd}");
            }

            if (_2dGeoExportDialog.Submit())
                try
                {
                    TwoDGeologySerializer.Write(_2dGeoExportDialog.SelectedPath, twoDDataset.ProfileData);
                    Logger.Log($"Successfully exported 2D Geology profile to: {_2dGeoExportDialog.SelectedPath}");
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to save 2D Geology profile: {ex.Message}");
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

        private void ExportSingleFrame(TwoDGeologyDataset dataset, string outputPath)
        {
            try
            {
                var frameData = RenderFrameForExport(dataset.ProfileData);
                SaveFrame(frameData, outputPath);
                Logger.Log($"Exported single frame to: {outputPath}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to export single frame: {ex.Message}");
            }
        }

        private void SaveFrame(byte[] imageData, string filePath)
        {
            var writer = new ImageWriter();
            var uiSettings = _animationExporter.GetType()
                .GetField("_imageWidth", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_animationExporter);
            var imageWidth = (int)uiSettings;
            var imageHeight = (int)_animationExporter.GetType()
                .GetField("_imageHeight", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_animationExporter);


            if (filePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = File.OpenWrite(filePath);
                writer.WritePng(imageData, imageWidth, imageHeight, ColorComponents.RedGreenBlueAlpha, stream);
            }
            else if (filePath.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = File.OpenWrite(filePath);
                writer.WriteBmp(imageData, imageWidth, imageHeight, ColorComponents.RedGreenBlueAlpha, stream);
            }
        }

        private byte[] RenderFrameForExport(GeologicalMapping.CrossSectionGenerator.CrossSection section)
        {
            // Reflectively get UI settings from the shared AnimationExporter instance
            var imageWidth = (int)_animationExporter.GetType()
                .GetField("_imageWidth", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_animationExporter);
            var imageHeight = (int)_animationExporter.GetType()
                .GetField("_imageHeight", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_animationExporter);
            var includeLabels = (bool)_animationExporter.GetType()
                .GetField("_includeLabels", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(_animationExporter);

            // Create RGBA image buffer
            var imageData = new byte[imageWidth * imageHeight * 4];

            // Fill with white background
            for (var i = 0; i < imageData.Length; i += 4)
            {
                imageData[i] = 255; // R
                imageData[i + 1] = 255; // G
                imageData[i + 2] = 255; // B
                imageData[i + 3] = 255; // A
            }

            // Calculate drawing area
            var margin = 80;
            var drawWidth = imageWidth - margin * 2;
            var drawHeight = imageHeight - margin * 2 - (includeLabels ? 80 : 0);

            var profile = section.Profile;
            var distRange = profile.TotalDistance;
            var elevRange = profile.MaxElevation - profile.MinElevation;
            if (elevRange < 1f) elevRange = 1f;

            var ve = section.VerticalExaggeration;

            // Helper function to convert world to screen coordinates
            (int x, int y) WorldToScreen(Vector2 worldPos)
            {
                var x = worldPos.X / distRange * drawWidth + margin;
                var y = imageHeight - margin - (includeLabels ? 80 : 0) -
                        (worldPos.Y - profile.MinElevation) / elevRange * drawHeight * ve;
                return ((int)x, (int)y);
            }

            // Draw formations (filled polygons)
            foreach (var formation in section.Formations)
            {
                if (formation.TopBoundary.Count < 2) continue;

                var polyPoints = new List<(int x, int y)>();
                foreach (var p in formation.TopBoundary) polyPoints.Add(WorldToScreen(p));
                foreach (var p in formation.BottomBoundary.AsEnumerable().Reverse()) polyPoints.Add(WorldToScreen(p));

                if (polyPoints.Count > 2)
                {
                    var color = new Color4(
                        (byte)(formation.Color.X * 255),
                        (byte)(formation.Color.Y * 255),
                        (byte)(formation.Color.Z * 255),
                        180 // Semi-transparent
                    );

                    FillPolygon(imageData, imageWidth, imageHeight, polyPoints, color);
                    DrawPolygon(imageData, imageWidth, imageHeight, polyPoints, new Color4(0, 0, 0, 255), 2);
                }
            }

            // Draw faults
            foreach (var fault in section.Faults)
            {
                var faultPoints = fault.FaultTrace.Select(p => WorldToScreen(p)).ToList();
                if (faultPoints.Count > 1)
                    DrawPolyline(imageData, imageWidth, imageHeight, faultPoints, new Color4(255, 0, 0, 255), 3);
            }

            // Draw topography
            var topoPoints = profile.Points
                .Select(p => WorldToScreen(new Vector2(p.Distance, p.Elevation)))
                .ToList();
            if (topoPoints.Count > 1)
                DrawPolyline(imageData, imageWidth, imageHeight, topoPoints, new Color4(0, 0, 0, 255), 3);

            // Draw labels
            if (includeLabels)
            {
                var title = "Geological Cross-Section";
                DrawText(imageData, imageWidth, imageHeight, title, margin, 30, 2.0f, new Color4(0, 0, 0, 255));
            }

            return imageData;
        }

        #region Drawing Primitives (Copied from AnimationExporter)

        private struct Color4
        {
            public readonly byte R;
            public readonly byte G;
            public readonly byte B;
            public readonly byte A;

            public Color4(byte r, byte g, byte b, byte a)
            {
                R = r;
                G = g;
                B = b;
                A = a;
            }
        }

        private void SetPixel(byte[] imageData, int width, int height, int x, int y, Color4 color)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return;

            var index = (y * width + x) * 4;

            if (color.A == 255)
            {
                imageData[index] = color.R;
                imageData[index + 1] = color.G;
                imageData[index + 2] = color.B;
                imageData[index + 3] = color.A;
            }
            else
            {
                var alpha = color.A / 255f;
                var invAlpha = 1f - alpha;

                imageData[index] = (byte)(color.R * alpha + imageData[index] * invAlpha);
                imageData[index + 1] = (byte)(color.G * alpha + imageData[index + 1] * invAlpha);
                imageData[index + 2] = (byte)(color.B * alpha + imageData[index + 2] * invAlpha);
            }
        }

        private void DrawLine(byte[] imageData, int width, int height, int x0, int y0, int x1, int y1,
            Color4 color, int thickness = 1)
        {
            var dx = Math.Abs(x1 - x0);
            var dy = Math.Abs(y1 - y0);
            var sx = x0 < x1 ? 1 : -1;
            var sy = y0 < y1 ? 1 : -1;
            var err = dx - dy;

            int x = x0, y = y0;

            while (true)
            {
                for (var tx = -thickness / 2; tx <= thickness / 2; tx++)
                for (var ty = -thickness / 2; ty <= thickness / 2; ty++)
                    SetPixel(imageData, width, height, x + tx, y + ty, color);

                if (x == x1 && y == y1) break;

                var e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x += sx;
                }

                if (e2 < dx)
                {
                    err += dx;
                    y += sy;
                }
            }
        }

        private void DrawPolyline(byte[] imageData, int width, int height, List<(int x, int y)> points,
            Color4 color, int thickness)
        {
            for (var i = 0; i < points.Count - 1; i++)
                DrawLine(imageData, width, height,
                    points[i].x, points[i].y,
                    points[i + 1].x, points[i + 1].y,
                    color, thickness);
        }

        private void DrawPolygon(byte[] imageData, int width, int height, List<(int x, int y)> points,
            Color4 color, int thickness)
        {
            for (var i = 0; i < points.Count; i++)
            {
                var p1 = points[i];
                var p2 = points[(i + 1) % points.Count];
                DrawLine(imageData, width, height, p1.x, p1.y, p2.x, p2.y, color, thickness);
            }
        }

        private void FillPolygon(byte[] imageData, int width, int height, List<(int x, int y)> points, Color4 color)
        {
            if (points.Count < 3) return;

            var minY = points.Min(p => p.y);
            var maxY = points.Max(p => p.y);
            var minX = points.Min(p => p.x);
            var maxX = points.Max(p => p.x);

            for (var y = minY; y <= maxY; y++)
            {
                var intersections = new List<int>();

                for (var i = 0; i < points.Count; i++)
                {
                    var p1 = points[i];
                    var p2 = points[(i + 1) % points.Count];

                    if ((p1.y <= y && p2.y > y) || (p2.y <= y && p1.y > y))
                    {
                        var t = (y - p1.y) / (float)(p2.y - p1.y);
                        var x = (int)(p1.x + t * (p2.x - p1.x));
                        intersections.Add(x);
                    }
                }

                intersections.Sort();

                for (var i = 0; i < intersections.Count - 1; i += 2)
                {
                    var x1 = Math.Max(minX, intersections[i]);
                    var x2 = Math.Min(maxX, intersections[i + 1]);

                    for (var x = x1; x <= x2; x++) SetPixel(imageData, width, height, x, y, color);
                }
            }
        }

        private void DrawText(byte[] imageData, int width, int height, string text, int x, int y,
            float scale, Color4 color)
        {
            var charWidth = (int)(8 * scale);

            for (var i = 0; i < text.Length; i++)
            {
                var charX = x + i * charWidth;
                DrawChar(imageData, width, height, text[i], charX, y, scale, color);
            }
        }

        private void DrawChar(byte[] imageData, int width, int height, char c, int x, int y,
            float scale, Color4 color)
        {
            var charData = GetCharBitmap(c);
            if (charData == null) return;

            for (var cy = 0; cy < 8; cy++)
            {
                var row = charData[cy];
                for (var cx = 0; cx < 8; cx++)
                    if ((row & (1 << (7 - cx))) != 0)
                        for (var sy = 0; sy < scale; sy++)
                        for (var sx = 0; sx < scale; sx++)
                            SetPixel(imageData, width, height,
                                x + (int)(cx * scale) + sx,
                                y + (int)(cy * scale) + sy,
                                color);
            }
        }

        private byte[] GetCharBitmap(char c)
        {
            // Simple 8x8 bitmap font - each byte represents one row of 8 pixels
            return c switch
            {
                'A' => new byte[] { 0x18, 0x3C, 0x66, 0x66, 0x7E, 0x66, 0x66, 0x00 },
                'B' => new byte[] { 0x7C, 0x66, 0x66, 0x7C, 0x66, 0x66, 0x7C, 0x00 },
                'C' => new byte[] { 0x3C, 0x66, 0x60, 0x60, 0x60, 0x66, 0x3C, 0x00 },
                'D' => new byte[] { 0x78, 0x6C, 0x66, 0x66, 0x66, 0x6C, 0x78, 0x00 },
                'E' => new byte[] { 0x7E, 0x60, 0x60, 0x7C, 0x60, 0x60, 0x7E, 0x00 },
                'F' => new byte[] { 0x7E, 0x60, 0x60, 0x7C, 0x60, 0x60, 0x60, 0x00 },
                'G' => new byte[] { 0x3C, 0x66, 0x60, 0x6E, 0x66, 0x66, 0x3C, 0x00 },
                'H' => new byte[] { 0x66, 0x66, 0x66, 0x7E, 0x66, 0x66, 0x66, 0x00 },
                'I' => new byte[] { 0x3C, 0x18, 0x18, 0x18, 0x18, 0x18, 0x3C, 0x00 },
                'J' => new byte[] { 0x06, 0x06, 0x06, 0x06, 0x06, 0x66, 0x3C, 0x00 },
                'K' => new byte[] { 0x66, 0x6C, 0x78, 0x70, 0x78, 0x6C, 0x66, 0x00 },
                'L' => new byte[] { 0x60, 0x60, 0x60, 0x60, 0x60, 0x60, 0x7E, 0x00 },
                'M' => new byte[] { 0x63, 0x77, 0x7F, 0x6B, 0x63, 0x63, 0x63, 0x00 },
                'N' => new byte[] { 0x66, 0x76, 0x7E, 0x7E, 0x6E, 0x66, 0x66, 0x00 },
                'O' => new byte[] { 0x3C, 0x66, 0x66, 0x66, 0x66, 0x66, 0x3C, 0x00 },
                'P' => new byte[] { 0x7C, 0x66, 0x66, 0x7C, 0x60, 0x60, 0x60, 0x00 },
                'Q' => new byte[] { 0x3C, 0x66, 0x66, 0x66, 0x66, 0x3C, 0x0E, 0x00 },
                'R' => new byte[] { 0x7C, 0x66, 0x66, 0x7C, 0x6C, 0x66, 0x63, 0x00 },
                'S' => new byte[] { 0x3C, 0x66, 0x60, 0x3C, 0x06, 0x66, 0x3C, 0x00 },
                'T' => new byte[] { 0x7E, 0x18, 0x18, 0x18, 0x18, 0x18, 0x18, 0x00 },
                'U' => new byte[] { 0x66, 0x66, 0x66, 0x66, 0x66, 0x66, 0x3C, 0x00 },
                'V' => new byte[] { 0x66, 0x66, 0x66, 0x66, 0x66, 0x3C, 0x18, 0x00 },
                'W' => new byte[] { 0x63, 0x63, 0x63, 0x6B, 0x7F, 0x77, 0x63, 0x00 },
                'X' => new byte[] { 0x66, 0x66, 0x3C, 0x18, 0x3C, 0x66, 0x66, 0x00 },
                'Y' => new byte[] { 0x66, 0x66, 0x66, 0x3C, 0x18, 0x18, 0x18, 0x00 },
                'Z' => new byte[] { 0x7E, 0x06, 0x0C, 0x18, 0x30, 0x60, 0x7E, 0x00 },
                'a' => new byte[] { 0x00, 0x00, 0x3C, 0x06, 0x3E, 0x66, 0x3E, 0x00 },
                'b' => new byte[] { 0x60, 0x60, 0x7C, 0x66, 0x66, 0x66, 0x7C, 0x00 },
                'c' => new byte[] { 0x00, 0x00, 0x3C, 0x60, 0x60, 0x60, 0x3C, 0x00 },
                'd' => new byte[] { 0x06, 0x06, 0x3E, 0x66, 0x66, 0x66, 0x3E, 0x00 },
                'e' => new byte[] { 0x00, 0x00, 0x3C, 0x66, 0x7E, 0x60, 0x3C, 0x00 },
                'f' => new byte[] { 0x0E, 0x18, 0x18, 0x7E, 0x18, 0x18, 0x18, 0x00 },
                'g' => new byte[] { 0x00, 0x00, 0x3E, 0x66, 0x66, 0x3E, 0x06, 0x3C },
                'h' => new byte[] { 0x60, 0x60, 0x7C, 0x66, 0x66, 0x66, 0x66, 0x00 },
                'i' => new byte[] { 0x18, 0x00, 0x38, 0x18, 0x18, 0x18, 0x3C, 0x00 },
                'j' => new byte[] { 0x18, 0x00, 0x38, 0x18, 0x18, 0x18, 0x18, 0x70 },
                'k' => new byte[] { 0x60, 0x60, 0x66, 0x6C, 0x78, 0x6C, 0x66, 0x00 },
                'l' => new byte[] { 0x38, 0x18, 0x18, 0x18, 0x18, 0x18, 0x3C, 0x00 },
                'm' => new byte[] { 0x00, 0x00, 0x76, 0x7F, 0x6B, 0x6B, 0x63, 0x00 },
                'n' => new byte[] { 0x00, 0x00, 0x7C, 0x66, 0x66, 0x66, 0x66, 0x00 },
                'o' => new byte[] { 0x00, 0x00, 0x3C, 0x66, 0x66, 0x66, 0x3C, 0x00 },
                'p' => new byte[] { 0x00, 0x00, 0x7C, 0x66, 0x66, 0x7C, 0x60, 0x60 },
                'q' => new byte[] { 0x00, 0x00, 0x3E, 0x66, 0x66, 0x3E, 0x06, 0x06 },
                'r' => new byte[] { 0x00, 0x00, 0x7C, 0x66, 0x60, 0x60, 0x60, 0x00 },
                's' => new byte[] { 0x00, 0x00, 0x3E, 0x60, 0x3C, 0x06, 0x7C, 0x00 },
                't' => new byte[] { 0x18, 0x18, 0x7E, 0x18, 0x18, 0x18, 0x0E, 0x00 },
                'u' => new byte[] { 0x00, 0x00, 0x66, 0x66, 0x66, 0x66, 0x3E, 0x00 },
                'v' => new byte[] { 0x00, 0x00, 0x66, 0x66, 0x66, 0x3C, 0x18, 0x00 },
                'w' => new byte[] { 0x00, 0x00, 0x63, 0x6B, 0x6B, 0x7F, 0x36, 0x00 },
                'x' => new byte[] { 0x00, 0x00, 0x66, 0x3C, 0x18, 0x3C, 0x66, 0x00 },
                'y' => new byte[] { 0x00, 0x00, 0x66, 0x66, 0x66, 0x3E, 0x06, 0x3C },
                'z' => new byte[] { 0x00, 0x00, 0x7E, 0x0C, 0x18, 0x30, 0x7E, 0x00 },
                '0' => new byte[] { 0x3C, 0x66, 0x6E, 0x76, 0x66, 0x66, 0x3C, 0x00 },
                '1' => new byte[] { 0x18, 0x38, 0x18, 0x18, 0x18, 0x18, 0x7E, 0x00 },
                '2' => new byte[] { 0x3C, 0x66, 0x06, 0x0C, 0x18, 0x30, 0x7E, 0x00 },
                '3' => new byte[] { 0x7E, 0x0C, 0x18, 0x0C, 0x06, 0x66, 0x3C, 0x00 },
                '4' => new byte[] { 0x0C, 0x1C, 0x3C, 0x6C, 0x7E, 0x0C, 0x0C, 0x00 },
                '5' => new byte[] { 0x7E, 0x60, 0x7C, 0x06, 0x06, 0x66, 0x3C, 0x00 },
                '6' => new byte[] { 0x1C, 0x30, 0x60, 0x7C, 0x66, 0x66, 0x3C, 0x00 },
                '7' => new byte[] { 0x7E, 0x06, 0x0C, 0x18, 0x30, 0x30, 0x30, 0x00 },
                '8' => new byte[] { 0x3C, 0x66, 0x66, 0x3C, 0x66, 0x66, 0x3C, 0x00 },
                '9' => new byte[] { 0x3C, 0x66, 0x66, 0x3E, 0x06, 0x0C, 0x38, 0x00 },
                '%' => new byte[] { 0x62, 0x66, 0x0C, 0x18, 0x30, 0x66, 0x46, 0x00 },
                '.' => new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x18, 0x18, 0x00 },
                ':' => new byte[] { 0x00, 0x18, 0x18, 0x00, 0x00, 0x18, 0x18, 0x00 },
                '-' => new byte[] { 0x00, 0x00, 0x00, 0x7E, 0x00, 0x00, 0x00, 0x00 },
                '(' => new byte[] { 0x0C, 0x18, 0x30, 0x30, 0x30, 0x18, 0x0C, 0x00 },
                ')' => new byte[] { 0x30, 0x18, 0x0C, 0x0C, 0x0C, 0x18, 0x30, 0x00 },
                '/' => new byte[] { 0x02, 0x06, 0x0C, 0x18, 0x30, 0x60, 0x40, 0x00 },
                ' ' => new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
                _ => null
            };
        }

        #endregion
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