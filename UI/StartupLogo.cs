// GAIA/UI/StartupLogo.cs

using System.Numerics;
using System.Reflection;
using GAIA.Util;
using ImGuiNET;
using OpenTK.Graphics.OpenGL;
using StbImageSharp;

namespace GAIA.UI;

/// <summary>
///     The GAIA logo drawn by the splash and loading screens. Both show the same image, so it is
///     uploaded once and released when startup finishes; the screens borrow it and never own it.
///     Callers do their own layout via <see cref="Measure" /> and <see cref="DrawAt" />, since the
///     two screens size and place the logo differently.
/// </summary>
public sealed class StartupLogo : IDisposable
{
    private const string ResourceName = "GAIA.image.png";

    private bool _disposed;

    public StartupLogo()
    {
        Load();
    }

    public IntPtr TextureId { get; private set; }
    public Vector2 Size { get; private set; }
    public bool IsLoaded => TextureId != IntPtr.Zero;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (TextureId != IntPtr.Zero)
            {
                GL.DeleteTexture((int)TextureId);
                TextureId = IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Could not dispose startup logo texture: {ex.Message}");
        }
    }

    private void Load()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();

            using var stream = assembly.GetManifestResourceStream(ResourceName);
            if (stream == null)
            {
                Logger.LogWarning($"Could not find embedded resource '{ResourceName}'");
                return;
            }

            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

            var texture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                image.Width, image.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, image.Data);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                (int)TextureMagFilter.Linear);
            GL.BindTexture(TextureTarget.Texture2D, 0);

            TextureId = (IntPtr)texture;
            Size = new Vector2(image.Width, image.Height);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error loading startup logo: {ex.Message}");
        }
    }

    /// <summary>
    ///     The size the logo would occupy when scaled to fit a square of <paramref name="maxExtent" />,
    ///     or <see cref="Vector2.Zero" /> if the logo is unavailable and callers should skip it.
    /// </summary>
    public Vector2 Measure(float maxExtent)
    {
        if (!IsLoaded || maxExtent <= 0f) return Vector2.Zero;

        var scale = Math.Min(maxExtent / Size.X, maxExtent / Size.Y);
        return Size * scale;
    }

    public void DrawAt(Vector2 position, Vector2 displaySize)
    {
        if (!IsLoaded || displaySize == Vector2.Zero) return;

        ImGui.SetCursorPos(position);
        ImGui.Image(TextureId, displaySize);
    }
}
