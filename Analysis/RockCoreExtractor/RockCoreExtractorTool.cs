// GeoscientistToolkit/Analysis/RockCoreExtractor/RockCoreExtractorTool.cs

using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.CtImageStack;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.RockCoreExtractor;

/// <summary>
///     Tool for defining and extracting cylindrical rock cores from CT stacks.
///     - Safe to use even before its UI runs (AttachDataset guards)
///     - Full UI (view, diameter, length, center, start, buttons)
///     - Extraction computes a 3D voxel index list (within the cylinder) and raises OnExtracted
/// </summary>
public class RockCoreExtractorTool
{
    // ------------------ Public API ------------------

    public enum CircularView
    {
        XY_Circular_Z_Lateral, // Circle on XY, length along Z
        XZ_Circular_Y_Lateral, // Circle on XZ, length along Y
        YZ_Circular_X_Lateral // Circle on YZ, length along X
    }

    private CancellationTokenSource _cancelToken;
    private Vector2 _coreCenter = new(0.5f, 0.5f); // normalized

    private float _coreDiameter = 100f; // vox
    private float _coreLength = 200f; // vox
    private float _coreStartPosition = 0.1f; // normalized

    private CtImageStackDataset _currentDataset;

    // processing/progress
    private bool _isProcessing;
    private float _progress;
    private CircularView _selectedView = CircularView.XY_Circular_Z_Lateral;

    // ------------------ Internal state ------------------

    private int _selectedViewIndex; // 0=XY, 1=XZ, 2=YZ

    private bool _showPreview;
    private string _statusMessage = "";

    public bool ShowPreview => _showPreview;
    public RockCoreOverlay Overlay { get; private set; }

    /// <summary>Raised when an extraction completes successfully.</summary>
    public event Action<ExtractionResult> OnExtracted;

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

    // Attach dataset early (safe to call any time)
    public void AttachDataset(CtImageStackDataset dataset)
    {
        if (dataset == null) return;
        _currentDataset = dataset;
        if (Overlay == null || !ReferenceEquals(Overlay.Dataset, dataset))
            Overlay = new RockCoreOverlay(this, dataset);
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
        var maxLen = Math.Max(1f, SafeGetMaxLength());
        var maxStart = Math.Max(0f, 1f - _coreLength / maxLen);
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
        string[] views =
        {
            "XY circle / Z length",
            "XZ circle / Y length",
            "YZ circle / X length"
        };
        var prevViewIndex = _selectedViewIndex;
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
        var maxDiameter = SafeGetMaxDiameter();
        var maxLength = SafeGetMaxLength();

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
            SetCoreStartPosition(_coreStartPosition);

        // Helper row
        if (ImGui.Button("Center core")) _coreCenter = new Vector2(0.5f, 0.5f);
        ImGui.SameLine();
        if (ImGui.Button("Fit max diameter")) _coreDiameter = SafeGetMaxDiameter();
        ImGui.SameLine();
        if (ImGui.Button("Fit max length"))
        {
            _coreLength = SafeGetMaxLength();
            SetCoreStartPosition(_coreStartPosition);
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset")) ResetDefaults();

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
            if (ImGui.Button("Extract Core")) StartExtraction();
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
            if (ImGui.Button("Cancel")) _cancelToken?.Cancel();
        }

        if (!string.IsNullOrEmpty(_statusMessage)) ImGui.TextWrapped(_statusMessage);

        ImGui.PopID();
    }

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
        _coreLength = Math.Clamp(_coreLength, 10f, SafeGetMaxLength());
        _coreCenter = new Vector2(Math.Clamp(_coreCenter.X, 0f, 1f), Math.Clamp(_coreCenter.Y, 0f, 1f));

        var maxLen = Math.Max(1f, SafeGetMaxLength());
        var maxStart = Math.Max(0f, 1f - _coreLength / maxLen);
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
        var W = _currentDataset.Width;
        var H = _currentDataset.Height;
        var D = _currentDataset.Depth;

        var radius = _coreDiameter * 0.5f;
        var length = _coreLength;

