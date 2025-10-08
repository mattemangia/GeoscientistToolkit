// GeoscientistToolkit/Data/Image/ImageExporter.cs
// Modified to use SkiaSharp and BitMiracle.LibTiff.NET for cross-platform, open-source image processing.

using System.Globalization;
using System.Runtime.InteropServices;
using BitMiracle.LibTiff.Classic;
using GeoscientistToolkit.Util;
using SkiaSharp;

// Added for TIFF support

namespace GeoscientistToolkit.Data.Image;

public class ImageExporter
{
    private readonly SKTypeface _defaultTypeface;

    public ImageExporter()
    {
        // Use SkiaSharp's font manager to find a suitable cross-platform font.
        try
        {
            var fontManager = SKFontManager.Default;
            var preferredFamilies = new[] { "Arial", "Helvetica", "Verdana", "sans-serif" };

            // Correctly iterate through preferred families to find the first match.
            foreach (var family in preferredFamilies)
            {
                _defaultTypeface = fontManager.MatchFamily(family, SKFontStyle.Normal);
                if (_defaultTypeface != null)
                    break;
            }

            // If no preferred font is found, use the system's default.
            _defaultTypeface ??= fontManager.MatchFamily(null, SKFontStyle.Normal);
        }
        catch
        {
            // Ultimate fallback if the font manager fails.
            _defaultTypeface = SKTypeface.Default;
        }
    }

    public void Export(ImageDataset dataset, string outputPath, bool includeScaleBar, bool includeTopInfo)
    {
        try
        {
            Logger.Log($"Exporting image to: {outputPath}");
            dataset.Load();

            if (dataset.ImageData == null)
            {
                Logger.LogError("Failed to export: Image data is null");
                return;
            }

            var gcHandle = GCHandle.Alloc(dataset.ImageData, GCHandleType.Pinned);
            var handleIsOwnedByBitmap = false;

            try
            {
                var info = new SKImageInfo(dataset.Width, dataset.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                using var bitmap = new SKBitmap();
                bitmap.InstallPixels(info, gcHandle.AddrOfPinnedObject(), info.RowBytes,
                    (addr, ctx) => ((GCHandle)ctx).Free(), gcHandle);
                handleIsOwnedByBitmap = true;

                using (var canvas = new SKCanvas(bitmap))
                {
                    if (includeTopInfo) DrawTopInformation(canvas, dataset);
                    if (includeScaleBar && dataset.PixelSize > 0)
                        DrawScaleBar(canvas, dataset, info.Width, info.Height);
                }

                var extension = Path.GetExtension(outputPath).ToLowerInvariant();

                // FIX: Handle TIFF separately using LibTiff.NET because SkiaSharp does not support it.
                if (extension == ".tif" || extension == ".tiff")
                    SaveBitmapAsTiff(bitmap, outputPath);
                else
                    // Use SkiaSharp's built-in encoders for other formats.
                    SaveBitmapWithSkia(bitmap, outputPath, extension);
            }
            finally
            {
                if (!handleIsOwnedByBitmap && gcHandle.IsAllocated) gcHandle.Free();
            }

            Logger.Log($"Successfully exported image to: {outputPath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export image: {ex.Message}");
        }
    }

    private void SaveBitmapWithSkia(SKBitmap bitmap, string outputPath, string extension)
    {
        using var image = SKImage.FromBitmap(bitmap);
        SKEncodedImageFormat format;
        var quality = 95; // For JPEG

        switch (extension)
        {
            case ".png":
                format = SKEncodedImageFormat.Png;
                break;
            case ".jpg":
            case ".jpeg":
                format = SKEncodedImageFormat.Jpeg;
                break;
            case ".bmp":
                format = SKEncodedImageFormat.Bmp;
                break;
            // FIX: Removed unsupported TIFF case.
            default:
                format = SKEncodedImageFormat.Png;
                outputPath = Path.ChangeExtension(outputPath, ".png");
                break;
        }

        using var stream = File.OpenWrite(outputPath);
        image.Encode(format, quality).SaveTo(stream);
    }

    private void SaveBitmapAsTiff(SKBitmap bitmap, string outputPath)
    {
        // Get the raw pixel data from the SkiaSharp bitmap.
        var pixels = bitmap.Bytes;
        var width = bitmap.Width;
        var height = bitmap.Height;
        var rowBytes = bitmap.RowBytes;

        using (var tiff = Tiff.Open(outputPath, "w"))
        {
            if (tiff == null) throw new IOException($"Could not open TIFF file for writing: {outputPath}");

            tiff.SetField(TiffTag.IMAGEWIDTH, width);
            tiff.SetField(TiffTag.IMAGELENGTH, height);
            tiff.SetField(TiffTag.SAMPLESPERPIXEL, 4); // RGBA
            tiff.SetField(TiffTag.BITSPERSAMPLE, 8);
            tiff.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);
            tiff.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
            tiff.SetField(TiffTag.PHOTOMETRIC, Photometric.RGB);

            // Specify that the 4th sample is an associated alpha channel.
            tiff.SetField(TiffTag.EXTRASAMPLES, 1, new[] { (short)ExtraSample.ASSOCALPHA });

            // Write the image data scanline by scanline.
            var scanline = new byte[rowBytes];
            for (var i = 0; i < height; i++)
            {
                Buffer.BlockCopy(pixels, i * rowBytes, scanline, 0, rowBytes);
                tiff.WriteScanline(scanline, i);
            }
        }
    }

