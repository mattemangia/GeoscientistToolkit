// GeoscientistToolkit/UI/ThumbnailViewerPanel.cs (Updated with TextureManager)
using System;
using System.Collections.Generic;
using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Util;
using ImGuiNET;
using Veldrid;

namespace GeoscientistToolkit.UI
{
    public class ThumbnailViewerPanel : BasePanel, IDisposable
    {
        public DatasetGroup Group { get; }
        private readonly Dictionary<string, ThumbnailInfo> _thumbnailMetadatas = new Dictionary<string, ThumbnailInfo>();
        private float _thumbnailSize = 128;
        private int _selectedIndex = -1;
        private Action<Dataset> _onImageSelected;
        
        private class ThumbnailInfo
        {
            public int Width { get; set; }
            public int Height { get; set; }
            public bool IsLoading { get; set; }
            public bool Failed { get; set; }
        }
        
        public ThumbnailViewerPanel(DatasetGroup group) : base($"Thumbnails: {group.Name}", new Vector2(600, 400))
        {
            Group = group;
            LoadThumbnailsAsync();
        }
        
        public void Submit(ref bool pOpen, Action<Dataset> onImageSelected)
        {
            _onImageSelected = onImageSelected;
            base.Submit(ref pOpen);
        }
        
        protected override void DrawContent()
        {
            DrawToolbar();
            ImGui.Separator();
            
            var contentSize = ImGui.GetContentRegionAvail();
            const int padding = 8;
            float cellSize = _thumbnailSize + padding * 2;
            
            int columns = Math.Max(1, (int)(contentSize.X / cellSize));
            
            if (ImGui.BeginChild("ThumbnailGrid", Vector2.Zero, ImGuiChildFlags.None))
            {
                int index = 0;
                ImGui.Columns(columns, "ThumbColumns", false);
                
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
            ImGui.SliderFloat("Size", ref _thumbnailSize, 64, 256, "%.0f");
            ImGui.SameLine();
            if (ImGui.Button("Refresh"))
            {
                RefreshThumbnails();
            }
        }
        
        private void DrawThumbnail(ImageDataset dataset, int index)
        {
            ImGui.PushID(index);
            
            var cursorPos = ImGui.GetCursorScreenPos();
            
            // Get texture from cache
            string thumbKey = dataset.FilePath + "_thumb";
            TextureManager textureManager = GlobalPerformanceManager.Instance.TextureCache.GetTexture(thumbKey, () =>
            {
                var thumbInfo = ImageLoader.CreateThumbnail(dataset.FilePath, 256, 256);
                var manager = TextureManager.CreateFromPixelData(thumbInfo.Data, (uint)thumbInfo.Width, (uint)thumbInfo.Height);
                long size = (long)thumbInfo.Width * thumbInfo.Height * 4;
                
                // Store metadata separately
                _thumbnailMetadatas[dataset.FilePath] = new ThumbnailInfo { Width = thumbInfo.Width, Height = thumbInfo.Height };
                
                return (manager, size);
            });
            
            if (textureManager != null && _thumbnailMetadatas.TryGetValue(dataset.FilePath, out var meta))
            {
                var textureId = textureManager.GetImGuiTextureId();
                if (textureId != IntPtr.Zero)
                {
                    float aspectRatio = (float)meta.Width / meta.Height;
                    Vector2 size = aspectRatio > 1.0f 
                        ? new Vector2(_thumbnailSize, _thumbnailSize / aspectRatio) 
                        : new Vector2(_thumbnailSize * aspectRatio, _thumbnailSize);
                    
                    Vector2 offset = (new Vector2(_thumbnailSize) - size) * 0.5f;
                    ImGui.SetCursorScreenPos(cursorPos + offset);
                    ImGui.Image(textureId, size);
                }
            }
            else
            {
                DrawPlaceholder(cursorPos, index, "Loading...");
            }

            ImGui.SetCursorScreenPos(cursorPos);
            ImGui.InvisibleButton($"thumb_{index}", new Vector2(_thumbnailSize, _thumbnailSize));

            if (ImGui.IsItemClicked()) _selectedIndex = index;
            if (ImGui.IsItemClicked() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) _onImageSelected?.Invoke(dataset);
            
            ImGui.PopID();
        }
        
        private void DrawPlaceholder(Vector2 cursorPos, int index, string status)
        {
            var drawList = ImGui.GetWindowDrawList();
            ImGui.SetCursorScreenPos(cursorPos);
            ImGui.InvisibleButton($"thumb_{index}", new Vector2(_thumbnailSize, _thumbnailSize));
            drawList.AddRect(cursorPos, cursorPos + new Vector2(_thumbnailSize, _thumbnailSize), 0x80808080);
            var statusTextSize = ImGui.CalcTextSize(status);
            drawList.AddText(cursorPos + (new Vector2(_thumbnailSize) - statusTextSize) * 0.5f, 0xFFFFFFFF, status);
        }
        
        private void LoadThumbnailsAsync()
        {
            // Pre-loading is now handled on-demand by the GetTexture call
        }

        private void RefreshThumbnails()
        {
            foreach (var dataset in Group.Datasets)
            {
                if (dataset is ImageDataset imageDataset)
                {
                    // Releasing will make it a candidate for eviction. The next GetTexture will recreate it.
                    GlobalPerformanceManager.Instance.TextureCache.ReleaseTexture(imageDataset.FilePath + "_thumb");
                }
            }
        }

        public override void Dispose()
        {
            foreach (var dataset in Group.Datasets)
            {
                 if (dataset is ImageDataset imageDataset)
                {
                    GlobalPerformanceManager.Instance.TextureCache.ReleaseTexture(imageDataset.FilePath + "_thumb");
                }
            }
            base.Dispose();
        }
    }
}