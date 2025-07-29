// GeoscientistToolkit/Data/CtImageStack/CtVolume3DControlPanel.cs
using System;
using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.UI;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using System.Linq;

namespace GeoscientistToolkit.Data.CtImageStack
{
    public class CtVolume3DControlPanel : BasePanel
    {
        private readonly CtVolume3DViewer _viewer;
        private readonly CtImageStackDataset _dataset; 

        private readonly ImGuiExportFileDialog _screenshotDialog;
        private readonly ImGuiExportFileDialog _exportModelDialog;
        
        private int _exportFileFormat = 0;
        
        // UI state for clipping plane editing
        private int _selectedPlaneIndex = -1;
        private string _newPlaneName = "Plane";

        public CtVolume3DControlPanel(CtVolume3DViewer viewer, CtImageStackDataset dataset)
            : base("3D Volume Controls", new Vector2(400, 600))
        {
            _viewer = viewer;
            _dataset = dataset;
            
            _screenshotDialog = new ImGuiExportFileDialog("ScreenshotDialog", "Save Screenshot");
            _screenshotDialog.SetExtensions((".png", "PNG Image"));
            _exportModelDialog = new ImGuiExportFileDialog("ExportModelDialog", "Export 3D Model");
            _exportModelDialog.SetExtensions((".obj", "Wavefront OBJ"), (".stl", "STL Mesh"));
        }

