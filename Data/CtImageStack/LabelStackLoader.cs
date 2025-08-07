// GeoscientistToolkit/Data/CtImageStack/LabelStackLoader.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.Util;
using StbImageSharp;

namespace GeoscientistToolkit.Data.CtImageStack
{
    /// <summary>
    /// Handles loading of label stacks from exported images, reconstructing materials and labels
    /// </summary>
    public static class LabelStackLoader
    {
        public class LabelLoadResult
        {
            public ChunkedVolume GrayscaleVolume { get; set; }
            public ChunkedLabelVolume LabelVolume { get; set; }
            public List<Material> Materials { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public int Depth { get; set; }
            public Dictionary<uint, byte> ColorToMaterialIdMap { get; set; }
        }

        /// <summary>
        /// Loads a label stack from a folder of colored images
        /// </summary>
        public static async Task<LabelLoadResult> LoadLabelStackAsync(
            string folderPath,
            bool createEmptyGrayscale,
            IProgress<float> progress = null,
            System.Threading.CancellationToken cancellationToken = default)
        {
            Logger.Log($"[LabelStackLoader] Loading label stack from: {folderPath}");

            // Get all image files
            var imageFiles = GetImageFiles(folderPath);
            if (imageFiles.Count == 0)
            {
                Logger.LogError("[LabelStackLoader] No supported image files found in the folder.");
                throw new FileNotFoundException("No supported image files found in the folder.");
            }

            // Sort files numerically
            imageFiles = SortImagesNumerically(imageFiles);
            Logger.Log($"[LabelStackLoader] Found {imageFiles.Count} label images");

            // Get dimensions from first image
            var (width, height) = await GetImageDimensionsAsync(imageFiles[0]);
            int depth = imageFiles.Count;

            Logger.Log($"[LabelStackLoader] Label volume dimensions: {width}×{height}×{depth}");

            // First pass: Analyze all images to find unique colors
            progress?.Report(0.1f);
            var uniqueColors = await AnalyzeUniqueColorsAsync(imageFiles, progress, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                Logger.LogWarning("[LabelStackLoader] Operation cancelled by user");
                return null;
            }

            Logger.Log($"[LabelStackLoader] Found {uniqueColors.Count} unique colors");

            // Create material mapping
            var (materials, colorToIdMap) = CreateMaterialsFromColors(uniqueColors);

            // Log material creation
            foreach (var material in materials)
            {
                Logger.Log($"[LabelStackLoader] Created material: {material.Name} (ID: {material.ID})");
            }

            // Create label volume
            var labelVolume = new ChunkedLabelVolume(width, height, depth, ChunkedVolume.DEFAULT_CHUNK_DIM, false);

            // Create grayscale volume (empty if requested)
            ChunkedVolume grayscaleVolume = null;
            if (createEmptyGrayscale)
            {
                grayscaleVolume = new ChunkedVolume(width, height, depth, ChunkedVolume.DEFAULT_CHUNK_DIM);
                Logger.Log("[LabelStackLoader] Created empty grayscale volume");
            }

            // Second pass: Load images and populate label volume
            await PopulateLabelVolumeAsync(imageFiles, labelVolume, colorToIdMap, progress, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                Logger.LogWarning("[LabelStackLoader] Operation cancelled by user");
                labelVolume?.Dispose();
                grayscaleVolume?.Dispose();
                return null;
            }

            Logger.Log("[LabelStackLoader] Label stack loading completed successfully");

            return new LabelLoadResult
            {
                GrayscaleVolume = grayscaleVolume,
                LabelVolume = labelVolume,
                Materials = materials,
                Width = width,
                Height = height,
                Depth = depth,
                ColorToMaterialIdMap = colorToIdMap
            };
        }