        switch (_selectedView)
        {
            case CircularView.XY_Circular_Z_Lateral:
                cx = _coreCenter.X * W;
                cy = _coreCenter.Y * H;
                cz = _coreStartPosition * D + length * 0.5f;
                // Axis along Z
                rx = 1;
                ry = 1;
                rz = 0; // used only to choose plane
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
                rx = 1;
                ry = 0;
                rz = 1;
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
                rx = 0;
                ry = 1;
                rz = 1;
                r = radius;
                xrange = (Math.Max(0, (int)MathF.Floor(_coreStartPosition * W)),
                    Math.Min(W - 1, (int)MathF.Ceiling(_coreStartPosition * W + length)));
                yrange = (Math.Max(0, (int)MathF.Floor(cy - r)), Math.Min(H - 1, (int)MathF.Ceiling(cy + r)));
                zrange = (Math.Max(0, (int)MathF.Floor(cz - r)), Math.Min(D - 1, (int)MathF.Ceiling(cz + r)));
                break;
        }
    }

    /// <summary>
    ///     Computes the voxel indices inside the cylinder (z-major layout).
    /// </summary>
    private ExtractionResult ComputeExtractionIndices(IProgress<float> progress = null, CancellationToken ct = default)
    {
        if (_currentDataset == null) throw new InvalidOperationException("Dataset not attached.");

        var W = _currentDataset.Width;
        var H = _currentDataset.Height;
        var D = _currentDataset.Depth;

        GetCylinderDefinition(out var cx, out var cy, out var cz,
            out var rx, out var ry, out var rz,
            out var r, out var xr, out var yr, out var zr);

        var r2 = r * r;

        var indices = new List<int>((int)(_coreDiameter * _coreDiameter * _coreLength * 0.25f)); // rough guess

        // Iterate only within the bounding ranges
        var totalLayers = zr.max - zr.min + 1;
        var processedLayers = 0;

        for (var z = zr.min; z <= zr.max; z++)
        {
            ct.ThrowIfCancellationRequested();

            for (var y = yr.min; y <= yr.max; y++)
            for (var x = xr.min; x <= xr.max; x++)
            {
                float dx = 0, dy = 0, dz = 0;
                var inCircle = false;

                // Check if the point is within the cylinder
                switch (_selectedView)
                {
                    case CircularView.XY_Circular_Z_Lateral:
                        dx = x - cx;
                        dy = y - cy;
                        inCircle = dx * dx + dy * dy <= r2;
                        break;
                    case CircularView.XZ_Circular_Y_Lateral:
                        dx = x - cx;
                        dz = z - cz;
                        inCircle = dx * dx + dz * dz <= r2;
                        break;
                    case CircularView.YZ_Circular_X_Lateral:
                        dy = y - cy;
                        dz = z - cz;
                        inCircle = dy * dy + dz * dz <= r2;
                        break;
                }

                if (inCircle)
                {
                    var idx = z * W * H + y * W + x;
                    indices.Add(idx);
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
        if (_currentDataset?.LabelData == null)
        {
            _statusMessage = "No dataset or label data available.";
            return;
        }

        _isProcessing = true;
        _progress = 0f;
        _statusMessage = "Extracting core...";
        _cancelToken = new CancellationTokenSource();
        var token = _cancelToken.Token;

        Task.Run(() =>
        {
            try
            {
                // Step 1: Compute indices of voxels INSIDE the core
                _statusMessage = "Calculating core voxels...";
                var progressReporter =
                    new Progress<float>(p => _progress = p * 0.25f); // 25% of progress bar for this step
                var result = ComputeExtractionIndices(progressReporter, token);
                token.ThrowIfCancellationRequested();

                // Step 2: Set all voxels OUTSIDE the core to Exterior (ID 0)
                _statusMessage = "Applying exterior mask...";
                var labelVolume = _currentDataset.LabelData;
                var W = _currentDataset.Width;
                var H = _currentDataset.Height;
                var D = _currentDataset.Depth;

                // Use a HashSet for O(1) lookups, which is much faster than list.Contains()
                var coreIndices = new HashSet<int>(result.SelectedVoxelIndices);
                var dataWasModified = false;

                for (var z = 0; z < D; z++)
                {
                    token.ThrowIfCancellationRequested();
                    _progress = 0.25f + 0.7f * (z / (float)D); // 70% of progress bar for this loop

                    var sliceBuffer = new byte[W * H];
                    labelVolume.ReadSliceZ(z, sliceBuffer);
                    var sliceModified = false;

                    // Use Parallel.For for faster processing of each slice
                    Parallel.For(0, W * H, i =>
                    {
                        var x = i % W;
                        var y = i / W;
                        var linearIndex = z * W * H + y * W + x;

                        // If the voxel is NOT in the core and is NOT already exterior, change it
                        if (!coreIndices.Contains(linearIndex) && sliceBuffer[i] != 0)
                        {
                            sliceBuffer[i] = 0; // Set to Exterior material
                            sliceModified = true;
                        }
                    });

                    if (sliceModified)
                    {
                        labelVolume.WriteSliceZ(z, sliceBuffer);
                        dataWasModified = true;
                    }
                }

                // Step 3: Finalize, save, and notify the application
                if (dataWasModified)
                {
                    _statusMessage = "Saving changes...";
                    _progress = 0.95f;
                    _currentDataset.SaveLabelData();
                    ProjectManager.Instance.NotifyDatasetDataChanged(_currentDataset);
                    ProjectManager.Instance.HasUnsavedChanges = true;
                    _statusMessage = "Extraction complete. Voxels outside the core have been set to Exterior.";
                }
                else
                {
                    _statusMessage = "Extraction complete. No changes were necessary.";
                }

                // Also invoke the event for other potential listeners (like analysis tools)
                OnExtracted?.Invoke(result);
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
        }, token);
    }

    /// <summary>Result raised after extraction.</summary>
    public sealed class ExtractionResult
    {
        public CtImageStackDataset SourceDataset { get; init; }
        public CircularView View { get; init; }
        public float DiameterVox { get; init; }
        public float LengthVox { get; init; }
        public Vector2 CenterNorm { get; init; } // normalized in circle plane
        public float StartNorm { get; init; } // normalized along lateral axis
        public (int min, int max) XRange { get; init; }
        public (int min, int max) YRange { get; init; }
        public (int min, int max) ZRange { get; init; }

        /// <summary>
        ///     Linear voxel indices in SourceDataset order (z-major: z * W * H + y * W + x).
        ///     This contains ONLY the indices that lie inside the cylinder.
        /// </summary>
        public int[] SelectedVoxelIndices { get; init; }
    }

    // Parameters exposed to overlay
    public struct CoreParameters
    {
        public CircularView View;
        public float Diameter;
        public float Length;
        public Vector2 Center; // normalized in the circular plane (U,V in [0..1])
        public float StartPosition; // normalized along the lateral (length) axis [0..1]
    }
}