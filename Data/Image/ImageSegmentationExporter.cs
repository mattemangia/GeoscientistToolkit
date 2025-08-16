// GeoscientistToolkit/Data/Image/ImageSegmentationExporter.cs
using System;
using System.IO;
using System.Numerics;
using GeoscientistToolkit.Util;
using SkiaSharp;
using BitMiracle.LibTiff.Classic;

namespace GeoscientistToolkit.Data.Image
{
    public static class ImageSegmentationExporter
    {
        /// <summary>
        /// Export segmentation labels as a colored image
        /// </summary>
        public static void ExportLabeledImage(ImageSegmentationData segmentation, string outputPath)
        {
            try
            {
                Logger.Log($"[ImageSegmentationExporter] Exporting labeled image to: {outputPath}");
                
                // Create RGBA image from labels
                byte[] rgbaData = new byte[segmentation.Width * segmentation.Height * 4];
                
                for (int i = 0; i < segmentation.LabelData.Length; i++)
                {
                    byte labelId = segmentation.LabelData[i];
                    var material = segmentation.GetMaterial(labelId);
                    
                    if (material != null)
                    {
                        int pixelIdx = i * 4;
                        rgbaData[pixelIdx] = (byte)(material.Color.X * 255);     // R
                        rgbaData[pixelIdx + 1] = (byte)(material.Color.Y * 255); // G
                        rgbaData[pixelIdx + 2] = (byte)(material.Color.Z * 255); // B
                        rgbaData[pixelIdx + 3] = (byte)(material.Color.W * 255); // A
                    }
                    else
                    {
                        // Default to black for unknown labels
                        int pixelIdx = i * 4;
                        rgbaData[pixelIdx] = 0;
                        rgbaData[pixelIdx + 1] = 0;
                        rgbaData[pixelIdx + 2] = 0;
                        rgbaData[pixelIdx + 3] = 255;
                    }
                }
                
                // Save based on extension
                string ext = Path.GetExtension(outputPath).ToLowerInvariant();
                
                if (ext == ".tif" || ext == ".tiff")
                {
                    SaveAsTiff(rgbaData, segmentation.Width, segmentation.Height, outputPath);
                }
                else
                {
                    SaveWithSkia(rgbaData, segmentation.Width, segmentation.Height, outputPath, ext);
                }
                
                // Also save material definitions
                SaveMaterialDefinitions(segmentation, Path.ChangeExtension(outputPath, ".materials.json"));
                
                Logger.Log($"[ImageSegmentationExporter] Successfully exported labeled image");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ImageSegmentationExporter] Failed to export: {ex.Message}");
                throw;
            }
        }

