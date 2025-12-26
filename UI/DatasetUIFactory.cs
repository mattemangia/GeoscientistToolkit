// GeoscientistToolkit/UI/DatasetUIFactory.cs

using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Data.Nerf;
using GeoscientistToolkit.Data.PhysicoChem;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.Data.Table;
using GeoscientistToolkit.Data.TwoDGeology;
using GeoscientistToolkit.Data.Seismic;
using GeoscientistToolkit.Data.Media;
using GeoscientistToolkit.Data.Text;
using GeoscientistToolkit.UI.Borehole;
using GeoscientistToolkit.UI.GIS;
using GeoscientistToolkit.UI.Seismic;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Tools;
using ImGuiNET;
using GeoscientistToolkit.Analysis.Geothermal;
using GeoscientistToolkit.Analysis.SlopeStability;

namespace GeoscientistToolkit.UI;

public static class DatasetUIFactory
{
    // Static cache to track BoreholeViewer instances for callback connection
    private static readonly Dictionary<BoreholeDataset, BoreholeViewer> _boreholeViewers = new();

    public static IDatasetViewer CreateViewer(Dataset dataset)
{
return dataset switch
{
// CT Volume datasets
StreamingCtVolumeDataset streamingDataset =>
streamingDataset.EditablePartner != null
? new CtCombinedViewer(streamingDataset.EditablePartner)
: new CtVolume3DViewer(streamingDataset),
CtImageStackDataset ctDataset => new CtCombinedViewer(ctDataset),

        // Image datasets
        ImageDataset imageDataset => new ImageViewer(imageDataset),

        // 3D Mesh datasets
        Mesh3DDataset mesh3DDataset => new Mesh3DViewer(mesh3DDataset),

        // Table datasets
        TableDataset tableDataset => new TableViewer(tableDataset),

        // GIS datasets
        SubsurfaceGISDataset subsurfaceGisDataset => new GISViewer(subsurfaceGisDataset),
        GISDataset gisDataset => new GISViewer(gisDataset),
        DatasetGroup group when group.Datasets.All(d => d is GISDataset) =>
            new GISViewer(group.Datasets.Cast<GISDataset>().ToList()),
        
        // Acoustic Volume datasets
        AcousticVolumeDataset acousticDataset => new AcousticVolumeViewer(acousticDataset),

        // PNM Dataset
        PNMDataset pnmDataset => new PNMViewer(pnmDataset),

        // Borehole Dataset
        BoreholeDataset boreholeDataset => CreateBoreholeViewer(boreholeDataset),

        // 2D Geology Dataset
        TwoDGeologyDataset twoDGeologyDataset => new TwoDGeologyViewerWrapper(twoDGeologyDataset),

        // Seismic Dataset
        SeismicDataset seismicDataset => new SeismicViewer(seismicDataset),

        // PhysicoChem Dataset
        PhysicoChemDataset physicoChemDataset => new PhysicoChemViewer(physicoChemDataset),

        // Media Datasets
        VideoDataset videoDataset => new VideoDatasetViewer(videoDataset),
        AudioDataset audioDataset => new AudioDatasetViewer(audioDataset),

        // Text Datasets
        TextDataset textDataset => new TextViewer(textDataset),

        // NeRF Datasets
        NerfDataset nerfDataset => new NerfViewer(nerfDataset),

        // Slope Stability Dataset
        SlopeStabilityDataset slopeDataset => new SlopeStabilityViewer(slopeDataset),

        // Dataset groups cannot be opened in a viewer
        DatasetGroup => throw new InvalidOperationException(
            "Cannot open a DatasetGroup in a viewer. Please open individual datasets."),

        _ => throw new NotSupportedException($"No viewer available for dataset type: {dataset.GetType().Name}")
    };
}

private static BoreholeViewer CreateBoreholeViewer(BoreholeDataset dataset)
{
    var viewer = new BoreholeViewer(dataset);
    _boreholeViewers[dataset] = viewer;
    return viewer;
}

private static BoreholeTools CreateBoreholeTools(BoreholeDataset dataset)
{
    var tools = new BoreholeTools();
    
    // Connect the viewer's OnLithologyClicked callback to the tools' EditUnit method
    // if a viewer for this dataset has been created
    if (_boreholeViewers.TryGetValue(dataset, out var viewer))
    {
        viewer.OnLithologyClicked = tools.EditUnit;
    }
    
    return tools;
}

public static IDatasetPropertiesRenderer CreatePropertiesRenderer(Dataset dataset)
{
    return dataset switch
    {
        ImageDataset => new ImagePropertiesRenderer(),
        CtImageStackDataset or StreamingCtVolumeDataset => new CtImageStackPropertiesRenderer(),
        Mesh3DDataset => new Mesh3DProperties(),
        TableDataset => new TableProperties(),
        SubsurfaceGISDataset => new GISProperties(),
        GISDataset => new GISProperties(),
        AcousticVolumeDataset => new AcousticVolumeProperties(),
        PNMDataset => new PNMPropertiesRenderer(),
        DatasetGroup => new DatasetGroupProperties(),
        BoreholeDataset => new BoreholePropertiesRenderer(),
        TwoDGeologyDataset => new TwoDGeologyProperties(),
        SeismicDataset => new SeismicProperties(),
        PhysicoChemDataset => new PhysicoChemPropertiesRenderer(),
        VideoDataset => new VideoDatasetProperties(),
        AudioDataset => new AudioDatasetProperties(),
        TextDataset => new TextPropertiesRenderer(),
        NerfDataset => new NerfPropertiesRenderer(),
        SlopeStabilityDataset => new DefaultPropertiesRenderer(),
        _ => new DefaultPropertiesRenderer()
    };
}

public static IDatasetTools CreateTools(Dataset dataset)
{
    return dataset switch
    {
        // Slope Stability
        SlopeStabilityDataset slopeDataset => new SlopeStabilityTools(slopeDataset),

        // --- MODIFIED: Use the composite tool for all CT-related tools ---
        CtImageStackDataset => new CtImageStackCompositeTool(),
        StreamingCtVolumeDataset sds when sds.EditablePartner != null => new CtImageStackCompositeTool(),
        // --- END MODIFICATION ---

        Mesh3DDataset => new Mesh3DTools(),
        TableDataset => new TableTools(),
        SubsurfaceGISDataset => new SubsurfaceGISTools(),
        GISDataset => new GISTools(),
        AcousticVolumeDataset => new AcousticVolumeTools(),
        PNMDataset => new PNMTools(),
        ImageDataset => new ImageTools(),
        BoreholeDataset boreholeDataset => CreateBoreholeTools(boreholeDataset),
        TwoDGeologyDataset => new TwoDGeologyToolsWrapper(),
        SeismicDataset => new SeismicTools(),
        PhysicoChemDataset => new PhysicoChemTools(),
        VideoDataset => new VideoDatasetTools(),
        AudioDataset => new AudioDatasetTools(),
        TextDataset => new TextTools(),
        NerfDataset => new NerfTools(),
        // --- MODIFIED: Changed .All to .Any to make tool appear even if non-borehole datasets are in the group ---
        DatasetGroup group when group.Datasets.Any(d => d is BoreholeDataset) => new MultiBoreholeTools(),
        _ => new DefaultTools(),
    };
}

// Default implementations for datasets without specific UI components
private class DefaultPropertiesRenderer : IDatasetPropertiesRenderer
{
    public void Draw(Dataset dataset)
    {
        ImGui.TextDisabled("No properties available for this dataset type.");
        ImGui.Separator();
        ImGui.Text($"Type: {dataset.Type}");
        ImGui.Text($"Name: {dataset.Name}");
        if (!string.IsNullOrEmpty(dataset.FilePath)) ImGui.Text($"Path: {dataset.FilePath}");
        ImGui.Text($"Size: {FormatBytes(dataset.GetSizeInBytes())}");
    }

