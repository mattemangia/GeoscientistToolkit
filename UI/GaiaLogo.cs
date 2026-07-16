// GAIA/UI/GaiaLogo.cs

using System.Reflection;
using GAIA.Util;
using OpenTK.Graphics.OpenGL;
using StbImageSharp;

namespace GAIA.UI;

/// <summary>
///     Owns the GAIA mark used by panel headers and the About window: one upload, reused
///     everywhere. Texture names are valid in every shared OpenTK context, so the same id
///     serves the main window and every pop-out.
/// </summary>
internal static class GaiaLogo
{
    private const string ResourceName = "GAIA.UI.Resources.gaia-mark.png";

    private static int _texture;
    private static float _aspect;
    private static bool _loadAttempted;

    /// <summary>Width divided by height of the mark, or 0 when it could not be loaded.</summary>
    public static float Aspect
    {
        get
        {
            Load();
            return _aspect;
        }
    }

    /// <summary>ImGui texture id for the mark, or <see cref="IntPtr.Zero"/> when unavailable.</summary>
    public static IntPtr TextureId
    {
        get
        {
            Load();
            return (IntPtr)_texture;
        }
    }

    /// <summary>Must run on the render thread; the GL context is current only there.</summary>
    private static void Load()
    {
        if (_loadAttempted) return;
        _loadAttempted = true;

        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream == null)
            {
                Logger.LogWarning($"[GaiaLogo] Embedded resource '{ResourceName}' not found.");
                return;
            }

            var image = ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            _aspect = image.Width / (float)image.Height;

            _texture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, _texture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, image.Width, image.Height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, image.Data);
            // The mark is authored larger than anything draws it. Plain linear minification samples
            // only 2x2 texels and drops most of the image, which is what makes a shrunken logo look
            // ragged and sparkly; a mipmap chain filters it properly at any size.
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                (int)TextureWrapMode.ClampToEdge);
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[GaiaLogo] Could not load the GAIA mark: {ex.Message}");
            _texture = 0;
            _aspect = 0;
        }
    }
}
