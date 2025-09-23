// GeoscientistToolkit/Analysis/Transform/TransformTool.cs
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.UI;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.Transform
{
    /// <summary>
    /// Tool for transforming a CT dataset (rotation, scaling, translation, cropping, resampling).
    /// </summary>
    public class TransformTool : IDatasetTools, IDisposable
    {
        private CtImageStackDataset _currentDataset;
        private TransformOverlay _overlay;

        // Public property to expose the overlay
        public TransformOverlay Overlay => _overlay;

        // Transform parameters
        internal Vector3 _translation = Vector3.Zero; // In voxels
        internal Vector3 _rotation = Vector3.Zero;    // In degrees
        internal Vector3 _scale = Vector3.One;        // Multiplier

        // Cropping (relative to the original volume)
        private Vector3 _cropMin = Vector3.Zero;
        private Vector3 _cropMax = Vector3.One;

        // Resampling
        private enum ResampleMode { VoxelCount, VoxelSize }
        private ResampleMode _resampleMode = ResampleMode.VoxelCount;
        private Vector3 _newVoxelCount = Vector3.Zero;
        private Vector3 _newVoxelSize = Vector3.Zero;
        private bool _lockAspectRatio = true;
        
        // UI and processing state
        private bool _showPreview = true;
        private bool _isProcessing = false;
        private float _progress = 0f;
        private string _statusMessage = "";

        // Dependencies
        private readonly ProgressBarDialog _progressDialog;
        private readonly CtImageStackExportDialog _exportDialog;
        private static readonly ConfirmationDialog _confirmApplyDialog = new ConfirmationDialog(
            "Confirm Apply", 
            "This will permanently replace the current dataset in the project with the transformed version.\nThis action cannot be undone.\n\nAre you sure you want to proceed?"
        );

        public TransformTool()
        {
            _progressDialog = new ProgressBarDialog("Transforming Dataset");
            _exportDialog = new CtImageStackExportDialog();
        }

        public void Draw(Dataset dataset)
        {
            if (dataset is not CtImageStackDataset ctDataset)
            {
                ImGui.TextDisabled("Transform tool requires an editable CT Image Stack dataset.");
                return;
            }

            if (_currentDataset != ctDataset || _newVoxelCount == Vector3.Zero)
            {
                InitializeForDataset(ctDataset);
            }

            // Create or update overlay
            if (_overlay == null || _overlay.Dataset != ctDataset)
            {
                _overlay = new TransformOverlay(this, ctDataset);
            }
            
            // Register with integration system
            TransformIntegration.RegisterTool(ctDataset, this);
            
            if (_isProcessing) ImGui.BeginDisabled();

            ImGui.Checkbox("Show Interactive Overlay", ref _showPreview);
            ImGui.Separator();

            // --- Transform Section ---
            if (ImGui.CollapsingHeader("Transform", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.DragFloat3("Translation (voxels)", ref _translation, 1.0f);
                ImGui.DragFloat3("Rotation (degrees)", ref _rotation, 1.0f);
                ImGui.DragFloat3("Scale", ref _scale, 0.01f);
                if (ImGui.Button("Reset Transform"))
                {
                    _translation = Vector3.Zero;
                    _rotation = Vector3.Zero;
                    _scale = Vector3.One;
                }
            }

            // --- Crop Section ---
            if (ImGui.CollapsingHeader("Crop", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Text("Min Corner (X, Y, Z)");
                ImGui.DragFloatRange2("X", ref _cropMin.X, ref _cropMax.X, 0.005f, 0.0f, 1.0f, "%.3f", "%.3f");
                ImGui.DragFloatRange2("Y", ref _cropMin.Y, ref _cropMax.Y, 0.005f, 0.0f, 1.0f, "%.3f", "%.3f");
                ImGui.DragFloatRange2("Z", ref _cropMin.Z, ref _cropMax.Z, 0.005f, 0.0f, 1.0f, "%.3f", "%.3f");
                if (ImGui.Button("Reset Crop"))
                {
                    _cropMin = Vector3.Zero;
                    _cropMax = Vector3.One;
                }
            }
            
            // --- Resample Section ---
            if (ImGui.CollapsingHeader("Resample", ImGuiTreeNodeFlags.DefaultOpen))
            {
                if (ImGui.RadioButton("By Voxel Count", _resampleMode == ResampleMode.VoxelCount)) _resampleMode = ResampleMode.VoxelCount;
                ImGui.SameLine();
                if (ImGui.RadioButton("By Voxel Size", _resampleMode == ResampleMode.VoxelSize)) _resampleMode = ResampleMode.VoxelSize;

                if (_resampleMode == ResampleMode.VoxelCount)
                {
                    int x = (int)_newVoxelCount.X, y = (int)_newVoxelCount.Y, z = (int)_newVoxelCount.Z;
                    if (ImGui.InputInt3("New Dimensions", ref x))
                    {
                        _newVoxelCount = new Vector3(x, y, z);
                        UpdateVoxelSizeFromCount();
                    }
                }
                else
                {
                    if (ImGui.InputFloat3($"New Voxel Size ({_currentDataset.Unit})", ref _newVoxelSize))
                    {
                        UpdateVoxelCountFromSize();
                    }
                }
                ImGui.Checkbox("Lock Aspect Ratio", ref _lockAspectRatio);
                if (ImGui.Button("Reset Resampling"))
                {
                    InitializeResampleParameters(ctDataset);
                }
                
                ImGui.TextDisabled($"Resulting Voxel Size: {_newVoxelSize.X:F2}x{_newVoxelSize.Y:F2}x{_newVoxelSize.Z:F2} {_currentDataset.Unit}");
                ImGui.TextDisabled($"Resulting Dimensions: {(int)_newVoxelCount.X}x{(int)_newVoxelCount.Y}x{(int)_newVoxelCount.Z}");
            }

            ImGui.Separator();

            // --- Actions ---
            if (_isProcessing)
            {
                ImGui.EndDisabled();
                _progressDialog.Submit();
            }
            else
            {
                if (ImGui.Button("Apply", new Vector2(ImGui.GetContentRegionAvail().X * 0.5f - 5, 0)))
                {
                    _confirmApplyDialog.Open();
                }
                ImGui.SameLine();
                if (ImGui.Button("Export", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
                {
                    _ = ProcessAndExportAsync();
                }
            }
            
            if (_confirmApplyDialog.Submit())
            {
                 _ = ProcessAndApplyAsync();
            }
        }
        
        private async Task ProcessAndApplyAsync()
        {
            _isProcessing = true;
            _progressDialog.Open("Initializing...");

            // Store a reference to the old dataset before it's replaced
            var oldDataset = _currentDataset;
            
            var transformedDataset = await CreateTransformedDatasetAsync();
            
            _progressDialog.Close();
            _isProcessing = false;
            
            if (transformedDataset != null)
            {
                // Effective replacement: add new, then remove old.
                ProjectManager.Instance.AddDataset(transformedDataset);
                ProjectManager.Instance.RemoveDataset(oldDataset);
                
                // Close the viewer associated with the old dataset. The main UI will open a new one if it's selected.
                DatasetViewPanel.CloseViewFor(oldDataset);

                _statusMessage = "Dataset transformed successfully.";
                // Do not re-initialize here, let the UI framework create a new tool for the new dataset.
            }
            else
            {
                _statusMessage = "Transformation failed. Check logs for details.";
            }
        }

        private async Task ProcessAndExportAsync()
        {
            _isProcessing = true;
            _progressDialog.Open("Preparing for export...");

            var transformedDataset = await CreateTransformedDatasetAsync();
            
            _progressDialog.Close();
            _isProcessing = false;
            
            if (transformedDataset != null)
            {
                _exportDialog.Open(transformedDataset);
                // Note: The UI loop needs to call _exportDialog.Submit()
            }
            else
            {
                _statusMessage = "Could not create transformed dataset for export.";
            }
        }

        private async Task<CtImageStackDataset> CreateTransformedDatasetAsync()
        {
            var source = _currentDataset;
            
            // Calculate transformation matrix (applies scale and rotation around the center)
            var center = new Vector3(source.Width / 2f, source.Height / 2f, source.Depth / 2f);
            var toOrigin = Matrix4x4.CreateTranslation(-center);
            var fromOrigin = Matrix4x4.CreateTranslation(center);
            var scaleMatrix = Matrix4x4.CreateScale(_scale);
            var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(
                _rotation.Y * (MathF.PI / 180f),
                _rotation.X * (MathF.PI / 180f),
                _rotation.Z * (MathF.PI / 180f)
            );
            var translationMatrix = Matrix4x4.CreateTranslation(_translation);
            var transformMatrix = toOrigin * scaleMatrix * rotationMatrix * fromOrigin * translationMatrix;
            
            if (!Matrix4x4.Invert(transformMatrix, out var inverseTransform))
            {
                Logger.LogError("[TransformTool] Transform matrix is not invertible.");
                return null;
            }

            // Define source bounding box based on crop settings
            var srcMin = new Vector3(source.Width * _cropMin.X, source.Height * _cropMin.Y, source.Depth * _cropMin.Z);
            var srcMax = new Vector3(source.Width * _cropMax.X, source.Height * _cropMax.Y, source.Depth * _cropMax.Z);

            // Create new volumes
            int destWidth = (int)_newVoxelCount.X;
            int destHeight = (int)_newVoxelCount.Y;
            int destDepth = (int)_newVoxelCount.Z;
            
            var newGrayscaleVolume = new ChunkedVolume(destWidth, destHeight, destDepth);
            var newLabelVolume = new ChunkedLabelVolume(destWidth, destHeight, destDepth, newGrayscaleVolume.ChunkDim, false);
            
            await Task.Run(() =>
            {
                Parallel.For(0, destDepth, z =>
                {
                    if (_progressDialog.IsCancellationRequested) return;
                    
                    for (int y = 0; y < destHeight; y++)
                    {
                        for (int x = 0; x < destWidth; x++)
                        {
                            // Map destination voxel back to source space
                            var destPos = new Vector3(x, y, z);
                            var sourcePos = Vector3.Transform(destPos, inverseTransform);

                            // Check if inside the cropped source bounds
                            if (sourcePos.X >= srcMin.X && sourcePos.X < srcMax.X &&
                                sourcePos.Y >= srcMin.Y && sourcePos.Y < srcMax.Y &&
                                sourcePos.Z >= srcMin.Z && sourcePos.Z < srcMax.Z)
                            {
                                // Sample using interpolation
                                newGrayscaleVolume[x, y, z] = SampleGrayscale(source.VolumeData, sourcePos);
                                newLabelVolume[x, y, z] = SampleLabel(source.LabelData, sourcePos);
                            }
                        }
                    }
                    
                    float currentProgress = (float)(z + 1) / destDepth;
                    _progressDialog.Update(currentProgress, $"Processing slice {z + 1}/{destDepth}");
                });
            });

            if (_progressDialog.IsCancellationRequested) return null;

            // Create new dataset object
            var newDataset = new CtImageStackDataset($"{source.Name}_transformed", source.FilePath)
            {
                Width = destWidth,
                Height = destHeight,
                Depth = destDepth,
                PixelSize = _newVoxelSize.X,
                SliceThickness = _newVoxelSize.Z, // Assuming Z is slice thickness
                Unit = source.Unit,
                Materials = source.Materials.Select(m => new Material(m.ID, m.Name, m.Color) 
                { 
                    IsVisible = m.IsVisible, 
                    Density = m.Density, 
                    IsExterior = m.IsExterior, 
                    MaxValue = m.MaxValue, 
                    MinValue = m.MinValue 
                }).ToList(), // Deep copy materials
            };
            
            // This is a bit of a hack to work around private setters, which should be improved in the future.
            var volumeDataField = newDataset.GetType().GetField("_volumeData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (volumeDataField != null)
            {
                 volumeDataField.SetValue(newDataset, newGrayscaleVolume);
            }
            newDataset.LabelData = newLabelVolume;

            return newDataset;
        }
        
        private byte SampleGrayscale(IGrayscaleVolumeData volume, Vector3 pos)
        {
            // Trilinear interpolation
            int x0 = (int)Math.Floor(pos.X); int x1 = x0 + 1;
            int y0 = (int)Math.Floor(pos.Y); int y1 = y0 + 1;
            int z0 = (int)Math.Floor(pos.Z); int z1 = z0 + 1;

            if (x0 < 0 || x1 >= volume.Width || y0 < 0 || y1 >= volume.Height || z0 < 0 || z1 >= volume.Depth) return 0;

            float xd = pos.X - x0;
            float yd = pos.Y - y0;
            float zd = pos.Z - z0;

            byte c000 = volume[x0, y0, z0]; byte c100 = volume[x1, y0, z0];
            byte c010 = volume[x0, y1, z0]; byte c110 = volume[x1, y1, z0];
            byte c001 = volume[x0, y0, z1]; byte c101 = volume[x1, y0, z1];
            byte c011 = volume[x0, y1, z1]; byte c111 = volume[x1, y1, z1];

            float c00 = c000 * (1 - xd) + c100 * xd;
            float c01 = c001 * (1 - xd) + c101 * xd;
            float c10 = c010 * (1 - xd) + c110 * xd;
            float c11 = c011 * (1 - xd) + c111 * xd;

            float c0 = c00 * (1 - yd) + c10 * yd;
            float c1 = c01 * (1 - yd) + c11 * yd;

            return (byte)(c0 * (1 - zd) + c1 * zd);
        }

        private byte SampleLabel(ILabelVolumeData volume, Vector3 pos)
        {
            // Nearest neighbor interpolation
            int x = (int)Math.Round(pos.X);
            int y = (int)Math.Round(pos.Y);
            int z = (int)Math.Round(pos.Z);

            if (x < 0 || x >= volume.Width || y < 0 || y >= volume.Height || z < 0 || z >= volume.Depth) return 0;
            
            return volume[x, y, z];
        }

        private void InitializeForDataset(CtImageStackDataset ds)
        {
            _currentDataset = ds;
            _translation = Vector3.Zero;
            _rotation = Vector3.Zero;
            _scale = Vector3.One;
            _cropMin = Vector3.Zero;
            _cropMax = Vector3.One;
            InitializeResampleParameters(ds);
        }

        private void InitializeResampleParameters(CtImageStackDataset ds)
        {
             _newVoxelCount = new Vector3(ds.Width, ds.Height, ds.Depth);
             _newVoxelSize = new Vector3(ds.PixelSize, ds.PixelSize, ds.SliceThickness);
        }
        
        private void UpdateVoxelSizeFromCount()
        {
            if (_lockAspectRatio && _currentDataset != null)
            {
                float originalAspectXY = (float)_currentDataset.Width / _currentDataset.Height;
                float newAspectXY = _newVoxelCount.X / _newVoxelCount.Y;
                if (Math.Abs(originalAspectXY - newAspectXY) > 0.01f)
                {
                    _newVoxelCount.Y = _newVoxelCount.X / originalAspectXY;
                }
            }
            
            _newVoxelSize.X = (_currentDataset.Width * _currentDataset.PixelSize) / _newVoxelCount.X;
            _newVoxelSize.Y = (_currentDataset.Height * _currentDataset.PixelSize) / _newVoxelCount.Y;
            _newVoxelSize.Z = (_currentDataset.Depth * _currentDataset.SliceThickness) / _newVoxelCount.Z;
        }
        
        private void UpdateVoxelCountFromSize()
        {
            if (_lockAspectRatio && _currentDataset != null)
            {
                float originalAspectXY = _currentDataset.PixelSize / _currentDataset.PixelSize; // is 1
                float newAspectXY = _newVoxelSize.X / _newVoxelSize.Y;
                if (Math.Abs(originalAspectXY - newAspectXY) > 0.01f)
                {
                    _newVoxelSize.Y = _newVoxelSize.X / originalAspectXY;
                }
            }
            
            _newVoxelCount.X = (_currentDataset.Width * _currentDataset.PixelSize) / _newVoxelSize.X;
            _newVoxelCount.Y = (_currentDataset.Height * _currentDataset.PixelSize) / _newVoxelSize.Y;
            _newVoxelCount.Z = (_currentDataset.Depth * _currentDataset.SliceThickness) / _newVoxelSize.Z;
        }

        // --- Public setters for interactive manipulation ---
        public void SetTranslation(Vector3 translation) => _translation = translation;
        public void SetRotation(Vector3 rotation) => _rotation = rotation;
        public void SetScale(Vector3 scale) => _scale = scale;

        // Public getters for the overlay
        public bool ShowPreview => _showPreview;
        public Matrix4x4 GetTransformMatrix()
        {
            var center = new Vector3(_currentDataset.Width / 2f, _currentDataset.Height / 2f, _currentDataset.Depth / 2f);
            var toOrigin = Matrix4x4.CreateTranslation(-center);
            var fromOrigin = Matrix4x4.CreateTranslation(center);

            var scaleMatrix = Matrix4x4.CreateScale(_scale);
            var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(
                _rotation.Y * (MathF.PI / 180f),
                _rotation.X * (MathF.PI / 180f),
                _rotation.Z * (MathF.PI / 180f)
            );
            var translationMatrix = Matrix4x4.CreateTranslation(_translation);
            return toOrigin * scaleMatrix * rotationMatrix * fromOrigin * translationMatrix;
        }
        public (Vector3 min, Vector3 max) GetCropBounds() => (_cropMin, _cropMax);

        public void Dispose()
        {
            if (_currentDataset != null)
            {
                TransformIntegration.UnregisterTool(_currentDataset);
            }
        }
    }
}