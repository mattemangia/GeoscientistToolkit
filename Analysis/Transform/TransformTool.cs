// GeoscientistToolkit/Analysis/Transform/TransformTool.cs
using System;
using System.Collections.Generic;
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
    public class TransformTool : IDatasetTools, IDisposable
    {
        private CtImageStackDataset _currentDataset;

        // Overlays
        private IOverlay _currentOverlay;
        private TransformOverlay _transformOverlay;
        private CropOverlay _cropOverlay;

        public enum OverlayMode { Transform, Crop }
        private OverlayMode _overlayMode = OverlayMode.Transform;
        public IOverlay Overlay => _currentOverlay;

        // Transform params
        internal Vector3 _translation = Vector3.Zero; // voxels
        internal Vector3 _rotation = Vector3.Zero;    // degrees
        internal Vector3 _scale = Vector3.One;        // multiplier

        // Crop (normalized)
        private Vector3 _cropMin = Vector3.Zero;
        private Vector3 _cropMax = Vector3.One;

        // Channel crop toggles (default ON)
        private bool _cropGrayscale = true;
        private bool _cropLabels = true;

        // Invert selection & padding & uniform
        private bool _invertCrop = false;
        public bool UniformCropFromCenter = false;
        private Vector3 _cropPaddingVox = Vector3.Zero; // voxels

        // Snapping
        public bool SnapEnabled = false;
        public float SnapTranslationStep = 1f; // vox
        public float SnapRotationStep = 5f;    // deg
        public float SnapScaleStep = 0.05f;    // multiplier
        public float SnapCropVoxStepX = 1f, SnapCropVoxStepY = 1f, SnapCropVoxStepZ = 1f;

        // Resampling
        private enum ResampleMode { VoxelCount, VoxelSize }
        private ResampleMode _resampleMode = ResampleMode.VoxelCount;
        private Vector3 _newVoxelCount = Vector3.Zero;
        private Vector3 _newVoxelSize = Vector3.Zero;
        private bool _lockAspectRatio = true;

        // UI state
        private bool _showPreview = true;
        private bool _isProcessing = false;
        private readonly ProgressBarDialog _progressDialog;
        private readonly CtImageStackExportDialog _exportDialog;
        private static readonly ConfirmationDialog _confirmApplyDialog = new ConfirmationDialog(
            "Confirm Apply",
            "This will permanently replace the current dataset in the project with the transformed version.\nThis action cannot be undone.\n\nAre you sure you want to proceed?"
        );

        // Presets (in-memory)
        private class Preset
        {
            public string Name = "Preset";
            public Vector3 Translation, Rotation, Scale;
            public Vector3 CropMin, CropMax;
            public bool CropGray, CropLabels, InvertCrop, UniformFromCenter, SnapOn;
            public float SnapTr, SnapRot, SnapScale, SnapCropX, SnapCropY, SnapCropZ;
            public Vector3 PaddingVox;
            public bool ByCount; public Vector3 NewCount; public Vector3 NewVoxelSize;
        }
        private static readonly List<Preset> _presets = new();
        private int _selectedPresetIndex = -1;
        private string _newPresetName = "My preset";

        public TransformTool()
        {
            _progressDialog = new ProgressBarDialog("Transforming Dataset");
            _exportDialog = new CtImageStackExportDialog();
        }

        // ---------- Publics for integration ----------
        public bool ShowPreview => _showPreview;
        public (Vector3 min, Vector3 max) GetCropBounds() => (_cropMin, _cropMax);

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

        // snapping helper
        public float Snap(float v, float step)
        {
            if (step <= 0) return v;
            return MathF.Round(v / step) * step;
        }

        // ---------- UI ----------
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

            if (_transformOverlay == null || _transformOverlay.Dataset != ctDataset)
                _transformOverlay = new TransformOverlay(this, ctDataset);
            if (_cropOverlay == null || _cropOverlay.Dataset != ctDataset)
                _cropOverlay = new CropOverlay(this, ctDataset);

            _currentOverlay = _overlayMode == OverlayMode.Transform ? (IOverlay)_transformOverlay : _cropOverlay;
            TransformIntegration.RegisterTool(ctDataset, this);

            if (_isProcessing) ImGui.BeginDisabled();

            // Overlay selector
            ImGui.Text("Overlay:");
            ImGui.SameLine();
            int mode = (int)_overlayMode;
            string[] modes = { "Transform", "Crop" };
            if (ImGui.Combo("##OverlayMode", ref mode, modes, modes.Length))
            {
                _overlayMode = (OverlayMode)mode;
                _currentOverlay = _overlayMode == OverlayMode.Transform ? (IOverlay)_transformOverlay : _cropOverlay;
            }
            ImGui.Checkbox("Show Interactive Overlay", ref _showPreview);

            // Presets row
            ImGui.Separator();
            ImGui.Text("Presets");
            ImGui.SameLine();
            if (_presets.Count == 0) ImGui.TextDisabled("(none yet)");
            else
            {
                string[] names = _presets.Select(p => p.Name).ToArray();
                ImGui.SetNextItemWidth(200);
                if (ImGui.Combo("##PresetSel", ref _selectedPresetIndex, names, names.Length) && _selectedPresetIndex >= 0)
                {
                    LoadPreset(_presets[_selectedPresetIndex]);
                }
                ImGui.SameLine();
                if (ImGui.Button("Delete") && _selectedPresetIndex >= 0)
                {
                    _presets.RemoveAt(_selectedPresetIndex);
                    _selectedPresetIndex = -1;
                }
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(180);
            ImGui.InputText("##PresetName", ref _newPresetName, 128);
            ImGui.SameLine();
            if (ImGui.Button("Save Current"))
            {
                var pr = MakePresetFromCurrent();
                pr.Name = string.IsNullOrWhiteSpace(_newPresetName) ? $"Preset {_presets.Count + 1}" : _newPresetName;
                _presets.Add(pr);
                _selectedPresetIndex = _presets.Count - 1;
            }

            // Transform
            ImGui.Separator();
            if (ImGui.CollapsingHeader("Transform", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.DragFloat3("Translation (vox)", ref _translation, 1.0f);
                ImGui.DragFloat3("Rotation (deg)", ref _rotation, 1.0f);
                ImGui.DragFloat3("Scale", ref _scale, 0.01f);
                if (ImGui.Button("Reset Transform"))
                {
                    _translation = Vector3.Zero; _rotation = Vector3.Zero; _scale = Vector3.One;
                }

                // Snapping controls
                ImGui.Spacing();
                ImGui.Checkbox("Enable Snapping", ref SnapEnabled);
                if (SnapEnabled)
                {
                    ImGui.Indent();
                    ImGui.DragFloat("Snap Translation (vox)", ref SnapTranslationStep, 0.1f, 0.01f, 1024f, "%.2f");
                    ImGui.DragFloat("Snap Rotation (deg)", ref SnapRotationStep, 0.5f, 0.1f, 90f, "%.1f");
                    ImGui.DragFloat("Snap Scale (mult)", ref SnapScaleStep, 0.01f, 0.001f, 10f, "%.3f");
                    ImGui.Unindent();
                }
            }

            // Crop
            if (ImGui.CollapsingHeader("Crop", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Text("Channels to crop (applied during processing):");
                ImGui.Checkbox("Crop Grayscale", ref _cropGrayscale);
                ImGui.SameLine();
                ImGui.Checkbox("Crop Labels", ref _cropLabels);
                ImGui.SameLine();
                ImGui.Checkbox("Invert Crop Selection", ref _invertCrop);
                ImGui.SameLine();
                ImGui.Checkbox("Uniform from center", ref UniformCropFromCenter);

                ImGui.Spacing();
                ImGui.Text("Normalized bounds [0..1]");
                ImGui.DragFloatRange2("X", ref _cropMin.X, ref _cropMax.X, 0.005f, 0.0f, 1.0f, "%.3f", "%.3f");
                ImGui.DragFloatRange2("Y", ref _cropMin.Y, ref _cropMax.Y, 0.005f, 0.0f, 1.0f, "%.3f", "%.3f");
                ImGui.DragFloatRange2("Z", ref _cropMin.Z, ref _cropMax.Z, 0.005f, 0.0f, 1.0f, "%.3f", "%.3f");

                // Padding (voxels)
                ImGui.Spacing();
                ImGui.Text("Padding (voxels):");
                ImGui.DragFloat3("Padding XYZ", ref _cropPaddingVox, 1f, 0f, 1e6f, "%.0f");

                // Crop snapping steps for voxel alignment
                if (SnapEnabled)
                {
                    ImGui.Spacing();
                    ImGui.Text("Crop Snap (vox):");
                    ImGui.DragFloat("X step", ref SnapCropVoxStepX, 0.1f, 0.1f, 1024f, "%.1f");
                    ImGui.SameLine();
                    ImGui.DragFloat("Y step", ref SnapCropVoxStepY, 0.1f, 0.1f, 1024f, "%.1f");
                    ImGui.SameLine();
                    ImGui.DragFloat("Z step", ref SnapCropVoxStepZ, 0.1f, 0.1f, 1024f, "%.1f");
                }

                if (ImGui.Button("Reset Crop"))
                {
                    _cropMin = Vector3.Zero; _cropMax = Vector3.One; _cropPaddingVox = Vector3.Zero; _invertCrop = false;
                }

                // Mini previews
                DrawCropMiniPreviews();
            }

            // Resample
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
                        _newVoxelCount = new Vector3(MathF.Max(1, x), MathF.Max(1, y), MathF.Max(1, z));
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
                    InitializeResampleParameters(_currentDataset);
                }

                ImGui.TextDisabled($"Resulting Voxel Size: {_newVoxelSize.X:F2}x{_newVoxelSize.Y:F2}x{_newVoxelSize.Z:F2} {_currentDataset.Unit}");
                ImGui.TextDisabled($"Resulting Dimensions: {(int)_newVoxelCount.X}x{(int)_newVoxelCount.Y}x{(int)_newVoxelCount.Z}");
            }

            ImGui.Separator();

            if (_isProcessing)
            {
                ImGui.EndDisabled();
                _progressDialog.Submit();
            }
            else
            {
                if (ImGui.Button("Apply", new Vector2(ImGui.GetContentRegionAvail().X * 0.5f - 5, 0)))
                    _confirmApplyDialog.Open();
                ImGui.SameLine();
                if (ImGui.Button("Export", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
                    _ = ProcessAndExportAsync();
            }

            if (_confirmApplyDialog.Submit())
            {
                _ = ProcessAndApplyAsync();
            }
        }

        private void DrawCropMiniPreviews()
        {
            // Three tiny rectangles (XY, XZ, YZ) with crop box overlay
            var avail = ImGui.GetContentRegionAvail().X;
            float h = 90f;
            DrawMini("XY preview", h, (x, y) => (x, y), _currentDataset.Width, _currentDataset.Height, _cropMin.X, _cropMax.X, _cropMin.Y, _cropMax.Y);
            ImGui.SameLine();
            DrawMini("XZ preview", h, (x, z) => (x, z), _currentDataset.Width, _currentDataset.Depth, _cropMin.X, _cropMax.X, _cropMin.Z, _cropMax.Z);
            ImGui.SameLine();
            DrawMini("YZ preview", h, (y, z) => (y, z), _currentDataset.Height, _currentDataset.Depth, _cropMin.Y, _cropMax.Y, _cropMin.Z, _cropMax.Z);
        }

        private void DrawMini(string label, float height, Func<float, float, (float, float)> map, float wTot, float hTot,
                              float minA, float maxA, float minB, float maxB)
        {
            ImGui.BeginChild(label, new Vector2(0, height), ImGuiChildFlags.None, ImGuiWindowFlags.None);
            var dl = ImGui.GetWindowDrawList();
            var p0 = ImGui.GetCursorScreenPos();
            var size = ImGui.GetContentRegionAvail(); size.Y = height - 6f;
            var p1 = p0 + size;

            uint frame = 0xFF3A3A3A, cropCol = 0xFF00FFFF, fill = 0x2211AAFF;
            dl.AddRect(p0, p1, frame, 3f, ImDrawFlags.None, 1.0f);

            // Crop rect
            var a0 = new Vector2(p0.X + size.X * minA, p0.Y + size.Y * minB);
            var a1 = new Vector2(p0.X + size.X * maxA, p0.Y + size.Y * maxB);
            dl.AddRectFilled(a0, a1, fill);
            dl.AddRect(a0, a1, cropCol, 2f, ImDrawFlags.None, 1.5f);

            ImGui.EndChild();
        }

        // ---------- Processing ----------
        private async Task ProcessAndApplyAsync()
        {
            _isProcessing = true;
            _progressDialog.Open("Initializing...");

            var oldDataset = _currentDataset;
            var transformedDataset = await CreateTransformedDatasetAsync();

            _progressDialog.Close();
            _isProcessing = false;

            if (transformedDataset != null)
            {
                ProjectManager.Instance.AddDataset(transformedDataset);
                ProjectManager.Instance.RemoveDataset(oldDataset);
                DatasetViewPanel.CloseViewFor(oldDataset);
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
            }
        }

        private async Task<CtImageStackDataset> CreateTransformedDatasetAsync()
        {
            var source = _currentDataset;

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

            // Compute crop in voxels + padding, clamped
            var baseMin = new Vector3(source.Width * _cropMin.X, source.Height * _cropMin.Y, source.Depth * _cropMin.Z);
            var baseMax = new Vector3(source.Width * _cropMax.X, source.Height * _cropMax.Y, source.Depth * _cropMax.Z);
            var pad = _cropPaddingVox;
            var srcMin = Vector3.Max(Vector3.Zero, baseMin - pad);
            var srcMax = Vector3.Min(new Vector3(source.Width, source.Height, source.Depth), baseMax + pad);

            // Per-channel boxes (or full volume if unchecked)
            var grayMin = _cropGrayscale ? srcMin : Vector3.Zero;
            var grayMax = _cropGrayscale ? srcMax : new Vector3(source.Width, source.Height, source.Depth);
            var labMin  = _cropLabels   ? srcMin : Vector3.Zero;
            var labMax  = _cropLabels   ? srcMax : new Vector3(source.Width, source.Height, source.Depth);

            // Destination
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
                            var destPos = new Vector3(x, y, z);
                            var sourcePos = Vector3.Transform(destPos, inverseTransform);

                            bool insideGray = sourcePos.X >= grayMin.X && sourcePos.X < grayMax.X &&
                                              sourcePos.Y >= grayMin.Y && sourcePos.Y < grayMax.Y &&
                                              sourcePos.Z >= grayMin.Z && sourcePos.Z < grayMax.Z;

                            bool insideLab = sourcePos.X >= labMin.X && sourcePos.X < labMax.X &&
                                             sourcePos.Y >= labMin.Y && sourcePos.Y < labMax.Y &&
                                             sourcePos.Z >= labMin.Z && sourcePos.Z < labMax.Z;

                            if (_invertCrop)
                            {
                                insideGray = !insideGray;
                                insideLab = !insideLab;
                            }

                            if (insideGray)
                                newGrayscaleVolume[x, y, z] = SampleGrayscale(source.VolumeData, sourcePos);
                            if (insideLab)
                                newLabelVolume[x, y, z] = SampleLabel(source.LabelData, sourcePos);
                        }
                    }
                    _progressDialog.Update((float)(z + 1) / destDepth, $"Processing slice {z + 1}/{destDepth}");
                });
            });

            if (_progressDialog.IsCancellationRequested) return null;

            var newDataset = new CtImageStackDataset($"{source.Name}_transformed", source.FilePath)
            {
                Width = destWidth,
                Height = destHeight,
                Depth = destDepth,
                PixelSize = _newVoxelSize.X,
                SliceThickness = _newVoxelSize.Z,
                Unit = source.Unit,
                Materials = source.Materials.Select(m => new Material(m.ID, m.Name, m.Color)
                {
                    IsVisible = m.IsVisible,
                    Density = m.Density,
                    IsExterior = m.IsExterior,
                    MaxValue = m.MaxValue,
                    MinValue = m.MinValue
                }).ToList(),
            };

            var volumeDataField = newDataset.GetType().GetField("_volumeData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (volumeDataField != null) volumeDataField.SetValue(newDataset, newGrayscaleVolume);
            newDataset.LabelData = newLabelVolume;

            return newDataset;
        }

        private byte SampleGrayscale(IGrayscaleVolumeData volume, Vector3 pos)
        {
            int x0 = (int)Math.Floor(pos.X); int x1 = x0 + 1;
            int y0 = (int)Math.Floor(pos.Y); int y1 = y0 + 1;
            int z0 = (int)Math.Floor(pos.Z); int z1 = z0 + 1;

            if (x0 < 0 || x1 >= volume.Width || y0 < 0 || y1 >= volume.Height || z0 < 0 || z1 >= volume.Depth) return 0;

            float xd = pos.X - x0; float yd = pos.Y - y0; float zd = pos.Z - z0;

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
            int x = (int)Math.Round(pos.X);
            int y = (int)Math.Round(pos.Y);
            int z = (int)Math.Round(pos.Z);

            if (x < 0 || x >= volume.Width || y < 0 || y >= volume.Height || z < 0 || z >= volume.Depth) return 0;

            return volume[x, y, z];
        }

        // ---------- init + helpers ----------
        private void InitializeForDataset(CtImageStackDataset ds)
        {
            _currentDataset = ds;
            _translation = Vector3.Zero;
            _rotation = Vector3.Zero;
            _scale = Vector3.One;

            _cropMin = Vector3.Zero; _cropMax = Vector3.One;
            _cropGrayscale = true; _cropLabels = true;
            _invertCrop = false; UniformCropFromCenter = false; _cropPaddingVox = Vector3.Zero;

            SnapEnabled = false;
            SnapTranslationStep = 1f; SnapRotationStep = 5f; SnapScaleStep = 0.05f;
            SnapCropVoxStepX = SnapCropVoxStepY = SnapCropVoxStepZ = 1f;

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
                float originalAspectXY = 1f;
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

        // setters used by overlays
        public void SetTranslation(Vector3 translation) => _translation = translation;
        public void SetRotation(Vector3 rotation) => _rotation = rotation;
        public void SetScale(Vector3 scale) => _scale = scale;

        public void SetCropBounds(Vector3 minNorm, Vector3 maxNorm)
        {
            _cropMin = new Vector3(Math.Clamp(minNorm.X, 0, 1), Math.Clamp(minNorm.Y, 0, 1), Math.Clamp(minNorm.Z, 0, 1));
            _cropMax = new Vector3(Math.Clamp(maxNorm.X, 0, 1), Math.Clamp(maxNorm.Y, 0, 1), Math.Clamp(maxNorm.Z, 0, 1));
        }

        // Presets
        private Preset MakePresetFromCurrent() => new Preset
        {
            Translation = _translation, Rotation = _rotation, Scale = _scale,
            CropMin = _cropMin, CropMax = _cropMax,
            CropGray = _cropGrayscale, CropLabels = _cropLabels,
            InvertCrop = _invertCrop, UniformFromCenter = UniformCropFromCenter,
            SnapOn = SnapEnabled, SnapTr = SnapTranslationStep, SnapRot = SnapRotationStep,
            SnapScale = SnapScaleStep, SnapCropX = SnapCropVoxStepX, SnapCropY = SnapCropVoxStepY, SnapCropZ = SnapCropVoxStepZ,
            PaddingVox = _cropPaddingVox,
            ByCount = _resampleMode == ResampleMode.VoxelCount,
            NewCount = _newVoxelCount, NewVoxelSize = _newVoxelSize
        };

        private void LoadPreset(Preset p)
        {
            _translation = p.Translation; _rotation = p.Rotation; _scale = p.Scale;
            _cropMin = p.CropMin; _cropMax = p.CropMax;
            _cropGrayscale = p.CropGray; _cropLabels = p.CropLabels;
            _invertCrop = p.InvertCrop; UniformCropFromCenter = p.UniformFromCenter;
            SnapEnabled = p.SnapOn; SnapTranslationStep = p.SnapTr; SnapRotationStep = p.SnapRot; SnapScaleStep = p.SnapScale;
            SnapCropVoxStepX = p.SnapCropX; SnapCropVoxStepY = p.SnapCropY; SnapCropVoxStepZ = p.SnapCropZ;
            _cropPaddingVox = p.PaddingVox;
            _resampleMode = p.ByCount ? ResampleMode.VoxelCount : ResampleMode.VoxelSize;
            _newVoxelCount = p.NewCount; _newVoxelSize = p.NewVoxelSize;
        }

        public void Dispose()
        {
            if (_currentDataset != null)
            {
                TransformIntegration.UnregisterTool(_currentDataset);
            }
        }
    }
}
