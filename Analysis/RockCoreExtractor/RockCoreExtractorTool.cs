// GeoscientistToolkit/Analysis/RockCoreExtractor/RockCoreExtractorTool.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using GeoscientistToolkit.Data.CtImageStack;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.RockCoreExtractor
{
    /// <summary>
    /// Tool for defining and extracting cylindrical rock cores from CT stacks.
    /// - Safe to use even before its UI runs (AttachDataset guards)
    /// - Full UI (view, diameter, length, center, start, buttons)
    /// - Extraction computes a 3D voxel index list (within the cylinder) and raises OnExtracted
    /// </summary>
    public class RockCoreExtractorTool
    {
        // ------------------ Public API ------------------

        public enum CircularView
        {
            XY_Circular_Z_Lateral, // Circle on XY, length along Z
            XZ_Circular_Y_Lateral, // Circle on XZ, length along Y
            YZ_Circular_X_Lateral  // Circle on YZ, length along X
        }

        /// <summary>Result raised after extraction.</summary>
        public sealed class ExtractionResult
        {
            public CtImageStackDataset SourceDataset { get; init; }
            public CircularView View { get; init; }
            public float DiameterVox { get; init; }
            public float LengthVox { get; init; }
            public Vector2 CenterNorm { get; init; }       // normalized in circle plane
            public float StartNorm { get; init; }          // normalized along lateral axis
            public (int min, int max) XRange { get; init; }
            public (int min, int max) YRange { get; init; }
            public (int min, int max) ZRange { get; init; }
            /// <summary>
            /// Linear voxel indices in SourceDataset order (z-major: z * W * H + y * W + x).
            /// This contains ONLY the indices that lie inside the cylinder.
            /// </summary>
            public int[] SelectedVoxelIndices { get; init; }
        }

        /// <summary>Raised when an extraction completes successfully.</summary>
        public event Action<ExtractionResult> OnExtracted;

        public bool ShowPreview => _showPreview;
        public RockCoreOverlay Overlay => _overlay;

        // Parameters exposed to overlay
        public struct CoreParameters
        {
            public CircularView View;
            public float Diameter;
            public float Length;
            public Vector2 Center;       // normalized in the circular plane (U,V in [0..1])
            public float StartPosition;  // normalized along the lateral (length) axis [0..1]
        }
        public CoreParameters GetCoreParameters() => new()
        {
            View = _selectedView,
            Diameter = _coreDiameter,
            Length = _coreLength,
            Center = _coreCenter,
            StartPosition = _coreStartPosition
        };

        // Attach dataset early (safe to call any time)
        public void AttachDataset(CtImageStackDataset dataset)
        {
            if (dataset == null) return;
            _currentDataset = dataset;
            if (_overlay == null || !ReferenceEquals(_overlay.Dataset, dataset))
                _overlay = new RockCoreOverlay(this, dataset);
            ClampAllParameters();
        }

        // Setters used by the overlay (all safe)
        public void SetCoreDiameter(float diameter)
        {
            _coreDiameter = Math.Clamp(diameter, 10f, SafeGetMaxDiameter());
        }
        public void SetCoreLength(float length)
        {
            _coreLength = Math.Clamp(length, 10f, SafeGetMaxLength());
        }
        public void SetCoreCenter(Vector2 center)
        {
            _coreCenter = new Vector2(
                Math.Clamp(center.X, 0f, 1f),
                Math.Clamp(center.Y, 0f, 1f)
            );
        }
        public void SetCoreStartPosition(float position)
        {
            float maxLen = Math.Max(1f, SafeGetMaxLength());
            float maxStart = Math.Max(0f, 1f - (_coreLength / maxLen));
            _coreStartPosition = Math.Clamp(position, 0f, maxStart);
        }

        // ------------------ UI ------------------

        public void DrawUI(CtImageStackDataset ctDataset)
        {
            if (ctDataset == null)
            {
                ImGui.TextDisabled("Rock core extraction requires a CT Image Stack dataset.");
                return;
            }

            // Ensure internal state is wired to this dataset
            AttachDataset(ctDataset);

            ImGui.PushID("RockCoreTool");

            // View
            string[] views = {
                "XY circle / Z length",
                "XZ circle / Y length",
                "YZ circle / X length"
            };
            int prevViewIndex = _selectedViewIndex;
            if (ImGui.Combo("View", ref _selectedViewIndex, views, views.Length))
            {
                _selectedView = _selectedViewIndex switch
                {
                    0 => CircularView.XY_Circular_Z_Lateral,
                    1 => CircularView.XZ_Circular_Y_Lateral,
                    _ => CircularView.YZ_Circular_X_Lateral
                };
                ClampAllParameters();
            }
            if (prevViewIndex != _selectedViewIndex) ImGui.SetTooltip("View changed");

            // Live preview
            ImGui.SameLine();
            ImGui.Checkbox("Preview overlay", ref _showPreview);

            ImGui.Separator();

            // Parameter sliders
            float maxDiameter = SafeGetMaxDiameter();
            float maxLength   = SafeGetMaxLength();

            if (ImGui.SliderFloat("Diameter (vox)", ref _coreDiameter, 10f, maxDiameter, "%.1f"))
            {
                _coreDiameter = Math.Clamp(_coreDiameter, 10f, maxDiameter);
                // keep start in range
                SetCoreStartPosition(_coreStartPosition);
            }

            if (ImGui.SliderFloat("Length (vox)", ref _coreLength, 10f, maxLength, "%.1f"))
            {
                _coreLength = Math.Clamp(_coreLength, 10f, maxLength);
                SetCoreStartPosition(_coreStartPosition);
            }

            if (ImGui.SliderFloat2("Center (norm circle plane)", ref _coreCenter, 0f, 1f))
            {
                _coreCenter.X = Math.Clamp(_coreCenter.X, 0f, 1f);
                _coreCenter.Y = Math.Clamp(_coreCenter.Y, 0f, 1f);
            }

            if (ImGui.SliderFloat("Start (norm lateral)", ref _coreStartPosition, 0f, 1f))
            {
                SetCoreStartPosition(_coreStartPosition);
            }

            // Helper row
            if (ImGui.Button("Center core"))
            {
                _coreCenter = new Vector2(0.5f, 0.5f);
            }
            ImGui.SameLine();
            if (ImGui.Button("Fit max diameter"))
            {
                _coreDiameter = SafeGetMaxDiameter();
            }
            ImGui.SameLine();
            if (ImGui.Button("Fit max length"))
            {
                _coreLength = SafeGetMaxLength();
                SetCoreStartPosition(_coreStartPosition);
            }
            ImGui.SameLine();
            if (ImGui.Button("Reset"))
            {
                ResetDefaults();
            }

            // Axis row
            if (ImGui.Button("Switch circle plane"))
            {
                // Cycle through the three views
                _selectedViewIndex = (_selectedViewIndex + 1) % 3;
                _selectedView = _selectedViewIndex switch
                {
                    0 => CircularView.XY_Circular_Z_Lateral,
                    1 => CircularView.XZ_Circular_Y_Lateral,
                    _ => CircularView.YZ_Circular_X_Lateral
                };
                ClampAllParameters();
            }

            // Extraction row (with progress/status)
            ImGui.Separator();
            if (!_isProcessing)
            {
                if (ImGui.Button("Extract Core"))
                {
                    StartExtraction();
                }
                ImGui.SameLine();
                if (ImGui.Button("Compute Selection Indices"))
                {
                    // Synchronous index computation (fast for most volumes)
                    var result = ComputeExtractionIndices();
                    OnExtracted?.Invoke(result);
                    _statusMessage = $"Computed {result.SelectedVoxelIndices.Length:N0} voxels inside cylinder.";
                }
            }
            else
            {
                ImGui.ProgressBar(_progress, new Vector2(-1, 0), $"{(int)(_progress * 100)}%");
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    _cancelToken?.Cancel();
                }
            }

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                ImGui.TextWrapped(_statusMessage);
            }

            ImGui.PopID();
        }

        // ------------------ Internal state ------------------

        private int _selectedViewIndex = 0; // 0=XY, 1=XZ, 2=YZ
        private CircularView _selectedView = CircularView.XY_Circular_Z_Lateral;

        private float _coreDiameter = 100f;     // vox
        private float _coreLength = 200f;       // vox
        private Vector2 _coreCenter = new(0.5f, 0.5f); // normalized
        private float _coreStartPosition = 0.1f;       // normalized

        private bool _showPreview = false;

        private CtImageStackDataset _currentDataset;
        private RockCoreOverlay _overlay;

        // processing/progress
        private bool _isProcessing = false;
        private float _progress = 0f;
        private string _statusMessage = "";
        private CancellationTokenSource _cancelToken;

        // ------------------ Helpers & math ------------------

        private float SafeGetMaxDiameter()
        {
            var ds = _currentDataset;
            if (ds == null) return 1f;
            return _selectedView switch
            {
                CircularView.XY_Circular_Z_Lateral => MathF.Min(ds.Width, ds.Height),
                CircularView.XZ_Circular_Y_Lateral => MathF.Min(ds.Width, ds.Depth),
                CircularView.YZ_Circular_X_Lateral => MathF.Min(ds.Height, ds.Depth),
                _ => 1f
            };
        }

        private float SafeGetMaxLength()
        {
            var ds = _currentDataset;
            if (ds == null) return 1f;
            return _selectedView switch
            {
                CircularView.XY_Circular_Z_Lateral => ds.Depth,
                CircularView.XZ_Circular_Y_Lateral => ds.Height,
                CircularView.YZ_Circular_X_Lateral => ds.Width,
                _ => 1f
            };
        }

        private void ClampAllParameters()
        {
            _coreDiameter = Math.Clamp(_coreDiameter, 10f, SafeGetMaxDiameter());
            _coreLength   = Math.Clamp(_coreLength,   10f, SafeGetMaxLength());
            _coreCenter = new Vector2(Math.Clamp(_coreCenter.X, 0f, 1f), Math.Clamp(_coreCenter.Y, 0f, 1f));

            float maxLen = Math.Max(1f, SafeGetMaxLength());
            float maxStart = Math.Max(0f, 1f - (_coreLength / maxLen));
            _coreStartPosition = Math.Clamp(_coreStartPosition, 0f, maxStart);
        }

        private void ResetDefaults()
        {
            _coreCenter = new Vector2(0.5f, 0.5f);
            _coreDiameter = MathF.Min(100f, SafeGetMaxDiameter());
            _coreLength = MathF.Min(200f, SafeGetMaxLength());
            _coreStartPosition = 0.1f;
            ClampAllParameters();
            _statusMessage = "Parameters reset.";
        }

        // Map the UI parameters to concrete voxel-space cylinder definition
        private void GetCylinderDefinition(out float cx, out float cy, out float cz,
                                           out float rx, out float ry, out float rz,
                                           out float r /*radius*/, out (int min, int max) xrange,
                                           out (int min, int max) yrange, out (int min, int max) zrange)
        {
            // Center in the circular plane is normalized in [0..1]
            int W = _currentDataset.Width;
            int H = _currentDataset.Height;
            int D = _currentDataset.Depth;

            float radius = _coreDiameter * 0.5f;
            float length = _coreLength;

            switch (_selectedView)
            {
                case CircularView.XY_Circular_Z_Lateral:
                    cx = _coreCenter.X * W;
                    cy = _coreCenter.Y * H;
                    cz = _coreStartPosition * D + length * 0.5f;
                    // Axis along Z
                    rx = 1; ry = 1; rz = 0; // used only to choose plane
                    r = radius;
                    xrange = (Math.Max(0, (int)MathF.Floor(cx - r)), Math.Min(W - 1, (int)MathF.Ceiling(cx + r)));
                    yrange = (Math.Max(0, (int)MathF.Floor(cy - r)), Math.Min(H - 1, (int)MathF.Ceiling(cy + r)));
                    zrange = (Math.Max(0, (int)MathF.Floor(_coreStartPosition * D)),
                              Math.Min(D - 1, (int)MathF.Ceiling(_coreStartPosition * D + length)));
                    break;

                case CircularView.XZ_Circular_Y_Lateral:
                    cx = _coreCenter.X * W;
                    cy = _coreStartPosition * H + length * 0.5f;
                    cz = _coreCenter.Y * D;
                    rx = 1; ry = 0; rz = 1;
                    r = radius;
                    xrange = (Math.Max(0, (int)MathF.Floor(cx - r)), Math.Min(W - 1, (int)MathF.Ceiling(cx + r)));
                    yrange = (Math.Max(0, (int)MathF.Floor(_coreStartPosition * H)),
                              Math.Min(H - 1, (int)MathF.Ceiling(_coreStartPosition * H + length)));
                    zrange = (Math.Max(0, (int)MathF.Floor(cz - r)), Math.Min(D - 1, (int)MathF.Ceiling(cz + r)));
                    break;

                default: // YZ_Circular_X_Lateral
                    cx = _coreStartPosition * W + length * 0.5f;
                    cy = _coreCenter.X * H;
                    cz = _coreCenter.Y * D;
                    rx = 0; ry = 1; rz = 1;
                    r = radius;
                    xrange = (Math.Max(0, (int)MathF.Floor(_coreStartPosition * W)),
                              Math.Min(W - 1, (int)MathF.Ceiling(_coreStartPosition * W + length)));
                    yrange = (Math.Max(0, (int)MathF.Floor(cy - r)), Math.Min(H - 1, (int)MathF.Ceiling(cy + r)));
                    zrange = (Math.Max(0, (int)MathF.Floor(cz - r)), Math.Min(D - 1, (int)MathF.Ceiling(cz + r)));
                    break;
            }
        }

        /// <summary>
        /// Computes the voxel indices inside the cylinder (z-major layout).
        /// </summary>
        private ExtractionResult ComputeExtractionIndices(IProgress<float> progress = null, CancellationToken ct = default)
        {
            if (_currentDataset == null) throw new InvalidOperationException("Dataset not attached.");

            int W = _currentDataset.Width;
            int H = _currentDataset.Height;
            int D = _currentDataset.Depth;

            GetCylinderDefinition(out float cx, out float cy, out float cz,
                                  out float rx, out float ry, out float rz,
                                  out float r, out var xr, out var yr, out var zr);

            float r2 = r * r;

            var indices = new List<int>((int)(_coreDiameter * _coreDiameter * _coreLength * 0.25f)); // rough guess

            // Iterate only within the bounding ranges
            int totalLayers = (zr.max - zr.min + 1);
            int processedLayers = 0;

            for (int z = zr.min; z <= zr.max; z++)
            {
                ct.ThrowIfCancellationRequested();

                // Choose circle plane depending on view
                if (_selectedView == CircularView.XY_Circular_Z_Lateral)
                {
                    for (int y = yr.min; y <= yr.max; y++)
                    {
                        float dy = y - cy;
                        for (int x = xr.min; x <= xr.max; x++)
                        {
                            float dx = x - cx;
                            if ((dx * dx + dy * dy) <= r2)
                            {
                                int idx = z * W * H + y * W + x;
                                indices.Add(idx);
                            }
                        }
                    }
                }
                else if (_selectedView == CircularView.XZ_Circular_Y_Lateral)
                {
                    for (int x = xr.min; x <= xr.max; x++)
                    {
                        float dx = x - cx;
                        for (int zz = z; zz <= z; zz++) // single z, loop kept symmetrical
                        {
                            float dz = zz - cz;
                            if ((dx * dx + dz * dz) <= r2)
                            {
                                for (int y = yr.min; y <= yr.max; y++)
                                {
                                    int idx = zz * W * H + y * W + x;
                                    indices.Add(idx);
                                }
                            }
                        }
                    }
                }
                else // YZ circle
                {
                    for (int y = yr.min; y <= yr.max; y++)
                    {
                        float dy = y - cy;
                        for (int zz = z; zz <= z; zz++)
                        {
                            float dz = zz - cz;
                            if ((dy * dy + dz * dz) <= r2)
                            {
                                for (int x = xr.min; x <= xr.max; x++)
                                {
                                    int idx = zz * W * H + y * W + x;
                                    indices.Add(idx);
                                }
                            }
                        }
                    }
                }

                processedLayers++;
                progress?.Report(processedLayers / (float)Math.Max(1, totalLayers));
            }

            return new ExtractionResult
            {
                SourceDataset = _currentDataset,
                View = _selectedView,
                DiameterVox = _coreDiameter,
                LengthVox = _coreLength,
                CenterNorm = _coreCenter,
                StartNorm = _coreStartPosition,
                XRange = xr,
                YRange = yr,
                ZRange = zr,
                SelectedVoxelIndices = indices.ToArray()
            };
        }

        // ------------------ Extraction orchestration ------------------

        private void StartExtraction()
        {
            if (_isProcessing) return;
            if (_currentDataset == null)
            {
                _statusMessage = "No dataset.";
                return;
            }

            _isProcessing = true;
            _progress = 0f;
            _statusMessage = "Extracting core...";
            _cancelToken = new CancellationTokenSource();

            // Offload to a background task (CPU), but the tool remains responsive.
            Task.Run(() =>
            {
                try
                {
                    var progress = new Progress<float>(p => _progress = p);
                    var result = ComputeExtractionIndices(progress, _cancelToken.Token);
                    OnExtracted?.Invoke(result);
                    _statusMessage = $"Done. Selected {result.SelectedVoxelIndices.Length:N0} voxels.";
                }
                catch (OperationCanceledException)
                {
                    _statusMessage = "Extraction cancelled.";
                }
                catch (Exception ex)
                {
                    _statusMessage = $"Error: {ex.Message}";
                }
                finally
                {
                    _isProcessing = false;
                    _cancelToken.Dispose();
                    _cancelToken = null;
                    _progress = 0f;
                }
            });
        }
    }
}