    // --- Drawing methods remain the same as the previous SkiaSharp version ---

    private void DrawTopInformation(SKCanvas canvas, ImageDataset dataset)
    {
        using var textPaint = new SKPaint(new SKFont(_defaultTypeface, 14));
        var lines = new List<string>
        {
            dataset.Name,
            $"Resolution: {dataset.Width} x {dataset.Height}",
            $"Date: {dataset.DateCreated:yyyy-MM-dd HH:mm}"
        };
        if (dataset.PixelSize > 0) lines.Add($"Scale: {dataset.PixelSize:F2} {dataset.Unit ?? "µm"}/pixel");
        float padding = 10, lineHeight = 20, yPos = padding;
        var maxWidth = lines.Max(line => textPaint.MeasureText(line));
        var bgWidth = maxWidth + padding * 2;
        var bgHeight = lines.Count * lineHeight + padding * 2 - (lineHeight - textPaint.TextSize);
        using (var bgPaint = new SKPaint { Color = new SKColor(0, 0, 0, 200), Style = SKPaintStyle.Fill })
        {
            canvas.DrawRect(SKRect.Create(0, 0, bgWidth, bgHeight), bgPaint);
        }

        textPaint.Color = SKColors.White;
        foreach (var line in lines)
        {
            canvas.DrawText(line, padding, yPos + textPaint.TextSize, textPaint);
            yPos += lineHeight;
        }
    }

    private void DrawScaleBar(SKCanvas canvas, ImageDataset dataset, int imageWidth, int imageHeight)
    {
        float targetWidthPixels = 120f, realWorldUnitsPerPixel = dataset.PixelSize;
        var barLengthInRealUnits = targetWidthPixels * realWorldUnitsPerPixel;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(barLengthInRealUnits)));
        var mostSignificantDigit = Math.Round(barLengthInRealUnits / magnitude);
        if (mostSignificantDigit > 5) mostSignificantDigit = 10;
        else if (mostSignificantDigit > 2) mostSignificantDigit = 5;
        else if (mostSignificantDigit > 1) mostSignificantDigit = 2;
        var niceLengthInRealUnits = (float)(mostSignificantDigit * magnitude);
        var finalBarLengthPixels = niceLengthInRealUnits / realWorldUnitsPerPixel;
        var label = $"{niceLengthInRealUnits.ToString("G", CultureInfo.InvariantCulture)} {dataset.Unit ?? "µm"}";
        using var textPaint = new SKPaint(new SKFont(_defaultTypeface, 14));
        var textBounds = new SKRect();
        textPaint.MeasureText(label, ref textBounds);
        float margin = 20, barHeight = 8, textPadding = 5;
        var x = imageWidth - finalBarLengthPixels - margin;
        var yBar = imageHeight - margin - barHeight;
        var yText = yBar - textPadding;
        float bgPadding = 8;
        var bgRect = SKRect.Create(x - bgPadding, yText - textBounds.Height - bgPadding,
            finalBarLengthPixels + bgPadding * 2, barHeight + textBounds.Height + textPadding + bgPadding * 2);
        using (var bgPaint = new SKPaint { Color = new SKColor(0, 0, 0, 128), Style = SKPaintStyle.Fill })
        {
            canvas.DrawRect(bgRect, bgPaint);
        }

        using (var barPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill })
        {
            canvas.DrawRect(SKRect.Create(x, yBar, finalBarLengthPixels, barHeight), barPaint);
        }

        textPaint.Color = SKColors.White;
        var textX = x + (finalBarLengthPixels - textBounds.Width) / 2;
        canvas.DrawText(label, textX, yText, textPaint);
    }
}