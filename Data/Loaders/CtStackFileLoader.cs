// GeoscientistToolkit/Data/Loaders/CtStackFileLoader.cs

using System.Text.Json;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Loaders;

public class CtStackFileLoader : IDataLoader
{
    public string Name => ".ctstack Loader";
    public string Description => "Load CT stack from a .ctstack manifest file";

    public string SourcePath { get; set; } = "";

    public bool CanImport
    {
        get
        {
            if (string.IsNullOrEmpty(SourcePath)) return false;
            return File.Exists(SourcePath) && Path.GetExtension(SourcePath).ToLowerInvariant() == ".ctstack";
        }
    }

    public string ValidationMessage
    {
        get
        {
            if (string.IsNullOrEmpty(SourcePath)) return "Please select a .ctstack file";
            if (!File.Exists(SourcePath)) return "File does not exist";
            return null;
        }
    }

    public void Reset()
    {
        SourcePath = "";
    }

    public async Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progressReporter)
    {
        return await Task.Run(async () =>
        {
            progressReporter?.Report((0.1f, "Parsing .ctstack file..."));

            var jsonContent = await File.ReadAllTextAsync(SourcePath);
            var manifest = JsonSerializer.Deserialize<CtStackManifest>(jsonContent);

            if (manifest == null || manifest.ImagePaths == null || manifest.ImagePaths.Count == 0)
                throw new InvalidOperationException("Invalid or empty .ctstack file");

            var baseDir = Path.GetDirectoryName(SourcePath);

            // Resolve paths relative to the .ctstack file
            var resolvedPaths = manifest.ImagePaths.Select(p =>
                Path.IsPathRooted(p) ? p : Path.Combine(baseDir, p)
            ).ToList();

            // Verify files exist
            var missing = resolvedPaths.FirstOrDefault(p => !File.Exists(p));
            if (missing != null)
                throw new FileNotFoundException($"Referenced image file not found: {missing}");

            // Load first image for dimensions
            var firstInfo = ImageLoader.LoadImageInfo(resolvedPaths[0]);
            int width = firstInfo.Width;
            int height = firstInfo.Height;
            int depth = resolvedPaths.Count;

            string datasetName = Path.GetFileNameWithoutExtension(SourcePath);
            string volumePath = Path.Combine(baseDir, $"{datasetName}.Volume.bin");

            // Load volume
            var volume = new ChunkedVolume(width, height, depth);
            volume.PixelSize = manifest.PixelSize * (manifest.Unit == "mm" ? 1e-3 : 1e-6);

            for (int z = 0; z < depth; z++)
            {
                // Use existing ImageLoader
                var sliceData = ImageLoader.LoadGrayscaleImage(resolvedPaths[z], out var w, out var h);
                if (w != width || h != height)
                    Logger.LogWarning($"Slice {z} dimensions mismatch. Expected {width}x{height}, got {w}x{h}");

                volume.WriteSliceZ(z, sliceData);

                if (z % 10 == 0)
                    progressReporter?.Report((0.2f + (0.7f * z / depth), $"Loading slice {z + 1}/{depth}"));
            }

            await volume.SaveAsBinAsync(volumePath);

            // Create Labels
            var labelsPath = Path.Combine(baseDir, $"{datasetName}.Labels.bin");
            if (!File.Exists(labelsPath))
            {
                var labels = new ChunkedLabelVolume(width, height, depth, ChunkedVolume.DEFAULT_CHUNK_DIM, false);
                labels.SaveAsBin(labelsPath);
            }

            var dataset = new CtImageStackDataset(datasetName, SourcePath)
            {
                Width = width,
                Height = height,
                Depth = depth,
                PixelSize = (float)manifest.PixelSize,
                SliceThickness = (float)manifest.PixelSize, // Assuming isotropic if not specified
                Unit = manifest.Unit ?? "µm",
                BinningSize = 1
            };

            progressReporter?.Report((1.0f, ".ctstack Loaded Successfully"));
            return dataset;
        });
    }

    private class CtStackManifest
    {
        public double PixelSize { get; set; }
        public string Unit { get; set; } // "mm", "um", etc.
        public List<string> ImagePaths { get; set; }
    }
}
