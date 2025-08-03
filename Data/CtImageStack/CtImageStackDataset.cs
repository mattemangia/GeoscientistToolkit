// GeoscientistToolkit/Data/CtImageStack/CtImageStackDataset.cs
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace GeoscientistToolkit.Data.CtImageStack
{
    public class CtImageStackDataset : Dataset, ISerializableDataset
    {
        // Dimensions
        public int Width { get; set; }
        public int Height { get; set; }
        public int Depth { get; set; } // Number of slices

        // Pixel/Voxel information
        public float PixelSize { get; set; } // In-plane pixel size
        public float SliceThickness { get; set; } // Distance between slices
        public string Unit { get; set; } = "µm";
        public int BitDepth { get; set; } = 16;

        // CT-specific properties
        public int BinningSize { get; set; } = 1;
        public float MinValue { get; set; }
        public float MaxValue { get; set; }

        // File paths for the image stack
        public List<string> ImagePaths { get; set; } = new List<string>();

        // Volume data
        private ChunkedVolume _volumeData;
        public ChunkedVolume VolumeData => _volumeData;

        // --- NEW PROPERTIES FOR MATERIALS AND LABELS ---
        public ChunkedLabelVolume LabelData { get; private set; }
        public List<Material> Materials { get; set; } = new List<Material>();

        public CtImageStackDataset(string name, string folderPath) : base(name, folderPath)
        {
            Type = DatasetType.CtImageStack;
            // Add a default "Exterior" material
            Materials.Add(new Material(0, "Exterior", new Vector4(0, 0, 0, 0)));
        }

        public override long GetSizeInBytes()
        {
            long totalSize = 0;
            string volumePath = GetVolumePath();
            if (File.Exists(volumePath))
            {
                totalSize += new FileInfo(volumePath).Length;
            }

            string labelPath = GetLabelPath();
            if (File.Exists(labelPath))
            {
                totalSize += new FileInfo(labelPath).Length;
            }

            // If binary files don't exist, calculate from images
            if (totalSize == 0 && Directory.Exists(FilePath))
            {
                var imageFiles = Directory.GetFiles(FilePath)
                    .Where(f => IsImageFile(f))
                    .ToList();

                foreach (var file in imageFiles)
                {
                    totalSize += new FileInfo(file).Length;
                }
            }

            return totalSize;
        }

        public override void Load()
        {
            // Load Grayscale Data
            if (_volumeData == null)
            {
                var volumePath = GetVolumePath();
                if (File.Exists(volumePath))
                {
                    var loadTask = ChunkedVolume.LoadFromBinAsync(volumePath, false);
                    _volumeData = loadTask.GetAwaiter().GetResult();
                    if (_volumeData != null)
                    {
                        Width = _volumeData.Width;
                        Height = _volumeData.Height;
                        Depth = _volumeData.Depth;
                    }
                }
            }

            // Load Label Data
            if (LabelData == null)
            {
                var labelPath = GetLabelPath();
                if (File.Exists(labelPath))
                {
                    var loadedLabels = ChunkedLabelVolume.LoadFromBin(labelPath, false);
                    if (_volumeData != null &&
                        (loadedLabels.Width != _volumeData.Width ||
                         loadedLabels.Height != _volumeData.Height ||
                         loadedLabels.Depth != _volumeData.Depth))
                    {
                        Logger.LogWarning($"[CtImageStackDataset] Mismatched dimensions for '{Name}'. Recreating empty label file.");
                        loadedLabels.Dispose();
                        try { File.Delete(labelPath); } catch { }
                        LabelData = new ChunkedLabelVolume(Width, Height, Depth, _volumeData.ChunkDim, false, labelPath);
                        LabelData.SaveAsBin(labelPath);
                    }
                    else
                    {
                        LabelData = loadedLabels;
                    }
                }
                else if (_volumeData != null)
                {
                    Logger.Log($"[CtImageStackDataset] No label file found for {Name}. Creating a new empty one.");
                    LabelData = new ChunkedLabelVolume(Width, Height, Depth, _volumeData.ChunkDim, false, labelPath);
                    LabelData.SaveAsBin(labelPath);
                }
            }
        }

        public override void Unload()
        {
            _volumeData?.Dispose();
            _volumeData = null;
            LabelData?.Dispose();
            LabelData = null;
        }

        public object ToSerializableObject()
        {
            var dto = new CtImageStackDatasetDTO
            {
                TypeName = nameof(CtImageStackDataset),
                Name = this.Name,
                FilePath = this.FilePath,
                PixelSize = this.PixelSize,
                SliceThickness = this.SliceThickness,
                Unit = this.Unit,
                BinningSize = this.BinningSize,
            };

            if (this.Materials != null)
            {
                foreach (var material in this.Materials)
                {
                    dto.Materials.Add(new MaterialDTO
                    {
                        ID = material.ID,
                        Name = material.Name,
                        Color = material.Color,
                        MinValue = material.MinValue,
                        MaxValue = material.MaxValue,
                        IsVisible = material.IsVisible,
                        IsExterior = material.IsExterior,
                        Density = material.Density
                    });
                }
            }
            return dto;
        }

        private string GetVolumePath()
        {
            // Handle both folder paths and file paths (e.g., multi-page TIFF)
            if (File.Exists(FilePath))
            {
                // FilePath is a file (e.g., multi-page TIFF)
                string directory = Path.GetDirectoryName(FilePath);
                string nameWithoutExt = Path.GetFileNameWithoutExtension(FilePath);
                return Path.Combine(directory, $"{nameWithoutExt}.Volume.bin");
            }
            else
            {
                // FilePath is a directory
                string folderName = Path.GetFileName(FilePath);
                return Path.Combine(FilePath, $"{folderName}.Volume.bin");
            }
        }

        private string GetLabelPath()
        {
            // Handle both folder paths and file paths (e.g., multi-page TIFF)
            if (File.Exists(FilePath))
            {
                // FilePath is a file (e.g., multi-page TIFF)
                string directory = Path.GetDirectoryName(FilePath);
                string nameWithoutExt = Path.GetFileNameWithoutExtension(FilePath);
                return Path.Combine(directory, $"{nameWithoutExt}.Labels.bin");
            }
            else
            {
                // FilePath is a directory
                string folderName = Path.GetFileName(FilePath);
                return Path.Combine(FilePath, $"{folderName}.Labels.bin");
            }
        }

        private bool IsImageFile(string path)
        {
            string ext = Path.GetExtension(path).ToLower();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" ||
                   ext == ".bmp" || ext == ".tif" || ext == ".tiff" ||
                   ext == ".tga" || ext == ".gif";
        }
    }
}