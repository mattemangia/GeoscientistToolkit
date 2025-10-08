// GeoscientistToolkit/UI/Tools/CtImageStackTools.cs

using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;

namespace GeoscientistToolkit;

public class CtImageStackTools : IDatasetTools
{
    private const int PREVIEW_UPDATE_DELAY_MS = 250;

    // --- Static members for 3D preview control ---
    private static readonly object _previewLock = new();
    private static WeakReference<Dataset> _previewDatasetRef;
    private static byte[] _external3DPreviewMask;
    private static Vector4 _externalPreviewColor;
    private static bool _isExternalPreviewActive;

    // --- NEW: Static state for real-time 2D threshold preview ---
    private static bool _isThresholdPreviewActive;
    private static byte _previewMinThreshold;
    private static byte _previewMaxThreshold = 255;
    private static WeakReference<Material> _previewMaterialRef;
    private CtImageStackDataset _currentDataset;
    private CtSegmentationIntegration _interactiveSegmentation;
    private bool _isGenerating3DPreview;
    private DateTime _lastPreviewUpdate = DateTime.MinValue;
    private byte _maxThreshold = 255;


    // --- Instance members for segmentation UI ---
    private byte _minThreshold;
    private Material _selectedMaterialForThresholding;
    private int _selectedMaterialIndex;

    // --- 3D Threshold preview state ---
    private bool _show3DThresholdPreview;
    private byte[] _thresholdPreviewMask3D;

    public void Draw(Dataset dataset)
    {
        if (dataset is not CtImageStackDataset ctDataset)
        {
            // Clean up state when tool is not active for this dataset
            if (_isThresholdPreviewActive) Set2DPreviewState(false, null);
            return;
        }

        if (_currentDataset != ctDataset)
        {
            Clear3DThresholdPreview();
            Set2DPreviewState(false, null);
            _currentDataset = ctDataset;
            _interactiveSegmentation = CtSegmentationIntegration.GetInstance(ctDataset);
        }

        var materials = ctDataset.Materials.Where(m => m.ID != 0).ToList();
        if (materials.Count == 0)
        {
            ImGui.TextDisabled("No materials defined. Add/edit materials in the 'Material Manager' tool.");
            if (_isThresholdPreviewActive) Set2DPreviewState(false, null); // Cleanup
            return;
        }

        ImGui.SeparatorText("Threshold-based Segmentation");

        var materialNames = materials.Select(m => m.Name).ToArray();
        ImGui.Text("Target Material:");
        ImGui.SetNextItemWidth(-1);
        if (_selectedMaterialIndex >= materialNames.Length) _selectedMaterialIndex = 0;
        if (ImGui.Combo("##TargetMaterial", ref _selectedMaterialIndex, materialNames, materialNames.Length))
            _selectedMaterialForThresholding = materials[_selectedMaterialIndex];
        _selectedMaterialForThresholding ??= materials.FirstOrDefault();

        ImGui.Spacing();
        ImGui.Text("Grayscale Range:");

        var thresholdChanged = false;
        int min = _minThreshold, max = _maxThreshold;

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.5f - 5);
        if (ImGui.DragInt("Min", ref min, 1, 0, 255))
        {
            _minThreshold = (byte)min;
            thresholdChanged = true;
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.DragInt("Max", ref max, 1, 0, 255))
        {
            _maxThreshold = (byte)max;
            thresholdChanged = true;
        }

        ImGui.SetNextItemWidth(-1);
        int range1 = _minThreshold, range2 = _maxThreshold;
        if (ImGui.DragIntRange2("Range", ref range1, ref range2, 1, 0, 255, "Min: %d", "Max: %d"))
        {
            _minThreshold = (byte)range1;
            _maxThreshold = (byte)range2;
            thresholdChanged = true;
        }

        // Activate 2D preview whenever the user is interacting with the sliders.
        if (ImGui.IsItemActive() || thresholdChanged) Set2DPreviewState(true, _selectedMaterialForThresholding);

        ImGui.Spacing();
        if (ImGui.Checkbox("Show 3D Preview", ref _show3DThresholdPreview))
        {
            if (_show3DThresholdPreview) _ = Update3DThresholdPreviewAsync(ctDataset);
            else Clear3DThresholdPreview();
        }

        if (_show3DThresholdPreview)
        {
            ImGui.SameLine();
            if (_isGenerating3DPreview) ImGui.TextColored(new Vector4(1, 1, 0, 1), "Generating...");
            else ImGui.TextColored(new Vector4(0, 1, 0, 1), "Preview Active");
            if (thresholdChanged && (DateTime.Now - _lastPreviewUpdate).TotalMilliseconds > PREVIEW_UPDATE_DELAY_MS)
                _ = Update3DThresholdPreviewAsync(ctDataset);
        }

        ImGui.Spacing();
        if (_selectedMaterialForThresholding == null) ImGui.BeginDisabled();

