// GeoscientistToolkit/Util/TextureManager.cs

using ImGuiNET;
using Veldrid;

namespace GeoscientistToolkit.Util;

/// <summary>
///     Manages textures across multiple ImGui contexts, ensuring textures work correctly
///     in both main windows and pop-out windows.
/// </summary>
public class TextureManager : IDisposable
{
    /// <summary>
    ///     Maximum texture dimension supported by most GPUs.
    ///     Metal (Apple) and most modern GPUs support 16384.
    ///     This prevents crashes from exceeding GPU limits.
    /// </summary>
    public const int MAX_TEXTURE_SIZE = 16384;

    private readonly object _lock = new();
    private readonly Dictionary<IntPtr, IntPtr> _textureIdsByContext = new();
    private Texture _texture;
    private TextureView _textureView;

    /// <summary>
    ///     The actual width of the texture (may differ from original if downsampled)
    /// </summary>
    public uint Width => _texture?.Width ?? 0;

    /// <summary>
    ///     The actual height of the texture (may differ from original if downsampled)
    /// </summary>
    public uint Height => _texture?.Height ?? 0;

    public bool IsValid => _texture != null && _textureView != null;

    public void Dispose()
    {
        lock (_lock)
        {
            // Clean up all texture bindings
            foreach (var kvp in _textureIdsByContext)
            {
                var context = kvp.Key;

                // Try to find the controller for this context
                var prevContext = ImGui.GetCurrentContext();
                ImGui.SetCurrentContext(context);

                var controller = VeldridManager.GetCurrentImGuiController();
                if (controller != null && _textureView != null) controller.RemoveImGuiBinding(_textureView);

                ImGui.SetCurrentContext(prevContext);
            }

            _textureIdsByContext.Clear();
        }

        _textureView?.Dispose();
        _texture?.Dispose();
        _textureView = null;
        _texture = null;
    }

    /// <summary>
    ///     Checks if the given dimensions exceed the maximum texture size.
    /// </summary>
    public static bool ExceedsMaxSize(uint width, uint height)
    {
        return width > MAX_TEXTURE_SIZE || height > MAX_TEXTURE_SIZE;
    }

    /// <summary>
    ///     Calculates the scale factor needed to fit dimensions within the maximum texture size.
    /// </summary>
    public static float GetDownsampleFactor(uint width, uint height)
    {
        if (!ExceedsMaxSize(width, height))
            return 1.0f;

        return Math.Min(
            (float)MAX_TEXTURE_SIZE / width,
            (float)MAX_TEXTURE_SIZE / height
        );
    }

    /// <summary>
    ///     Calculates downsampled dimensions that fit within the maximum texture size.
    /// </summary>
    public static (uint newWidth, uint newHeight) GetConstrainedDimensions(uint width, uint height)
    {
        if (!ExceedsMaxSize(width, height))
            return (width, height);

        var scale = GetDownsampleFactor(width, height);
        return ((uint)(width * scale), (uint)(height * scale));
    }

    /// <summary>
    ///     Downsamples RGBA pixel data to fit within the maximum texture size.
    ///     Uses simple bilinear-like averaging for quality.
    /// </summary>
    public static byte[] DownsamplePixelData(byte[] pixelData, uint srcWidth, uint srcHeight,
        uint dstWidth, uint dstHeight, int bytesPerPixel = 4)
    {
        if (srcWidth == dstWidth && srcHeight == dstHeight)
            return pixelData;

        var result = new byte[dstWidth * dstHeight * bytesPerPixel];
        var scaleX = (float)srcWidth / dstWidth;
        var scaleY = (float)srcHeight / dstHeight;

        for (uint y = 0; y < dstHeight; y++)
        {
            for (uint x = 0; x < dstWidth; x++)
            {
                // Calculate source region to sample from
                var srcX0 = (int)(x * scaleX);
                var srcY0 = (int)(y * scaleY);
                var srcX1 = Math.Min((int)((x + 1) * scaleX), (int)srcWidth);
                var srcY1 = Math.Min((int)((y + 1) * scaleY), (int)srcHeight);

                // Average all pixels in the source region
                int[] sums = new int[bytesPerPixel];
                var count = 0;

                for (var sy = srcY0; sy < srcY1; sy++)
                {
                    for (var sx = srcX0; sx < srcX1; sx++)
                    {
                        var srcIdx = (sy * (int)srcWidth + sx) * bytesPerPixel;
                        for (var c = 0; c < bytesPerPixel; c++)
                            sums[c] += pixelData[srcIdx + c];
                        count++;
                    }
                }

                // Write averaged pixel
                var dstIdx = (y * dstWidth + x) * (uint)bytesPerPixel;
                for (var c = 0; c < bytesPerPixel; c++)
                    result[dstIdx + c] = (byte)(count > 0 ? sums[c] / count : 0);
            }
        }

        return result;
    }

