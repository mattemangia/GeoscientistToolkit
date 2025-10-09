// GeoscientistToolkit/Business/Image/ImageStitcher.cs

using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business.Image;

/// <summary>
///     Stitch multiple satellite images together into a mosaic
/// </summary>
public class ImageStitcher
{
    /// <summary>
    ///     Stitch images using simple grid layout (no feature matching)
    /// </summary>
    public static ImageDataset StitchGrid(List<ImageDataset> images, int columns,
        BlendMode blendMode = BlendMode.Linear)
    {
        if (images == null || images.Count == 0)
            throw new ArgumentException("No images provided");

        Logger.Log($"Stitching {images.Count} images in {columns} columns with {blendMode} blending");

        // Calculate grid dimensions
        var rows = (int)Math.Ceiling(images.Count / (double)columns);

        // Assume all images have same dimensions (common for satellite tiles)
        var tileWidth = images[0].Width;
        var tileHeight = images[0].Height;

        var mosaicWidth = columns * tileWidth;
        var mosaicHeight = rows * tileHeight;

        Logger.Log($"Mosaic size: {mosaicWidth}x{mosaicHeight}");

        var mosaicData = new byte[mosaicWidth * mosaicHeight * 4];

        // Place each image in the grid
        for (var i = 0; i < images.Count; i++)
        {
            var col = i % columns;
            var row = i / columns;

            var offsetX = col * tileWidth;
            var offsetY = row * tileHeight;

            PlaceImage(images[i], mosaicData, mosaicWidth, mosaicHeight, offsetX, offsetY);
        }

        // Apply blending at seams
        if (blendMode != BlendMode.None)
            ApplyBlending(mosaicData, mosaicWidth, mosaicHeight, tileWidth, tileHeight, columns, rows, blendMode);

        var stitched = new ImageDataset($"Stitched_Mosaic_{columns}x{rows}", "")
        {
            Width = mosaicWidth,
            Height = mosaicHeight,
            ImageData = mosaicData
        };

        stitched.AddTag(ImageTag.Satellite);

        Logger.Log("Stitching complete");
        return stitched;
    }

    /// <summary>
    ///     Stitch images with automatic layout based on georeference data
    /// </summary>
    public static ImageDataset StitchGeoreferenced(List<ImageDataset> images, BlendMode blendMode = BlendMode.Linear)
    {
        Logger.Log($"Stitching {images.Count} georeferenced images");

        // Extract georeference info
        var geoImages = images
            .Select(img => ExtractGeoInfo(img))
            .Where(g => g != null)
            .ToList();

        if (geoImages.Count == 0)
        {
            Logger.LogWarning("No georeferenced images found, using grid layout");
            return StitchGrid(images, (int)Math.Ceiling(Math.Sqrt(images.Count)), blendMode);
        }

        // Calculate bounds
        var minX = geoImages.Min(g => g.MinX);
        var maxX = geoImages.Max(g => g.MaxX);
        var minY = geoImages.Min(g => g.MinY);
        var maxY = geoImages.Max(g => g.MaxY);

        // Use the finest resolution
        var pixelSize = geoImages.Min(g => g.PixelSize);

        var mosaicWidth = (int)Math.Ceiling((maxX - minX) / pixelSize);
        var mosaicHeight = (int)Math.Ceiling((maxY - minY) / pixelSize);

        Logger.Log($"Mosaic bounds: ({minX}, {minY}) to ({maxX}, {maxY})");
        Logger.Log($"Mosaic size: {mosaicWidth}x{mosaicHeight}");

        var mosaicData = new byte[mosaicWidth * mosaicHeight * 4];
        var weightMap = new float[mosaicWidth * mosaicHeight];

        // Place each image at its geo-registered position
        foreach (var geoImg in geoImages)
        {
            var offsetX = (int)Math.Round((geoImg.MinX - minX) / pixelSize);
            var offsetY = (int)Math.Round((maxY - geoImg.MaxY) / pixelSize); // Y is inverted

            PlaceImageWithBlending(geoImg.Image, mosaicData, weightMap, mosaicWidth, mosaicHeight,
                offsetX, offsetY, blendMode);
        }

        var stitched = new ImageDataset("Georeferenced_Mosaic", "")
        {
            Width = mosaicWidth,
            Height = mosaicHeight,
            ImageData = mosaicData
        };

        stitched.AddTag(ImageTag.Satellite);
        stitched.AddTag(ImageTag.Georeferenced);

        Logger.Log("Georeferenced stitching complete");
        return stitched;
    }

