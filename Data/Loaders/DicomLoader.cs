// GeoscientistToolkit/Data/Loaders/DicomLoader.cs

using FellowOakDicom;
using FellowOakDicom.Imaging;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Loaders;

public class DicomLoader : IDataLoader
{
    public string Name => "DICOM Loader";
    public string Description => "Import DICOM Series (CT/MRI) from a directory";

    // We can load if it's a directory containing .dcm files
    public bool CanImport
    {
        get
        {
            if (string.IsNullOrEmpty(SourcePath)) return false;

            // Check if directory
            if (Directory.Exists(SourcePath))
            {
                // Check if it contains at least one .dcm file
                return Directory.EnumerateFiles(SourcePath, "*.dcm", SearchOption.TopDirectoryOnly).Any();
            }
            return false;
        }
    }

    public string ValidationMessage
    {
        get
        {
            if (string.IsNullOrEmpty(SourcePath)) return "Please select a directory";
            if (!Directory.Exists(SourcePath)) return "Directory does not exist";

            bool hasDcm = Directory.EnumerateFiles(SourcePath, "*.dcm", SearchOption.TopDirectoryOnly).Any();
            if (!hasDcm) return "No .dcm files found in the directory";

            return null;
        }
    }

    public string SourcePath { get; set; } = "";

    // Options
    public bool UseMemoryMapping { get; set; } = false;
    public int BinningFactor { get; set; } = 1;

    public void Reset()
    {
        SourcePath = "";
        UseMemoryMapping = false;
        BinningFactor = 1;
    }

