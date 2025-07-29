// GeoscientistToolkit/Data/CtImageStack/CtRenderingPanel.cs
using System;
using System.Numerics;
using GeoscientistToolkit.UI;
using ImGuiNET;
using System.Linq;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;

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

        // State for the clipping plane editor
        private int _selectedPlaneIndex = -1;
        private string _newPlaneName = "Plane";

        private int _planeToRenameIndex = -1;
        private string _renameBuffer = "";
        private bool _isRenamePopupOpen = false;
        
        private readonly ImGuiExportFileDialog _stlExportDialog;
        private static ProgressBarDialog _exportProgressDialog = new ProgressBarDialog("Exporting STL");


        public CtRenderingPanel(CtCombinedViewer viewer, CtImageStackDataset dataset)
            : base("Rendering Controls", new Vector2(350, 500))
        {
            _viewer = viewer ?? throw new ArgumentNullException(nameof(viewer));
            _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
            
            _stlExportDialog = new ImGuiExportFileDialog("ExportStlDialog", "Export Visible Volume as STL");
            _stlExportDialog.SetExtensions((".stl", "STL Mesh File"));
        }
        
        protected override void DrawContent()
        {
            // Submit the progress dialog if it's active. It will handle its own visibility.
            _exportProgressDialog.Submit();

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

                if (ImGui.BeginTabItem("Slicing"))
                {
                    DrawSlicingTab();
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
                
                if (ImGui.BeginTabItem("Export"))
                {
                    DrawExportTab();
                    ImGui.EndTabItem();
                }
                
                ImGui.EndTabBar();
            }
            
            if (_stlExportDialog.Submit())
            {
                HandleStlExport(_stlExportDialog.SelectedPath);
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
        
        private void DrawSlicingTab()
        {
            var volumeViewer = _viewer.VolumeViewer;
            if (volumeViewer == null)
            {
                ImGui.TextDisabled("3D Volume Viewer not available or not yet initialized.");
                ImGui.TextWrapped("To enable 3D slicing, import this dataset with the 'Optimized for 3D' option.");
                return;
            }

            ImGui.Text("Volume Slicing & Clipping");
            ImGui.Separator();

            // Visual aid toggle
            bool showPlaneVis = volumeViewer.ShowPlaneVisualizations;
            if (ImGui.Checkbox("Show All Visual Aids", ref showPlaneVis))
            {
                volumeViewer.ShowPlaneVisualizations = showPlaneVis;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Toggle visual representation of ALL cutting and clipping planes in the 3D view");

            ImGui.Separator();

            // Orthogonal Slices
            if (ImGui.CollapsingHeader("Orthogonal Slices (2D Views)", ImGuiTreeNodeFlags.DefaultOpen))
            {
                bool syncViews = _viewer.SyncViews;
                if (ImGui.Checkbox("Sync with 2D Views", ref syncViews))
                {
                    _viewer.SyncViews = syncViews;
                }

                if (!syncViews)
                {
                    ImGui.Indent();
                    ImGui.Text("X Slice (Red)");
                    ImGui.SliderFloat("##xslice", ref volumeViewer.SlicePositions.X, 0.0f, 1.0f, "%.3f");
                    ImGui.Text("Y Slice (Green)");
                    ImGui.SliderFloat("##yslice", ref volumeViewer.SlicePositions.Y, 0.0f, 1.0f, "%.3f");
                    ImGui.Text("Z Slice (Blue)");
                    ImGui.SliderFloat("##zslice", ref volumeViewer.SlicePositions.Z, 0.0f, 1.0f, "%.3f");
                    ImGui.Spacing();
                    if (ImGui.Button("Reset to Center")) volumeViewer.SlicePositions = new Vector3(0.5f);
                    ImGui.Unindent();
                }
            }

            // Axis-Aligned Cutting Planes
            if (ImGui.CollapsingHeader("Axis-Aligned Cutting Planes", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();

                // X Cutting Plane
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0.3f, 0.3f, 1)); ImGui.Text("X Cutting Plane"); ImGui.PopStyleColor();
                ImGui.Checkbox("Enable X Cut", ref volumeViewer.CutXEnabled);
                ImGui.SameLine();
                bool showAidX = volumeViewer.ShowCutXPlaneVisual;
                if (ImGui.Checkbox("Show Aid##X", ref showAidX)) volumeViewer.ShowCutXPlaneVisual = showAidX;
                if (volumeViewer.CutXEnabled)
                {
                    ImGui.Indent();
                    ImGui.SliderFloat("Position##X", ref volumeViewer.CutXPosition, 0.0f, 1.0f, "%.3f");
                    bool forward = volumeViewer.CutXForward;
                    if (ImGui.Checkbox("Cut Forward##X", ref forward)) volumeViewer.CutXForward = forward;
                    ImGui.SameLine();
                    if (ImGui.Button("Mirror##X")) volumeViewer.CutXForward = !volumeViewer.CutXForward;
                    ImGui.Unindent();
                }

                ImGui.Spacing();

                // Y Cutting Plane
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.3f, 1, 0.3f, 1)); ImGui.Text("Y Cutting Plane"); ImGui.PopStyleColor();
                ImGui.Checkbox("Enable Y Cut", ref volumeViewer.CutYEnabled);
                ImGui.SameLine();
                bool showAidY = volumeViewer.ShowCutYPlaneVisual;
                if(ImGui.Checkbox("Show Aid##Y", ref showAidY)) volumeViewer.ShowCutYPlaneVisual = showAidY;
                if (volumeViewer.CutYEnabled)
                {
                    ImGui.Indent();
                    ImGui.SliderFloat("Position##Y", ref volumeViewer.CutYPosition, 0.0f, 1.0f, "%.3f");
                    bool forward = volumeViewer.CutYForward;
                    if (ImGui.Checkbox("Cut Forward##Y", ref forward)) volumeViewer.CutYForward = forward;
                    ImGui.SameLine();
                    if (ImGui.Button("Mirror##Y")) volumeViewer.CutYForward = !volumeViewer.CutYForward;
                    ImGui.Unindent();
                }

                ImGui.Spacing();

                // Z Cutting Plane
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.3f, 0.3f, 1, 1)); ImGui.Text("Z Cutting Plane"); ImGui.PopStyleColor();
                ImGui.Checkbox("Enable Z Cut", ref volumeViewer.CutZEnabled);
                ImGui.SameLine();
                bool showAidZ = volumeViewer.ShowCutZPlaneVisual;
                if(ImGui.Checkbox("Show Aid##Z", ref showAidZ)) volumeViewer.ShowCutZPlaneVisual = showAidZ;
                if (volumeViewer.CutZEnabled)
                {
                    ImGui.Indent();
                    ImGui.SliderFloat("Position##Z", ref volumeViewer.CutZPosition, 0.0f, 1.0f, "%.3f");
                    bool forward = volumeViewer.CutZForward;
                    if (ImGui.Checkbox("Cut Forward##Z", ref forward)) volumeViewer.CutZForward = forward;
                    ImGui.SameLine();
                    if (ImGui.Button("Mirror##Z")) volumeViewer.CutZForward = !volumeViewer.CutZForward;
                    ImGui.Unindent();
                }

                ImGui.Unindent();
            }
            
            // Arbitrary Clipping Planes
            if (ImGui.CollapsingHeader("Arbitrary Clipping Planes"))
            {
                ImGui.Indent();
                
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X - 80);
                ImGui.InputText("##NewPlaneName", ref _newPlaneName, 50);
                ImGui.SameLine();
                if (ImGui.Button("Add Plane", new Vector2(80, 0)))
                {
                    volumeViewer.ClippingPlanes.Add(new ClippingPlane(_newPlaneName));
                    _newPlaneName = $"Plane {volumeViewer.ClippingPlanes.Count + 1}";
                }
                
                ImGui.Separator();
                
                if (volumeViewer.ClippingPlanes.Count > 0)
                {
                    if (ImGui.BeginChild("PlaneList", new Vector2(0, 100), ImGuiChildFlags.Border))
                    {
                        for (int i = 0; i < volumeViewer.ClippingPlanes.Count; i++)
                        {
                            var plane = volumeViewer.ClippingPlanes[i];
                            ImGui.PushID(i);
                            
                            if (ImGui.Selectable(plane.Name, _selectedPlaneIndex == i, ImGuiSelectableFlags.AllowOverlap))
                                _selectedPlaneIndex = i;

                            if (ImGui.BeginPopupContextItem($"PlaneContext_{i}"))
                            {
                                if (ImGui.MenuItem("Rename"))
                                {
                                    _planeToRenameIndex = i;
                                    _renameBuffer = plane.Name;
                                    _isRenamePopupOpen = true; 
                                    ImGui.OpenPopup("Rename Clipping Plane");
                                }
                                if (ImGui.MenuItem("Remove"))
                                {
                                    volumeViewer.ClippingPlanes.RemoveAt(i);
                                    if (_selectedPlaneIndex >= i) _selectedPlaneIndex--;
                                    ImGui.PopID();
                                    ImGui.EndPopup();
                                    break; 
                                }
                                ImGui.EndPopup();
                            }
                            
                            ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - 25);
                            bool enabled = plane.Enabled;
                            if (ImGui.Checkbox("##enabled", ref enabled))
                            {
                                plane.Enabled = enabled;
                            }
                            
                            ImGui.PopID();
                        }
                    }
                    ImGui.EndChild();
                    
                    if (_selectedPlaneIndex >= 0 && _selectedPlaneIndex < volumeViewer.ClippingPlanes.Count)
                    {
                        ImGui.Separator();
                        ImGui.Text("Edit Selected Plane");
                        var plane = volumeViewer.ClippingPlanes[_selectedPlaneIndex];
                        
                        string name = plane.Name;
                        if (ImGui.InputText("Name##edit", ref name, 50)) plane.Name = name;
                        
                        var rotation = plane.Rotation;
                        if (ImGui.DragFloat3("Rotation", ref rotation, 1.0f))
                        {
                            plane.Rotation = rotation;
                            volumeViewer.UpdateClippingPlaneNormal(plane);
                        }
                        
                        float distance = plane.Distance;
                        if (ImGui.SliderFloat("Distance", ref distance, -0.5f, 1.5f, "%.3f")) plane.Distance = distance;
                        
                        bool mirror = plane.Mirror;
                        if (ImGui.Checkbox("Mirror", ref mirror)) plane.Mirror = mirror;

                        ImGui.SameLine();
                        bool vis = plane.IsVisualizationVisible;
                        if (ImGui.Checkbox("Show Visual Aid", ref vis)) plane.IsVisualizationVisible = vis;
                    }
                }
                else
                {
                    ImGui.TextDisabled("No clipping planes defined.");
                }
                
                ImGui.Unindent();
            }

            if (_isRenamePopupOpen)
            {
                ImGui.SetNextWindowSize(new Vector2(300, 0));
                if (ImGui.BeginPopupModal("Rename Clipping Plane", ref _isRenamePopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
                {
                    ImGui.Text("Enter a new name for the plane:");
                    ImGui.InputText("##rename", ref _renameBuffer, 100);
                    if (ImGui.Button("OK", new Vector2(120, 0)))
                    {
                        if (_planeToRenameIndex != -1 && _planeToRenameIndex < volumeViewer.ClippingPlanes.Count)
                        {
                            volumeViewer.ClippingPlanes[_planeToRenameIndex].Name = _renameBuffer;
                        }
                        _planeToRenameIndex = -1;
                        _isRenamePopupOpen = false;
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Cancel", new Vector2(120, 0)))
                    {
                        _planeToRenameIndex = -1;
                        _isRenamePopupOpen = false;
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.EndPopup();
                }
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
        
        private void DrawExportTab()
        {
            ImGui.Text("3D Model Export (STL)");
            ImGui.Separator();
            ImGui.TextWrapped("This will export the currently visible 3D model to an STL file using a Greedy Meshing algorithm. This process can be slow for large or complex volumes.");
            ImGui.Spacing();

            // Check if export is possible
            bool canExport = _viewer.VolumeViewer != null && _dataset.LabelData != null;
            if (!canExport)
            {
                ImGui.TextColored(new Vector4(1, 0.5f, 0.5f, 1), "Cannot export:");
                if (_viewer.VolumeViewer == null) ImGui.BulletText("3D Viewer is not initialized.");
                if (_dataset.LabelData == null) ImGui.BulletText("Dataset has no label data.");
                ImGui.BeginDisabled();
            }

            if (ImGui.Button("Export Visible Volume as STL..."))
            {
                string defaultName = $"{_dataset.Name}_Export_{DateTime.Now:yyyyMMdd_HHmmss}";
                _stlExportDialog.Open(defaultName);
            }

            if (!canExport)
            {
                ImGui.EndDisabled();
            }
        }

        private void HandleStlExport(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || _viewer.VolumeViewer == null)
            {
                return;
            }

            // Define the progress reporting action
            Action<float, string> onProgress = (progress, message) =>
            {
                _exportProgressDialog.Update(progress, message);
                if (progress >= 1.0f)
                {
                    // Delay closing the dialog to allow the user to see the final message
                    System.Threading.Thread.Sleep(1500); 
                    _exportProgressDialog.Close();
                }
            };

            // Start the export process by opening the dialog
            _exportProgressDialog.Open("Initializing export...");
            _ = StlExporter.ExportVisibleToStlAsync(_dataset, _viewer, filePath, onProgress);
        }
    }
}