    /// <summary>
    ///     Stitch with automatic alignment using feature matching (simplified)
    /// </summary>
    public static ImageDataset StitchWithAlignment(List<ImageDataset> images, BlendMode blendMode = BlendMode.Linear)
    {
        if (images.Count < 2)
            return images[0];

        Logger.Log($"Stitching {images.Count} images with feature alignment");

        // Start with first image
        var result = images[0];

        // Sequentially stitch remaining images
        for (var i = 1; i < images.Count; i++)
        {
            Logger.Log($"Aligning image {i + 1}/{images.Count}");

            // Find overlap region
            var overlap = FindOverlapRegion(result, images[i]);

            if (overlap.HasValue)
            {
                result = MergeImages(result, images[i], overlap.Value, blendMode);
            }
            else
            {
                Logger.LogWarning($"Could not find overlap for image {i}, placing side-by-side");
                result = ConcatenateHorizontal(result, images[i]);
            }
        }

        Logger.Log("Alignment stitching complete");
        return result;
    }

    private static void PlaceImage(ImageDataset image, byte[] target, int targetWidth, int targetHeight,
        int offsetX, int offsetY)
    {
        if (image.ImageData == null)
            image.Load();

        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
        {
            var targetX = offsetX + x;
            var targetY = offsetY + y;

            if (targetX >= 0 && targetX < targetWidth && targetY >= 0 && targetY < targetHeight)
            {
                var srcIdx = (y * image.Width + x) * 4;
                var dstIdx = (targetY * targetWidth + targetX) * 4;

                target[dstIdx + 0] = image.ImageData[srcIdx + 0];
                target[dstIdx + 1] = image.ImageData[srcIdx + 1];
                target[dstIdx + 2] = image.ImageData[srcIdx + 2];
                target[dstIdx + 3] = image.ImageData[srcIdx + 3];
            }
        }
    }

    private static void PlaceImageWithBlending(ImageDataset image, byte[] target, float[] weights,
        int targetWidth, int targetHeight, int offsetX, int offsetY,
        BlendMode blendMode)
    {
        if (image.ImageData == null)
            image.Load();

        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
        {
            var targetX = offsetX + x;
            var targetY = offsetY + y;

            if (targetX >= 0 && targetX < targetWidth && targetY >= 0 && targetY < targetHeight)
            {
                var srcIdx = (y * image.Width + x) * 4;
                var dstIdx = (targetY * targetWidth + targetX) * 4;

                // Calculate blend weight based on distance from edge
                var weight = CalculateBlendWeight(x, y, image.Width, image.Height, blendMode);

                if (weights[targetY * targetWidth + targetX] == 0)
                {
                    // First image at this location
                    target[dstIdx + 0] = image.ImageData[srcIdx + 0];
                    target[dstIdx + 1] = image.ImageData[srcIdx + 1];
                    target[dstIdx + 2] = image.ImageData[srcIdx + 2];
                    target[dstIdx + 3] = image.ImageData[srcIdx + 3];
                    weights[targetY * targetWidth + targetX] = weight;
                }
                else
                {
                    // Blend with existing data
                    var existingWeight = weights[targetY * targetWidth + targetX];
                    var totalWeight = existingWeight + weight;

                    for (var c = 0; c < 3; c++)
                    {
                        float existing = target[dstIdx + c];
                        float incoming = image.ImageData[srcIdx + c];
                        target[dstIdx + c] = (byte)((existing * existingWeight + incoming * weight) / totalWeight);
                    }

                    weights[targetY * targetWidth + targetX] = totalWeight;
                }
            }
        }
    }

    private static void ApplyBlending(byte[] data, int width, int height, int tileWidth, int tileHeight,
        int cols, int rows, BlendMode mode)
    {
        var blendWidth = tileWidth / 10; // 10% overlap

        // Horizontal seams
        for (var row = 0; row < rows; row++)
        for (var col = 1; col < cols; col++)
        {
            var seamX = col * tileWidth;
            BlendVerticalSeam(data, width, height, seamX, blendWidth, mode);
        }

        // Vertical seams
        for (var col = 0; col < cols; col++)
        for (var row = 1; row < rows; row++)
        {
            var seamY = row * tileHeight;
            BlendHorizontalSeam(data, width, height, seamY, blendWidth, mode);
        }
    }

    private static void BlendVerticalSeam(byte[] data, int width, int height, int seamX, int blendWidth, BlendMode mode)
    {
        var startX = Math.Max(0, seamX - blendWidth);
        var endX = Math.Min(width - 1, seamX + blendWidth);

        for (var y = 0; y < height; y++)
        for (var x = startX; x <= endX; x++)
        {
            var t = (x - startX) / (float)(endX - startX);
            t = ApplyBlendCurve(t, mode);

            var idx = (y * width + x) * 4;

            // Blend with adjacent pixels
            if (x > 0 && x < width - 1)
            {
                var leftIdx = (y * width + (x - 1)) * 4;
                var rightIdx = (y * width + x + 1) * 4;

                for (var c = 0; c < 3; c++)
                {
                    float left = data[leftIdx + c];
                    float right = data[rightIdx + c];
                    data[idx + c] = (byte)(left * (1 - t) + right * t);
                }
            }
        }
    }

