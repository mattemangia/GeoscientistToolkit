// GeoscientistToolkit/UI/Utils/AnimationExporter.cs

using System.Numerics;
using GeoscientistToolkit.Business.GIS;
using GeoscientistToolkit.Data.TwoDGeology;
using GeoscientistToolkit.Util;
using ImGuiNET;
using StbImageWriteSharp;

namespace GeoscientistToolkit.UI.Utils;

/// <summary>
///     Exports animations of structural restoration and forward modeling processes.
///     Creates frame-by-frame image sequences or animated GIFs.
///     Uses StbImageWriteSharp for image export (public domain license).
/// </summary>
public class AnimationExporter
{
    public enum AnimationFormat
    {
        PNGSequence, // Folder with PNG files
        BMPSequence // Folder with BMP files (larger but faster)
    }

    public enum AnimationType
    {
        Restoration, // 0% to 100% restoration
        ForwardModeling, // 0% to 100% deformation
        RoundTrip // 0% to 100% and back to 0%
    }

    private readonly ImGuiExportFileDialog _exportDialog;
    private float _exportProgress;
    private string _exportStatus = "";
    private AnimationFormat _format = AnimationFormat.PNGSequence;

    // Animation settings
    private int _frameCount = 50;
    private int _frameRate = 10; // FPS
    private int _imageHeight = 1080;
    private int _imageWidth = 1920;
    private bool _includeLabels = true;

    // Export state
    private bool _isExporting;
    private bool _showPercentage = true;

    public AnimationExporter()
    {
        _exportDialog = new ImGuiExportFileDialog("AnimationExport", "Export Animation");
        _exportDialog.SetExtensions(
            (".png", "PNG Sequence (folder)"),
            (".bmp", "BMP Sequence (folder)")
        );
    }

    public void DrawUI()
    {
        ImGui.PushID("AnimationExporter");

        ImGui.Text("Animation Settings:");
        ImGui.Indent();

        ImGui.SetNextItemWidth(150);
        ImGui.InputInt("Frame Count", ref _frameCount);
        _frameCount = Math.Clamp(_frameCount, 10, 500);

        ImGui.SetNextItemWidth(150);
        ImGui.InputInt("Frame Rate (FPS)", ref _frameRate);
        _frameRate = Math.Clamp(_frameRate, 1, 60);
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text("For reference when importing into video editing software");
            ImGui.EndTooltip();
        }

        ImGui.SetNextItemWidth(150);
        if (ImGui.BeginCombo("Format", _format.ToString()))
        {
            foreach (var format in Enum.GetValues<AnimationFormat>())
                if (ImGui.Selectable(format.ToString(), _format == format))
                    _format = format;

            ImGui.EndCombo();
        }

        ImGui.SetNextItemWidth(150);
        ImGui.InputInt("Width", ref _imageWidth);
        _imageWidth = Math.Clamp(_imageWidth, 640, 4096);

        ImGui.SetNextItemWidth(150);
        ImGui.InputInt("Height", ref _imageHeight);
        _imageHeight = Math.Clamp(_imageHeight, 480, 2160);

        ImGui.Checkbox("Include Labels", ref _includeLabels);
        ImGui.Checkbox("Show Percentage", ref _showPercentage);

        ImGui.Unindent();

        // Show export progress if exporting
        if (_isExporting)
        {
            ImGui.Separator();
            ImGui.ProgressBar(_exportProgress, new Vector2(-1, 0), $"{_exportProgress * 100:F0}%");
            ImGui.TextWrapped(_exportStatus);
        }

        ImGui.Separator();
        ImGui.TextWrapped(
            "Tip: Import the PNG/BMP sequence into video editing software (e.g., FFmpeg, Premiere, After Effects) to create MP4/GIF animations.");