    private string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        var order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }
}

private class DefaultTools : IDatasetTools
{
    public void Draw(Dataset dataset)
    {
        ImGui.TextDisabled("No tools available for this dataset type.");
    }
}

// Properties renderer for dataset groups
private class DatasetGroupProperties : IDatasetPropertiesRenderer
{
    public void Draw(Dataset dataset)
    {
        if (dataset is DatasetGroup group)
        {
            ImGui.Text($"Group Name: {group.Name}");
            ImGui.Text($"Datasets: {group.Datasets.Count}");
            ImGui.Text($"Total Size: {FormatBytes(group.GetSizeInBytes())}");

            ImGui.Separator();
            ImGui.Text("Contents:");
            foreach (var child in group.Datasets) ImGui.BulletText($"{child.Name} ({child.Type})");
        }
    }

    private string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        var order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }
}

// Image tools implementation
private class ImageTools : IDatasetTools
{
    private bool _showHistogram = false;
    private int[] _histogram;
    private int _histogramMax = 1;
    private string _exportPath = "";

    public void Draw(Dataset dataset)
    {
        if (dataset is not ImageDataset image)
            return;

        ImGui.Text("Image Tools");
        ImGui.Separator();

        // Image Information
        if (ImGui.CollapsingHeader("Image Information", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var channels = GetChannelCount(image);
            ImGui.Text($"Dimensions: {image.Width} x {image.Height}");
            ImGui.Text($"Channels: {channels}");
            ImGui.Text($"Bit Depth: {image.BitDepth}-bit");
            ImGui.Text($"Size: {FormatBytes(image.GetSizeInBytes())}");
        }

        ImGui.Spacing();

        // Histogram
        if (ImGui.CollapsingHeader("Histogram"))
        {
            if (ImGui.Button(_showHistogram ? "Hide Histogram" : "Calculate Histogram", new Vector2(-1, 0)))
            {
                if (!_showHistogram)
                {
                    CalculateHistogram(image);
                }
                _showHistogram = !_showHistogram;
            }

            if (_showHistogram && _histogram != null)
            {
                ImGui.Spacing();
                DrawHistogram(_histogram, _histogramMax);
            }
        }

        ImGui.Spacing();

        // Export
        if (ImGui.CollapsingHeader("Export"))
        {
            ImGui.Text("Export image to file:");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("##ExportPath", ref _exportPath, 512);

            ImGui.Spacing();

            if (ImGui.Button("Export as PNG", new Vector2(-1, 0)))
            {
                ExportImage(image, _exportPath, "png");
            }

            if (ImGui.Button("Export as JPG", new Vector2(-1, 0)))
            {
                ExportImage(image, _exportPath, "jpg");
            }

            if (ImGui.Button("Export as BMP", new Vector2(-1, 0)))
            {
                ExportImage(image, _exportPath, "bmp");
            }
        }
    }

    private void CalculateHistogram(ImageDataset image)
    {
        _histogram = new int[256];
        _histogramMax = 1;

        if (image.ImageData == null || image.ImageData.Length == 0)
            return;

        var channels = GetChannelCount(image);
        var data = image.ImageData;

        for (int i = 0; i < data.Length; i += channels)
        {
            // Average RGB if color image, otherwise just use grayscale
            int value = 0;
            if (channels >= 3)
            {
                value = (data[i] + data[i + 1] + data[i + 2]) / 3;
            }
            else
            {
                value = data[i];
            }

            _histogram[value]++;
            if (_histogram[value] > _histogramMax)
                _histogramMax = _histogram[value];
        }
    }

    private int GetChannelCount(ImageDataset image)
    {
        if (image.ImageData == null || image.Width == 0 || image.Height == 0)
            return 0;

        var totalPixels = image.Width * image.Height;
        return totalPixels > 0 ? image.ImageData.Length / totalPixels : 0;
    }

    private string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        var order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }

