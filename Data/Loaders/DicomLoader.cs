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

    public bool CanImport
    {
        get
        {
            if (string.IsNullOrEmpty(SourcePath)) return false;
            if (!Directory.Exists(SourcePath)) return false;
            return Directory.EnumerateFiles(SourcePath, "*.dcm", SearchOption.TopDirectoryOnly).Any();
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

            var orderedFiles = new List<(string Path, double Location, int Instance)>();

            int scanned = 0;
            foreach (var file in dcmFiles)
            {
                try
                {
                    var fileMeta = await DicomFile.OpenAsync(file.FullName, FileReadOption.ReadLargeOnDemand);
                    var ds = fileMeta.Dataset;

                    double location = 0;
                    if (ds.Contains(DicomTag.SliceLocation))
                        location = ds.GetSingleValue<double>(DicomTag.SliceLocation);
                    else if (ds.Contains(DicomTag.ImagePositionPatient))
                        location = ds.GetValues<double>(DicomTag.ImagePositionPatient)[2];

                    int instance = 0;
                    if (ds.Contains(DicomTag.InstanceNumber))
                        instance = ds.GetSingleValue<int>(DicomTag.InstanceNumber);

                    orderedFiles.Add((file.FullName, location, instance));
                }
                catch (DicomFileException)
                {
                    // Skip files that cannot be parsed as DICOM
                }

                scanned++;
                if (scanned % 10 == 0)
                    progressReporter?.Report((0.1f + (0.1f * scanned / dcmFiles.Count), $"Scanning file {scanned}/{dcmFiles.Count}"));
            }

            // Sort by Location then Instance
            var sortedPaths = orderedFiles.OrderBy(x => x.Location).ThenBy(x => x.Instance).Select(x => x.Path).ToList();

            if (sortedPaths.Count == 0)
                throw new Exception("No valid DICOM files could be parsed.");

            var firstDicom = await DicomFile.OpenAsync(sortedPaths[0]);
            var firstDs = firstDicom.Dataset;

            int width = firstDs.GetSingleValue<ushort>(DicomTag.Columns);
            int height = firstDs.GetSingleValue<ushort>(DicomTag.Rows);
            int depth = sortedPaths.Count;

            double pixelSpacing = 1.0;
            if (firstDs.Contains(DicomTag.PixelSpacing))
            {
                var spacing = firstDs.GetValues<double>(DicomTag.PixelSpacing);
                if (spacing.Length > 0) pixelSpacing = spacing[0];
            }

            double windowCenter = 0;
            double windowWidth = 0;
            bool hasWindowing = false;

            if (firstDs.Contains(DicomTag.WindowCenter) && firstDs.Contains(DicomTag.WindowWidth))
            {
                var wc = firstDs.GetValues<double>(DicomTag.WindowCenter);
                var ww = firstDs.GetValues<double>(DicomTag.WindowWidth);
                if (wc.Length > 0 && ww.Length > 0)
                {
                    windowCenter = wc[0];
                    windowWidth = ww[0];
                    hasWindowing = true;
                }
            }

            double slope = 1.0;
            double intercept = 0.0;
            if (firstDs.Contains(DicomTag.RescaleSlope)) slope = firstDs.GetSingleValue<double>(DicomTag.RescaleSlope);
            if (firstDs.Contains(DicomTag.RescaleIntercept)) intercept = firstDs.GetSingleValue<double>(DicomTag.RescaleIntercept);

            string datasetName = new DirectoryInfo(SourcePath).Name;
            string volumePath = Path.Combine(SourcePath, $"{datasetName}.Volume.bin");

            ChunkedVolume volume = new ChunkedVolume(width, height, depth);
            volume.PixelSize = pixelSpacing * 1e-3;

            for (int z = 0; z < depth; z++)
            {
                var dcm = await DicomFile.OpenAsync(sortedPaths[z]);
                var ds = dcm.Dataset;

                var pixelData = DicomPixelData.Create(ds);
                byte[] sliceBytes = new byte[width * height];

                if (pixelData.BitsStored <= 8)
                {
                    byte[] raw = pixelData.GetFrame(0).Data;
                    if (raw.Length == width * height)
                    {
                        Array.Copy(raw, sliceBytes, raw.Length);
                    }
                    else if (raw.Length == width * height * 3)
                    {
                        // RGB data - convert to grayscale using luminance formula
                        for (int i = 0; i < width * height; i++)
                        {
                            int r = raw[i * 3];
                            int g = raw[i * 3 + 1];
                            int b = raw[i * 3 + 2];
                            sliceBytes[i] = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
                        }
                    }
                }
                else
                {
                    var rawBuffer = pixelData.GetFrame(0).Data;
                    bool isSigned = (ds.GetSingleValue<ushort>(DicomTag.PixelRepresentation) == 1);

                    double currentSlope = slope;
                    double currentIntercept = intercept;
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

                    if (!hasWindowing)
                    {
                        double minVal = double.MaxValue;
                        double maxVal = double.MinValue;
                        for (int i = 0; i < width * height; i++)
                        {
                            int rawVal = isSigned
                                ? BitConverter.ToInt16(rawBuffer, i * 2)
                                : BitConverter.ToUInt16(rawBuffer, i * 2);

                            double val = rawVal * currentSlope + currentIntercept;
                            if (val < minVal) minVal = val;
                            if (val > maxVal) maxVal = val;
                        }
                        wc = (maxVal + minVal) / 2.0;
                        ww = maxVal - minVal;
                        if (ww == 0) ww = 1;
                    }

                    double windowMin = wc - ww / 2.0;

                    for (int i = 0; i < width * height; i++)
                    {
                        int rawVal = isSigned
                            ? BitConverter.ToInt16(rawBuffer, i * 2)
                            : BitConverter.ToUInt16(rawBuffer, i * 2);

                        double val = rawVal * currentSlope + currentIntercept;
                        double normalized = Math.Clamp((val - windowMin) / ww, 0.0, 1.0);
                        sliceBytes[i] = (byte)(normalized * 255.0);
                    }
                }

                volume.WriteSliceZ(z, sliceBytes);

                if (z % 5 == 0)
                     progressReporter?.Report((0.2f + (0.8f * z / depth), $"Loading slice {z + 1}/{depth}"));
            }

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
