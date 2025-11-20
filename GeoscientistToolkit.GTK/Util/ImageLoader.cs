// GeoscientistToolkit/Util/ImageLoader.cs
// STUB/SIMPLIFIED for GTK

using System;

namespace GeoscientistToolkit.Util;

public class ImageInfo
{
    public int Width { get; set; }
    public int Height { get; set; }
}

public static class ImageLoader
{
    public static ImageInfo LoadImageInfo(string path)
    {
        // Stub implementation using OpenCV or minimal reader
        // For now, return dummy or implement basic reading
        return new ImageInfo { Width = 100, Height = 100 };
    }

    public static byte[] LoadGrayscaleImage(string path, out int width, out int height)
    {
         width = 100;
         height = 100;
         return new byte[10000];
    }
}