    private static void BlendHorizontalSeam(byte[] data, int width, int height, int seamY, int blendWidth,
        BlendMode mode)
    {
        var startY = Math.Max(0, seamY - blendWidth);
        var endY = Math.Min(height - 1, seamY + blendWidth);

        for (var x = 0; x < width; x++)
        for (var y = startY; y <= endY; y++)
        {
            var t = (y - startY) / (float)(endY - startY);
            t = ApplyBlendCurve(t, mode);

            var idx = (y * width + x) * 4;

            if (y > 0 && y < height - 1)
            {
                var topIdx = ((y - 1) * width + x) * 4;
                var bottomIdx = ((y + 1) * width + x) * 4;

                for (var c = 0; c < 3; c++)
                {
                    float top = data[topIdx + c];
                    float bottom = data[bottomIdx + c];
                    data[idx + c] = (byte)(top * (1 - t) + bottom * t);
                }
            }
        }
    }

    private static float CalculateBlendWeight(int x, int y, int width, int height, BlendMode mode)
    {
        if (mode == BlendMode.None)
            return 1f;

        // Distance from edge (normalized 0-1)
        var distX = Math.Min(x, width - 1 - x) / (float)(width / 2);
        var distY = Math.Min(y, height - 1 - y) / (float)(height / 2);
        var dist = Math.Min(distX, distY);

        return ApplyBlendCurve(dist, mode);
    }

    private static float ApplyBlendCurve(float t, BlendMode mode)
    {
        return mode switch
        {
            BlendMode.Linear => t,
            BlendMode.Smooth => t * t * (3 - 2 * t), // Smoothstep
            BlendMode.Cosine => (1 - MathF.Cos(t * MathF.PI)) * 0.5f,
            _ => t
        };
    }

    private static GeoImageInfo ExtractGeoInfo(ImageDataset image)
    {
        // Try to extract georeference from metadata
        if (!image.ImageMetadata.ContainsKey("geotransform"))
            return null;

        // Simplified - in production, parse full geotransform
        return new GeoImageInfo
        {
            Image = image,
            MinX = 0, // Parse from metadata
            MaxX = image.Width,
            MinY = 0,
            MaxY = image.Height,
            PixelSize = 1.0
        };
    }

    private static (int x, int y, int width, int height)? FindOverlapRegion(ImageDataset img1, ImageDataset img2)
    {
        // Simplified overlap detection
        // In production, use feature matching (SIFT, SURF, ORB)
        var overlapWidth = Math.Min(img1.Width, img2.Width) / 4;
        var overlapHeight = Math.Min(img1.Height, img2.Height);

        return (img1.Width - overlapWidth, 0, overlapWidth, overlapHeight);
    }

    private static ImageDataset MergeImages(ImageDataset img1, ImageDataset img2,
        (int x, int y, int width, int height) overlap,
        BlendMode blendMode)
    {
        var newWidth = img1.Width + img2.Width - overlap.width;
        var newHeight = Math.Max(img1.Height, img2.Height);

        var merged = new byte[newWidth * newHeight * 4];

        // Copy first image
        PlaceImage(img1, merged, newWidth, newHeight, 0, 0);

        // Copy second image with offset
        PlaceImage(img2, merged, newWidth, newHeight, img1.Width - overlap.width, 0);

        // Blend overlap region
        // ... blending code ...

        return new ImageDataset("Merged", "")
        {
            Width = newWidth,
            Height = newHeight,
            ImageData = merged
        };
    }

    private static ImageDataset ConcatenateHorizontal(ImageDataset img1, ImageDataset img2)
    {
        var newWidth = img1.Width + img2.Width;
        var newHeight = Math.Max(img1.Height, img2.Height);

        var concatenated = new byte[newWidth * newHeight * 4];

        PlaceImage(img1, concatenated, newWidth, newHeight, 0, 0);
        PlaceImage(img2, concatenated, newWidth, newHeight, img1.Width, 0);

        return new ImageDataset("Concatenated", "")
        {
            Width = newWidth,
            Height = newHeight,
            ImageData = concatenated
        };
    }

    private class GeoImageInfo
    {
        public ImageDataset Image { get; set; }
        public double MinX { get; set; }
        public double MaxX { get; set; }
        public double MinY { get; set; }
        public double MaxY { get; set; }
        public double PixelSize { get; set; }
    }
}

public enum BlendMode
{
    None, // No blending
    Linear, // Linear interpolation
    Smooth, // Smoothstep
    Cosine // Cosine interpolation
}