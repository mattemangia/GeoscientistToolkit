// GeoscientistToolkit/UI/ExampleTexturePanel.cs
// Example showing how to use TextureManager in any panel

using System;
using System.Numerics;
using GeoscientistToolkit.Util;
using ImGuiNET;
using Veldrid;

namespace GeoscientistToolkit.UI
{
    /// <summary>
    /// Example panel showing how to use TextureManager for displaying textures
    /// that work correctly in both main and pop-out windows.
    /// </summary>
    public class ExampleTexturePanel : BasePanel
    {
        private TextureManager _myTexture;
        private TextureManager _sharedIcon;
        
        public ExampleTexturePanel() : base("Example Texture Panel", new Vector2(400, 300))
        {
            // Example 1: Create a texture from pixel data
            CreateSampleTexture();
            
            // Example 2: Use a shared texture (e.g., for icons)
            _sharedIcon = SharedTextureManager.GetOrCreate("example_icon", () =>
            {
                // Create a simple 32x32 icon
                byte[] iconData = CreateIconData(32, 32);
                return TextureManager.CreateFromPixelData(iconData, 32, 32);
            });
        }
        
        protected override void DrawContent()
        {
            ImGui.Text("This panel demonstrates texture usage across contexts.");
            ImGui.Separator();
            
            // Example 1: Display the custom texture
            if (_myTexture != null && _myTexture.IsValid)
            {
                var textureId = _myTexture.GetImGuiTextureId();
                if (textureId != IntPtr.Zero)
                {
                    ImGui.Text("Custom texture:");
                    ImGui.Image(textureId, new Vector2(128, 128));
                }
                else
                {
                    ImGui.TextColored(new Vector4(1, 0, 0, 1), "Failed to get texture for current context");
                }
            }
            
            ImGui.Spacing();
            
            // Example 2: Display the shared icon
            if (_sharedIcon != null && _sharedIcon.IsValid)
            {
                var iconId = _sharedIcon.GetImGuiTextureId();
                if (iconId != IntPtr.Zero)
                {
                    ImGui.Text("Shared icon:");
                    ImGui.Image(iconId, new Vector2(32, 32));
                }
            }
            
            ImGui.Spacing();
            ImGui.TextWrapped("Try popping out this window - the textures will continue to work!");
        }
        
        private void CreateSampleTexture()
        {
            // Create a simple gradient texture
            const int width = 256;
            const int height = 256;
            byte[] pixelData = new byte[width * height * 4]; // RGBA
            
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = (y * width + x) * 4;
                    pixelData[index + 0] = (byte)(x * 255 / width);     // R
                    pixelData[index + 1] = (byte)(y * 255 / height);    // G
                    pixelData[index + 2] = 128;                         // B
                    pixelData[index + 3] = 255;                         // A
                }
            }
            
            _myTexture = TextureManager.CreateFromPixelData(pixelData, width, height);
        }
        
        private byte[] CreateIconData(uint width, uint height)
        {
            // Create a simple checkerboard pattern
            byte[] data = new byte[width * height * 4];
            
            for (uint y = 0; y < height; y++)
            {
                for (uint x = 0; x < width; x++)
                {
                    int index = (int)((y * width + x) * 4);
                    bool isWhite = ((x / 8) + (y / 8)) % 2 == 0;
                    byte color = isWhite ? (byte)255 : (byte)64;
                    
                    data[index + 0] = color; // R
                    data[index + 1] = color; // G
                    data[index + 2] = color; // B
                    data[index + 3] = 255;   // A
                }
            }
            
            return data;
        }
        
        public override void Dispose()
        {
            _myTexture?.Dispose();
            // Note: Don't dispose shared textures here - they're managed by SharedTextureManager
            base.Dispose();
        }
    }
    
    /// <summary>
    /// Quick reference for using TextureManager in panels:
    /// 
    /// 1. Create a texture:
    ///    _texture = TextureManager.CreateFromPixelData(pixelData, width, height);
    /// 
    /// 2. Display it:
    ///    var textureId = _texture.GetImGuiTextureId();
    ///    if (textureId != IntPtr.Zero)
    ///        ImGui.Image(textureId, size);
    /// 
    /// 3. Clean up:
    ///    _texture?.Dispose();
    /// 
    /// The TextureManager handles all the complexity of multiple contexts!
    /// </summary>
}