        ImGui.PopID();
    }

    public async void ExportAnimation(TwoDGeologyDataset dataset, StructuralRestoration restoration,
        AnimationType type, string defaultFileName)
    {
        if (_isExporting)
        {
            Logger.LogWarning("Animation export already in progress");
            return;
        }

        // Use default path
        var outputFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            $"{defaultFileName}_{DateTime.Now:yyyyMMdd_HHmmss}"
        );

        _isExporting = true;
        _exportProgress = 0f;
        _exportStatus = "Starting export...";

        try
        {
            await Task.Run(() => PerformExport(dataset, restoration, type, outputFolder));

            _exportStatus = $"Export complete! Saved to: {outputFolder}";
            Logger.Log($"Animation exported successfully to: {outputFolder}");
        }
        catch (Exception ex)
        {
            _exportStatus = $"Export failed: {ex.Message}";
            Logger.LogError($"Animation export failed: {ex.Message}");
        }
        finally
        {
            _isExporting = false;
        }
    }

    private void PerformExport(TwoDGeologyDataset dataset, StructuralRestoration restoration,
        AnimationType type, string outputFolder)
    {
        // Create output directory
        Directory.CreateDirectory(outputFolder);

        _exportStatus = "Generating frames...";

        // Generate and save frames
        for (var i = 0; i < _frameCount; i++)
        {
            var percentage = (float)i / (_frameCount - 1) * 100f;

            // Apply transformation based on type
            switch (type)
            {
                case AnimationType.Restoration:
                    restoration.Restore(percentage);
                    break;
                case AnimationType.ForwardModeling:
                    restoration.Deform(percentage);
                    break;
                case AnimationType.RoundTrip:
                    // Go to 100% and back
                    var tripPercentage = i < _frameCount / 2
                        ? (float)i / (_frameCount / 2) * 100f
                        : (1f - (float)(i - _frameCount / 2) / (_frameCount / 2)) * 100f;
                    restoration.Restore(tripPercentage);
                    break;
            }

            // Render and save frame
            var frameData = RenderFrame(restoration.RestoredSection, percentage, type);
            var fileName = $"frame_{i:D5}.{(_format == AnimationFormat.PNGSequence ? "png" : "bmp")}";
            var filePath = Path.Combine(outputFolder, fileName);

            SaveFrame(frameData, filePath);

            _exportProgress = (float)(i + 1) / _frameCount;
            _exportStatus = $"Generated frame {i + 1} / {_frameCount}";
        }

        // Create metadata file
        CreateMetadataFile(outputFolder, type);

        _exportStatus = "Animation export complete!";
    }

    private byte[] RenderFrame(GeologicalMapping.CrossSectionGenerator.CrossSection section,
        float percentage, AnimationType type)
    {
        // Create RGBA image buffer
        var imageData = new byte[_imageWidth * _imageHeight * 4];

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
        var drawWidth = _imageWidth - margin * 2;
        var drawHeight = _imageHeight - margin * 2 - (_includeLabels ? 80 : 0);

        var profile = section.Profile;
        var distRange = profile.TotalDistance;
        var elevRange = profile.MaxElevation - profile.MinElevation;
        if (elevRange < 1f) elevRange = 1f;

        var ve = section.VerticalExaggeration;

        // Helper function to convert world to screen coordinates
        (int x, int y) WorldToScreen(Vector2 worldPos)
        {
            var x = worldPos.X / distRange * drawWidth + margin;
            var y = _imageHeight - margin - (_includeLabels ? 80 : 0) -
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

                FillPolygon(imageData, _imageWidth, _imageHeight, polyPoints, color);
                DrawPolygon(imageData, _imageWidth, _imageHeight, polyPoints, new Color4(0, 0, 0, 255), 2);
            }
        }

        // Draw faults
        foreach (var fault in section.Faults)
        {
            var faultPoints = fault.FaultTrace.Select(p => WorldToScreen(p)).ToList();
            if (faultPoints.Count > 1)
                DrawPolyline(imageData, _imageWidth, _imageHeight, faultPoints, new Color4(255, 0, 0, 255), 3);
        }

        // Draw topography
        var topoPoints = profile.Points
            .Select(p => WorldToScreen(new Vector2(p.Distance, p.Elevation)))
            .ToList();
        if (topoPoints.Count > 1)
            DrawPolyline(imageData, _imageWidth, _imageHeight, topoPoints, new Color4(0, 0, 0, 255), 3);

        // Draw labels
        if (_includeLabels)
        {
            var title = type switch
            {
                AnimationType.Restoration => "Structural Restoration",
                AnimationType.ForwardModeling => "Forward Modeling",
                AnimationType.RoundTrip => "Round-Trip Validation",
                _ => ""
            };

            DrawText(imageData, _imageWidth, _imageHeight, title, margin, 30, 2.0f, new Color4(0, 0, 0, 255));

            if (_showPercentage)
            {
                var percentText = $"{percentage:F1}%";
                DrawText(imageData, _imageWidth, _imageHeight, percentText,
                    _imageWidth - margin - 150, _imageHeight - 50, 1.5f, new Color4(0, 0, 0, 255));
            }
        }

        return imageData;
    }

    private void SaveFrame(byte[] imageData, string filePath)
    {
        var writer = new ImageWriter();

        if (filePath.EndsWith(".png"))
        {
            using var stream = File.OpenWrite(filePath);
            writer.WritePng(imageData, _imageWidth, _imageHeight, ColorComponents.RedGreenBlueAlpha, stream);
        }
        else if (filePath.EndsWith(".bmp"))
        {
            using var stream = File.OpenWrite(filePath);
            writer.WriteBmp(imageData, _imageWidth, _imageHeight, ColorComponents.RedGreenBlueAlpha, stream);
        }
    }

    private void CreateMetadataFile(string outputFolder, AnimationType type)
    {
        var metadataPath = Path.Combine(outputFolder, "animation_info.txt");
        var metadata = $@"Animation Export Information
Generated: {DateTime.Now}
Type: {type}
Frame Count: {_frameCount}
Frame Rate: {_frameRate} FPS
Resolution: {_imageWidth}x{_imageHeight}
Format: {_format}

To create a video from this sequence using FFmpeg:
ffmpeg -framerate {_frameRate} -i frame_%05d.{(_format == AnimationFormat.PNGSequence ? "png" : "bmp")} -c:v libx264 -pix_fmt yuv420p output.mp4

To create an animated GIF:
ffmpeg -framerate {_frameRate} -i frame_%05d.{(_format == AnimationFormat.PNGSequence ? "png" : "bmp")} -vf ""scale={_imageWidth}:-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse"" output.gif
";

        File.WriteAllText(metadataPath, metadata);
    }

    #region Drawing Primitives

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

        // Alpha blending
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
        // Bresenham's line algorithm with thickness
        var dx = Math.Abs(x1 - x0);
        var dy = Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var err = dx - dy;

        int x = x0, y = y0;

        while (true)
        {
            // Draw thick point
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

        // Find bounding box
        var minY = points.Min(p => p.y);
        var maxY = points.Max(p => p.y);
        var minX = points.Min(p => p.x);
        var maxX = points.Max(p => p.x);

        // Scanline fill algorithm
        for (var y = minY; y <= maxY; y++)
        {
            var intersections = new List<int>();

            // Find intersections with this scanline
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

            // Fill between pairs of intersections
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
        // Simple bitmap font rendering (8x8 characters)
        var charWidth = (int)(8 * scale);
        var charHeight = (int)(8 * scale);

        for (var i = 0; i < text.Length; i++)
        {
            var charX = x + i * charWidth;
            DrawChar(imageData, width, height, text[i], charX, y, scale, color);
        }
    }

    private void DrawChar(byte[] imageData, int width, int height, char c, int x, int y,
        float scale, Color4 color)
    {
        // Very simple 8x8 bitmap font for basic characters
        var charData = GetCharBitmap(c);
        if (charData == null) return;

        for (var cy = 0; cy < 8; cy++)
        {
            var row = charData[cy];
            for (var cx = 0; cx < 8; cx++)
                if ((row & (1 << (7 - cx))) != 0)
                    // Draw scaled pixel
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
        // Simple 8x8 bitmap font - only basic characters for labels
        // Each byte represents one row of 8 pixels
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
            'L' => new byte[] { 0x60, 0x60, 0x60, 0x60, 0x60, 0x60, 0x7E, 0x00 },
            'M' => new byte[] { 0x63, 0x77, 0x7F, 0x6B, 0x63, 0x63, 0x63, 0x00 },
            'N' => new byte[] { 0x66, 0x76, 0x7E, 0x7E, 0x6E, 0x66, 0x66, 0x00 },
            'O' => new byte[] { 0x3C, 0x66, 0x66, 0x66, 0x66, 0x66, 0x3C, 0x00 },
            'P' => new byte[] { 0x7C, 0x66, 0x66, 0x7C, 0x60, 0x60, 0x60, 0x00 },
            'R' => new byte[] { 0x7C, 0x66, 0x66, 0x7C, 0x6C, 0x66, 0x63, 0x00 },
            'S' => new byte[] { 0x3C, 0x66, 0x60, 0x3C, 0x06, 0x66, 0x3C, 0x00 },
            'T' => new byte[] { 0x7E, 0x18, 0x18, 0x18, 0x18, 0x18, 0x18, 0x00 },
            'U' => new byte[] { 0x66, 0x66, 0x66, 0x66, 0x66, 0x66, 0x3C, 0x00 },
            'V' => new byte[] { 0x66, 0x66, 0x66, 0x66, 0x66, 0x3C, 0x18, 0x00 },
            'W' => new byte[] { 0x63, 0x63, 0x63, 0x6B, 0x7F, 0x77, 0x63, 0x00 },
            'a' => new byte[] { 0x00, 0x00, 0x3C, 0x06, 0x3E, 0x66, 0x3E, 0x00 },
            'd' => new byte[] { 0x06, 0x06, 0x3E, 0x66, 0x66, 0x66, 0x3E, 0x00 },
            'e' => new byte[] { 0x00, 0x00, 0x3C, 0x66, 0x7E, 0x60, 0x3C, 0x00 },
            'g' => new byte[] { 0x00, 0x00, 0x3E, 0x66, 0x66, 0x3E, 0x06, 0x3C },
            'i' => new byte[] { 0x18, 0x00, 0x38, 0x18, 0x18, 0x18, 0x3C, 0x00 },
            'l' => new byte[] { 0x38, 0x18, 0x18, 0x18, 0x18, 0x18, 0x3C, 0x00 },
            'n' => new byte[] { 0x00, 0x00, 0x7C, 0x66, 0x66, 0x66, 0x66, 0x00 },
            'o' => new byte[] { 0x00, 0x00, 0x3C, 0x66, 0x66, 0x66, 0x3C, 0x00 },
            'r' => new byte[] { 0x00, 0x00, 0x7C, 0x66, 0x60, 0x60, 0x60, 0x00 },
            's' => new byte[] { 0x00, 0x00, 0x3E, 0x60, 0x3C, 0x06, 0x7C, 0x00 },
            't' => new byte[] { 0x18, 0x18, 0x7E, 0x18, 0x18, 0x18, 0x0E, 0x00 },
            'u' => new byte[] { 0x00, 0x00, 0x66, 0x66, 0x66, 0x66, 0x3E, 0x00 },
            'w' => new byte[] { 0x00, 0x00, 0x63, 0x6B, 0x6B, 0x7F, 0x36, 0x00 },
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
            ' ' => new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
            'J' => new byte[] { 0x06, 0x06, 0x06, 0x06, 0x06, 0x66, 0x3C, 0x00 },
            'K' => new byte[] { 0x66, 0x6C, 0x78, 0x70, 0x78, 0x6C, 0x66, 0x00 },
            'Q' => new byte[] { 0x3C, 0x66, 0x66, 0x66, 0x66, 0x3C, 0x0E, 0x00 },
            'X' => new byte[] { 0x66, 0x66, 0x3C, 0x18, 0x3C, 0x66, 0x66, 0x00 },
            'Y' => new byte[] { 0x66, 0x66, 0x66, 0x3C, 0x18, 0x18, 0x18, 0x00 },
            'Z' => new byte[] { 0x7E, 0x06, 0x0C, 0x18, 0x30, 0x60, 0x7E, 0x00 },
            'b' => new byte[] { 0x60, 0x60, 0x7C, 0x66, 0x66, 0x66, 0x7C, 0x00 },
            'c' => new byte[] { 0x00, 0x00, 0x3C, 0x60, 0x60, 0x60, 0x3C, 0x00 },
            'f' => new byte[] { 0x0E, 0x18, 0x18, 0x7E, 0x18, 0x18, 0x18, 0x00 },
            'h' => new byte[] { 0x60, 0x60, 0x7C, 0x66, 0x66, 0x66, 0x66, 0x00 },
            'j' => new byte[] { 0x18, 0x00, 0x38, 0x18, 0x18, 0x18, 0x18, 0x70 },
            'k' => new byte[] { 0x60, 0x60, 0x66, 0x6C, 0x78, 0x6C, 0x66, 0x00 },
            'm' => new byte[] { 0x00, 0x00, 0x76, 0x7F, 0x6B, 0x6B, 0x63, 0x00 },
            'p' => new byte[] { 0x00, 0x00, 0x7C, 0x66, 0x66, 0x7C, 0x60, 0x60 },
            'q' => new byte[] { 0x00, 0x00, 0x3E, 0x66, 0x66, 0x3E, 0x06, 0x06 },
            'v' => new byte[] { 0x00, 0x00, 0x66, 0x66, 0x66, 0x3C, 0x18, 0x00 },
            'x' => new byte[] { 0x00, 0x00, 0x66, 0x3C, 0x18, 0x3C, 0x66, 0x00 },
            'y' => new byte[] { 0x00, 0x00, 0x66, 0x66, 0x66, 0x3E, 0x06, 0x3C },
            'z' => new byte[] { 0x00, 0x00, 0x7E, 0x0C, 0x18, 0x30, 0x7E, 0x00 },
            '(' => new byte[] { 0x0C, 0x18, 0x30, 0x30, 0x30, 0x18, 0x0C, 0x00 },
            ')' => new byte[] { 0x30, 0x18, 0x0C, 0x0C, 0x0C, 0x18, 0x30, 0x00 },
            '/' => new byte[] { 0x02, 0x06, 0x0C, 0x18, 0x30, 0x60, 0x40, 0x00 },
            _ => null
        };
    }

    #endregion
}