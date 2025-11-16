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
    private readonly object _lock = new();
    private readonly Dictionary<IntPtr, IntPtr> _textureIdsByContext = new();
    private Texture _texture;
    private TextureView _textureView;

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
    ///     Creates a texture from raw pixel data
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

            manager._texture = factory.CreateTexture(TextureDescription.Texture2D(
                width, height, 1, 1, format, TextureUsage.Sampled));

            graphicsDevice.UpdateTexture(
                manager._texture, pixelData,
                0, 0, 0, width, height, 1, 0, 0);

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