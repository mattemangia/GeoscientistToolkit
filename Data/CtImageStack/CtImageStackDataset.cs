// GeoscientistToolkit/Data/CtImageStack/CtImageStackDataset.cs
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json; // Added for material serialization
using System;

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
        public ChunkedLabelVolume LabelData { get; set; }
        public List<Material> Materials { get; set; } = new List<Material>();

        public CtImageStackDataset(string name, string folderPath) : base(name, folderPath)
        {
            Type = DatasetType.CtImageStack;
            // Add a default "Exterior" material that will be replaced if a local materials file is found
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

            // --- MODIFIED: Load materials from local file first ---
            LoadMaterials();

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
                // Metadata will be handled by ProjectSerializer
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

        // --- NEW METHOD: Save label data ---
        public void SaveLabelData()
        {
            if (LabelData != null)
            {
                string labelPath = GetLabelPath();
                if (!string.IsNullOrEmpty(labelPath))
                {
                    Logger.Log($"[CtImageStackDataset] Saving label data for '{Name}' to {labelPath}");
                    LabelData.SaveAsBin(labelPath);
                }
            }
        }

        // --- NEW METHOD: Save materials to a local JSON file ---
        public void SaveMaterials()
        {
            if (Materials == null || Materials.Count == 0) return;

            try
            {
                string materialsPath = GetMaterialsPath();
                var options = new JsonSerializerOptions { WriteIndented = true };
                var dtos = Materials.Select(m => new MaterialDTO
                {
                    ID = m.ID,
                    Name = m.Name,
                    Color = m.Color,
                    MinValue = m.MinValue,
                    MaxValue = m.MaxValue,
                    IsVisible = m.IsVisible,
                    IsExterior = m.IsExterior,
                    Density = m.Density
                }).ToList();

                string jsonString = JsonSerializer.Serialize(dtos, options);
                File.WriteAllText(materialsPath, jsonString);
                Logger.Log($"[CtImageStackDataset] Saved {Materials.Count} materials to {materialsPath}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[CtImageStackDataset] Failed to save materials: {ex.Message}");
            }
        }

        // --- NEW METHOD: Load materials from a local JSON file ---
        private void LoadMaterials()
        {
            string materialsPath = GetMaterialsPath();
            if (File.Exists(materialsPath))
            {
                try
                {
                    string jsonString = File.ReadAllText(materialsPath);
                    var options = new JsonSerializerOptions();
                    var dtos = JsonSerializer.Deserialize<List<MaterialDTO>>(jsonString, options);

                    if (dtos != null && dtos.Any())
                    {
                        Materials.Clear();
                        foreach (var dto in dtos)
                        {
                            Materials.Add(new Material(dto.ID, dto.Name, dto.Color)
                            {
                                MinValue = dto.MinValue,
                                MaxValue = dto.MaxValue,
                                IsVisible = dto.IsVisible,
                                IsExterior = dto.IsExterior,
                                Density = dto.Density
                            });
                        }
                        Logger.Log($"[CtImageStackDataset] Loaded {Materials.Count} materials from {materialsPath}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[CtImageStackDataset] Failed to load materials from {materialsPath}: {ex.Message}. Using default.");
                    // Ensure default material exists if loading fails
                    if (!Materials.Any(m => m.ID == 0))
                    {
                        Materials.Clear();
                        Materials.Add(new Material(0, "Exterior", new Vector4(0, 0, 0, 0)));
                    }
                }
            }
        }

        // --- NEW HELPER: Get path for the local materials file ---
        private string GetMaterialsPath()
        {
            if (File.Exists(FilePath))
            {
                string directory = Path.GetDirectoryName(FilePath);
                string nameWithoutExt = Path.GetFileNameWithoutExtension(FilePath);
                return Path.Combine(directory, $"{nameWithoutExt}.Materials.json");
            }
            else
            {
                string folderName = Path.GetFileName(FilePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                return Path.Combine(FilePath, $"{folderName}.Materials.json");
            }
        }

        private string GetVolumePath()
        {
            if (File.Exists(FilePath))
            {
                string directory = Path.GetDirectoryName(FilePath);
                string nameWithoutExt = Path.GetFileNameWithoutExtension(FilePath);
                return Path.Combine(directory, $"{nameWithoutExt}.Volume.bin");
            }
            else
            {
                string folderName = Path.GetFileName(FilePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                return Path.Combine(FilePath, $"{folderName}.Volume.bin");
            }
        }

        private string GetLabelPath()
        {
            if (File.Exists(FilePath))
            {
                string directory = Path.GetDirectoryName(FilePath);
                string nameWithoutExt = Path.GetFileNameWithoutExtension(FilePath);
                return Path.Combine(directory, $"{nameWithoutExt}.Labels.bin");
            }
            else
            {
                string folderName = Path.GetFileName(FilePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
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