    private void DrawHistogram(int[] histogram, int maxValue)
    {
        var plotSize = new Vector2(-1, 150);
        var dl = ImGui.GetWindowDrawList();
        var plotPos = ImGui.GetCursorScreenPos();
        var regionAvail = ImGui.GetContentRegionAvail();
        plotSize.X = regionAvail.X;

        // Background
        dl.AddRectFilled(plotPos, plotPos + plotSize,
            ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 1.0f)));

        // Draw bars
        var barWidth = plotSize.X / 256f;
        for (int i = 0; i < 256; i++)
        {
            if (histogram[i] == 0) continue;

            var barHeight = (histogram[i] / (float)maxValue) * plotSize.Y;
            var barPos = new Vector2(plotPos.X + i * barWidth, plotPos.Y + plotSize.Y - barHeight);
            var barSize = new Vector2(barWidth, barHeight);

            dl.AddRectFilled(barPos, barPos + barSize,
                ImGui.GetColorU32(new Vector4(0.3f, 0.7f, 1.0f, 0.8f)));
        }

        // Border
        dl.AddRect(plotPos, plotPos + plotSize,
            ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 1.0f)));

        // Labels
        dl.AddText(new Vector2(plotPos.X + 5, plotPos.Y + 5),
            ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), $"Max: {maxValue} pixels");
        dl.AddText(new Vector2(plotPos.X + 5, plotPos.Y + plotSize.Y - 20),
            ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), "0");
        dl.AddText(new Vector2(plotPos.X + plotSize.X - 30, plotPos.Y + plotSize.Y - 20),
            ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), "255");

        ImGui.Dummy(plotSize);
    }

    private void ExportImage(ImageDataset image, string path, string format)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                GeoscientistToolkit.Util.Logger.LogError("Export path is empty");
                return;
            }

            // Ensure correct extension
            var extension = $".{format}";
            if (!path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                path += extension;
            }

            // Check if we have image data
            if (image.ImageData == null || image.ImageData.Length == 0)
            {
                GeoscientistToolkit.Util.Logger.LogError("No pixel data available for export");
                return;
            }

            // Determine number of channels from data size
            var totalPixels = image.Width * image.Height;
            var channels = image.ImageData.Length / totalPixels;

            if (channels < 1 || channels > 4)
            {
                GeoscientistToolkit.Util.Logger.LogError($"Invalid channel count: {channels}");
                return;
            }

            var colorComponents = channels switch
            {
                1 => StbImageWriteSharp.ColorComponents.Grey,
                2 => StbImageWriteSharp.ColorComponents.GreyAlpha,
                3 => StbImageWriteSharp.ColorComponents.RedGreenBlue,
                4 => StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha,
                _ => StbImageWriteSharp.ColorComponents.RedGreenBlue
            };

            GeoscientistToolkit.Util.Logger.Log($"Exporting {image.Width}x{image.Height} image ({channels} channels) to {path}...");

            var writer = new StbImageWriteSharp.ImageWriter();
            using var stream = File.Create(path);

            switch (format.ToLower())
            {
                case "png":
                    writer.WritePng(image.ImageData, image.Width, image.Height, colorComponents, stream);
                    break;

                case "jpg":
                case "jpeg":
                    writer.WriteJpg(image.ImageData, image.Width, image.Height, colorComponents, stream, 90); // Quality: 90
                    break;

                case "bmp":
                    writer.WriteBmp(image.ImageData, image.Width, image.Height, colorComponents, stream);
                    break;

                default:
                    GeoscientistToolkit.Util.Logger.LogError($"Unsupported format: {format}");
                    return;
            }

            GeoscientistToolkit.Util.Logger.Log($"Successfully exported image to: {path}");
        }
        catch (Exception ex)
        {
            GeoscientistToolkit.Util.Logger.LogError($"Failed to export image: {ex.Message}");
        }
    }
}

