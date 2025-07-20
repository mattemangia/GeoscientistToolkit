// GeoscientistToolkit/UI/ThumbnailViewerPanel.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Util;
using ImGuiNET;
using Veldrid;

namespace GeoscientistToolkit.UI
{
    // FIX: Implement IDisposable to allow MainWindow to manage its lifecycle correctly.
    public class ThumbnailViewerPanel : BasePanel, IDisposable
    {
        public DatasetGroup Group { get; }
        private readonly Dictionary<string, ThumbnailInfo> _thumbnails = new Dictionary<string, ThumbnailInfo>();
        private float _thumbnailSize = 128;
        private int _selectedIndex = -1;
        private Action<Dataset> _onImageSelected;
        
        private class ThumbnailInfo
        {
            public TextureView TextureView { get; set; }
            public IntPtr TextureId { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public bool IsLoading { get; set; }
            public bool Failed { get; set; }
        }
        
        public ThumbnailViewerPanel(DatasetGroup group) : base($"Thumbnails: {group.Name}", new Vector2(600, 400))
        {
            Group = group;
            
            // Start loading thumbnails asynchronously
            LoadThumbnailsAsync();
        }
        
        public void Submit(ref bool pOpen, Action<Dataset> onImageSelected)
        {
            _onImageSelected = onImageSelected;
            base.Submit(ref pOpen);
        }
        
        protected override void DrawContent()
        {
            // Toolbar
            DrawToolbar();
            ImGui.Separator();
            
            // Calculate grid layout
            var contentSize = ImGui.GetContentRegionAvail();
            const int padding = 8;
            const int labelHeight = 20;
            float cellSize = _thumbnailSize + padding * 2;
            
            int columns = Math.Max(1, (int)(contentSize.X / cellSize));
            
            // Scrollable child region
            // FIX: The method signature for BeginChild changed. The boolean 'border'
            // parameter is now an ImGuiChildFlags enum. 'false' is replaced with 'ImGuiChildFlags.None'.
            if (ImGui.BeginChild("ThumbnailGrid", Vector2.Zero, ImGuiChildFlags.None))
            {
                // Draw grid
                int index = 0;
                ImGui.Columns(columns, "ThumbColumns", false);
                
                // Use a copy of the list to prevent modification during enumeration issues
                foreach (var dataset in Group.Datasets.ToList())
                {
                    if (dataset is ImageDataset imageDataset)
                    {
                        DrawThumbnail(imageDataset, index);
                        index++;
                        ImGui.NextColumn();
                    }
                }
                
                ImGui.Columns(1);
                ImGui.EndChild();
            }
        }
        
        private void DrawToolbar()
        {
            ImGui.Text($"{Group.Datasets.Count} images");
            
            ImGui.SameLine();
            ImGui.SetNextItemWidth(150);
            if (ImGui.SliderFloat("Size", ref _thumbnailSize, 64, 256, "%.0f"))
            {
                // Size changed
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Refresh"))
            {
                RefreshThumbnails();
            }
        }
        
        private void DrawThumbnail(ImageDataset dataset, int index)
        {
            ImGui.PushID(index);
            
            var drawList = ImGui.GetWindowDrawList();
            var cursorPos = ImGui.GetCursorScreenPos();
            bool isSelected = _selectedIndex == index;
            
            // Background for selection
            if (isSelected)
            {
                drawList.AddRectFilled(
                    cursorPos - new Vector2(4),
                    cursorPos + new Vector2(_thumbnailSize + 4, _thumbnailSize + 4),
                    ImGui.GetColorU32(new Vector4(0.2f, 0.5f, 0.8f, 0.3f)),
                    4.0f
                );
            }
            
            // Check if thumbnail is loaded
            if (_thumbnails.TryGetValue(dataset.FilePath, out var thumb) && thumb.TextureId != IntPtr.Zero)
            {
                // Calculate centered position maintaining aspect ratio
                float aspectRatio = (float)thumb.Width / thumb.Height;
                Vector2 size;
                
                if (aspectRatio > 1.0f)
                {
                    size = new Vector2(_thumbnailSize, _thumbnailSize / aspectRatio);
                }
                else
                {
                    size = new Vector2(_thumbnailSize * aspectRatio, _thumbnailSize);
                }
                
                // FIX: Cannot subtract a Vector2 from a float. Create a Vector2 from the float first.
                Vector2 offset = (new Vector2(_thumbnailSize) - size) * 0.5f;
                Vector2 imagePos = cursorPos + offset;
                
                // Draw thumbnail
                ImGui.SetCursorScreenPos(imagePos);
                ImGui.Image(thumb.TextureId, size);
                
                // Reset cursor for click detection
                ImGui.SetCursorScreenPos(cursorPos);
                ImGui.InvisibleButton($"thumb_{index}", new Vector2(_thumbnailSize, _thumbnailSize));
                
                if (ImGui.IsItemClicked())
                {
                    _selectedIndex = index;
                }
                
                if (ImGui.IsItemClicked() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    _onImageSelected?.Invoke(dataset);
                }
            }
            else
            {
                // Draw placeholder
                ImGui.SetCursorScreenPos(cursorPos);
                ImGui.InvisibleButton($"thumb_{index}", new Vector2(_thumbnailSize, _thumbnailSize));
                
                drawList.AddRect(
                    cursorPos,
                    cursorPos + new Vector2(_thumbnailSize, _thumbnailSize),
                    ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 0.5f)),
                    0.0f,
                    ImDrawFlags.None,
                    1.0f
                );
                
                string status = thumb?.Failed == true ? "Failed" : "Loading...";
                var statusTextSize = ImGui.CalcTextSize(status);
                drawList.AddText(
                    cursorPos + (new Vector2(_thumbnailSize) - statusTextSize) * 0.5f,
                    ImGui.GetColorU32(new Vector4(0.7f, 0.7f, 0.7f, 1.0f)),
                    status
                );
            }
            
