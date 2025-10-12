// GeoscientistToolkit/Data/CtImageStack/CtImageStackDataset.cs

using System.Numerics;
using System.Text.Json;
using GeoscientistToolkit.Analysis.NMR;
using GeoscientistToolkit.Analysis.ThermalConductivity;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.Util;

// Added for material serialization

namespace GeoscientistToolkit.Data.CtImageStack;

public class CtImageStackDataset : Dataset, ISerializableDataset
{
    // Volume data

    public CtImageStackDataset(string name, string folderPath) : base(name, folderPath)
    {
        Type = DatasetType.CtImageStack;
        // Add a default "Exterior" material that will be replaced if a local materials file is found
        Materials.Add(new Material(0, "Exterior", new Vector4(0, 0, 0, 0)));
    }

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
    public List<string> ImagePaths { get; set; } = new();
    public ChunkedVolume VolumeData { get; set; }

    // --- NEW PROPERTIES FOR MATERIALS AND LABELS ---
    public ChunkedLabelVolume LabelData { get; set; }
    public List<Material> Materials { get; set; } = new();

    // --- NEW PROPERTIES FOR STORING ANALYSIS RESULTS ---
    public NMRResults NmrResults { get; set; }
    public ThermalResults ThermalResults { get; set; }

    public object ToSerializableObject()
    {
        var dto = new CtImageStackDatasetDTO
        {
            TypeName = nameof(CtImageStackDataset),
            Name = Name,
            FilePath = FilePath,
            PixelSize = PixelSize,
            SliceThickness = SliceThickness,
            Unit = Unit,
            BinningSize = BinningSize
        };

        if (Materials != null)
            foreach (var material in Materials)
                dto.Materials.Add(new MaterialDTO
                {
                    ID = material.ID,
                    Name = material.Name,
                    Color = material.Color,
                    MinValue = material.MinValue,
                    MaxValue = material.MaxValue,
                    IsVisible = material.IsVisible,
                    IsExterior = material.IsExterior,
                    Density = material.Density,
                    PhysicalMaterialName = material.PhysicalMaterialName // NEW
                });
        // --- NEW: Serialize results ---
        if (NmrResults != null)
        {
            dto.NmrResults = new NMRResultsDTO
            {
                TimePoints = NmrResults.TimePoints,
                Magnetization = NmrResults.Magnetization,
                T2Histogram = NmrResults.T2Histogram,
                T2HistogramBins = NmrResults.T2HistogramBins,
                T1Histogram = NmrResults.T1Histogram,
                T1HistogramBins = NmrResults.T1HistogramBins,
                HasT1T2Data = NmrResults.HasT1T2Data,
                PoreSizes = NmrResults.PoreSizes,
                PoreSizeDistribution = NmrResults.PoreSizeDistribution,
                MeanT2 = NmrResults.MeanT2,
                GeometricMeanT2 = NmrResults.GeometricMeanT2,
                T2PeakValue = NmrResults.T2PeakValue,
                NumberOfWalkers = NmrResults.NumberOfWalkers,
                TotalSteps = NmrResults.TotalSteps,
                TimeStep = NmrResults.TimeStep,
                PoreMaterial = NmrResults.PoreMaterial,
                MaterialRelaxivities = NmrResults.MaterialRelaxivities,
                ComputationTimeSeconds = NmrResults.ComputationTime.TotalSeconds,
                ComputationMethod = NmrResults.ComputationMethod
            };

            if (NmrResults.T1T2Map != null && NmrResults.HasT1T2Data)
            {
                var t1Count = NmrResults.T1T2Map.GetLength(0);
                var t2Count = NmrResults.T1T2Map.GetLength(1);
                dto.NmrResults.T1T2Map_T1Count = t1Count;
                dto.NmrResults.T1T2Map_T2Count = t2Count;
                dto.NmrResults.T1T2MapData = new double[t1Count * t2Count];
                Buffer.BlockCopy(NmrResults.T1T2Map, 0, dto.NmrResults.T1T2MapData, 0,
                    t1Count * t2Count * sizeof(double));
            }
        }

        if (ThermalResults != null)
        {
            dto.ThermalResults = new ThermalResultsDTO
            {
                EffectiveConductivity = ThermalResults.EffectiveConductivity,
                MaterialConductivities = ThermalResults.MaterialConductivities,
                AnalyticalEstimates = ThermalResults.AnalyticalEstimates,
                ComputationTimeSeconds = ThermalResults.ComputationTime.TotalSeconds,
                IterationsPerformed = ThermalResults.IterationsPerformed,
                FinalError = ThermalResults.FinalError
            };

            if (ThermalResults.TemperatureField != null)
            {
                var w = ThermalResults.TemperatureField.GetLength(0);
                var h = ThermalResults.TemperatureField.GetLength(1);
                var d = ThermalResults.TemperatureField.GetLength(2);
                dto.ThermalResults.TempField_W = w;
                dto.ThermalResults.TempField_H = h;
                dto.ThermalResults.TempField_D = d;
                dto.ThermalResults.TemperatureFieldData = new float[w * h * d];
                Buffer.BlockCopy(ThermalResults.TemperatureField, 0, dto.ThermalResults.TemperatureFieldData, 0,
                    w * h * d * sizeof(float));
            }
        }