// Wrapper for TwoDGeologyTools to conform to IDatasetTools interface
private class TwoDGeologyToolsWrapper : IDatasetTools
{
    private TwoDGeologyTools _tools;
    private TwoDGeologyViewer _viewer;
    
    public void Draw(Dataset dataset)
    {
        if (dataset is not TwoDGeologyDataset twoDGeoDataset)
        {
            ImGui.TextDisabled("Invalid dataset type for 2D Geology tools.");
            return;
        }

        // Get or create the viewer reference
        if (_viewer == null)
        {
            _viewer = twoDGeoDataset.GetViewer();
            
            if (_viewer == null)
            {
                ImGui.TextWrapped("Please open the dataset in a viewer first to access editing tools.");
                return;
            }
        }

        // Initialize tools if needed
        if (_tools == null && _viewer != null)
        {
            _tools = new TwoDGeologyTools(_viewer, twoDGeoDataset);
        }

        // Draw the tools panel
        if (_tools != null)
        {
            _tools.RenderToolsPanel();
            _tools.RenderInteractiveDrawingTool();
        }
        else
        {
            ImGui.TextWrapped("2D Geology tools are available when viewing the dataset.");
        }
    }
}

// Wrapper for TwoDGeologyViewer to conform to IDatasetViewer interface
private class TwoDGeologyViewerWrapper : IDatasetViewer
{
    private readonly TwoDGeologyViewer _viewer;
    private readonly TwoDGeologyDataset _dataset;