            // Draw filename
            ImGui.SetCursorScreenPos(cursorPos + new Vector2(0, _thumbnailSize + 2));
            string fileName = System.IO.Path.GetFileName(dataset.Name);
            
            // Truncate long names
            var maxTextWidth = _thumbnailSize;
            // FIX: Renamed variable to avoid conflict with 'statusTextSize' scope.
            var fileNameTextSize = ImGui.CalcTextSize(fileName);
            if (fileNameTextSize.X > maxTextWidth)
            {
                int maxChars = (int)(fileName.Length * (maxTextWidth / fileNameTextSize.X)) - 3;
                if (maxChars > 0)
                {
                    fileName = fileName.Substring(0, maxChars) + "...";
                }
            }
            
            ImGui.TextUnformatted(fileName);
            
            // Tooltip
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text(dataset.Name);
                ImGui.Text($"Size: {dataset.Width} x {dataset.Height}");
                if (dataset.PixelSize > 0)
                {
                    ImGui.Text($"Scale: {dataset.PixelSize} {dataset.Unit}/pixel");
                }
                ImGui.EndTooltip();
            }
            
            // Context menu
            if (ImGui.BeginPopupContextItem())
            {
                if (ImGui.MenuItem("Open"))
                {
                    _onImageSelected?.Invoke(dataset);
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Remove from group"))
                {
                    Group.RemoveDataset(dataset);
                    if (_thumbnails.TryGetValue(dataset.FilePath, out var thumbInfo))
                    {
                        DisposeThumbnail(thumbInfo);
                        _thumbnails.Remove(dataset.FilePath);
                    }
                }
                ImGui.EndPopup();
            }
            
            ImGui.PopID();
        }
        
        private async void LoadThumbnailsAsync()
        {
            foreach (var dataset in Group.Datasets)
            {
                if (dataset is ImageDataset imageDataset && !_thumbnails.ContainsKey(imageDataset.FilePath))
                {
                    _thumbnails[imageDataset.FilePath] = new ThumbnailInfo { IsLoading = true };
                    
                    // Load thumbnail on background thread
                    await System.Threading.Tasks.Task.Run(() => LoadThumbnail(imageDataset));
                }
            }
        }
        
        private void LoadThumbnail(ImageDataset dataset)
        {
            try
            {
                // Create thumbnail
                var thumbInfo = ImageLoader.CreateThumbnail(dataset.FilePath, 256, 256);
                
                // Create texture on main thread
                VeldridManager.ExecuteOnMainThread(() =>
                {
                    var texture = VeldridManager.Factory.CreateTexture(TextureDescription.Texture2D(
                        (uint)thumbInfo.Width, (uint)thumbInfo.Height, 1, 1,
                        PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
                    
                    VeldridManager.GraphicsDevice.UpdateTexture(
                        texture,
                        thumbInfo.Data,
                        0, 0, 0, (uint)thumbInfo.Width, (uint)thumbInfo.Height, 1, 0, 0);
                    
                    var textureView = VeldridManager.Factory.CreateTextureView(texture);
                    var textureId = VeldridManager.ImGuiController.GetOrCreateImGuiBinding(
                        VeldridManager.Factory, textureView);
                    
                    _thumbnails[dataset.FilePath] = new ThumbnailInfo
                    {
                        TextureView = textureView,
                        TextureId = textureId,
                        Width = thumbInfo.Width,
                        Height = thumbInfo.Height,
                        IsLoading = false,
                        Failed = false
                    };
                });
            }
            catch (Exception ex)
            {
                // Assume a static Logger class exists
                // Logger.Log($"Failed to load thumbnail for {dataset.Name}: {ex.Message}");
                if (_thumbnails.ContainsKey(dataset.FilePath))
                {
                   _thumbnails[dataset.FilePath].Failed = true;
                   _thumbnails[dataset.FilePath].IsLoading = false;
                }
            }
        }
        
        private void RefreshThumbnails()
        {
            // Dispose existing thumbnails
            foreach (var thumb in _thumbnails.Values)
            {
                DisposeThumbnail(thumb);
            }
            
            _thumbnails.Clear();
            LoadThumbnailsAsync();
        }

        private void DisposeThumbnail(ThumbnailInfo thumb)
        {
            if (thumb.TextureView != null)
            {
                VeldridManager.ImGuiController.RemoveImGuiBinding(thumb.TextureView);
                thumb.TextureView.Target.Dispose();
                thumb.TextureView.Dispose();
            }
        }

        // FIX: Replaced OnClose with the standard IDisposable pattern.
        public void Dispose()
        {
            // Clean up textures
            foreach (var thumb in _thumbnails.Values)
            {
                DisposeThumbnail(thumb);
            }
            
            _thumbnails.Clear();
        }
    }
}