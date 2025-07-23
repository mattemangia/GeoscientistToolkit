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
        // --- THE FIX: ADD A FIELD TO HOLD THE DATASET ---
        private readonly CtImageStackDataset _dataset; 

        private readonly ImGuiExportFileDialog _screenshotDialog;
        private readonly ImGuiExportFileDialog _exportModelDialog;
        
        private int _cutXForward = 1;
        private int _cutYForward = 1;
        private int _cutZForward = 1;
        private int _exportFileFormat = 0;
        
        private float _rotationX = 0;
        private float _rotationY = 0;
        private float _rotationZ = 0;

        // --- THE FIX: UPDATE THE CONSTRUCTOR SIGNATURE ---
        public CtVolume3DControlPanel(CtVolume3DViewer viewer, CtImageStackDataset dataset)
            : base("3D Volume Controls", new Vector2(400, 600))
        {
            _viewer = viewer;
            _dataset = dataset; // Assign the dataset
            
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
                if (ImGui.BeginTabItem("Slices")) { DrawSlicesTab(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Cutting")) { DrawCuttingTab(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Clipping")) { DrawClippingTab(); ImGui.EndTabItem(); }
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

            // --- THE FIX: USE THE LOCAL _dataset FIELD ---
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
                // --- THE FIX: USE THE LOCAL _dataset FIELD ---
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

        private void DrawSlicesTab()
        {
            ImGui.Text("Orthogonal Slice Controls");
            ImGui.Separator();
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

        private void DrawCuttingTab()
        {
            ImGui.Text("Dataset Cutting Controls");
            ImGui.Separator();

            if (ImGui.CollapsingHeader("X Cutting Plane", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Checkbox("Enable X Cut", ref _viewer.CutXEnabled);
                if (_viewer.CutXEnabled)
                {
                    ImGui.RadioButton("Forward##X", ref _cutXForward, 1); ImGui.SameLine();
                    ImGui.RadioButton("Backward##X", ref _cutXForward, 0);
                    _viewer.CutXForward = _cutXForward == 1;
                    ImGui.SliderFloat("Position##X", ref _viewer.CutXPosition, 0.0f, 1.0f);
                }
            }
            if (ImGui.CollapsingHeader("Y Cutting Plane", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Checkbox("Enable Y Cut", ref _viewer.CutYEnabled);
                 if (_viewer.CutYEnabled)
                {
                    ImGui.RadioButton("Forward##Y", ref _cutYForward, 1); ImGui.SameLine();
                    ImGui.RadioButton("Backward##Y", ref _cutYForward, 0);
                    _viewer.CutYForward = _cutYForward == 1;
                    ImGui.SliderFloat("Position##Y", ref _viewer.CutYPosition, 0.0f, 1.0f);
                }
            }
             if (ImGui.CollapsingHeader("Z Cutting Plane", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Checkbox("Enable Z Cut", ref _viewer.CutZEnabled);
                 if (_viewer.CutZEnabled)
                {
                    ImGui.RadioButton("Forward##Z", ref _cutZForward, 1); ImGui.SameLine();
                    ImGui.RadioButton("Backward##Z", ref _cutZForward, 0);
                    _viewer.CutZForward = _cutZForward == 1;
                    ImGui.SliderFloat("Position##Z", ref _viewer.CutZPosition, 0.0f, 1.0f);
                }
            }
        }
        
        private void DrawClippingTab()
        {
            ImGui.Text("Arbitrary Clipping Plane");
            ImGui.Separator();
            ImGui.Checkbox("Enable Clipping Plane", ref _viewer.ClippingEnabled);

            if (_viewer.ClippingEnabled)
            {
                ImGui.Spacing();
                ImGui.Text("Plane Orientation (Euler Angles):");
                ImGui.Indent();
                bool changed = false;
                if (ImGui.SliderFloat("Pitch (X rot)", ref _rotationX, -180, 180, "%.0f°")) changed = true;
                if (ImGui.SliderFloat("Yaw (Y rot)", ref _rotationY, -180, 180, "%.0f°")) changed = true;
                if (ImGui.SliderFloat("Roll (Z rot)", ref _rotationZ, -180, 180, "%.0f°")) changed = true;
                if (changed) UpdateClippingPlaneNormal();
                ImGui.Unindent();
                ImGui.Spacing();
                ImGui.Text("Position along normal:");
                ImGui.SliderFloat("##distance", ref _viewer.ClippingDistance, 0.0f, 1.0f, "%.3f");
                ImGui.Checkbox("Mirror (cut other side)", ref _viewer.ClippingMirror);
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Text("Quick Presets:");
                if (ImGui.Button("XY")) SetClippingPreset(0, 0, 0); ImGui.SameLine();
                if (ImGui.Button("XZ")) SetClippingPreset(-90, 0, 0); ImGui.SameLine();
                if (ImGui.Button("YZ")) SetClippingPreset(0, 90, 0); ImGui.SameLine();
                if (ImGui.Button("Diagonal")) SetClippingPreset(-45, 45, 0);
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
        
        private void SetClippingPreset(float x, float y, float z)
        {
            _rotationX = x;
            _rotationY = y;
            _rotationZ = z;
            UpdateClippingPlaneNormal();
        }

        private void UpdateClippingPlaneNormal()
        {
            float pitchRad = _rotationX * MathF.PI / 180.0f;
            float yawRad = _rotationY * MathF.PI / 180.0f;
            float rollRad = _rotationZ * MathF.PI / 180.0f;
            var rotX = Matrix4x4.CreateRotationX(pitchRad);
            var rotY = Matrix4x4.CreateRotationY(yawRad);
            var rotZ = Matrix4x4.CreateRotationZ(rollRad);
            var rotation = rotZ * rotY * rotX; 
            _viewer.ClippingNormal = Vector3.Normalize(Vector3.Transform(-Vector3.UnitZ, rotation));
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