        private static void SaveAsTiff(byte[] rgbaData, int width, int height, string outputPath)
        {
            using (Tiff tiff = Tiff.Open(outputPath, "w"))
            {
                tiff.SetField(TiffTag.IMAGEWIDTH, width);
                tiff.SetField(TiffTag.IMAGELENGTH, height);
                tiff.SetField(TiffTag.SAMPLESPERPIXEL, 4);
                tiff.SetField(TiffTag.BITSPERSAMPLE, 8);
                tiff.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);
                tiff.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
                tiff.SetField(TiffTag.PHOTOMETRIC, Photometric.RGB);
                tiff.SetField(TiffTag.EXTRASAMPLES, 1, new short[] { (short)ExtraSample.ASSOCALPHA });
                
                int rowBytes = width * 4;
                byte[] scanline = new byte[rowBytes];
                
                for (int y = 0; y < height; y++)
                {
                    Buffer.BlockCopy(rgbaData, y * rowBytes, scanline, 0, rowBytes);
                    tiff.WriteScanline(scanline, y);
                }
            }
        }

        private static void SaveWithSkia(byte[] rgbaData, int width, int height, string outputPath, string ext)
        {
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            
            using (var bitmap = new SKBitmap())
            {
                var gcHandle = System.Runtime.InteropServices.GCHandle.Alloc(rgbaData, System.Runtime.InteropServices.GCHandleType.Pinned);
                try
                {
                    bitmap.InstallPixels(info, gcHandle.AddrOfPinnedObject(), info.RowBytes,
                        (addr, ctx) => ((System.Runtime.InteropServices.GCHandle)ctx).Free(), gcHandle);
                    
                    using (var image = SKImage.FromBitmap(bitmap))
                    {
                        SKEncodedImageFormat format = ext switch
                        {
                            ".png" => SKEncodedImageFormat.Png,
                            ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
                            ".bmp" => SKEncodedImageFormat.Bmp,
                            _ => SKEncodedImageFormat.Png
                        };
                        
                        using (var stream = File.OpenWrite(outputPath))
                        {
                            image.Encode(format, 100).SaveTo(stream);
                        }
                    }
                }
                catch
                {
                    gcHandle.Free();
                    throw;
                }
            }
        }

        private static void SaveMaterialDefinitions(ImageSegmentationData segmentation, string path)
        {
            using (var writer = new StreamWriter(path))
            {
                writer.WriteLine("{");
                writer.WriteLine("  \"materials\": [");
                
                for (int i = 0; i < segmentation.Materials.Count; i++)
                {
                    var mat = segmentation.Materials[i];
                    writer.WriteLine($"    {{");
                    writer.WriteLine($"      \"id\": {mat.ID},");
                    writer.WriteLine($"      \"name\": \"{mat.Name}\",");
                    writer.WriteLine($"      \"color\": [{mat.Color.X}, {mat.Color.Y}, {mat.Color.Z}, {mat.Color.W}]");
                    writer.Write("    }");
                    if (i < segmentation.Materials.Count - 1) writer.Write(",");
                    writer.WriteLine();
                }
                
                writer.WriteLine("  ]");
                writer.WriteLine("}");
            }
        }

        /// <summary>
        /// Import segmentation from a labeled image
        /// </summary>
        public static ImageSegmentationData ImportLabeledImage(string imagePath, int targetWidth, int targetHeight)
        {
            try
            {
                Logger.Log($"[ImageSegmentationExporter] Importing labeled image from: {imagePath}");
                
                // Load the image
                byte[] colorData;
                int width, height, channels;
                colorData = ImageLoader.LoadColorImage(imagePath, out width, out height, out channels);
                
                if (colorData == null || colorData.Length == 0)
                {
                    Logger.LogError("[ImageSegmentationExporter] Failed to load image data");
                    return null;
                }
                
                // Create segmentation data
                var segmentation = new ImageSegmentationData(targetWidth, targetHeight);
                
                // Map colors to materials
                var colorToMaterial = new Dictionary<uint, byte>();
                byte nextMaterialId = 1;
                
                // Always map black to exterior
                colorToMaterial[0xFF000000] = 0;
                
                // First pass: find unique colors and create materials
                var uniqueColors = new HashSet<uint>();
                for (int i = 0; i < width * height; i++)
                {
                    int pixelIdx = i * 4;
                    uint color = ((uint)colorData[pixelIdx + 3] << 24) |  // A
                                ((uint)colorData[pixelIdx] << 16) |      // R
                                ((uint)colorData[pixelIdx + 1] << 8) |   // G
                                colorData[pixelIdx + 2];                  // B
                    
                    if (!uniqueColors.Contains(color) && color != 0xFF000000)
                    {
                        uniqueColors.Add(color);
                        
                        // Create material for this color
                        var matColor = new Vector4(
                            colorData[pixelIdx] / 255f,
                            colorData[pixelIdx + 1] / 255f,
                            colorData[pixelIdx + 2] / 255f,
                            colorData[pixelIdx + 3] / 255f
                        );
                        
                        var material = segmentation.AddMaterial($"Material_{nextMaterialId}", matColor);
                        colorToMaterial[color] = material.ID;
                        nextMaterialId++;
                    }
                }
                
                // Second pass: assign labels (with potential resizing)
                if (width == targetWidth && height == targetHeight)
                {
                    // Direct copy
                    for (int i = 0; i < width * height; i++)
                    {
                        int pixelIdx = i * 4;
                        uint color = ((uint)colorData[pixelIdx + 3] << 24) |
                                    ((uint)colorData[pixelIdx] << 16) |
                                    ((uint)colorData[pixelIdx + 1] << 8) |
                                    colorData[pixelIdx + 2];
                        
                        segmentation.LabelData[i] = colorToMaterial.GetValueOrDefault(color, (byte)0);
                    }
                }
                else
                {
                    // Nearest neighbor resize
                    float scaleX = (float)width / targetWidth;
                    float scaleY = (float)height / targetHeight;
                    
                    for (int y = 0; y < targetHeight; y++)
                    {
                        for (int x = 0; x < targetWidth; x++)
                        {
                            int srcX = (int)(x * scaleX);
                            int srcY = (int)(y * scaleY);
                            srcX = Math.Min(srcX, width - 1);
                            srcY = Math.Min(srcY, height - 1);
                            
                            int srcIdx = (srcY * width + srcX) * 4;
                            uint color = ((uint)colorData[srcIdx + 3] << 24) |
                                        ((uint)colorData[srcIdx] << 16) |
                                        ((uint)colorData[srcIdx + 1] << 8) |
                                        colorData[srcIdx + 2];
                            
                            int dstIdx = y * targetWidth + x;
                            segmentation.LabelData[dstIdx] = colorToMaterial.GetValueOrDefault(color, (byte)0);
                        }
                    }
                }
                
                // Try to load material definitions if they exist
                string materialsPath = Path.ChangeExtension(imagePath, ".materials.json");
                if (File.Exists(materialsPath))
                {
                    LoadMaterialDefinitions(segmentation, materialsPath);
                }
                
                Logger.Log($"[ImageSegmentationExporter] Successfully imported labeled image with {segmentation.Materials.Count} materials");
                return segmentation;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ImageSegmentationExporter] Failed to import: {ex.Message}");
                return null;
            }
        }

        private static void LoadMaterialDefinitions(ImageSegmentationData segmentation, string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                // Simple JSON parsing (in production, use a proper JSON library)
                // This is a simplified version - you'd want to use Newtonsoft.Json or System.Text.Json
                Logger.Log("[ImageSegmentationExporter] Material definitions file found, but JSON parsing not implemented in this example");
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[ImageSegmentationExporter] Could not load material definitions: {ex.Message}");
            }
        }
    }
}