        /// <summary>
        /// Analyzes all images to find unique colors
        /// </summary>
        private static async Task<HashSet<uint>> AnalyzeUniqueColorsAsync(
            List<string> imageFiles,
            IProgress<float> progress,
            System.Threading.CancellationToken cancellationToken)
        {
            var uniqueColors = new HashSet<uint>();
            var colorLock = new object();
            int processedCount = 0;
            int totalFiles = imageFiles.Count;

            Logger.Log("[LabelStackLoader] Starting color analysis pass...");

            // Process images in parallel with limited concurrency
            var semaphore = new SemaphoreSlim(Environment.ProcessorCount);
            var tasks = new List<Task>();

            foreach (var imagePath in imageFiles)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                await semaphore.WaitAsync(cancellationToken);

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var localColors = await AnalyzeImageColorsAsync(imagePath);

                        lock (colorLock)
                        {
                            foreach (var color in localColors)
                            {
                                uniqueColors.Add(color);
                            }

                            processedCount++;
                            float currentProgress = 0.1f + (0.3f * processedCount / totalFiles);
                            progress?.Report(currentProgress);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"[LabelStackLoader] Error analyzing image {Path.GetFileName(imagePath)}: {ex.Message}");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks);

            // Always ensure black (background) is in the color set
            uniqueColors.Add(0xFF000000); // Black in ARGB format

            return uniqueColors;
        }

        /// <summary>
        /// Analyzes a single image for unique colors
        /// </summary>
        private static async Task<HashSet<uint>> AnalyzeImageColorsAsync(string imagePath)
        {
            return await Task.Run(() =>
            {
                var colors = new HashSet<uint>();

                using (var stream = File.OpenRead(imagePath))
                {
                    var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

                    for (int i = 0; i < image.Data.Length; i += 4)
                    {
                        byte r = image.Data[i];
                        byte g = image.Data[i + 1];
                        byte b = image.Data[i + 2];
                        byte a = image.Data[i + 3];

                        // Convert to ARGB uint
                        uint color = ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
                        colors.Add(color);
                    }
                }

                return colors;
            });
        }

        /// <summary>
        /// Creates materials from unique colors
        /// </summary>
        private static (List<Material>, Dictionary<uint, byte>) CreateMaterialsFromColors(HashSet<uint> uniqueColors)
        {
            var materials = new List<Material>();
            var colorToIdMap = new Dictionary<uint, byte>();
            byte nextId = 0;

            Logger.Log($"[LabelStackLoader] Creating materials from {uniqueColors.Count} unique colors");

            // Sort colors for consistent material assignment
            var sortedColors = uniqueColors.OrderBy(c => c).ToList();

            foreach (var color in sortedColors)
            {
                // Extract RGBA components
                byte a = (byte)((color >> 24) & 0xFF);
                byte r = (byte)((color >> 16) & 0xFF);
                byte g = (byte)((color >> 8) & 0xFF);
                byte b = (byte)(color & 0xFF);

                // Skip if this is black (background/exterior)
                if (r == 0 && g == 0 && b == 0)
                {
                    colorToIdMap[color] = 0; // Map to exterior
                    continue;
                }

                // Skip if we've reached the maximum number of materials
                if (nextId >= 255)
                {
                    Logger.LogWarning($"[LabelStackLoader] Maximum material count (255) reached. Skipping remaining colors.");
                    break;
                }

                nextId++;

                // Create material with the color
                var material = new Material(
                    nextId,
                    $"Material_{nextId:D3}",
                    new Vector4(r / 255f, g / 255f, b / 255f, a / 255f)
                );

                materials.Add(material);
                colorToIdMap[color] = nextId;
            }

            // Always add an exterior material if not already present
            if (!materials.Any(m => m.ID == 0))
            {
                materials.Insert(0, new Material(0, "Exterior", new Vector4(0, 0, 0, 1))
                {
                    IsExterior = true
                });
            }

            Logger.Log($"[LabelStackLoader] Created {materials.Count} materials from colors");

            return (materials, colorToIdMap);
        }

        /// <summary>
        /// Populates the label volume from images
        /// </summary>
        private static async Task PopulateLabelVolumeAsync(
            List<string> imageFiles,
            ChunkedLabelVolume labelVolume,
            Dictionary<uint, byte> colorToIdMap,
            IProgress<float> progress,
            System.Threading.CancellationToken cancellationToken)
        {
            Logger.Log("[LabelStackLoader] Starting label volume population...");

            int processedCount = 0;
            int totalFiles = imageFiles.Count;

            // Process images sequentially for Z-order
            for (int z = 0; z < imageFiles.Count; z++)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                await Task.Run(() =>
                {
                    try
                    {
                        LoadLabelSlice(imageFiles[z], labelVolume, z, colorToIdMap);

                        processedCount++;
                        float currentProgress = 0.4f + (0.6f * processedCount / totalFiles);
                        progress?.Report(currentProgress);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"[LabelStackLoader] Error loading slice {z} from {Path.GetFileName(imageFiles[z])}: {ex.Message}");
                    }
                }, cancellationToken);
            }

