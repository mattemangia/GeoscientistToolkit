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
            return width.HasValue && height.HasValue
                ? pixbuf.ScaleSimple(width.Value, height.Value, InterpType.Bilinear)
                : pixbuf;
        }

        if (File.Exists("image.png"))
        {
            var pixbuf = new Pixbuf("image.png");
            return width.HasValue && height.HasValue
                ? pixbuf.ScaleSimple(width.Value, height.Value, InterpType.Bilinear)
                : pixbuf;
        }

        throw new FileNotFoundException(
            "Unable to find the embedded GTK logo resource or fallback image.png file.");
    }
}
