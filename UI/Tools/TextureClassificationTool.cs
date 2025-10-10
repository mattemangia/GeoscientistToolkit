// GeoscientistToolkit/UI/Tools/TextureClassificationTool.cs

using System.Numerics;
using GeoscientistToolkit.Analysis.TextureClassification;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Tools;

public class TextureClassificationTool : IDatasetTools, IDisposable
{
    private readonly bool _gpuAvailable;
    private float _classificationProgress;

    // Classification parameters
    private float _classificationThreshold = 0.7f;
    private TextureClassifier _classifier;
    private CtImageStackDataset _currentDataset;
    private int _currentDrawingClass = 1;

    private bool _disposed;
    private int _gaborOrientations = 8;
    private int _gaborScales = 4;
    private int _glcmAngles = 4;

    // Feature extraction parameters
    private int _glcmDistance = 1;
    private bool _isClassifying;
    private bool _isDrawingMode;
    private bool _isTraining;
    private int _lbpPoints = 8;
    private int _lbpRadius = 1;
    private TexturePatchManager _patchManager;

    // Patch-based learning parameters
    private int _patchSize = 32;
    private int _selectedMaterialIndex;

    private ClassificationMethod _selectedMethod = ClassificationMethod.PatchBasedLearning;
    private Material _targetMaterial;
    private bool _useGPU = true;

    public TextureClassificationTool()
    {
        _gpuAvailable = TextureClassifier.IsGPUAvailable();
    }

    public void Draw(Dataset dataset)
    {
        if (_disposed) return;

        if (dataset is not CtImageStackDataset ctDataset)
        {
            ImGui.TextDisabled("Texture classification is available for CT Image Stack datasets.");
            Cleanup();
            return;
        }

        if (!ReferenceEquals(ctDataset, _currentDataset))
        {
            Cleanup();
            Initialize(ctDataset);
        }

        DrawUI(ctDataset);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Cleanup();
    }

    private void Initialize(CtImageStackDataset dataset)
    {
        _currentDataset = dataset;
        _patchManager = new TexturePatchManager(dataset);
        _classifier = new TextureClassifier(_useGPU);

        TextureClassificationIntegration.RegisterTool(dataset, this);

        Logger.Log("[TextureClassificationTool] Initialized for dataset: " + dataset.Name);
    }

    private void Cleanup()
    {
        if (_currentDataset != null)
        {
            TextureClassificationIntegration.UnregisterTool(_currentDataset);
            _patchManager?.Dispose();
            _classifier?.Dispose();
            _currentDataset = null;
        }
    }