        return dto;
    }

    public override long GetSizeInBytes()
    {
        long totalSize = 0;
        var volumePath = GetVolumePath();
        if (File.Exists(volumePath)) totalSize += new FileInfo(volumePath).Length;

        var labelPath = GetLabelPath();
        if (File.Exists(labelPath)) totalSize += new FileInfo(labelPath).Length;

        // If binary files don't exist, calculate from images
        if (totalSize == 0 && Directory.Exists(FilePath))
        {
            var imageFiles = Directory.GetFiles(FilePath)
                .Where(f => IsImageFile(f))
                .ToList();

            foreach (var file in imageFiles) totalSize += new FileInfo(file).Length;
        }

        return totalSize;
    }

    public override void Load()
    {
        // Load Grayscale Data
        if (VolumeData == null)
        {
            var volumePath = GetVolumePath();
            if (File.Exists(volumePath))
            {
                var loadTask = ChunkedVolume.LoadFromBinAsync(volumePath, false);
                VolumeData = loadTask.GetAwaiter().GetResult();
                if (VolumeData != null)
                {
                    Width = VolumeData.Width;
                    Height = VolumeData.Height;
                    Depth = VolumeData.Depth;
                }
            }
        }

        // --- MODIFIED: Load materials from local file first ---
        LoadMaterials();

        // Load Label Data (non-destructively)
        if (LabelData == null)
        {
            var labelPath = GetLabelPath();
            if (File.Exists(labelPath))
            {
                try
                {
                    var loadedLabels = ChunkedLabelVolume.LoadFromBin(labelPath, false);
                    if (VolumeData != null &&
                        (loadedLabels.Width != VolumeData.Width ||
                         loadedLabels.Height != VolumeData.Height ||
                         loadedLabels.Depth != VolumeData.Depth))
                        // Log a warning about the mismatch but still load the data.
                        Logger.LogWarning(
                            $"[CtImageStackDataset] Mismatched dimensions for '{Name}'. Label data may not align with volume data. " +
                            $"Labels: {loadedLabels.Width}x{loadedLabels.Height}x{loadedLabels.Depth}, " +
                            $"Volume: {VolumeData.Width}x{VolumeData.Height}x{VolumeData.Depth}");
                    LabelData = loadedLabels;
                    Logger.Log($"[CtImageStackDataset] Loaded label data for '{Name}' from {labelPath}");
                }
                catch (Exception ex)
                {
                    Logger.LogError(
                        $"[CtImageStackDataset] Failed to load label file '{labelPath}': {ex.Message}. A new empty label volume will be used.");
                    if (VolumeData != null)
                        LabelData = new ChunkedLabelVolume(Width, Height, Depth, VolumeData.ChunkDim, false, labelPath);
                }
            }
            else if (VolumeData != null)
            {
                // If the label file doesn't exist, create an in-memory representation
                // but DO NOT save it automatically. It will be saved when the user performs
                // a segmentation action or saves the project.
                Logger.Log(
                    $"[CtImageStackDataset] No label file found for {Name}. A new in-memory label volume will be used.");
                LabelData = new ChunkedLabelVolume(Width, Height, Depth, VolumeData.ChunkDim, false, labelPath);
            }
        }
    }

    public override void Unload()
    {
        VolumeData?.Dispose();
        VolumeData = null;
        LabelData?.Dispose();
        LabelData = null;
    }

    // --- NEW METHOD: Save label data ---
    public void SaveLabelData()
    {
        if (LabelData != null)
        {
            var labelPath = GetLabelPath();
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
            var materialsPath = GetMaterialsPath();
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
                Density = m.Density,
                PhysicalMaterialName = m.PhysicalMaterialName // NEW
            }).ToList();

            var jsonString = JsonSerializer.Serialize(dtos, options);
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
        // If the Materials list contains more than just the default 'Exterior' material,
        // it means it has likely been populated by the project loader. In that case,
        // we should not overwrite the correctly deserialized data from the project file.
        if (Materials.Any(m => m.ID != 0))
        {
            Logger.Log(
                $"[CtImageStackDataset] Materials for '{Name}' appear to be pre-loaded (e.g., from a project file). Checking for black materials...");

            // FIX: Check for black materials and assign random colors
            FixBlackMaterials();
            return;
        }