    public async Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progressReporter)
    {
        return await Task.Run(async () =>
        {
            progressReporter?.Report((0.1f, "Scanning DICOM files..."));

            var dcmFiles = Directory.GetFiles(SourcePath, "*.dcm")
                                    .Select(f => new FileInfo(f))
                                    .OrderBy(f => f.Name) // Initial sort by filename
                                    .ToList();

            if (dcmFiles.Count == 0)
                throw new FileNotFoundException("No DICOM files found in directory.");

            // Parse metadata to sort correctly by Slice Location or Instance Number
            var orderedFiles = new List<(string Path, double Location, int Instance)>();

            int scanned = 0;
            foreach (var file in dcmFiles)
            {
                try
                {
                    // Read only tags, not pixel data yet
                    var fileMeta = await DicomFile.OpenAsync(file.FullName, FileReadOption.ReadLargeOnDemand);
                    var ds = fileMeta.Dataset;

                    double location = 0;
                    if (ds.Contains(DicomTag.SliceLocation))
                        location = ds.GetSingleValue<double>(DicomTag.SliceLocation);
                    else if (ds.Contains(DicomTag.ImagePositionPatient))
                        location = ds.GetValues<double>(DicomTag.ImagePositionPatient)[2]; // Z coord

                    int instance = 0;
                    if (ds.Contains(DicomTag.InstanceNumber))
                        instance = ds.GetSingleValue<int>(DicomTag.InstanceNumber);

                    orderedFiles.Add((file.FullName, location, instance));
                }
                catch
                {
                    // If we can't read it, skip it
                }

                scanned++;
                if (scanned % 10 == 0)
                    progressReporter?.Report((0.1f + (0.1f * scanned / dcmFiles.Count), $"Scanning file {scanned}/{dcmFiles.Count}"));
            }

            // Sort by Location then Instance
            var sortedPaths = orderedFiles.OrderBy(x => x.Location).ThenBy(x => x.Instance).Select(x => x.Path).ToList();

            if (sortedPaths.Count == 0)
                throw new Exception("No valid DICOM files could be parsed.");

            // Read first file to get dimensions and params
            var firstDicom = await DicomFile.OpenAsync(sortedPaths[0]);
            var firstDs = firstDicom.Dataset;

            // Get dimensions
            int width = firstDs.GetSingleValue<ushort>(DicomTag.Columns);
            int height = firstDs.GetSingleValue<ushort>(DicomTag.Rows);
            int depth = sortedPaths.Count;

            // Extract pixel spacing if possible
            double pixelSpacing = 1.0; // Default
            if (firstDs.Contains(DicomTag.PixelSpacing))
            {
                // Pixel Spacing is Row\Col
                var spacing = firstDs.GetValues<double>(DicomTag.PixelSpacing);
                if (spacing.Length > 0) pixelSpacing = spacing[0]; // Assuming isotropic XY
            }

            // Window/Level
            double windowCenter = 0;
            double windowWidth = 0;
            bool hasWindowing = false;

            if (firstDs.Contains(DicomTag.WindowCenter) && firstDs.Contains(DicomTag.WindowWidth))
            {
                // Can be multiple values, take first
                var wc = firstDs.GetValues<double>(DicomTag.WindowCenter);
                var ww = firstDs.GetValues<double>(DicomTag.WindowWidth);
                if (wc.Length > 0 && ww.Length > 0)
                {
                    windowCenter = wc[0];
                    windowWidth = ww[0];
                    hasWindowing = true;
                }
            }

            // If no windowing, we might need to rely on RescaleIntercept/Slope to get HU, then assume a default window for CT
            // Default CT soft tissue: WL 40, WW 400
            // Default CT bone: WL 400, WW 1800
            double slope = 1.0;
            double intercept = 0.0;
            if (firstDs.Contains(DicomTag.RescaleSlope)) slope = firstDs.GetSingleValue<double>(DicomTag.RescaleSlope);
            if (firstDs.Contains(DicomTag.RescaleIntercept)) intercept = firstDs.GetSingleValue<double>(DicomTag.RescaleIntercept);

            if (!hasWindowing)
            {
                // Fallback to min/max scanning or default
                // Let's scan first slice to guess range if needed, or just default to full dynamic range mapping
                // For simplicity and robustness, we will map Min/Max of the pixel data to 0-255 if no windowing is provided.
                // However, usually CT has standard values.
            }

            // Create Volume
            string datasetName = new DirectoryInfo(SourcePath).Name;
            string volumePath = Path.Combine(SourcePath, $"{datasetName}.Volume.bin");

            ChunkedVolume volume = new ChunkedVolume(width, height, depth);
            volume.PixelSize = pixelSpacing * 1e-3; // mm to meters

            // Load slice by slice
            for (int z = 0; z < depth; z++)
            {
                var dcm = await DicomFile.OpenAsync(sortedPaths[z]);
                var ds = dcm.Dataset;

                var pixelData = DicomPixelData.Create(ds);

                // We assume grayscale here.
                // Pixel data can be allocated in different ways.
                // Commonly explicit VR little endian -> uncompressed.

                byte[] sliceBytes = new byte[width * height];

                // Logic to convert raw pixel data to byte[] using Window/Level
                if (pixelData.BitsStored <= 8)
                {
                    // 8-bit data, just copy
                    byte[] raw = pixelData.GetFrame(0).Data;
                    // If buffer size matches
                    if (raw.Length == width * height)
                    {
                        Array.Copy(raw, sliceBytes, raw.Length);
                    }
                    else
                    {
                        // Mismatch or RGB?
                        // If RGB (PlanarConfiguration), it's 3 bytes per pixel.
                        // We are targeting Grayscale.
                        // Simple fallback: take Green channel or average?
                        // For now, assume simple copy if size matches, else fill 0.
                    }
                }
                else
                {
                    // 16-bit data usually
                    // Get raw buffer
                    var rawBuffer = pixelData.GetFrame(0).Data;

                    // Interpret as short/ushort
                    // Determine if signed
                    bool isSigned = (ds.GetSingleValue<ushort>(DicomTag.PixelRepresentation) == 1);

                    // Parse based on endianness (usually Little Endian for DICOM)
                    // We can use BitConverter or simple math

                    double currentSlope = slope;
                    double currentIntercept = intercept;
                    // Update per slice if present
                    if (ds.Contains(DicomTag.RescaleSlope)) currentSlope = ds.GetSingleValue<double>(DicomTag.RescaleSlope);
                    if (ds.Contains(DicomTag.RescaleIntercept)) currentIntercept = ds.GetSingleValue<double>(DicomTag.RescaleIntercept);

                    double wc = windowCenter;
                    double ww = windowWidth;
                    if (ds.Contains(DicomTag.WindowCenter) && ds.Contains(DicomTag.WindowWidth))
                    {
                         wc = ds.GetValues<double>(DicomTag.WindowCenter)[0];
                         ww = ds.GetValues<double>(DicomTag.WindowWidth)[0];
                         hasWindowing = true;
                    }

                    // If we still don't have windowing, find min/max
                    if (!hasWindowing)
                    {
                        // Quick scan for min/max
                        double minVal = double.MaxValue;
                        double maxVal = double.MinValue;
                        for (int i = 0; i < width * height; i++)
                        {
                            int rawVal = 0;
                            if (isSigned)
                                rawVal = BitConverter.ToInt16(rawBuffer, i * 2);
                            else
                                rawVal = BitConverter.ToUInt16(rawBuffer, i * 2);

                            double val = rawVal * currentSlope + currentIntercept;
                            if (val < minVal) minVal = val;
                            if (val > maxVal) maxVal = val;
                        }
                        wc = (maxVal + minVal) / 2.0;
                        ww = maxVal - minVal;
                        if (ww == 0) ww = 1;
                    }

                    double windowMin = wc - ww / 2.0;
                    double windowMax = wc + ww / 2.0;

                    for (int i = 0; i < width * height; i++)
                    {
                        int rawVal = 0;
                        if (isSigned)
                            rawVal = BitConverter.ToInt16(rawBuffer, i * 2); // Little Endian assumption
                        else
                            rawVal = BitConverter.ToUInt16(rawBuffer, i * 2);

                        // Apply Modality LUT (Rescale)
                        double val = rawVal * currentSlope + currentIntercept;

                        // Apply VOI LUT (Windowing)
                        double normalized = (val - windowMin) / ww;

                        // Clamp to 0-1
                        if (normalized < 0) normalized = 0;
                        if (normalized > 1) normalized = 1;

                        sliceBytes[i] = (byte)(normalized * 255.0);
                    }
                }

                volume.WriteSliceZ(z, sliceBytes);

                if (z % 5 == 0)
                     progressReporter?.Report((0.2f + (0.8f * z / depth), $"Loading slice {z + 1}/{depth}"));
            }

            // Create Dataset
            var dataset = new CtImageStackDataset(datasetName, SourcePath)
            {
                Width = width,
                Height = height,
                Depth = depth,
                PixelSize = (float)(pixelSpacing),
                SliceThickness = (float)(pixelSpacing),
                Unit = "mm",
                BinningSize = 1
            };

            await volume.SaveAsBinAsync(volumePath);

            var labelsPath = Path.Combine(SourcePath, $"{datasetName}.Labels.bin");
            if (!File.Exists(labelsPath))
            {
                var labels = new ChunkedLabelVolume(width, height, depth, ChunkedVolume.DEFAULT_CHUNK_DIM, false);
                labels.SaveAsBin(labelsPath);
            }

            progressReporter?.Report((1.0f, "DICOM Loaded Successfully"));

            return dataset;
        });
    }
}
