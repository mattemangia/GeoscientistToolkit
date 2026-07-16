namespace GAIA.Util;

/// <summary>OpenGL texture owner. Numeric texture names are valid in every shared OpenTK window context.</summary>
public sealed class TextureManager : IDisposable
{
    public const int MAX_TEXTURE_SIZE = OpenTkTextureManager.MaxTextureSizeFallback;
    private OpenTkTextureManager _texture;
    public uint Width => (uint)(_texture?.Width ?? 0);
    public uint Height => (uint)(_texture?.Height ?? 0);
    public bool IsValid => _texture?.IsValid == true;

    public static TextureManager CreateFromPixelData(byte[] rgba, uint width, uint height)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        var (finalWidth, finalHeight) = GetConstrainedDimensions(width, height);
        var pixels = finalWidth == width && finalHeight == height
            ? rgba : DownsamplePixelData(rgba, width, height, finalWidth, finalHeight);
        return new TextureManager
        {
            _texture = OpenTkTextureManager.CreateFromRgba(pixels, checked((int)finalWidth), checked((int)finalHeight))
        };
    }

    public static TextureManager WrapOpenGlTexture(int textureId, int width, int height,
        bool takeOwnership = false) => new()
    {
        _texture = OpenTkTextureManager.Wrap(textureId, width, height, takeOwnership)
    };

    public void UpdateFromPixelData(byte[] rgba, uint width, uint height)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        var (finalWidth, finalHeight) = GetConstrainedDimensions(width, height);
        var pixels = finalWidth == width && finalHeight == height
            ? rgba : DownsamplePixelData(rgba, width, height, finalWidth, finalHeight);
        if (_texture == null)
            _texture = OpenTkTextureManager.CreateFromRgba(pixels, checked((int)finalWidth), checked((int)finalHeight));
        else
            _texture.UpdateRgba(pixels, checked((int)finalWidth), checked((int)finalHeight));
    }

    public IntPtr GetImGuiTextureId() => _texture?.ImGuiTextureId ?? IntPtr.Zero;
    public void RemoveContextBinding(IntPtr context) { }
    public void CleanupStaleBindings() { }
    public void Dispose() { _texture?.Dispose(); _texture = null; }

    public static bool ExceedsMaxSize(uint width, uint height) => width > CurrentLimit || height > CurrentLimit;
    public static float GetDownsampleFactor(uint width, uint height) => ExceedsMaxSize(width, height)
        ? Math.Min((float)CurrentLimit / width, (float)CurrentLimit / height) : 1f;
    public static (uint newWidth, uint newHeight) GetConstrainedDimensions(uint width, uint height)
    {
        if (width == 0 || height == 0) throw new ArgumentOutOfRangeException(nameof(width));
        var factor = GetDownsampleFactor(width, height);
        return factor >= 1 ? (width, height) : (Math.Max(1, (uint)(width * factor)), Math.Max(1, (uint)(height * factor)));
    }

    private static int CurrentLimit
    {
        get
        {
            if (!OpenTkManager.IsInitialized) return MAX_TEXTURE_SIZE;
            var limit = OpenTK.Graphics.OpenGL.GL.GetInteger(OpenTK.Graphics.OpenGL.GetPName.MaxTextureSize);
            return limit > 0 ? limit : MAX_TEXTURE_SIZE;
        }
    }

    public static byte[] DownsamplePixelData(byte[] source, uint sourceWidth, uint sourceHeight,
        uint targetWidth, uint targetHeight, int bytesPerPixel = 4)
    {
        if (sourceWidth == targetWidth && sourceHeight == targetHeight) return source;
        var target = new byte[checked((int)(targetWidth * targetHeight * (uint)bytesPerPixel))];
        var scaleX = sourceWidth / (double)targetWidth; var scaleY = sourceHeight / (double)targetHeight;
        for (var y=0;y<(int)targetHeight;y++) for(var x=0;x<(int)targetWidth;x++)
        {
            var sx=Math.Min((int)sourceWidth-1,(int)((x+.5)*scaleX));
            var sy=Math.Min((int)sourceHeight-1,(int)((y+.5)*scaleY));
            System.Buffer.BlockCopy(source,(sy*(int)sourceWidth+sx)*bytesPerPixel,target,
                (y*(int)targetWidth+x)*bytesPerPixel,bytesPerPixel);
        }
        return target;
    }
}

public static class SharedTextureManager
{
    private static readonly Dictionary<string, TextureManager> Shared = new();
    private static readonly object Gate = new();
    public static TextureManager GetOrCreate(string key, Func<TextureManager> factory)
    {
        lock(Gate){if(Shared.TryGetValue(key,out var existing))return existing;var value=factory();Shared[key]=value;return value;}
    }
    public static void Remove(string key){lock(Gate){if(!Shared.Remove(key,out var texture))return;texture.Dispose();}}
    public static void DisposeAll(){lock(Gate){foreach(var texture in Shared.Values)texture.Dispose();Shared.Clear();}}
}