            Logger.Log($"[LabelStackLoader] Populated {processedCount} slices into label volume");
        }

        /// <summary>
        /// Loads a single slice into the label volume
        /// </summary>
        private static void LoadLabelSlice(
            string imagePath,
            ChunkedLabelVolume labelVolume,
            int z,
            Dictionary<uint, byte> colorToIdMap)
        {
            using (var stream = File.OpenRead(imagePath))
            {
                var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

                if (image.Width != labelVolume.Width || image.Height != labelVolume.Height)
                {
                    Logger.LogWarning($"[LabelStackLoader] Image dimensions ({image.Width}×{image.Height}) don't match volume ({labelVolume.Width}×{labelVolume.Height})");
                    return;
                }

                var sliceData = new byte[labelVolume.Width * labelVolume.Height];

                for (int y = 0; y < image.Height; y++)
                {
                    for (int x = 0; x < image.Width; x++)
                    {
                        int pixelIndex = (y * image.Width + x) * 4;

                        byte r = image.Data[pixelIndex];
                        byte g = image.Data[pixelIndex + 1];
                        byte b = image.Data[pixelIndex + 2];
                        byte a = image.Data[pixelIndex + 3];

                        // Convert to ARGB uint
                        uint color = ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;

                        // Map color to material ID
                        byte materialId = 0; // Default to exterior
                        if (colorToIdMap.TryGetValue(color, out byte id))
                        {
                            materialId = id;
                        }
                        else
                        {
                            // If color not found, try to find closest match or use exterior
                            // For now, just use exterior
                            materialId = 0;
                        }

                        sliceData[y * labelVolume.Width + x] = materialId;
                    }
                }

                // Write the slice to the volume
                labelVolume.WriteSliceZ(z, sliceData);
            }
        }

        /// <summary>
        /// Gets all supported image files from a folder
        /// </summary>
        private static List<string> GetImageFiles(string folder)
        {
            var extensions = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".gif", ".tif", ".tiff" };
            return Directory.GetFiles(folder)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()))
                .ToList();
        }

        /// <summary>
        /// Sorts image files numerically based on numbers in filename
        /// </summary>
        private static List<string> SortImagesNumerically(List<string> files)
        {
            return files.OrderBy(f =>
            {
                string name = Path.GetFileNameWithoutExtension(f);
                string numbers = new string(name.Where(char.IsDigit).ToArray());
                return int.TryParse(numbers, out int n) ? n : 0;
            }).ToList();
        }

        /// <summary>
        /// Gets dimensions of an image
        /// </summary>
        private static async Task<(int width, int height)> GetImageDimensionsAsync(string imagePath)
        {
            return await Task.Run(() =>
            {
                var info = ImageLoader.LoadImageInfo(imagePath);
                return (info.Width, info.Height);
            });
        }

        /// <summary>
        /// Saves the label load result as a dataset
        /// </summary>
        public static void SaveAsDataset(
            LabelLoadResult result,
            string outputPath,
            string datasetName)
        {
            Logger.Log($"[LabelStackLoader] Saving dataset to: {outputPath}");

            try
            {
                // Save grayscale volume if it exists
                if (result.GrayscaleVolume != null)
                {
                    string volumePath = Path.Combine(outputPath, $"{datasetName}.Volume.bin");
                    result.GrayscaleVolume.SaveAsBin(volumePath);
                    Logger.Log($"[LabelStackLoader] Saved grayscale volume to: {volumePath}");
                }

                // Save label volume
                string labelPath = Path.Combine(outputPath, $"{datasetName}.Labels.bin");
                result.LabelVolume.SaveAsBin(labelPath);
                Logger.Log($"[LabelStackLoader] Saved label volume to: {labelPath}");

                Logger.Log($"[LabelStackLoader] Dataset saved successfully with {result.Materials.Count} materials");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[LabelStackLoader] Error saving dataset: {ex.Message}");
                throw;
            }
        }
    }
}