// GeoscientistToolkit/Data/CtImageStack/CtRenderingPanel.cs
using System;
using System.Numerics;
using GeoscientistToolkit.UI;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using System.Linq;

namespace GeoscientistToolkit.Data.CtImageStack
{
    /// <summary>
    /// Control panel for CT rendering settings in the combined viewer.
    /// This panel focuses exclusively on view and rendering adjustments.
    /// </summary>
    public class CtRenderingPanel : BasePanel
    {
        private readonly CtCombinedViewer _viewer;
        private readonly CtImageStackDataset _dataset;
        private int _currentViewMode = 0;
        
        public CtRenderingPanel(CtCombinedViewer viewer, CtImageStackDataset dataset) 
            : base("CT Rendering Controls", new Vector2(400, 600))
        {
            _viewer = viewer;
            _dataset = dataset;
            _currentViewMode = (int)_viewer.ViewMode;
        }

        protected override void DrawContent()
        {
            if (ImGui.BeginTabBar("RenderingTabs"))
            {
                if (ImGui.BeginTabItem("View Settings"))
                {
                    DrawViewSettings();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("3D Rendering"))
                {
                    Draw3DSettings();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Materials"))
                {
                    DrawMaterialsTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Cutting Planes"))
                {
                    DrawCuttingPlanesTab();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }

        // Add this method to allow inline drawing when popped out
        public void DrawContentInline()
        {
            DrawContent();
        }

        private void DrawViewSettings()
        {
            ImGui.Text("View Mode:");
            ImGui.RadioButton("Combined", ref _currentViewMode, 0);
            ImGui.SameLine();
            ImGui.RadioButton("Slices Only", ref _currentViewMode, 1);
            ImGui.SameLine();
            ImGui.RadioButton("3D Only", ref _currentViewMode, 2);

            if (_currentViewMode != (int)_viewer.ViewMode)
            {
                _viewer.ViewMode = (CtCombinedViewer.ViewModeEnum)_currentViewMode;
            }

            ImGui.Separator();

            ImGui.Text("Display Options:");
            ImGui.Indent();
            
            bool showCrosshairs = _viewer.ShowCrosshairs;
            if (ImGui.Checkbox("Show Crosshairs", ref showCrosshairs))
                _viewer.ShowCrosshairs = showCrosshairs;

            bool syncViews = _viewer.SyncViews;
            if (ImGui.Checkbox("Sync Views", ref syncViews))
                _viewer.SyncViews = syncViews;

            bool showScaleBar = _viewer.ShowScaleBar;
            if (ImGui.Checkbox("Show Scale Bar", ref showScaleBar))
                _viewer.ShowScaleBar = showScaleBar;

            bool showCuttingPlanes = _viewer.ShowCuttingPlanes;
            if (ImGui.Checkbox("Show Cutting Planes", ref showCuttingPlanes))
                _viewer.ShowCuttingPlanes = showCuttingPlanes;

            ImGui.Unindent();

            ImGui.Separator();

            ImGui.Text("Window/Level:");
            float windowLevel = _viewer.WindowLevel;
            float windowWidth = _viewer.WindowWidth;

            if (ImGui.DragFloat("Level", ref windowLevel, 1f, 0f, 255f))
                _viewer.WindowLevel = windowLevel;

            if (ImGui.DragFloat("Width", ref windowWidth, 1f, 1f, 255f))
                _viewer.WindowWidth = windowWidth;

            if (ImGui.Button("Auto W/L"))
            {
                _viewer.WindowLevel = 128;
                _viewer.WindowWidth = 255;
            }

            ImGui.Separator();

            ImGui.Text("Slice Positions:");
            int sliceX = _viewer.SliceX;
            int sliceY = _viewer.SliceY;
            int sliceZ = _viewer.SliceZ;

            if (ImGui.SliderInt("X", ref sliceX, 0, _dataset.Width - 1))
                _viewer.SliceX = sliceX;

            if (ImGui.SliderInt("Y", ref sliceY, 0, _dataset.Height - 1))
                _viewer.SliceY = sliceY;

            if (ImGui.SliderInt("Z", ref sliceZ, 0, _dataset.Depth - 1))
                _viewer.SliceZ = sliceZ;

            if (ImGui.Button("Center All"))
            {
                _viewer.SliceX = _dataset.Width / 2;
                _viewer.SliceY = _dataset.Height / 2;
                _viewer.SliceZ = _dataset.Depth / 2;
            }
        }

        private void Draw3DSettings()
        {
            if (_viewer.VolumeViewer == null)
            {
                ImGui.TextColored(new Vector4(1, 0.5f, 0.5f, 1), "3D viewer not available");
                ImGui.TextWrapped("Import dataset with 'Optimized for 3D' option to enable volume rendering.");
                return;
            }

            ImGui.Text("Volume Rendering:");
            
            bool showVolumeData = _viewer.ShowVolumeData;
            if (ImGui.Checkbox("Show Grayscale", ref showVolumeData))
            {
                _viewer.ShowVolumeData = showVolumeData;
            }

            ImGui.Separator();

            float stepSize = _viewer.VolumeStepSize;
            if (ImGui.SliderFloat("Step Size", ref stepSize, 0.5f, 10.0f, "%.1f"))
            {
                _viewer.VolumeStepSize = stepSize;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Lower values = higher quality but slower performance");

            float minThreshold = _viewer.MinThreshold;
            float maxThreshold = _viewer.MaxThreshold;

            if (ImGui.DragFloatRange2("Threshold", ref minThreshold, ref maxThreshold, 0.01f, 0.0f, 1.0f, 
                "Min: %.2f", "Max: %.2f"))
            {
                _viewer.MinThreshold = minThreshold;
                _viewer.MaxThreshold = maxThreshold;
            }

            ImGui.Separator();

            ImGui.Text("Color Map:");
            string[] colorMapNames = { "Grayscale", "Hot", "Cool", "Rainbow" };
            int colorMapIndex = _viewer.ColorMapIndex;
            if (ImGui.Combo("##ColorMap", ref colorMapIndex, colorMapNames, colorMapNames.Length))
            {
                _viewer.ColorMapIndex = colorMapIndex;
            }

            ImGui.Separator();

            if (ImGui.Button("Reset Camera"))
            {
                _viewer.VolumeViewer?.ResetCamera();
            }

            ImGui.SameLine();

            if (ImGui.Button("Save Screenshot..."))
            {
                // Use the volume viewer's screenshot functionality directly
                if (_viewer.VolumeViewer != null)
                {
                    string filename = "volume_screenshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
                    string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), filename);
                    _viewer.VolumeViewer.SaveScreenshot(path);
                    Logger.Log($"Screenshot saved to: {path}");
                }
            }
        }

        private void DrawMaterialsTab()
        {
            ImGui.Text("Material Visibility:");
            
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

            ImGui.Separator();

            if (ImGui.BeginChild("MaterialList", new Vector2(0, -1), ImGuiChildFlags.Border))
            {
                foreach (var material in _dataset.Materials.Where(m => m.ID != 0))
                {
                    ImGui.PushID((int)material.ID);
                    
                    bool isVisible = _viewer.GetMaterialVisibility(material.ID);
                    if (ImGui.Checkbox($"##vis{material.ID}", ref isVisible))
                    {
                        _viewer.SetMaterialVisibility(material.ID, isVisible);
                    }
                    
                    ImGui.SameLine();
                    
                    Vector4 color = material.Color;
                    Vector3 color3 = new Vector3(color.X, color.Y, color.Z);
                    if (ImGui.ColorEdit3($"##color{material.ID}", ref color3, 
                        ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
                    {
                        material.Color = new Vector4(color3, color.W);
                        _viewer.VolumeViewer?.SetMaterialVisibility(material.ID, isVisible); // Force update
                    }
                    
                    ImGui.SameLine();
                    ImGui.Text(material.Name);
                    
                    if (_viewer.VolumeViewer != null)
                    {
                        float opacity = _viewer.GetMaterialOpacity(material.ID);
                        ImGui.SetNextItemWidth(100);
                        if (ImGui.SliderFloat($"##opacity{material.ID}", ref opacity, 0.0f, 1.0f, "%.2f"))
                        {
                            _viewer.SetMaterialOpacity(material.ID, opacity);
                        }
                    }
                    
                    ImGui.PopID();
                }
            }
            ImGui.EndChild();
        }

        private void DrawCuttingPlanesTab()
        {
            if (_viewer.VolumeViewer == null)
            {
                ImGui.TextColored(new Vector4(1, 0.5f, 0.5f, 1), "3D viewer not available");
                return;
            }

            ImGui.Text("Axis-Aligned Cutting Planes:");
            ImGui.Separator();

            // X-axis cutting plane
            bool cutXEnabled = _viewer.VolumeViewer.CutXEnabled;
            float cutXPosition = _viewer.VolumeViewer.CutXPosition;
            bool cutXForward = _viewer.VolumeViewer.CutXForward;
            bool showCutXVisual = _viewer.VolumeViewer.ShowCutXPlaneVisual;
            
            DrawAxisCuttingPlane("X", ref cutXEnabled, ref cutXPosition, ref cutXForward, ref showCutXVisual);
            
            _viewer.VolumeViewer.CutXEnabled = cutXEnabled;
            _viewer.VolumeViewer.CutXPosition = cutXPosition;
            _viewer.VolumeViewer.CutXForward = cutXForward;
            _viewer.VolumeViewer.ShowCutXPlaneVisual = showCutXVisual;

            ImGui.Separator();

            // Y-axis cutting plane
            bool cutYEnabled = _viewer.VolumeViewer.CutYEnabled;
            float cutYPosition = _viewer.VolumeViewer.CutYPosition;
            bool cutYForward = _viewer.VolumeViewer.CutYForward;
            bool showCutYVisual = _viewer.VolumeViewer.ShowCutYPlaneVisual;
            
            DrawAxisCuttingPlane("Y", ref cutYEnabled, ref cutYPosition, ref cutYForward, ref showCutYVisual);
            
            _viewer.VolumeViewer.CutYEnabled = cutYEnabled;
            _viewer.VolumeViewer.CutYPosition = cutYPosition;
            _viewer.VolumeViewer.CutYForward = cutYForward;
            _viewer.VolumeViewer.ShowCutYPlaneVisual = showCutYVisual;

            ImGui.Separator();

            // Z-axis cutting plane
            bool cutZEnabled = _viewer.VolumeViewer.CutZEnabled;
            float cutZPosition = _viewer.VolumeViewer.CutZPosition;
            bool cutZForward = _viewer.VolumeViewer.CutZForward;
            bool showCutZVisual = _viewer.VolumeViewer.ShowCutZPlaneVisual;
            
            DrawAxisCuttingPlane("Z", ref cutZEnabled, ref cutZPosition, ref cutZForward, ref showCutZVisual);
            
            _viewer.VolumeViewer.CutZEnabled = cutZEnabled;
            _viewer.VolumeViewer.CutZPosition = cutZPosition;
            _viewer.VolumeViewer.CutZForward = cutZForward;
            _viewer.VolumeViewer.ShowCutZPlaneVisual = showCutZVisual;

            ImGui.Separator();
            ImGui.Separator();

            // Arbitrary clipping planes
            ImGui.Text("Arbitrary Clipping Planes:");
            
            if (ImGui.Button("Add Plane"))
            {
                _viewer.VolumeViewer.ClippingPlanes.Add(
                    new ClippingPlane($"Plane {_viewer.VolumeViewer.ClippingPlanes.Count + 1}"));
            }

            ImGui.SameLine();
            
            bool showVisualizations = _viewer.VolumeViewer.ShowPlaneVisualizations;
            if (ImGui.Checkbox("Show All Visualizations", ref showVisualizations))
            {
                _viewer.VolumeViewer.ShowPlaneVisualizations = showVisualizations;
            }

            if (_viewer.VolumeViewer.ClippingPlanes.Count > 0)
            {
                ImGui.Separator();
                
                for (int i = 0; i < _viewer.VolumeViewer.ClippingPlanes.Count; i++)
                {
                    var plane = _viewer.VolumeViewer.ClippingPlanes[i];
                    ImGui.PushID($"Plane{i}");
                    
                    if (ImGui.CollapsingHeader(plane.Name))
                    {
                        bool enabled = plane.Enabled;
                        if (ImGui.Checkbox("Enabled", ref enabled))
                        {
                            plane.Enabled = enabled;
                        }
                        
                        ImGui.SameLine();
                        bool showVisualization = plane.IsVisualizationVisible;
                        if (ImGui.Checkbox("Show Visualization", ref showVisualization))
                        {
                            plane.IsVisualizationVisible = showVisualization;
                        }
                        
                        ImGui.SameLine();
                        if (ImGui.Button("Remove"))
                        {
                            _viewer.VolumeViewer.ClippingPlanes.RemoveAt(i);
                            ImGui.PopID();
                            break;
                        }

                        float distance = plane.Distance;
                        if (ImGui.DragFloat("Distance", ref distance, 0.01f, -1.0f, 1.0f))
                        {
                            plane.Distance = distance;
                        }
                        
                        bool mirror = plane.Mirror;
                        if (ImGui.Checkbox("Mirror", ref mirror))
                        {
                            plane.Mirror = mirror;
                        }

                        Vector3 rotation = plane.Rotation;
                        if (ImGui.DragFloat3("Rotation", ref rotation, 1.0f, -180.0f, 180.0f))
                        {
                            plane.Rotation = rotation;
                            _viewer.VolumeViewer.UpdateClippingPlaneNormal(plane);
                        }
                    }
                    
                    ImGui.PopID();
                }
            }
        }

        private void DrawAxisCuttingPlane(string axis, ref bool enabled, ref float position, 
            ref bool forward, ref bool showVisual)
        {
            ImGui.Checkbox($"{axis}-Axis Cut", ref enabled);
            
            if (enabled)
            {
                ImGui.SameLine();
                ImGui.Checkbox($"Show##{axis}", ref showVisual);
                
                ImGui.DragFloat($"Position##{axis}", ref position, 0.01f, 0.0f, 1.0f);
                
                if (ImGui.RadioButton($"Forward##{axis}", forward))
                    forward = true;
                ImGui.SameLine();
                if (ImGui.RadioButton($"Backward##{axis}", !forward))
                    forward = false;
            }
        }
    }
}