// GeoscientistToolkit/Data/CtImageStack/CtRenderingPanel.cs
using System;
using System.Numerics;
using GeoscientistToolkit.UI;
using ImGuiNET;
using System.Linq;

namespace GeoscientistToolkit.Data.CtImageStack
{
    /// <summary>
    /// Rendering control panel for the combined CT viewer (2D+3D)
    /// </summary>
    public class CtRenderingPanel : BasePanel
    {
        private readonly CtCombinedViewer _viewer;
        private readonly CtImageStackDataset _dataset;
        
        // UI state
        private string[] _viewModes = { "Combined", "Slices Only", "3D Only", "XY (Axial) Only", "XZ (Coronal) Only", "YZ (Sagittal) Only" };
        private string[] _colorMaps = { "Grayscale", "Hot", "Cool", "Rainbow" };
        
        public CtRenderingPanel(CtCombinedViewer viewer, CtImageStackDataset dataset)
            : base("Rendering Controls", new Vector2(350, 500))
        {
            _viewer = viewer ?? throw new ArgumentNullException(nameof(viewer));
            _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        }
        
        protected override void DrawContent()
        {
            if (ImGui.BeginTabBar("RenderingTabs"))
            {
                if (ImGui.BeginTabItem("View"))
                {
                    DrawViewTab();
                    ImGui.EndTabItem();
                }
                
                if (ImGui.BeginTabItem("Volume"))
                {
                    DrawVolumeTab();
                    ImGui.EndTabItem();
                }
                
                if (ImGui.BeginTabItem("Materials"))
                {
                    DrawMaterialsTab();
                    ImGui.EndTabItem();
                }
                
                if (ImGui.BeginTabItem("Display"))
                {
                    DrawDisplayTab();
                    ImGui.EndTabItem();
                }
                
                ImGui.EndTabBar();
            }
        }
        
        private void DrawViewTab()
{
    ImGui.Text("View Mode");
    ImGui.Separator();
    
    // View mode selection
    int currentMode = (int)_viewer.ViewMode;
    ImGui.SetNextItemWidth(-1);
    if (ImGui.Combo("##ViewMode", ref currentMode, _viewModes, _viewModes.Length))
    {
        _viewer.ViewMode = (CtCombinedViewer.ViewModeEnum)currentMode;
    }
    
    ImGui.Spacing();
    ImGui.Separator();
    ImGui.Spacing();
    
    // Slice positions
    if (_viewer.ViewMode != CtCombinedViewer.ViewModeEnum.VolumeOnly)
    {
        ImGui.Text("Slice Positions");
        ImGui.Spacing();
        
        int sliceX = _viewer.SliceX;
        int sliceY = _viewer.SliceY;
        int sliceZ = _viewer.SliceZ;
        
        ImGui.PushItemWidth(-60);
        
        if (ImGui.SliderInt("X (Sagittal)", ref sliceX, 0, _dataset.Width - 1))
        {
            _viewer.SliceX = sliceX;
        }
        ImGui.SameLine();
        ImGui.Text($"{sliceX + 1}/{_dataset.Width}");
        
        if (ImGui.SliderInt("Y (Coronal)", ref sliceY, 0, _dataset.Height - 1))
        {
            _viewer.SliceY = sliceY;
        }
        ImGui.SameLine();
        ImGui.Text($"{sliceY + 1}/{_dataset.Height}");
        
        if (ImGui.SliderInt("Z (Axial)", ref sliceZ, 0, _dataset.Depth - 1))
        {
            _viewer.SliceZ = sliceZ;
        }
        ImGui.SameLine();
        ImGui.Text($"{sliceZ + 1}/{_dataset.Depth}");
        
        ImGui.PopItemWidth();
        
        ImGui.Spacing();
        if (ImGui.Button("Center Slices"))
        {
            _viewer.SliceX = _dataset.Width / 2;
            _viewer.SliceY = _dataset.Height / 2;
            _viewer.SliceZ = _dataset.Depth / 2;
        }
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
        // Add zoom controls here
        ImGui.Text("Zoom Controls");
        ImGui.Spacing();
        
        if (ImGui.Button("Zoom In", new Vector2(80, 0)))
        {
            // You'll need to add methods to CtCombinedViewer to handle zoom
            _viewer.ZoomAllViews(1.25f);
        }
        ImGui.SameLine();
        if (ImGui.Button("Zoom Out", new Vector2(80, 0)))
        {
            _viewer.ZoomAllViews(0.8f);
        }
        ImGui.SameLine();
        if (ImGui.Button("Fit to Window", new Vector2(-1, 0)))
        {
            _viewer.ResetAllViews();
        }
    }
    
}
        public void DrawContentInline()
        {
            // Draw the content directly without window management
            DrawContent();
        }
        
