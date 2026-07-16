using OpenTK.Graphics.OpenGL;

namespace GAIA.Util;

/// <summary>
/// Owns an OpenGL texture whose numeric name is used directly as ImGui's TextureId.
/// Shared OpenTK contexts see the same object, so no per-window binding registry is needed.
/// </summary>
public sealed class OpenTkTextureManager : IDisposable
{
    public const int MaxTextureSizeFallback = 16384;

    private bool _ownsTexture;
    private bool _disposed;

    private OpenTkTextureManager(int textureId, int width, int height, bool ownsTexture)
    {
        TextureId = textureId;
        Width = width;
        Height = height;
        _ownsTexture = ownsTexture;
    }

    public int TextureId { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool IsValid => !_disposed && TextureId != 0;
    public IntPtr ImGuiTextureId => (IntPtr)TextureId;

    public static OpenTkTextureManager CreateFromRgba(byte[] pixels, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ValidateDimensions(width, height, pixels.LongLength);
        var texture = GL.GenTexture();
        try
        {
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, width, height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            ConfigureSampling();
            return new OpenTkTextureManager(texture, width, height, true);
        }
        catch
        {
            GL.DeleteTexture(texture);
            throw;
        }
        finally
        {
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }
    }

    public static OpenTkTextureManager Wrap(int textureId, int width, int height, bool takeOwnership = false)
    {
        if (textureId <= 0) throw new ArgumentOutOfRangeException(nameof(textureId));
        return new OpenTkTextureManager(textureId, width, height, takeOwnership);
    }

    public void UpdateRgba(byte[] pixels, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(pixels);
        ValidateDimensions(width, height, pixels.LongLength);
        GL.BindTexture(TextureTarget.Texture2D, TextureId);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        if (width != Width || height != Height)
        {
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, width, height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            Width = width;
            Height = height;
        }
        else
        {
            GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, width, height,
                PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        }
        ConfigureSampling();
        GL.BindTexture(TextureTarget.Texture2D, 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsTexture && TextureId != 0) GL.DeleteTexture(TextureId);
        TextureId = 0;
        Width = 0;
        Height = 0;
        _ownsTexture = false;
    }

    private static void ConfigureSampling()
    {
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
            (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
            (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
            (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
            (int)TextureWrapMode.ClampToEdge);
    }

    private static void ValidateDimensions(int width, int height, long byteCount)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var max = GL.GetInteger(GetPName.MaxTextureSize);
        if (max <= 0) max = MaxTextureSizeFallback;
        if (width > max || height > max)
            throw new NotSupportedException($"Texture {width}×{height} exceeds GL_MAX_TEXTURE_SIZE={max}.");
        if ((long)width * height * 4 > byteCount)
            throw new ArgumentException("RGBA buffer is smaller than the requested texture dimensions.");
    }
}
