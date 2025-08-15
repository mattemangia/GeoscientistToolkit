// GeoscientistToolkit/UI/DatasetUIFactory.cs
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Data.Table;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.GIS;
using GeoscientistToolkit.Data.AcousticVolume;
using ImGuiNET;
using System;
using System.Numerics;

namespace GeoscientistToolkit.UI
{
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

                CtImageStackDataset ctDataset => new CtImageStackViewer(ctDataset),

                // Image datasets
                ImageDataset imageDataset => new ImageViewer(imageDataset),

                // 3D Mesh datasets
                Mesh3DDataset mesh3DDataset => new Mesh3DViewer(mesh3DDataset),

                // Table datasets
                TableDataset tableDataset => new TableViewer(tableDataset),

                // GIS datasets - Use real implementations when available
                GISDataset gisDataset => new GeoscientistToolkit.UI.GIS.GISViewer(gisDataset),

                // Acoustic Volume datasets - Use real implementations when available
                AcousticVolumeDataset acousticDataset => new GeoscientistToolkit.Data.AcousticVolume.AcousticVolumeViewer(acousticDataset),

                // Dataset groups cannot be opened in a viewer
                DatasetGroup => throw new InvalidOperationException("Cannot open a DatasetGroup in a viewer. Please open individual datasets."),

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
                GISDataset => new GeoscientistToolkit.UI.GIS.GISProperties(),
                AcousticVolumeDataset => new GeoscientistToolkit.Data.AcousticVolume.AcousticVolumeProperties(),
                DatasetGroup => new DatasetGroupProperties(),
                _ => new DefaultPropertiesRenderer()
            };
        }

        public static IDatasetTools CreateTools(Dataset dataset)
        {
            return dataset switch
            {
                CtImageStackDataset => new CtImageStackTools(),
                StreamingCtVolumeDataset sds when sds.EditablePartner != null => new CtImageStackTools(),
                Mesh3DDataset => new Mesh3DTools(),
                TableDataset => new TableTools(),
                GISDataset => new GeoscientistToolkit.UI.GIS.GISTools(),
                AcousticVolumeDataset => new GeoscientistToolkit.Data.AcousticVolume.AcousticVolumeTools(),
                ImageDataset => new ImageTools(),
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
                if (!string.IsNullOrEmpty(dataset.FilePath))
                {
                    ImGui.Text($"Path: {dataset.FilePath}");
                }
                ImGui.Text($"Size: {FormatBytes(dataset.GetSizeInBytes())}");
            }

            private string FormatBytes(long bytes)
            {
                string[] sizes = { "B", "KB", "MB", "GB", "TB" };
                int order = 0;
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
                    foreach (var child in group.Datasets)
                    {
                        ImGui.BulletText($"{child.Name} ({child.Type})");
                    }
                }
            }

            private string FormatBytes(long bytes)
            {
                string[] sizes = { "B", "KB", "MB", "GB", "TB" };
                int order = 0;
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
    }
}