        private void DrawVolumeTab()
        {
            ImGui.Text("Volume Rendering");
            ImGui.Separator();
            
            // Enable/disable volume rendering
            bool showVolume = _viewer.ShowVolumeData;
            if (ImGui.Checkbox("Show Volume Data", ref showVolume))
            {
                _viewer.ShowVolumeData = showVolume;
            }
            
            if (!showVolume)
            {
                ImGui.BeginDisabled();
            }
            
            ImGui.Spacing();
            
            // Rendering quality
            ImGui.Text("Rendering Quality");
            float stepSize = _viewer.VolumeStepSize;
            if (ImGui.SliderFloat("Step Size", ref stepSize, 0.1f, 5.0f, "%.2f"))
            {
                _viewer.VolumeStepSize = stepSize;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Lower values = higher quality but slower rendering");
            }
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            // Threshold range
            ImGui.Text("Density Threshold");
            float minThreshold = _viewer.MinThreshold;
            float maxThreshold = _viewer.MaxThreshold;
            
            if (ImGui.DragFloatRange2("Range", ref minThreshold, ref maxThreshold, 0.005f, 0.0f, 1.0f, "Min: %.3f", "Max: %.3f"))
            {
                _viewer.MinThreshold = minThreshold;
                _viewer.MaxThreshold = maxThreshold;
            }
            
            ImGui.Spacing();
            
            // Quick presets
            if (ImGui.Button("Low Density"))
            {
                _viewer.MinThreshold = 0.0f;
                _viewer.MaxThreshold = 0.3f;
            }
            ImGui.SameLine();
            if (ImGui.Button("Medium Density"))
            {
                _viewer.MinThreshold = 0.2f;
                _viewer.MaxThreshold = 0.6f;
            }
            ImGui.SameLine();
            if (ImGui.Button("High Density"))
            {
                _viewer.MinThreshold = 0.5f;
                _viewer.MaxThreshold = 1.0f;
            }
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            // Color map
            ImGui.Text("Color Map");
            int colorMapIndex = _viewer.ColorMapIndex;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo("##ColorMap", ref colorMapIndex, _colorMaps, _colorMaps.Length))
            {
                _viewer.ColorMapIndex = colorMapIndex;
            }
            
            if (!showVolume)
            {
                ImGui.EndDisabled();
            }
        }
        