    private void DrawUI(CtImageStackDataset dataset)
    {
        var materials = dataset.Materials.Where(m => m.ID != 0).ToList();
        if (materials.Count == 0)
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "Warning: No materials defined.");
            ImGui.TextWrapped("Create materials in the Material Manager before using texture classification.");
            return;
        }

        // Method selection
        ImGui.SeparatorText("Classification Method");

        var methodNames = new[] { "Patch-Based Learning", "GLCM Features", "Local Binary Pattern", "Gabor Filters" };
        var methodIndex = (int)_selectedMethod;

        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##Method", ref methodIndex, methodNames, methodNames.Length))
            _selectedMethod = (ClassificationMethod)methodIndex;

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Method-specific UI
        switch (_selectedMethod)
        {
            case ClassificationMethod.PatchBasedLearning:
                DrawPatchBasedUI(dataset, materials);
                break;
            case ClassificationMethod.GLCM:
                DrawGLCMUI(dataset, materials);
                break;
            case ClassificationMethod.LocalBinaryPattern:
                DrawLBPUI(dataset, materials);
                break;
            case ClassificationMethod.GaborFilters:
                DrawGaborUI(dataset, materials);
                break;
        }

        // GPU/SIMD selection
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.SeparatorText("Acceleration");

        if (_gpuAvailable)
        {
            ImGui.Checkbox("Use GPU Acceleration (OpenCL)", ref _useGPU);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Use GPU for faster classification. Falls back to SIMD if unavailable.");
        }
        else
        {
            ImGui.TextColored(new Vector4(1, 0.7f, 0, 1), "GPU not available - using SIMD acceleration");
        }
    }

    private void DrawPatchBasedUI(CtImageStackDataset dataset, List<Material> materials)
    {
        ImGui.SeparatorText("Training Data");

        // Target material selection
        ImGui.Text("Target Material:");
        var materialNames = materials.Select(m => m.Name).ToArray();
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##TargetMaterial", ref _selectedMaterialIndex, materialNames, materialNames.Length))
            _targetMaterial = materials[_selectedMaterialIndex];
        _targetMaterial ??= materials.FirstOrDefault();

        ImGui.Spacing();

        // Patch size
        ImGui.Text("Patch Size:");
        ImGui.SetNextItemWidth(-1);
        ImGui.SliderInt("##PatchSize", ref _patchSize, 8, 64);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Size of training patches (pixels)");

        ImGui.Spacing();

        // Drawing controls
        ImGui.SeparatorText("Draw Training Patches");

        ImGui.Text("Class to Draw:");
        ImGui.SetNextItemWidth(-1);
        ImGui.SliderInt("##DrawClass", ref _currentDrawingClass, 1, 5);

        ImGui.Spacing();

        var drawModeText = _isDrawingMode ? "Drawing Mode: ON" : "Drawing Mode: OFF";
        var drawModeColor = _isDrawingMode ? new Vector4(0, 1, 0, 1) : new Vector4(0.7f, 0.7f, 0.7f, 1);
        ImGui.TextColored(drawModeColor, drawModeText);

        ImGui.Spacing();

        if (_isDrawingMode)
        {
            if (ImGui.Button("Stop Drawing", new Vector2(-1, 0))) _isDrawingMode = false;
            ImGui.TextWrapped("Click on slices to add training patches. Right-click to remove.");
        }
        else
        {
            if (ImGui.Button("Start Drawing", new Vector2(-1, 0))) _isDrawingMode = true;
        }

        ImGui.Spacing();

        // Patch statistics
        var patchCount = _patchManager?.GetTotalPatchCount() ?? 0;
        ImGui.Text($"Total Patches: {patchCount}");

        if (patchCount > 0)
        {
            var classCounts = _patchManager.GetClassCounts();
            ImGui.Indent();
            foreach (var (classId, count) in classCounts) ImGui.Text($"Class {classId}: {count} patches");
            ImGui.Unindent();
        }

        ImGui.Spacing();

        if (ImGui.Button("Clear All Patches", new Vector2(-1, 0))) _patchManager?.ClearAllPatches();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Training
        ImGui.SeparatorText("Train Classifier");

        if (patchCount < 10)
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "Need at least 10 patches to train");
            ImGui.BeginDisabled();
        }

        if (_isTraining)
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "Training in progress...");
            ImGui.ProgressBar(_classificationProgress);
        }
        else
        {
            if (ImGui.Button("Train Classifier", new Vector2(-1, 0))) _ = TrainClassifierAsync(dataset);
        }

        if (patchCount < 10) ImGui.EndDisabled();

        ImGui.Spacing();

        // Classification
        ImGui.SeparatorText("Apply Classification");

        ImGui.Text("Confidence Threshold:");
        ImGui.SetNextItemWidth(-1);
        ImGui.SliderFloat("##Threshold", ref _classificationThreshold, 0.1f, 1.0f, "%.2f");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Minimum confidence to assign a voxel to the target material");

        ImGui.Spacing();

        if (!_classifier.IsTrained)
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "Train the classifier first");
            ImGui.BeginDisabled();
        }

        if (_isClassifying)
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "Classifying volume...");
            ImGui.ProgressBar(_classificationProgress);
        }
        else
        {
            if (ImGui.Button("Classify Entire Volume", new Vector2(-1, 0))) _ = ClassifyVolumeAsync(dataset);
        }

        if (!_classifier.IsTrained) ImGui.EndDisabled();
    }

    private void DrawGLCMUI(CtImageStackDataset dataset, List<Material> materials)
    {
        ImGui.TextWrapped(
            "Gray Level Co-occurrence Matrix (GLCM) based classification using statistical texture features.");
        ImGui.Spacing();

        ImGui.Text("Distance:");
        ImGui.SetNextItemWidth(-1);
        ImGui.SliderInt("##GLCMDistance", ref _glcmDistance, 1, 5);

        ImGui.Text("Angles:");
        ImGui.SetNextItemWidth(-1);
        ImGui.SliderInt("##GLCMAngles", ref _glcmAngles, 4, 8);

        ImGui.Spacing();
        DrawCommonClassificationUI(dataset, materials);
    }

    private void DrawLBPUI(CtImageStackDataset dataset, List<Material> materials)
    {
        ImGui.TextWrapped("Local Binary Pattern based classification using local texture descriptors.");
        ImGui.Spacing();

        ImGui.Text("Radius:");
        ImGui.SetNextItemWidth(-1);
        ImGui.SliderInt("##LBPRadius", ref _lbpRadius, 1, 3);

        ImGui.Text("Points:");
        ImGui.SetNextItemWidth(-1);
        ImGui.SliderInt("##LBPPoints", ref _lbpPoints, 8, 24);

        ImGui.Spacing();
        DrawCommonClassificationUI(dataset, materials);
    }

    private void DrawGaborUI(CtImageStackDataset dataset, List<Material> materials)
    {
        ImGui.TextWrapped("Gabor filter bank based classification using multi-scale and multi-orientation features.");
        ImGui.Spacing();

        ImGui.Text("Scales:");
        ImGui.SetNextItemWidth(-1);
        ImGui.SliderInt("##GaborScales", ref _gaborScales, 2, 6);

        ImGui.Text("Orientations:");
        ImGui.SetNextItemWidth(-1);
        ImGui.SliderInt("##GaborOrientations", ref _gaborOrientations, 4, 12);

        ImGui.Spacing();
        DrawCommonClassificationUI(dataset, materials);
    }

    private void DrawCommonClassificationUI(CtImageStackDataset dataset, List<Material> materials)
    {
        // Target material selection
        ImGui.Text("Target Material:");
        var materialNames = materials.Select(m => m.Name).ToArray();
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##TargetMaterial", ref _selectedMaterialIndex, materialNames, materialNames.Length))
            _targetMaterial = materials[_selectedMaterialIndex];
        _targetMaterial ??= materials.FirstOrDefault();

        ImGui.Spacing();

        ImGui.Text("Threshold:");
        ImGui.SetNextItemWidth(-1);
        ImGui.SliderFloat("##Threshold", ref _classificationThreshold, 0.1f, 1.0f, "%.2f");

        ImGui.Spacing();

        if (_isClassifying)
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "Classifying...");
            ImGui.ProgressBar(_classificationProgress);
        }
        else
        {
            if (ImGui.Button("Apply Classification", new Vector2(-1, 0)))
                _ = ApplyFeatureBasedClassificationAsync(dataset);
        }
    }

    private async Task TrainClassifierAsync(CtImageStackDataset dataset)
    {
        if (_isTraining) return;

        _isTraining = true;
        _classificationProgress = 0;

        try
        {
            Logger.Log("[TextureClassificationTool] Starting classifier training...");

            await Task.Run(() =>
            {
                var patches = _patchManager.GetAllPatches();
                _classifier.Train(patches, _patchSize, progress => { _classificationProgress = progress; });
            });

            Logger.Log("[TextureClassificationTool] Training completed successfully");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[TextureClassificationTool] Training failed: {ex.Message}");
        }
        finally
        {
            _isTraining = false;
            _classificationProgress = 0;
        }
    }

    private async Task ClassifyVolumeAsync(CtImageStackDataset dataset)
    {
        if (_isClassifying || _targetMaterial == null) return;

        _isClassifying = true;
        _classificationProgress = 0;

        try
        {
            Logger.Log($"[TextureClassificationTool] Classifying volume for material {_targetMaterial.Name}...");

            await Task.Run(() =>
            {
                var width = dataset.Width;
                var height = dataset.Height;
                var depth = dataset.Depth;

                Parallel.For(0, depth, z =>
                {
                    var graySlice = new byte[width * height];
                    var labelSlice = new byte[width * height];

                    dataset.VolumeData.ReadSliceZ(z, graySlice);
                    dataset.LabelData.ReadSliceZ(z, labelSlice);

                    var predictions = _classifier.ClassifySlice(graySlice, width, height, _patchSize);

                    for (var i = 0; i < predictions.Length; i++)
                        if (predictions[i] >= _classificationThreshold)
                            labelSlice[i] = _targetMaterial.ID;

                    dataset.LabelData.WriteSliceZ(z, labelSlice);

                    _classificationProgress = (float)(z + 1) / depth;
                });
            });

            // Save and notify
            dataset.SaveLabelData();
            dataset.SaveMaterials();
            ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
            ProjectManager.Instance.HasUnsavedChanges = true;

            Logger.Log("[TextureClassificationTool] Classification completed successfully");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[TextureClassificationTool] Classification failed: {ex.Message}");
        }
        finally
        {
            _isClassifying = false;
            _classificationProgress = 0;
        }
    }

    private async Task ApplyFeatureBasedClassificationAsync(CtImageStackDataset dataset)
    {
        if (_isClassifying || _targetMaterial == null) return;

        _isClassifying = true;
        _classificationProgress = 0;

        try
        {
            Logger.Log($"[TextureClassificationTool] Applying {_selectedMethod} classification...");

            await Task.Run(() =>
            {
                var extractor = new TextureFeatureExtractor();
                var width = dataset.Width;
                var height = dataset.Height;
                var depth = dataset.Depth;

                Parallel.For(0, depth, z =>
                {
                    var graySlice = new byte[width * height];
                    var labelSlice = new byte[width * height];

                    dataset.VolumeData.ReadSliceZ(z, graySlice);
                    dataset.LabelData.ReadSliceZ(z, labelSlice);

                    float[] featureMap = null;

                    switch (_selectedMethod)
                    {
                        case ClassificationMethod.GLCM:
                            featureMap =
                                extractor.ExtractGLCMFeatures(graySlice, width, height, _glcmDistance, _glcmAngles);
                            break;
                        case ClassificationMethod.LocalBinaryPattern:
                            featureMap = extractor.ExtractLBPFeatures(graySlice, width, height, _lbpRadius, _lbpPoints);
                            break;
                        case ClassificationMethod.GaborFilters:
                            featureMap = extractor.ExtractGaborFeatures(graySlice, width, height, _gaborScales,
                                _gaborOrientations);
                            break;
                    }

                    if (featureMap != null)
                    {
                        // Normalize and threshold
                        var max = featureMap.Max();
                        if (max > 0)
                            for (var i = 0; i < featureMap.Length; i++)
                            {
                                var normalized = featureMap[i] / max;
                                if (normalized >= _classificationThreshold) labelSlice[i] = _targetMaterial.ID;
                            }
                    }

                    dataset.LabelData.WriteSliceZ(z, labelSlice);

                    _classificationProgress = (float)(z + 1) / depth;
                });
            });

            // Save and notify
            dataset.SaveLabelData();
            dataset.SaveMaterials();
            ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
            ProjectManager.Instance.HasUnsavedChanges = true;

            Logger.Log("[TextureClassificationTool] Feature-based classification completed");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[TextureClassificationTool] Classification failed: {ex.Message}");
        }
        finally
        {
            _isClassifying = false;
            _classificationProgress = 0;
        }
    }

    // Called by the integration when user clicks on a slice
    public void HandleViewerClick(Vector2 clickPos, int sliceIndex, int viewIndex, int width, int height)
    {
        if (!_isDrawingMode || _patchManager == null) return;

        var x = (int)clickPos.X;
        var y = (int)clickPos.Y;

        _patchManager.AddPatch(x, y, sliceIndex, viewIndex, _currentDrawingClass, _patchSize);
        ProjectManager.Instance.NotifyDatasetDataChanged(_currentDataset);
    }

    public void HandleViewerRightClick(Vector2 clickPos, int sliceIndex, int viewIndex, int width, int height)
    {
        if (!_isDrawingMode || _patchManager == null) return;

        var x = (int)clickPos.X;
        var y = (int)clickPos.Y;

        _patchManager.RemovePatchAt(x, y, sliceIndex, viewIndex);
        ProjectManager.Instance.NotifyDatasetDataChanged(_currentDataset);
    }

    public bool IsDrawingMode()
    {
        return _isDrawingMode;
    }

    public List<TrainingPatch> GetPatchesForSlice(int sliceIndex, int viewIndex)
    {
        return _patchManager?.GetPatchesForSlice(sliceIndex, viewIndex) ?? new List<TrainingPatch>();
    }

    private enum ClassificationMethod
    {
        PatchBasedLearning,
        GLCM,
        LocalBinaryPattern,
        GaborFilters
    }
}