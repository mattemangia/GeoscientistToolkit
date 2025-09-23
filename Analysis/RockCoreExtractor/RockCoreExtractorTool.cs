// GeoscientistToolkit/Analysis/RockCoreExtractor/RockCoreExtractorTool.cs
using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.RockCoreExtractor
{
    /// <summary>
    /// Tool for extracting cylindrical rock cores from segmented CT data.
    /// Allows users to define a cylinder and remove all voxels outside it.
    /// </summary>
    public class RockCoreExtractorTool : IDatasetTools, IDisposable
    {
        public enum CircularView
        {
            XY_Circular_Z_Lateral,
            XZ_Circular_Y_Lateral,
            YZ_Circular_X_Lateral
        }

        private int _selectedViewIndex = 0; // 0=XY, 1=XZ, 2=YZ
        private CircularView _selectedView = CircularView.XY_Circular_Z_Lateral;
        private float _coreDiameter = 100f; // in voxels
        private float _coreLength = 200f; // in voxels
        private Vector2 _coreCenter = new Vector2(0.5f, 0.5f); // normalized coordinates
        private float _coreStartPosition = 0.1f; // normalized position along lateral axis
        
        private bool _showPreview = true;
        private bool _isProcessing = false;
        private float _progress = 0f;
        private string _statusMessage = "";
        
        // Preview management
        private byte[] _previewMask = null;
        private bool _needsPreviewUpdate = true;
        private CtImageStackDataset _currentDataset = null;
        private RockCoreOverlay _overlay = null;
        
        // Public properties for overlay access
        public bool ShowPreview => _showPreview;
        public RockCoreOverlay Overlay => _overlay;
        
        // Core parameters structure
        public struct CoreParameters
        {
            public CircularView View;
            public float Diameter;
            public float Length;
            public Vector2 Center;
            public float StartPosition;
        }
        
        public RockCoreExtractorTool()
        {
            Logger.Log("[RockCoreExtractorTool] Initialized");
        }

        public void Draw(Dataset dataset)
        {
            if (dataset is not CtImageStackDataset ctDataset)
            {
                ImGui.TextDisabled("Rock core extraction requires a CT Image Stack dataset.");
                return;
            }

            _currentDataset = ctDataset;
            
            // Create or update overlay
            if (_overlay == null || _overlay.Dataset != ctDataset)
            {
                _overlay = new RockCoreOverlay(this, ctDataset);
            }
            
            // Register with integration system for viewer overlay
            RockCoreIntegration.RegisterTool(ctDataset, this);

            if (_isProcessing)
            {
                ImGui.BeginDisabled();
            }

            // View selection
            ImGui.Text("Core Orientation:");
            if (ImGui.RadioButton("XY circular, Z lateral", ref _selectedViewIndex, 0))
                _selectedView = CircularView.XY_Circular_Z_Lateral;
            if (ImGui.RadioButton("XZ circular, Y lateral", ref _selectedViewIndex, 1))
                _selectedView = CircularView.XZ_Circular_Y_Lateral;
            if (ImGui.RadioButton("YZ circular, X lateral", ref _selectedViewIndex, 2))
                _selectedView = CircularView.YZ_Circular_X_Lateral;

            ImGui.Separator();

            // Core parameters
            ImGui.Text("Core Parameters:");
            
            float maxDiameter = GetMaxDiameter(ctDataset);
            float maxLength = GetMaxLength(ctDataset);
            
            if (ImGui.SliderFloat("Diameter (voxels)", ref _coreDiameter, 10f, maxDiameter, "%.1f"))
            {
                _needsPreviewUpdate = true;
            }
            
            if (ImGui.SliderFloat("Length (voxels)", ref _coreLength, 10f, maxLength, "%.1f"))
            {
                _needsPreviewUpdate = true;
            }

            // Center position
            ImGui.Text("Center Position:");
            float centerX = _coreCenter.X * GetCircularViewWidth(ctDataset);
            float centerY = _coreCenter.Y * GetCircularViewHeight(ctDataset);
            
            bool centerChanged = false;
            if (ImGui.DragFloat("Center X", ref centerX, 1f, 0, GetCircularViewWidth(ctDataset)))
            {
                _coreCenter.X = centerX / GetCircularViewWidth(ctDataset);
                centerChanged = true;
            }
            if (ImGui.DragFloat("Center Y", ref centerY, 1f, 0, GetCircularViewHeight(ctDataset)))
            {
                _coreCenter.Y = centerY / GetCircularViewHeight(ctDataset);
                centerChanged = true;
            }
            
            if (centerChanged)
            {
                _needsPreviewUpdate = true;
            }

            // Starting position along lateral axis
            if (ImGui.SliderFloat("Start Position", ref _coreStartPosition, 0f, 1f - (_coreLength / maxLength), "%.2f"))
            {
                _needsPreviewUpdate = true;
            }

            ImGui.Separator();

            // Preview toggle
            if (ImGui.Checkbox("Show Preview", ref _showPreview))
            {
                if (_showPreview)
                {
                    _needsPreviewUpdate = true;
                }
                else
                {
                    ClearPreview();
                }
            }

            // Physical dimensions display
            if (ctDataset.PixelSize > 0 && !string.IsNullOrEmpty(ctDataset.Unit))
            {
                ImGui.Separator();
                ImGui.Text("Physical Dimensions:");
                float diameterPhys = _coreDiameter * ctDataset.PixelSize;
                float lengthPhys = _coreLength * GetLateralPixelSize(ctDataset);
                ImGui.Text($"Diameter: {diameterPhys:F2} {ctDataset.Unit}");
                ImGui.Text($"Length: {lengthPhys:F2} {ctDataset.Unit}");
                
                // Volume calculation
                float volumeMm3 = (float)(Math.PI * Math.Pow(diameterPhys / 2, 2) * lengthPhys);
                if (ctDataset.Unit == "µm")
                {
                    volumeMm3 /= 1000000f; // Convert from µm³ to mm³
                    ImGui.Text($"Volume: {volumeMm3:F3} mm³");
                }
            }

            ImGui.Separator();

            // Extract button
            if (_isProcessing)
            {
                ImGui.EndDisabled();
                ImGui.ProgressBar(_progress, new Vector2(-1, 0), _statusMessage);
            }
            else
            {
                if (ImGui.Button("Extract Core", new Vector2(-1, 0)))
                {
                    _ = ExtractCoreAsync(ctDataset);
                }
                
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("This will permanently modify the segmented materials,\nremoving all voxels outside the defined cylinder.");
                }
            }

            // Status messages
            if (!string.IsNullOrEmpty(_statusMessage) && !_isProcessing)
            {
                ImGui.TextWrapped(_statusMessage);
            }

            // Update preview if needed
            if (_showPreview && _needsPreviewUpdate)
            {
                UpdatePreview(ctDataset);
            }
        }

        // Public methods for overlay interaction
        public CoreParameters GetCoreParameters()
        {
            return new CoreParameters
            {
                View = _selectedView,
                Diameter = _coreDiameter,
                Length = _coreLength,
                Center = _coreCenter,
                StartPosition = _coreStartPosition
            };
        }
        
        public void SetCoreDiameter(float diameter)
        {
            _coreDiameter = Math.Clamp(diameter, 10f, GetMaxDiameter(_currentDataset));
            _needsPreviewUpdate = true;
        }
        
        public void SetCoreLength(float length)
        {
            _coreLength = Math.Clamp(length, 10f, GetMaxLength(_currentDataset));
            _needsPreviewUpdate = true;
        }
        
        public void SetCoreCenter(Vector2 center)
        {
            _coreCenter = new Vector2(
                Math.Clamp(center.X, 0f, 1f),
                Math.Clamp(center.Y, 0f, 1f)
            );
            _needsPreviewUpdate = true;
        }
        
        public void SetCoreStartPosition(float position)
        {
            _coreStartPosition = Math.Clamp(position, 0f, 1f - (_coreLength / GetMaxLength(_currentDataset)));
            _needsPreviewUpdate = true;
        }

        private void UpdatePreview(CtImageStackDataset dataset)
        {
            if (dataset.VolumeData == null || dataset.LabelData == null)
            {
                return;
            }

            _needsPreviewUpdate = false;
            
            // Create preview mask
            int width = dataset.Width;
            int height = dataset.Height;
            int depth = dataset.Depth;
            
            _previewMask = new byte[width * height * depth];
            
            // Fill preview mask based on cylinder parameters
            FillCylinderMask(_previewMask, dataset);
            
            // Send preview to viewers
            var previewColor = new Vector4(0.2f, 0.8f, 0.2f, 0.5f); // Semi-transparent green
            CtImageStackTools.Update3DPreviewFromExternal(dataset, _previewMask, previewColor);
        }

        private void ClearPreview()
        {
            if (_currentDataset != null)
            {
                CtImageStackTools.Update3DPreviewFromExternal(_currentDataset, null, Vector4.Zero);
            }
            _previewMask = null;
        }

        private void FillCylinderMask(byte[] mask, CtImageStackDataset dataset)
        {
            int width = dataset.Width;
            int height = dataset.Height;
            int depth = dataset.Depth;
            
            float radius = _coreDiameter / 2f;
            float centerX, centerY;
            int startIdx, endIdx;
            
            switch (_selectedView)
            {
                case CircularView.XY_Circular_Z_Lateral:
                    centerX = _coreCenter.X * width;
                    centerY = _coreCenter.Y * height;
                    startIdx = (int)(_coreStartPosition * depth);
                    endIdx = Math.Min(depth, startIdx + (int)_coreLength);
                    
                    Parallel.For(startIdx, endIdx, z =>
                    {
                        for (int y = 0; y < height; y++)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                float dx = x - centerX;
                                float dy = y - centerY;
                                if (dx * dx + dy * dy <= radius * radius)
                                {
                                    mask[z * width * height + y * width + x] = 255;
                                }
                            }
                        }
                    });
                    break;
                    
                case CircularView.XZ_Circular_Y_Lateral:
                    centerX = _coreCenter.X * width;
                    centerY = _coreCenter.Y * depth;
                    startIdx = (int)(_coreStartPosition * height);
                    endIdx = Math.Min(height, startIdx + (int)_coreLength);
                    
                    Parallel.For(0, depth, z =>
                    {
                        for (int y = startIdx; y < endIdx; y++)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                float dx = x - centerX;
                                float dz = z - centerY;
                                if (dx * dx + dz * dz <= radius * radius)
                                {
                                    mask[z * width * height + y * width + x] = 255;
                                }
                            }
                        }
                    });
                    break;
                    
                case CircularView.YZ_Circular_X_Lateral:
                    centerX = _coreCenter.X * height;
                    centerY = _coreCenter.Y * depth;
                    startIdx = (int)(_coreStartPosition * width);
                    endIdx = Math.Min(width, startIdx + (int)_coreLength);
                    
                    Parallel.For(0, depth, z =>
                    {
                        for (int y = 0; y < height; y++)
                        {
                            for (int x = startIdx; x < endIdx; x++)
                            {
                                float dy = y - centerX;
                                float dz = z - centerY;
                                if (dy * dy + dz * dz <= radius * radius)
                                {
                                    mask[z * width * height + y * width + x] = 255;
                                }
                            }
                        }
                    });
                    break;
            }
        }

        private async Task ExtractCoreAsync(CtImageStackDataset dataset)
        {
            if (dataset.LabelData == null)
            {
                _statusMessage = "No segmented materials found. Please segment materials first.";
                return;
            }

            _isProcessing = true;
            _progress = 0f;
            _statusMessage = "Extracting core...";
            
            ClearPreview(); // Clear preview during processing

            await Task.Run(() =>
            {
                try
                {
                    int width = dataset.Width;
                    int height = dataset.Height;
                    int depth = dataset.Depth;
                    
                    float radius = _coreDiameter / 2f;
                    int modifiedVoxels = 0;
                    int totalVoxels = 0;
                    
                    // Process based on selected view
                    switch (_selectedView)
                    {
                        case CircularView.XY_Circular_Z_Lateral:
                            ProcessXYCircularView(dataset, radius, ref modifiedVoxels, ref totalVoxels);
                            break;
                        case CircularView.XZ_Circular_Y_Lateral:
                            ProcessXZCircularView(dataset, radius, ref modifiedVoxels, ref totalVoxels);
                            break;
                        case CircularView.YZ_Circular_X_Lateral:
                            ProcessYZCircularView(dataset, radius, ref modifiedVoxels, ref totalVoxels);
                            break;
                    }
                    
                    // Save the modified labels
                    dataset.SaveLabelData();
                    
                    // Notify system of changes
                    Business.ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
                    Business.ProjectManager.Instance.HasUnsavedChanges = true;
                    
                    _statusMessage = $"Core extracted successfully. {modifiedVoxels:N0} voxels removed from materials.";
                    Logger.Log($"[RockCoreExtractorTool] Extraction complete. Modified {modifiedVoxels} out of {totalVoxels} non-exterior voxels.");
                }
                catch (Exception ex)
                {
                    _statusMessage = $"Error during extraction: {ex.Message}";
                    Logger.LogError($"[RockCoreExtractorTool] Extraction failed: {ex}");
                }
            });

            _isProcessing = false;
            _progress = 1f;
        }

        private void ProcessXYCircularView(CtImageStackDataset dataset, float radius, ref int modifiedVoxels, ref int totalVoxels)
        {
            int width = dataset.Width;
            int height = dataset.Height;
            int depth = dataset.Depth;
            
            float centerX = _coreCenter.X * width;
            float centerY = _coreCenter.Y * height;
            int startZ = (int)(_coreStartPosition * depth);
            int endZ = Math.Min(depth, startZ + (int)_coreLength);
            
            // Use local counters for thread-safe operations
            int localModified = 0;
            int localTotal = 0;
            object lockObj = new object();
            
            Parallel.For(0, depth, z =>
            {
                byte[] sliceData = new byte[width * height];
                dataset.LabelData.ReadSliceZ(z, sliceData);
                bool modified = false;
                int sliceModified = 0;
                int sliceTotal = 0;
                
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int idx = y * width + x;
                        if (sliceData[idx] != 0) // Not exterior
                        {
                            sliceTotal++;
                            
                            // Check if outside cylinder
                            bool outsideLateral = z < startZ || z >= endZ;
                            float dx = x - centerX;
                            float dy = y - centerY;
                            bool outsideRadius = (dx * dx + dy * dy) > (radius * radius);
                            
                            if (outsideLateral || outsideRadius)
                            {
                                sliceData[idx] = 0; // Set to exterior
                                sliceModified++;
                                modified = true;
                            }
                        }
                    }
                }
                
                if (modified)
                {
                    dataset.LabelData.WriteSliceZ(z, sliceData);
                }
                
                // Thread-safe update of counters
                lock (lockObj)
                {
                    localModified += sliceModified;
                    localTotal += sliceTotal;
                }
                
                _progress = (float)z / depth;
            });
            
            modifiedVoxels = localModified;
            totalVoxels = localTotal;
        }

        private void ProcessXZCircularView(CtImageStackDataset dataset, float radius, ref int modifiedVoxels, ref int totalVoxels)
        {
            int width = dataset.Width;
            int height = dataset.Height;
            int depth = dataset.Depth;
            
            float centerX = _coreCenter.X * width;
            float centerZ = _coreCenter.Y * depth;
            int startY = (int)(_coreStartPosition * height);
            int endY = Math.Min(height, startY + (int)_coreLength);
            
            // Use local counters for thread-safe operations
            int localModified = 0;
            int localTotal = 0;
            object lockObj = new object();
            
            Parallel.For(0, depth, z =>
            {
                byte[] sliceData = new byte[width * height];
                dataset.LabelData.ReadSliceZ(z, sliceData);
                bool modified = false;
                int sliceModified = 0;
                int sliceTotal = 0;
                
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int idx = y * width + x;
                        if (sliceData[idx] != 0) // Not exterior
                        {
                            sliceTotal++;
                            
                            // Check if outside cylinder
                            bool outsideLateral = y < startY || y >= endY;
                            float dx = x - centerX;
                            float dz = z - centerZ;
                            bool outsideRadius = (dx * dx + dz * dz) > (radius * radius);
                            
                            if (outsideLateral || outsideRadius)
                            {
                                sliceData[idx] = 0; // Set to exterior
                                sliceModified++;
                                modified = true;
                            }
                        }
                    }
                }
                
                if (modified)
                {
                    dataset.LabelData.WriteSliceZ(z, sliceData);
                }
                
                // Thread-safe update of counters
                lock (lockObj)
                {
                    localModified += sliceModified;
                    localTotal += sliceTotal;
                }
                
                _progress = (float)z / depth;
            });
            
            modifiedVoxels = localModified;
            totalVoxels = localTotal;
        }

        private void ProcessYZCircularView(CtImageStackDataset dataset, float radius, ref int modifiedVoxels, ref int totalVoxels)
        {
            int width = dataset.Width;
            int height = dataset.Height;
            int depth = dataset.Depth;
            
            float centerY = _coreCenter.X * height;
            float centerZ = _coreCenter.Y * depth;
            int startX = (int)(_coreStartPosition * width);
            int endX = Math.Min(width, startX + (int)_coreLength);
            
            // Use local counters for thread-safe operations
            int localModified = 0;
            int localTotal = 0;
            object lockObj = new object();
            
            Parallel.For(0, depth, z =>
            {
                byte[] sliceData = new byte[width * height];
                dataset.LabelData.ReadSliceZ(z, sliceData);
                bool modified = false;
                int sliceModified = 0;
                int sliceTotal = 0;
                
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int idx = y * width + x;
                        if (sliceData[idx] != 0) // Not exterior
                        {
                            sliceTotal++;
                            
                            // Check if outside cylinder
                            bool outsideLateral = x < startX || x >= endX;
                            float dy = y - centerY;
                            float dz = z - centerZ;
                            bool outsideRadius = (dy * dy + dz * dz) > (radius * radius);
                            
                            if (outsideLateral || outsideRadius)
                            {
                                sliceData[idx] = 0; // Set to exterior
                                sliceModified++;
                                modified = true;
                            }
                        }
                    }
                }
                
                if (modified)
                {
                    dataset.LabelData.WriteSliceZ(z, sliceData);
                }
                
                // Thread-safe update of counters
                lock (lockObj)
                {
                    localModified += sliceModified;
                    localTotal += sliceTotal;
                }
                
                _progress = (float)z / depth;
            });
            
            modifiedVoxels = localModified;
            totalVoxels = localTotal;
        }

        private float GetMaxDiameter(CtImageStackDataset dataset)
        {
            return _selectedView switch
            {
                CircularView.XY_Circular_Z_Lateral => Math.Min(dataset.Width, dataset.Height),
                CircularView.XZ_Circular_Y_Lateral => Math.Min(dataset.Width, dataset.Depth),
                CircularView.YZ_Circular_X_Lateral => Math.Min(dataset.Height, dataset.Depth),
                _ => 100f
            };
        }

        private float GetMaxLength(CtImageStackDataset dataset)
        {
            return _selectedView switch
            {
                CircularView.XY_Circular_Z_Lateral => dataset.Depth,
                CircularView.XZ_Circular_Y_Lateral => dataset.Height,
                CircularView.YZ_Circular_X_Lateral => dataset.Width,
                _ => 100f
            };
        }

        private float GetCircularViewWidth(CtImageStackDataset dataset)
        {
            return _selectedView switch
            {
                CircularView.XY_Circular_Z_Lateral => dataset.Width,
                CircularView.XZ_Circular_Y_Lateral => dataset.Width,
                CircularView.YZ_Circular_X_Lateral => dataset.Height,
                _ => dataset.Width
            };
        }

        private float GetCircularViewHeight(CtImageStackDataset dataset)
        {
            return _selectedView switch
            {
                CircularView.XY_Circular_Z_Lateral => dataset.Height,
                CircularView.XZ_Circular_Y_Lateral => dataset.Depth,
                CircularView.YZ_Circular_X_Lateral => dataset.Depth,
                _ => dataset.Height
            };
        }

        private float GetLateralPixelSize(CtImageStackDataset dataset)
        {
            return _selectedView switch
            {
                CircularView.XY_Circular_Z_Lateral => dataset.SliceThickness,
                CircularView.XZ_Circular_Y_Lateral => dataset.PixelSize,
                CircularView.YZ_Circular_X_Lateral => dataset.PixelSize,
                _ => dataset.PixelSize
            };
        }

        public void Dispose()
        {
            ClearPreview();
            
            // Unregister from integration system
            if (_currentDataset != null)
            {
                RockCoreIntegration.UnregisterTool(_currentDataset);
            }
            
            _previewMask = null;
            _currentDataset = null;
            _overlay = null;
        }
    }
}