        private void DrawMaterialsTab()
        {
            ImGui.Text("Material Visibility & Opacity");
            ImGui.Separator();
            
            if (_dataset.Materials.Count <= 1)
            {
                ImGui.TextDisabled("No materials defined for this dataset.");
                ImGui.TextWrapped("Use the segmentation tools to create materials based on density thresholds.");
                return;
            }
            
            // Quick actions
            if (ImGui.Button("Show All"))
            {
                foreach (var mat in _dataset.Materials.Where(m => m.ID != 0))
                {
                    _viewer.SetMaterialVisibility(mat.ID, true);
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Hide All"))
            {
                foreach (var mat in _dataset.Materials.Where(m => m.ID != 0))
                {
                    _viewer.SetMaterialVisibility(mat.ID, false);
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Reset Opacity"))
            {
                foreach (var mat in _dataset.Materials.Where(m => m.ID != 0))
                {
                    _viewer.SetMaterialOpacity(mat.ID, 1.0f);
                }
            }
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            // Material list
            if (ImGui.BeginChild("MaterialList", new Vector2(0, -1), ImGuiChildFlags.Border))
            {
                foreach (var material in _dataset.Materials)
                {
                    if (material.ID == 0) continue; // Skip exterior
                    
                    ImGui.PushID((int)material.ID);
                    
                    // Color indicator
                    Vector4 color = material.Color;
                    ImGui.ColorButton("##color", color, ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoAlpha, new Vector2(20, 20));
                    ImGui.SameLine();
                    
                    // Visibility checkbox
                    bool visible = _viewer.GetMaterialVisibility(material.ID);
                    if (ImGui.Checkbox($"##visible", ref visible))
                    {
                        _viewer.SetMaterialVisibility(material.ID, visible);
                    }
                    ImGui.SameLine();
                    
                    // Material name
                    ImGui.Text(material.Name);
                    
                    // Opacity slider (only if visible)
                    if (visible)
                    {
                        ImGui.SameLine();
                        ImGui.SetNextItemWidth(100);
                        float opacity = _viewer.GetMaterialOpacity(material.ID);
                        if (ImGui.SliderFloat("##opacity", ref opacity, 0.0f, 1.0f, "%.2f"))
                        {
                            _viewer.SetMaterialOpacity(material.ID, opacity);
                        }
                        
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip("Opacity: 0 = transparent, 1 = opaque");
                        }
                    }
                    
                    ImGui.PopID();
                }
            }
            ImGui.EndChild();
        }
        
        private void DrawDisplayTab()
        {
            ImGui.Text("Display Options");
            ImGui.Separator();
            
            // Window/Level controls
            if (_viewer.ViewMode != CtCombinedViewer.ViewModeEnum.VolumeOnly)
            {
                ImGui.Text("Contrast (Window/Level)");
                float windowWidth = _viewer.WindowWidth;
                float windowLevel = _viewer.WindowLevel;
                
                ImGui.PushItemWidth(-60);
                if (ImGui.DragFloat("Window", ref windowWidth, 1f, 1f, 255f, "%.0f"))
                {
                    _viewer.WindowWidth = windowWidth;
                }
                
                if (ImGui.DragFloat("Level", ref windowLevel, 1f, 0f, 255f, "%.0f"))
                {
                    _viewer.WindowLevel = windowLevel;
                }
                ImGui.PopItemWidth();
                
                // Presets
                ImGui.Text("Presets:");
                if (ImGui.Button("Soft Tissue"))
                {
                    _viewer.WindowWidth = 200;
                    _viewer.WindowLevel = 100;
                }
                ImGui.SameLine();
                if (ImGui.Button("Rock"))
                {
                    _viewer.WindowWidth = 255;
                    _viewer.WindowLevel = 128;
                }
                ImGui.SameLine();
                if (ImGui.Button("High Contrast"))
                {
                    _viewer.WindowWidth = 100;
                    _viewer.WindowLevel = 128;
                }
                
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
            }
            
            // View options
            ImGui.Text("View Options");
            
            bool showCrosshairs = _viewer.ShowCrosshairs;
            if (ImGui.Checkbox("Show Crosshairs", ref showCrosshairs))
            {
                _viewer.ShowCrosshairs = showCrosshairs;
            }
            
            bool showScaleBar = _viewer.ShowScaleBar;
            if (ImGui.Checkbox("Show Scale Bar", ref showScaleBar))
            {
                _viewer.ShowScaleBar = showScaleBar;
            }
            
            bool syncViews = _viewer.SyncViews;
            if (ImGui.Checkbox("Synchronize Views", ref syncViews))
            {
                _viewer.SyncViews = syncViews;
            }
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            // Reset button
            if (ImGui.Button("Reset All Views", new Vector2(-1, 0)))
            {
                _viewer.ResetAllViews();
            }
        }
    }
}