        var materialsPath = GetMaterialsPath();
        if (File.Exists(materialsPath))
        {
            try
            {
                var jsonString = File.ReadAllText(materialsPath);
                var options = new JsonSerializerOptions();
                var dtos = JsonSerializer.Deserialize<List<MaterialDTO>>(jsonString, options);

                if (dtos != null && dtos.Any())
                {
                    Materials.Clear();
                    foreach (var dto in dtos)
                    {
                        var material = new Material(dto.ID, dto.Name, dto.Color)
                        {
                            MinValue = dto.MinValue,
                            MaxValue = dto.MaxValue,
                            IsVisible = dto.IsVisible,
                            IsExterior = dto.IsExterior,
                            Density = dto.Density,
                            PhysicalMaterialName = dto.PhysicalMaterialName // NEW
                        };
                        Materials.Add(material);
                    }

                    Logger.Log($"[CtImageStackDataset] Loaded {Materials.Count} materials from {materialsPath}");

                    // Check for black materials after loading
                    FixBlackMaterials();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[CtImageStackDataset] Failed to load materials: {ex.Message}");
                // Add default if loading fails
                if (!Materials.Any(m => m.ID == 0))
                {
                    Materials.Clear();
                    Materials.Add(new Material(0, "Exterior", new Vector4(0, 0, 0, 0)));
                }
            }
        }
        else
        {
            // No materials file - ensure default exists
            if (!Materials.Any()) Materials.Add(new Material(0, "Exterior", new Vector4(0, 0, 0, 0)));
        }
    }

    private void FixBlackMaterials()
    {
        var random = new Random();
        var anyFixed = false;

        foreach (var material in Materials)
        {
            // Skip exterior material (ID 0)
            if (material.ID == 0)
            {
                // Ensure exterior is transparent
                material.Color = new Vector4(0, 0, 0, 0);
                material.IsExterior = true;
                continue;
            }

            // Check if material is black or nearly black (all RGB components < 0.1)
            if (material.Color.X < 0.1f && material.Color.Y < 0.1f && material.Color.Z < 0.1f)
            {
                // Generate a random, visually distinct color
                var hue = random.Next(0, 360) / 360f;
                var newColor = HsvToRgb(hue, 0.7f, 0.8f);
                material.Color = new Vector4(newColor.X, newColor.Y, newColor.Z, 1.0f);
                anyFixed = true;

                Logger.Log(
                    $"[CtImageStackDataset] Assigned random color to material {material.ID} ({material.Name}): RGB({material.Color.X:F2}, {material.Color.Y:F2}, {material.Color.Z:F2})");
            }
        }

        if (anyFixed)
        {
            Logger.Log("[CtImageStackDataset] Fixed black materials with random colors");
            SaveMaterials(); // Save the fixed colors
        }
    }

    private static Vector4 HsvToRgb(float h, float s, float v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h * 6 % 2 - 1));
        var m = v - c;

        float r, g, b;
        if (h < 1f / 6)
        {
            r = c;
            g = x;
            b = 0;
        }
        else if (h < 2f / 6)
        {
            r = x;
            g = c;
            b = 0;
        }
        else if (h < 3f / 6)
        {
            r = 0;
            g = c;
            b = x;
        }
        else if (h < 4f / 6)
        {
            r = 0;
            g = x;
            b = c;
        }
        else if (h < 5f / 6)
        {
            r = x;
            g = 0;
            b = c;
        }
        else
        {
            r = c;
            g = 0;
            b = x;
        }

        return new Vector4(r + m, g + m, b + m, 1.0f);
    }

    // --- NEW HELPER: Get path for the local materials file ---
    private string GetMaterialsPath()
    {
        if (File.Exists(FilePath))
        {
            var directory = Path.GetDirectoryName(FilePath);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(FilePath);
            return Path.Combine(directory, $"{nameWithoutExt}.Materials.json");
        }

        var folderName =
            Path.GetFileName(FilePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return Path.Combine(FilePath, $"{folderName}.Materials.json");
    }

    private string GetVolumePath()
    {
        if (File.Exists(FilePath))
        {
            var directory = Path.GetDirectoryName(FilePath);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(FilePath);
            return Path.Combine(directory, $"{nameWithoutExt}.Volume.bin");
        }

        var folderName =
            Path.GetFileName(FilePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return Path.Combine(FilePath, $"{folderName}.Volume.bin");
    }

    private string GetLabelPath()
    {
        if (File.Exists(FilePath))
        {
            var directory = Path.GetDirectoryName(FilePath);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(FilePath);
            return Path.Combine(directory, $"{nameWithoutExt}.Labels.bin");
        }

        var folderName =
            Path.GetFileName(FilePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return Path.Combine(FilePath, $"{folderName}.Labels.bin");
    }

    private bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLower();
        return ext == ".png" || ext == ".jpg" || ext == ".jpeg" ||
               ext == ".bmp" || ext == ".tif" || ext == ".tiff" ||
               ext == ".tga" || ext == ".gif";
    }
}