        protected override void DrawContent()
        {
            if (ImGui.BeginTabBar("ControlTabs"))
            {
                if (ImGui.BeginTabItem("Rendering")) { DrawRenderingTab(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Materials")) { DrawMaterialsTab(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Slicing")) { DrawSlicingTab(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Export")) { DrawExportTab(); ImGui.EndTabItem(); }
                ImGui.EndTabBar();
            }
            HandleFileDialogs();
        }
        
        private void DrawRenderingTab()
        {
            ImGui.Text("Volume Rendering Controls");
            ImGui.Separator();

            ImGui.Text("Rendering Quality");
            ImGui.SliderFloat("Step Size", ref _viewer.StepSize, 0.1f, 5.0f, "%.2f");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Lower values = higher quality but slower rendering.");

            ImGui.Spacing();

            ImGui.Text("Grayscale Threshold");
            ImGui.DragFloatRange2("Range", ref _viewer.MinThreshold, ref _viewer.MaxThreshold, 0.005f, 0.0f, 1.0f, "Min: %.3f", "Max: %.3f");
            
            ImGui.Spacing();

            ImGui.Text("Display Options");
            ImGui.Checkbox("Show Grayscale Volume", ref _viewer.ShowGrayscale);

            ImGui.Text("Color Map");
            string[] colorMaps = { "Grayscale", "Hot", "Cool", "Rainbow" };
            ImGui.Combo("##ColorMap", ref _viewer.ColorMapIndex, colorMaps, colorMaps.Length);
        }
        
        private void DrawMaterialsTab()
        {
            ImGui.Text("Material Visibility & Opacity");
            ImGui.Separator();

            if (_dataset.Materials.Count <= 1)
            {
                ImGui.TextDisabled("No material labels loaded for this dataset.");
                return;
            }

            if (ImGui.Button("Show All")) _viewer.SetAllMaterialsVisibility(true); ImGui.SameLine();
            if (ImGui.Button("Hide All")) _viewer.SetAllMaterialsVisibility(false); ImGui.SameLine();
            if (ImGui.Button("Reset Opacity")) _viewer.ResetAllMaterialOpacities();

            ImGui.Spacing();

            if (ImGui.BeginChild("MaterialList", new Vector2(0, -50), ImGuiChildFlags.Border))
            {
                foreach (var material in _dataset.Materials)
                {
                    if (material.ID == 0) continue;

                    ImGui.PushID((int)material.ID);
                    
                    Vector4 color = material.Color;
                    ImGui.ColorButton("##color", color, ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoAlpha, new Vector2(20, 20));
                    ImGui.SameLine();

                    bool visible = _viewer.GetMaterialVisibility(material.ID);
                    if (ImGui.Checkbox($"##visible", ref visible))
                    {
                        _viewer.SetMaterialVisibility(material.ID, visible);
                    }
                    ImGui.SameLine();
                    ImGui.Text(material.Name);

                    if (visible)
                    {
                        ImGui.SameLine();
                        ImGui.SetNextItemWidth(100);
                        float opacity = _viewer.GetMaterialOpacity(material.ID);
                        if (ImGui.SliderFloat("##opacity", ref opacity, 0.0f, 1.0f, "%.2f"))
                        {
                            _viewer.SetMaterialOpacity(material.ID, opacity);
                        }
                    }
                    ImGui.PopID();
                }
            }
            ImGui.EndChild();
        }

        private void DrawSlicingTab()
        {
            ImGui.Text("Volume Slicing Controls");
            ImGui.Separator();
            
            // Visual aid toggle
            bool showPlaneVis = _viewer.ShowPlaneVisualizations;
            if (ImGui.Checkbox("Show Plane Visualizations", ref showPlaneVis))
            {
                _viewer.ShowPlaneVisualizations = showPlaneVis;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Toggle visual representation of cutting and clipping planes in the 3D view");
            
            ImGui.Separator();
            
            // Orthogonal Slices
            if (ImGui.CollapsingHeader("Orthogonal Slices", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Checkbox("Enable Orthogonal Slices", ref _viewer.ShowSlices);

                if (_viewer.ShowSlices)
                {
                    ImGui.Spacing();
                    ImGui.Indent();
                    ImGui.Text("X Slice (Red)");
                    ImGui.SliderFloat("##xslice", ref _viewer.SlicePositions.X, 0.0f, 1.0f, "%.3f");
                    ImGui.Text("Y Slice (Green)");
                    ImGui.SliderFloat("##yslice", ref _viewer.SlicePositions.Y, 0.0f, 1.0f, "%.3f");
                    ImGui.Text("Z Slice (Blue)");
                    ImGui.SliderFloat("##zslice", ref _viewer.SlicePositions.Z, 0.0f, 1.0f, "%.3f");
                    ImGui.Spacing();
                    if (ImGui.Button("Reset to Center")) _viewer.SlicePositions = new Vector3(0.5f);
                    ImGui.Unindent();
                }
            }
            
            // Axis-Aligned Cutting Planes
            if (ImGui.CollapsingHeader("Axis-Aligned Cutting Planes", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                
                // X Cutting Plane
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0.3f, 0.3f, 1));
                ImGui.Text("X Cutting Plane");
                ImGui.PopStyleColor();
                
                ImGui.Checkbox("Enable X Cut", ref _viewer.CutXEnabled);
                if (_viewer.CutXEnabled)
                {
                    ImGui.Indent();
                    ImGui.SliderFloat("Position##X", ref _viewer.CutXPosition, 0.0f, 1.0f, "%.3f");
                    bool forward = _viewer.CutXForward;
                    if (ImGui.Checkbox("Cut Forward##X", ref forward))
                        _viewer.CutXForward = forward;
                    ImGui.SameLine();
                    if (ImGui.Button("Mirror##X"))
                        _viewer.CutXForward = !_viewer.CutXForward;
                    ImGui.Unindent();
                }
                
                ImGui.Spacing();
                
                // Y Cutting Plane
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.3f, 1, 0.3f, 1));
                ImGui.Text("Y Cutting Plane");
                ImGui.PopStyleColor();
                
                ImGui.Checkbox("Enable Y Cut", ref _viewer.CutYEnabled);
                if (_viewer.CutYEnabled)
                {
                    ImGui.Indent();
                    ImGui.SliderFloat("Position##Y", ref _viewer.CutYPosition, 0.0f, 1.0f, "%.3f");
                    bool forward = _viewer.CutYForward;
                    if (ImGui.Checkbox("Cut Forward##Y", ref forward))
                        _viewer.CutYForward = forward;
                    ImGui.SameLine();
                    if (ImGui.Button("Mirror##Y"))
                        _viewer.CutYForward = !_viewer.CutYForward;
                    ImGui.Unindent();
                }
                
                ImGui.Spacing();
                
                // Z Cutting Plane
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.3f, 0.3f, 1, 1));
                ImGui.Text("Z Cutting Plane");
                ImGui.PopStyleColor();
                
                ImGui.Checkbox("Enable Z Cut", ref _viewer.CutZEnabled);
                if (_viewer.CutZEnabled)
                {
                    ImGui.Indent();
                    ImGui.SliderFloat("Position##Z", ref _viewer.CutZPosition, 0.0f, 1.0f, "%.3f");
                    bool forward = _viewer.CutZForward;
                    if (ImGui.Checkbox("Cut Forward##Z", ref forward))
                        _viewer.CutZForward = forward;
                    ImGui.SameLine();
                    if (ImGui.Button("Mirror##Z"))
                        _viewer.CutZForward = !_viewer.CutZForward;
                    ImGui.Unindent();
                }
                
                ImGui.Unindent();
            }
            
