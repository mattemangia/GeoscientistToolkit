// GeoscientistToolkit/UI/DatasetUIFactory.cs

using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.Data.Table;
using GeoscientistToolkit.Data.TwoDGeology;
using GeoscientistToolkit.UI.Borehole;
using GeoscientistToolkit.UI.GIS;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Tools;
using ImGuiNET;

namespace GeoscientistToolkit.UI;

public static class DatasetUIFactory
{
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
            GISDataset gisDataset => new GISViewer(gisDataset),
            DatasetGroup group when group.Datasets.All(d => d is GISDataset) =>
                new GISViewer(group.Datasets.Cast<GISDataset>().ToList()),
            
            // Acoustic Volume datasets
            AcousticVolumeDataset acousticDataset => new AcousticVolumeViewer(acousticDataset),

            // PNM Dataset
            PNMDataset pnmDataset => new PNMViewer(pnmDataset),

            // Borehole Dataset
            BoreholeDataset boreholeDataset => new BoreholeViewer(boreholeDataset),

            // 2D Geology Dataset
            TwoDGeologyDataset twoDGeologyDataset => new TwoDGeologyViewerWrapper(twoDGeologyDataset),

            // Dataset groups cannot be opened in a viewer
            DatasetGroup => throw new InvalidOperationException(
                "Cannot open a DatasetGroup in a viewer. Please open individual datasets."),

            _ => throw new NotSupportedException($"No viewer available for dataset type: {dataset.GetType().Name}")
        };
    }

    public static IDatasetPropertiesRenderer CreatePropertiesRenderer(Dataset dataset)
    {
        return dataset switch
        {
            ImageDataset => new ImagePropertiesRenderer(),
            CtImageStackDataset or StreamingCtVolumeDataset => new CtImageStackPropertiesRenderer(),
            Mesh3DDataset => new Mesh3DProperties(),
            TableDataset => new TableProperties(),
            GISDataset => new GISProperties(),
            AcousticVolumeDataset => new AcousticVolumeProperties(),
            PNMDataset => new PNMPropertiesRenderer(),
            DatasetGroup => new DatasetGroupProperties(),
            BoreholeDataset => new BoreholePropertiesRenderer(),
            TwoDGeologyDataset => new TwoDGeologyProperties(),
            _ => new DefaultPropertiesRenderer()
        };
    }

    public static IDatasetTools CreateTools(Dataset dataset)
    {
        return dataset switch
        {
            // --- MODIFIED: Use the composite tool for all CT-related tools ---
            CtImageStackDataset => new CtImageStackCompositeTool(),
            StreamingCtVolumeDataset sds when sds.EditablePartner != null => new CtImageStackCompositeTool(),
            // --- END MODIFICATION ---

            Mesh3DDataset => new Mesh3DTools(),
            TableDataset => new TableTools(),
            GISDataset => new GISTools(),
            AcousticVolumeDataset => new AcousticVolumeTools(),
            PNMDataset => new PNMTools(),
            ImageDataset => new ImageTools(),
            BoreholeDataset => new BoreholeTools(),
            TwoDGeologyDataset => new TwoDGeologyToolsWrapper(),
            _ => new DefaultTools()
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
        public void Draw(Dataset dataset)
        {
            if (dataset is ImageDataset image)
            {
                ImGui.Text("Image Tools");
                ImGui.Separator();

                if (ImGui.Button("Show Histogram"))
                {
                    // Implementation pending
                }

                if (ImGui.Button("Export"))
                {
                    // Implementation pending
                }

                ImGui.Separator();
                ImGui.TextDisabled("Additional image tools coming soon");
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
}