        if (ImGui.Button("Add to Material", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
        {
            Set2DPreviewState(false, null);
            if (_show3DThresholdPreview) Clear3DThresholdPreview();
            _ = MaterialOperations.AddVoxelsByThresholdAsync(ctDataset.VolumeData, ctDataset.LabelData,
                _selectedMaterialForThresholding.ID, _minThreshold, _maxThreshold, ctDataset);
        }

        if (ImGui.Button("Remove from Material", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
        {
            Set2DPreviewState(false, null);
            if (_show3DThresholdPreview) Clear3DThresholdPreview();
            _ = MaterialOperations.RemoveVoxelsByThresholdAsync(ctDataset.VolumeData, ctDataset.LabelData,
                _selectedMaterialForThresholding.ID, _minThreshold, _maxThreshold, ctDataset);
        }

        if (_selectedMaterialForThresholding == null) ImGui.EndDisabled();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.SeparatorText("Interactive Segmentation Tools");
        _interactiveSegmentation?.DrawSegmentationControls(_selectedMaterialForThresholding);
    }

    // --- Static events for external preview control ---
    public static event Action<CtImageStackDataset, byte[], Vector4> Preview3DChanged;
    public static event Action<CtImageStackDataset> PreviewChanged;

    public static void Update3DPreviewFromExternal(CtImageStackDataset dataset, byte[] mask, Vector4 color)
    {
        lock (_previewLock)
        {
            if (mask == null)
            {
                _isExternalPreviewActive = false;
                _external3DPreviewMask = null;
                _previewDatasetRef = null;
            }
            else
            {
                _previewDatasetRef = new WeakReference<Dataset>(dataset);
                _external3DPreviewMask = mask;
                _externalPreviewColor = color;
                _isExternalPreviewActive = true;
            }

            ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
            Preview3DChanged?.Invoke(dataset, mask, color);
            PreviewChanged?.Invoke(dataset);
        }
    }

    public static (bool isActive, byte[] mask, Vector4 color) GetPreviewData(Dataset dataset)
    {
        lock (_previewLock)
        {
            if (_isExternalPreviewActive && _previewDatasetRef != null &&
                _previewDatasetRef.TryGetTarget(out var target) &&
                target == dataset) return (true, _external3DPreviewMask, _externalPreviewColor);
            return (false, null, Vector4.Zero);
        }
    }

    /// <summary>
    ///     Gets the state for the real-time 2D threshold preview.
    /// </summary>
    public static (bool IsActive, byte Min, byte Max, Vector4 Color) Get2DThresholdPreviewState()
    {
        if (_isThresholdPreviewActive && _previewMaterialRef != null &&
            _previewMaterialRef.TryGetTarget(out var material))
            return (true, _previewMinThreshold, _previewMaxThreshold, material.Color);
        return (false, 0, 0, Vector4.Zero);
    }

    public Material GetSelectedMaterialForThresholding()
    {
        return _selectedMaterialForThresholding;
    }

    private async Task Update3DThresholdPreviewAsync(CtImageStackDataset dataset)
    {
        if (_isGenerating3DPreview) return;

        _isGenerating3DPreview = true;

        try
        {
            await Task.Run(() =>
            {
                var volume = dataset.VolumeData;
                var width = dataset.Width;
                var height = dataset.Height;
                var depth = dataset.Depth;

                _thresholdPreviewMask3D = new byte[width * height * depth];

                Parallel.For(0, depth, z =>
                {
                    var slice = new byte[width * height];
                    volume.ReadSliceZ(z, slice);

                    for (var i = 0; i < slice.Length; i++)
                        if (slice[i] >= _minThreshold && slice[i] <= _maxThreshold)
                            _thresholdPreviewMask3D[z * width * height + i] = 255;
                });
            });

            var previewColor = _selectedMaterialForThresholding?.Color ?? new Vector4(1, 0, 0, 0.5f);
            previewColor.W = 0.5f;
            Update3DPreviewFromExternal(dataset, _thresholdPreviewMask3D, previewColor);

            _lastPreviewUpdate = DateTime.Now;
        }
        finally
        {
            _isGenerating3DPreview = false;
        }
    }

    private void Clear3DThresholdPreview()
    {
        if (_currentDataset != null)
        {
            Update3DPreviewFromExternal(_currentDataset, null, Vector4.Zero);
            _thresholdPreviewMask3D = null;
        }
    }

    private void Set2DPreviewState(bool isActive, Material material)
    {
        _isThresholdPreviewActive = isActive;
        if (isActive && material != null)
        {
            _previewMinThreshold = _minThreshold;
            _previewMaxThreshold = _maxThreshold;
            _previewMaterialRef = new WeakReference<Material>(material);
        }
        else
        {
            _previewMaterialRef = null;
        }

        // Notify viewers that they need to redraw because the preview state changed.
        if (_currentDataset != null) PreviewChanged?.Invoke(_currentDataset);
    }
}