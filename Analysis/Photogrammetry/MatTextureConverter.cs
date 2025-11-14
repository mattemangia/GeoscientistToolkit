// GeoscientistToolkit/Analysis/Photogrammetry/MatTextureConverter.cs

using OpenCvSharp;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.Photogrammetry;

/// <summary>
/// Converts OpenCV Mat images to GPU textures for real-time display.
/// </summary>
public static class MatTextureConverter
{
    /// <summary>
    /// Converts an OpenCV Mat to a TextureManager for display with ImGui.
    /// </summary>
    /// <param name="mat">The input Mat (BGR or grayscale)</param>
    /// <returns>A TextureManager ready for ImGui.Image(), or null if conversion failed</returns>
    public static TextureManager ConvertToTexture(Mat mat)
    {
        if (mat == null || mat.Empty())
            return null;

        try
        {
            // Convert to RGBA format (required for texture upload)
            byte[] pixelData;
            uint width = (uint)mat.Width;
            uint height = (uint)mat.Height;

            if (mat.Channels() == 1)
            {
                // Grayscale to RGBA
                pixelData = ConvertGrayscaleToRGBA(mat);
            }
            else if (mat.Channels() == 3)
            {
                // BGR to RGBA
                pixelData = ConvertBGRToRGBA(mat);
            }
            else if (mat.Channels() == 4)
            {
                // Already BGRA, convert to RGBA
                pixelData = ConvertBGRAToRGBA(mat);
            }
            else
            {
                Logger.LogError($"Unsupported Mat format: {mat.Channels()} channels");
                return null;
            }

            // Create texture from pixel data
            return TextureManager.CreateFromPixelData(pixelData, width, height);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to convert Mat to texture: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Updates an existing texture with new Mat data.
    /// </summary>
    /// <param name="texture">Existing texture to update</param>
    /// <param name="mat">New Mat data</param>
    /// <returns>New texture (old one is disposed)</returns>
    public static TextureManager UpdateTexture(TextureManager texture, Mat mat)
    {
        // Dispose old texture
        texture?.Dispose();

        // Create new texture
        return ConvertToTexture(mat);
    }

    private static byte[] ConvertGrayscaleToRGBA(Mat mat)
    {
        var width = mat.Width;
        var height = mat.Height;
        var pixelData = new byte[width * height * 4];

        unsafe
        {
            var src = (byte*)mat.DataPointer;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int srcIdx = (int)(y * mat.Step() + x);
                    int dstIdx = (y * width + x) * 4;

                    byte gray = src[srcIdx];
                    pixelData[dstIdx + 0] = gray; // R
                    pixelData[dstIdx + 1] = gray; // G
                    pixelData[dstIdx + 2] = gray; // B
                    pixelData[dstIdx + 3] = 255;  // A
                }
            }
        }

        return pixelData;
    }

    private static byte[] ConvertBGRToRGBA(Mat mat)
    {
        var width = mat.Width;
        var height = mat.Height;
        var pixelData = new byte[width * height * 4];

        unsafe
        {
            var src = (byte*)mat.DataPointer;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int srcIdx = (int)(y * mat.Step() + x * 3);
                    int dstIdx = (y * width + x) * 4;

                    pixelData[dstIdx + 0] = src[srcIdx + 2]; // R (from B)
                    pixelData[dstIdx + 1] = src[srcIdx + 1]; // G
                    pixelData[dstIdx + 2] = src[srcIdx + 0]; // B (from R)
                    pixelData[dstIdx + 3] = 255;             // A
                }
            }
        }

        return pixelData;
    }

    private static byte[] ConvertBGRAToRGBA(Mat mat)
    {
        var width = mat.Width;
        var height = mat.Height;
        var pixelData = new byte[width * height * 4];

        unsafe
        {
            var src = (byte*)mat.DataPointer;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int srcIdx = (int)(y * mat.Step() + x * 4);
                    int dstIdx = (y * width + x) * 4;

                    pixelData[dstIdx + 0] = src[srcIdx + 2]; // R (from B)
                    pixelData[dstIdx + 1] = src[srcIdx + 1]; // G
                    pixelData[dstIdx + 2] = src[srcIdx + 0]; // B (from R)
                    pixelData[dstIdx + 3] = src[srcIdx + 3]; // A
                }
            }
        }

        return pixelData;
    }

    /// <summary>
    /// Converts a depth map (CV_32F) to a colored visualization texture.
    /// </summary>
    /// <param name="depthMap">Depth map (single channel float)</param>
    /// <returns>Texture with colorized depth</returns>
    public static TextureManager ConvertDepthToTexture(Mat depthMap)
    {
        if (depthMap == null || depthMap.Empty())
            return null;

        try
        {
            // Normalize depth to 0-255 range
            Mat normalized = new Mat();
            Cv2.Normalize(depthMap, normalized, 0, 255, NormTypes.MinMax);
            normalized.ConvertTo(normalized, MatType.CV_8UC1);

            // Apply colormap for better visualization
            Mat colored = new Mat();
            Cv2.ApplyColorMap(normalized, colored, ColormapTypes.Inferno);

            // Convert to texture
            var texture = ConvertToTexture(colored);

            // Cleanup
            normalized.Dispose();
            colored.Dispose();

            return texture;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to convert depth map to texture: {ex.Message}");
            return null;
        }
    }
}
