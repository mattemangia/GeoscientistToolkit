using System;
using System.IO;
using Gdk;

namespace GeoscientistToolkit.GtkUI;

internal static class GtkResourceLoader
{
    private const string LogoResourceName = "GeoscientistToolkit.image.png";

    public static Pixbuf LoadLogoPixbuf(int? width = null, int? height = null)
    {
        var coreAssembly = typeof(global::GeoscientistToolkit.Application).Assembly;
        using var stream = coreAssembly.GetManifestResourceStream(LogoResourceName);

        if (stream != null)
        {
            var pixbuf = new Pixbuf(stream);
            return ScalePreservingAspect(pixbuf, width, height);
        }

        if (File.Exists("image.png"))
        {
            var pixbuf = new Pixbuf("image.png");
            return ScalePreservingAspect(pixbuf, width, height);
        }

        throw new FileNotFoundException(
            "Unable to find the embedded GTK logo resource or fallback image.png file.");
    }

    private static Pixbuf ScalePreservingAspect(Pixbuf pixbuf, int? maxWidth, int? maxHeight)
    {
        if (!maxWidth.HasValue && !maxHeight.HasValue)
            return pixbuf;

        double widthScale = maxWidth.HasValue ? maxWidth.Value / (double)pixbuf.Width : double.PositiveInfinity;
        double heightScale = maxHeight.HasValue ? maxHeight.Value / (double)pixbuf.Height : double.PositiveInfinity;
        var scale = Math.Min(widthScale, heightScale);

        if (double.IsPositiveInfinity(scale) || Math.Abs(scale - 1.0) < 0.001)
            return pixbuf;

        var scaledWidth = Math.Max(1, (int)Math.Round(pixbuf.Width * scale));
        var scaledHeight = Math.Max(1, (int)Math.Round(pixbuf.Height * scale));
        return pixbuf.ScaleSimple(scaledWidth, scaledHeight, InterpType.Bilinear);
    }
}