    public TwoDGeologyViewerWrapper(TwoDGeologyDataset dataset)
    {
        _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        _viewer = new TwoDGeologyViewer(dataset);
    }

    public void DrawToolbarControls()
    {
        // The TwoDGeologyViewer's toolbar is rendered as part of its Render() method
        // We don't need separate toolbar controls here
    }

    public void DrawContent(ref float zoom, ref Vector2 pan)
    {
        // The TwoDGeologyViewer manages its own zoom and pan internally
        // Just render the viewer
        _viewer.Render();
    }

    public void Dispose()
    {
        _viewer?.Dispose();
    }
}

// Properties renderer for TextDataset
private class TextPropertiesRenderer : IDatasetPropertiesRenderer
{
    public void Draw(Dataset dataset)
    {
        if (dataset is not TextDataset textDataset)
        {
            ImGui.TextDisabled("Invalid dataset type.");
            return;
        }

        ImGui.Text("Text Document Properties");
        ImGui.Separator();

        ImGui.Text($"Format: {textDataset.Format?.ToUpper() ?? "Unknown"}");
        ImGui.Text($"Encoding: {textDataset.FileEncoding?.WebName ?? "Unknown"}");
        ImGui.Separator();

        ImGui.Text($"Lines: {textDataset.LineCount:N0}");
        ImGui.Text($"Words: {textDataset.WordCount:N0}");
        ImGui.Text($"Characters: {textDataset.CharacterCount:N0}");
        ImGui.Separator();

        ImGui.Text($"File Size: {FormatBytes(textDataset.GetSizeInBytes())}");

        if (textDataset.IsGeneratedReport)
        {
            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 0.5f, 1.0f), "Generated Report");
            if (!string.IsNullOrEmpty(textDataset.GeneratedBy))
            {
                ImGui.Text($"Generated by: {textDataset.GeneratedBy}");
            }
            if (textDataset.GeneratedDate.HasValue)
            {
                ImGui.Text($"Generated on: {textDataset.GeneratedDate.Value:yyyy-MM-dd HH:mm:ss}");
            }
        }
    }

    private string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        var order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}

// Tools for TextDataset
private class TextTools : IDatasetTools
{
    public void Draw(Dataset dataset)
    {
        if (dataset is not TextDataset textDataset)
        {
            ImGui.TextDisabled("Invalid dataset type.");
            return;
        }

        ImGui.Text("Text Document Tools");
        ImGui.Separator();

        // Document info
        if (ImGui.CollapsingHeader("Document Information", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Text($"Format: {textDataset.Format?.ToUpper() ?? "Unknown"}");
            ImGui.Text($"Lines: {textDataset.LineCount:N0}");
            ImGui.Text($"Words: {textDataset.WordCount:N0}");
            ImGui.Text($"Characters: {textDataset.CharacterCount:N0}");
        }

        ImGui.Spacing();

        // Quick actions
        if (ImGui.CollapsingHeader("Quick Actions"))
        {
            if (ImGui.Button("Reload from Disk", new Vector2(-1, 0)))
            {
                textDataset.Load();
                Util.Logger.Log($"Reloaded text document: {textDataset.Name}");
            }

            if (ImGui.Button("Save to Disk", new Vector2(-1, 0)))
            {
                textDataset.Save();
                Util.Logger.Log($"Saved text document: {textDataset.Name}");
            }
        }

        ImGui.Spacing();

        // Statistics
        if (ImGui.CollapsingHeader("Statistics"))
        {
            var avgWordLength = textDataset.WordCount > 0
                ? (double)textDataset.CharacterCount / textDataset.WordCount
                : 0;
            var avgWordsPerLine = textDataset.LineCount > 0
                ? (double)textDataset.WordCount / textDataset.LineCount
                : 0;

            ImGui.Text($"Avg. word length: {avgWordLength:F1} chars");
            ImGui.Text($"Avg. words per line: {avgWordsPerLine:F1}");
            ImGui.Text($"File size: {FormatBytes(textDataset.GetSizeInBytes())}");
        }
    }

    private string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        var order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}

}