            // Arbitrary Clipping Planes
            if (ImGui.CollapsingHeader("Arbitrary Clipping Planes", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                
                // Add new plane
                ImGui.InputText("Name", ref _newPlaneName, 50);
                ImGui.SameLine();
                if (ImGui.Button("Add Plane"))
                {
                    _viewer.ClippingPlanes.Add(new ClippingPlane(_newPlaneName));
                    _newPlaneName = $"Plane {_viewer.ClippingPlanes.Count + 1}";
                }
                
                ImGui.Separator();
                
                // List of clipping planes
                if (_viewer.ClippingPlanes.Count == 0)
                {
                    ImGui.TextDisabled("No clipping planes defined.");
                    ImGui.TextWrapped("Click 'Add Plane' to create a new clipping plane.");
                }
                else
                {
                    // Quick actions
                    if (ImGui.Button("Enable All"))
                    {
                        foreach (var plane in _viewer.ClippingPlanes)
                            plane.Enabled = true;
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Disable All"))
                    {
                        foreach (var plane in _viewer.ClippingPlanes)
                            plane.Enabled = false;
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Clear All"))
                    {
                        _viewer.ClippingPlanes.Clear();
                        _selectedPlaneIndex = -1;
                    }
                    
                    ImGui.Spacing();
                    
                    // Plane list
                    if (ImGui.BeginChild("PlaneList", new Vector2(0, 150), ImGuiChildFlags.Border))
                    {
                        for (int i = 0; i < _viewer.ClippingPlanes.Count; i++)
                        {
                            var plane = _viewer.ClippingPlanes[i];
                            ImGui.PushID(i);
                            
                            bool isSelected = _selectedPlaneIndex == i;
                            if (ImGui.Selectable($"{plane.Name}##plane", isSelected))
                            {
                                _selectedPlaneIndex = i;
                            }
                            
                            ImGui.SameLine();
                            ImGui.SetCursorPosX(ImGui.GetContentRegionAvail().X - 80);
                            
                            bool enabled = plane.Enabled;
                            if (ImGui.Checkbox("##enabled", ref enabled))
                            {
                                plane.Enabled = enabled;
                            }
                            
                            ImGui.SameLine();
                            if (ImGui.SmallButton("X"))
                            {
                                _viewer.ClippingPlanes.RemoveAt(i);
                                if (_selectedPlaneIndex >= _viewer.ClippingPlanes.Count)
                                    _selectedPlaneIndex = _viewer.ClippingPlanes.Count - 1;
                                ImGui.PopID();
                                break;
                            }
                            
                            ImGui.PopID();
                        }
                    }
                    ImGui.EndChild();
                    
                    // Edit selected plane
                    if (_selectedPlaneIndex >= 0 && _selectedPlaneIndex < _viewer.ClippingPlanes.Count)
                    {
                        ImGui.Separator();
                        ImGui.Text("Edit Clipping Plane");
                        
                        var plane = _viewer.ClippingPlanes[_selectedPlaneIndex];
                        
                        ImGui.PushID(_selectedPlaneIndex);
                        
                        // Name
                        string name = plane.Name;
                        if (ImGui.InputText("Name", ref name, 50))
                        {
                            plane.Name = name;
                        }
                        
                        // Rotation controls
                        ImGui.Text("Orientation (Euler Angles):");
                        bool rotationChanged = false;
                        
                        var rotation = plane.Rotation;
                        if (ImGui.DragFloat("Pitch (X)", ref rotation.X, 1.0f, -180, 180, "%.0f°"))
                            rotationChanged = true;
                        if (ImGui.DragFloat("Yaw (Y)", ref rotation.Y, 1.0f, -180, 180, "%.0f°"))
                            rotationChanged = true;
                        if (ImGui.DragFloat("Roll (Z)", ref rotation.Z, 1.0f, -180, 180, "%.0f°"))
                            rotationChanged = true;

                        if (rotationChanged)
                        {
                            plane.Rotation = rotation;
                            _viewer.UpdateClippingPlaneNormal(plane);
                        }
                        
                        // Position
                        ImGui.Text("Distance from Center:");
                        float distance = plane.Distance;
                        if (ImGui.SliderFloat("##distance", ref distance, -0.5f, 1.5f, "%.3f"))
                        {
                            plane.Distance = distance;
                        }
                        
                        // Mirror
                        bool mirror = plane.Mirror;
                        if (ImGui.Checkbox("Mirror (Cut Opposite Side)", ref mirror))
                        {
                            plane.Mirror = mirror;
                        }
                        
                        // Presets
                        ImGui.Spacing();
                        ImGui.Text("Quick Presets:");
                        if (ImGui.Button("XY", new Vector2(40, 0)))
                        {
                            plane.Rotation = new Vector3(0, 0, 0);
                            _viewer.UpdateClippingPlaneNormal(plane);
                        }
                        ImGui.SameLine();
                        if (ImGui.Button("XZ", new Vector2(40, 0)))
                        {
                            plane.Rotation = new Vector3(-90, 0, 0);
                            _viewer.UpdateClippingPlaneNormal(plane);
                        }
                        ImGui.SameLine();
                        if (ImGui.Button("YZ", new Vector2(40, 0)))
                        {
                            plane.Rotation = new Vector3(0, 90, 0);
                            _viewer.UpdateClippingPlaneNormal(plane);
                        }
                        ImGui.SameLine();
                        if (ImGui.Button("Diagonal", new Vector2(60, 0)))
                        {
                            plane.Rotation = new Vector3(-45, 45, 0);
                            _viewer.UpdateClippingPlaneNormal(plane);
                        }
                        
                        ImGui.PopID();
                    }
                }
                
                ImGui.Unindent();
            }
        }

        private void DrawExportTab()
        {
            ImGui.Text("Export Options");
            ImGui.Separator();
            ImGui.Text("Screenshot");
            if (ImGui.Button("Save Screenshot..."))
            {
                _screenshotDialog.Open("screenshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            }
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("3D Model Export");
            ImGui.TextWrapped("Export requires a dedicated meshing library (e.g., Marching Cubes). This is a placeholder.");
            ImGui.RadioButton("OBJ", ref _exportFileFormat, 0); ImGui.SameLine();
            ImGui.RadioButton("STL", ref _exportFileFormat, 1);
            if (ImGui.Button("Export 3D Model..."))
            {
                 _exportModelDialog.Open("model_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            }
        }

        private void HandleFileDialogs()
        {
            if (_screenshotDialog.Submit())
            {
                _viewer.SaveScreenshot(_screenshotDialog.SelectedPath);
            }
            if (_exportModelDialog.Submit())
            {
                Logger.LogWarning($"[CtVolume3DControlPanel] Model export to '{_exportModelDialog.SelectedPath}' is not implemented. A marching cubes library is required.");
            }
        }
    }
}