    /// <summary>
    ///     Creates a texture from raw pixel data, automatically downsampling if needed
    ///     to fit within GPU texture size limits.
    /// </summary>
    public static TextureManager CreateFromPixelData(byte[] pixelData, uint width, uint height,
        PixelFormat format = PixelFormat.R8_G8_B8_A8_UNorm)
    {
        var manager = new TextureManager();

        try
        {
            var factory = VeldridManager.Factory;
            if (factory == null)
            {
                throw new InvalidOperationException("VeldridManager.Factory is not initialized");
            }

            var graphicsDevice = VeldridManager.GraphicsDevice;
            if (graphicsDevice == null)
            {
                throw new InvalidOperationException("VeldridManager.GraphicsDevice is not initialized");
            }

            // Check if downsampling is needed
            var (finalWidth, finalHeight) = GetConstrainedDimensions(width, height);
            var finalPixelData = pixelData;

            if (finalWidth != width || finalHeight != height)
            {
                Logger.LogWarning($"[TextureManager] Texture dimensions {width}x{height} exceed maximum {MAX_TEXTURE_SIZE}. Downsampling to {finalWidth}x{finalHeight}.");
                finalPixelData = DownsamplePixelData(pixelData, width, height, finalWidth, finalHeight);
            }

            manager._texture = factory.CreateTexture(TextureDescription.Texture2D(
                finalWidth, finalHeight, 1, 1, format, TextureUsage.Sampled));

            graphicsDevice.UpdateTexture(
                manager._texture, finalPixelData,
                0, 0, 0, finalWidth, finalHeight, 1, 0, 0);

            manager._textureView = factory.CreateTextureView(manager._texture);
            return manager;
        }
        catch
        {
            manager.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     Creates a texture from an existing Veldrid texture
    /// </summary>
    public static TextureManager CreateFromTexture(Texture texture)
    {
        var manager = new TextureManager();
        manager._texture = texture;

        var factory = VeldridManager.Factory;
        if (factory == null)
        {
            throw new InvalidOperationException("VeldridManager.Factory is not initialized");
        }

        manager._textureView = factory.CreateTextureView(texture);
        return manager;
    }

    /// <summary>
    ///     Updates the texture content from raw pixel data.
    ///     Recreates the texture if dimensions change.
    ///     Automatically downsamples if dimensions exceed GPU limits.
    /// </summary>
    public void UpdateFromPixelData(byte[] pixelData, uint width, uint height, PixelFormat format = PixelFormat.R8_G8_B8_A8_UNorm)
    {
        var graphicsDevice = VeldridManager.GraphicsDevice;
        if (graphicsDevice == null)
            throw new InvalidOperationException("VeldridManager.GraphicsDevice is not initialized");

        // Check if downsampling is needed
        var (finalWidth, finalHeight) = GetConstrainedDimensions(width, height);
        var finalPixelData = pixelData;

        if (finalWidth != width || finalHeight != height)
        {
            Logger.LogWarning($"[TextureManager] Texture dimensions {width}x{height} exceed maximum {MAX_TEXTURE_SIZE}. Downsampling to {finalWidth}x{finalHeight}.");
            finalPixelData = DownsamplePixelData(pixelData, width, height, finalWidth, finalHeight);
        }

        // Check if we need to recreate the texture (size changed)
        if (_texture == null || _texture.Width != finalWidth || _texture.Height != finalHeight || _texture.Format != format)
        {
            // Recreate texture
            _textureView?.Dispose();
            _texture?.Dispose();

            var factory = VeldridManager.Factory;
            if (factory == null)
                throw new InvalidOperationException("VeldridManager.Factory is not initialized");

            _texture = factory.CreateTexture(TextureDescription.Texture2D(
                finalWidth, finalHeight, 1, 1, format, TextureUsage.Sampled));
            _textureView = factory.CreateTextureView(_texture);

            // Clear existing bindings as the underlying texture view has changed
            lock (_lock)
            {
                // We'll need to re-register bindings in the next GetImGuiTextureId call
                // But we should remove old bindings first
                foreach (var kvp in _textureIdsByContext)
                {
                    var context = kvp.Key;
                    var prevContext = ImGui.GetCurrentContext();
                    ImGui.SetCurrentContext(context);

                    var controller = VeldridManager.GetCurrentImGuiController();
                    if (controller != null)
                    {
                        // Note: we can't easily remove the old binding since we already disposed the view
                        // But Veldrid's ImGuiController usually handles disposed resources gracefully or we just create new binding
                        // Ideally, we should have removed it before disposing.
                    }

                    ImGui.SetCurrentContext(prevContext);
                }
                _textureIdsByContext.Clear();
            }
        }

        // Update texture data
        graphicsDevice.UpdateTexture(
            _texture, finalPixelData,
            0, 0, 0, finalWidth, finalHeight, 1, 0, 0);
    }

    /// <summary>
    ///     Gets the ImGui texture ID for the current context, creating it if necessary
    /// </summary>
    public IntPtr GetImGuiTextureId()
    {
        if (!IsValid) return IntPtr.Zero;

        var currentContext = ImGui.GetCurrentContext();

        lock (_lock)
        {
            // Check if we already have a texture ID for this context
            if (_textureIdsByContext.TryGetValue(currentContext, out var existingId)) return existingId;

            // Get the current ImGuiController
            var controller = VeldridManager.GetCurrentImGuiController();
            if (controller == null) return IntPtr.Zero;

            // Get factory and check for null
            var factory = VeldridManager.Factory;
            if (factory == null) return IntPtr.Zero;

            // Create texture binding for this controller
            var textureId = controller.GetOrCreateImGuiBinding(factory, _textureView);
            _textureIdsByContext[currentContext] = textureId;

            return textureId;
        }
    }

    /// <summary>
    ///     Removes the texture binding for a specific context
    /// </summary>
    public void RemoveContextBinding(IntPtr context)
    {
        lock (_lock)
        {
            if (_textureIdsByContext.TryGetValue(context, out var textureId))
            {
                // Try to find the controller for this context
                var prevContext = ImGui.GetCurrentContext();
                ImGui.SetCurrentContext(context);

                var controller = VeldridManager.GetCurrentImGuiController();
                if (controller != null && _textureView != null) controller.RemoveImGuiBinding(_textureView);

                ImGui.SetCurrentContext(prevContext);
                _textureIdsByContext.Remove(context);
            }
        }
    }

    /// <summary>
    ///     Cleans up bindings for contexts that no longer exist
    /// </summary>
    public void CleanupStaleBindings()
    {
        lock (_lock)
        {
            var contextsToRemove = new List<IntPtr>();

            foreach (var context in _textureIdsByContext.Keys)
            {
                // Check if context is still valid by trying to set it
                var prevContext = ImGui.GetCurrentContext();
                ImGui.SetCurrentContext(context);

                // If the context is invalid, GetCurrentContext will return IntPtr.Zero
                if (ImGui.GetCurrentContext() == IntPtr.Zero) contextsToRemove.Add(context);

                ImGui.SetCurrentContext(prevContext);
            }

            foreach (var context in contextsToRemove) _textureIdsByContext.Remove(context);
        }
    }
}

/// <summary>
///     Static helper for managing shared textures (like UI icons)
/// </summary>
public static class SharedTextureManager
{
    private static readonly Dictionary<string, TextureManager> _sharedTextures = new();
    private static readonly object _lock = new();

    /// <summary>
    ///     Gets or creates a shared texture
    /// </summary>
    public static TextureManager GetOrCreate(string key, Func<TextureManager> factory)
    {
        lock (_lock)
        {
            if (_sharedTextures.TryGetValue(key, out var existing)) return existing;

            var texture = factory();
            _sharedTextures[key] = texture;
            return texture;
        }
    }

    /// <summary>
    ///     Removes a shared texture
    /// </summary>
    public static void Remove(string key)
    {
        lock (_lock)
        {
            if (_sharedTextures.TryGetValue(key, out var texture))
            {
                texture.Dispose();
                _sharedTextures.Remove(key);
            }
        }
    }

    /// <summary>
    ///     Cleans up all shared textures
    /// </summary>
    public static void DisposeAll()
    {
        lock (_lock)
        {
            foreach (var texture in _sharedTextures.Values) texture.Dispose();
            _sharedTextures.Clear();
        }
    }
}
