// GeoscientistToolkit/UI/Image/SatelliteImageTools.cs

using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Business.Image;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Image;

public class SatelliteImageTools : IDatasetTools
{
    private readonly List<BandComposite> _availableComposites;
    private readonly string[] _blendModes = { "None", "Linear", "Smooth", "Cosine" };

    // Band selection for composition
    private readonly List<ImageDataset> _selectedBands = new();
    private bool _autoBalance;
    private bool _autoContrast;
    private int _blendModeIndex = 1; // Linear

    // Color correction parameters
    private float _brightness;
    private float _contrast = 1f;
    private float _dehazeStrength;
    private float _gamma = 1f;
    private float _saturation = 1f;
    private int _selectedCompositeIndex;
    private float _sharpenAmount;
    private bool _showBandSelector;

    // Stitching parameters
    private int _stitchColumns = 2;

    public SatelliteImageTools()
    {
        _availableComposites = SatelliteBandComposer.BandCombinations.GetAllCombinations();
    }

    public void Draw(Dataset dataset)
    {
        if (dataset is not ImageDataset imageDataset)
            return;

        // Only show for satellite images
        if (!imageDataset.HasTag(ImageTag.Satellite))
        {
            ImGui.TextDisabled("These tools are only available for satellite imagery.");
            ImGui.TextDisabled("Tag this image as 'Satellite' to enable.");
            return;
        }

        if (ImGui.CollapsingHeader("Band Composition", ImGuiTreeNodeFlags.DefaultOpen))
            DrawBandComposition(imageDataset);

        if (ImGui.CollapsingHeader("Image Stitching")) DrawStitching(imageDataset);

        if (ImGui.CollapsingHeader("Color Correction", ImGuiTreeNodeFlags.DefaultOpen))
            DrawColorCorrection(imageDataset);

        if (ImGui.CollapsingHeader("Advanced Processing")) DrawAdvancedProcessing(imageDataset);
    }

    private void DrawBandComposition(ImageDataset dataset)
    {
        ImGui.TextWrapped("Combine multispectral bands into RGB composite images.");
        ImGui.Spacing();

        // Find other satellite images that could be bands
        var potentialBands = ProjectManager.Instance.LoadedDatasets
            .OfType<ImageDataset>()
            .Where(d => d.HasTag(ImageTag.Satellite) || d.HasTag(ImageTag.Multispectral))
            .ToList();

        ImGui.Text($"Available satellite images: {potentialBands.Count}");

        if (potentialBands.Count < 2)
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "Load at least 2 band images to create composites.");
            return;
        }

        ImGui.Separator();

        // Preset combinations
        ImGui.Text("Preset Combinations:");
        ImGui.SetNextItemWidth(250);

        var compositeNames = _availableComposites.Select(c => c.Name).ToArray();
        if (ImGui.Combo("##Preset", ref _selectedCompositeIndex, compositeNames, compositeNames.Length))
            Logger.Log($"Selected preset: {_availableComposites[_selectedCompositeIndex].Name}");

        var selectedComposite = _availableComposites[_selectedCompositeIndex];

        ImGui.SameLine();
        ImGui.TextDisabled(
            $"(R:{selectedComposite.RedBand}, G:{selectedComposite.GreenBand}, B:{selectedComposite.BlueBand})");

        ImGui.Spacing();

        // Band selector
        if (ImGui.Button("Select Bands for Composition...")) _showBandSelector = true;

        ImGui.SameLine();
        ImGui.Text($"{_selectedBands.Count} bands selected");

        if (_showBandSelector) DrawBandSelectorWindow(potentialBands);

        ImGui.Spacing();

        // Compose button
        ImGui.BeginDisabled(_selectedBands.Count < 3);

        if (ImGui.Button("Create RGB Composite", new Vector2(200, 30))) CreateComposite(selectedComposite);

        ImGui.EndDisabled();

        if (_selectedBands.Count < 3)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(Need 3+ bands)");
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Auto-detect
        if (ImGui.Button("Auto-Detect Band Combination"))
        {
            var detected = SatelliteBandComposer.DetectBandCombination(potentialBands);
            Logger.Log($"Detected: {detected.Name}");

            _selectedCompositeIndex = _availableComposites.FindIndex(c => c.Name == detected.Name);
            if (_selectedCompositeIndex < 0)
                _selectedCompositeIndex = 0;
        }
    }

    private void DrawBandSelectorWindow(List<ImageDataset> availableBands)
    {
        ImGui.SetNextWindowSize(new Vector2(400, 500), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Select Bands", ref _showBandSelector))
        {
            ImGui.Text("Select bands to include in composition:");
            ImGui.Separator();

            if (ImGui.BeginChild("BandList"))
                foreach (var band in availableBands)
                {
                    var isSelected = _selectedBands.Contains(band);

                    if (ImGui.Checkbox($"##band_{band.GetHashCode()}", ref isSelected))
                    {
                        if (isSelected && !_selectedBands.Contains(band))
                            _selectedBands.Add(band);
                        else if (!isSelected && _selectedBands.Contains(band)) _selectedBands.Remove(band);
                    }

                    ImGui.SameLine();
                    ImGui.Text(band.Name);

                    ImGui.Indent();
                    ImGui.TextDisabled($"{band.Width}x{band.Height}");
                    ImGui.Unindent();
                }

            ImGui.EndChild();

            ImGui.Separator();

            if (ImGui.Button("Clear Selection")) _selectedBands.Clear();

            ImGui.SameLine();

            if (ImGui.Button("Select All"))
            {
                _selectedBands.Clear();
                _selectedBands.AddRange(availableBands);
            }

            ImGui.End();
        }
    }

    private void CreateComposite(BandComposite composition)
    {
        try
        {
            var correction = GetCurrentCorrection();
            var composite = SatelliteBandComposer.ComposeRGB(_selectedBands, composition, correction);

            ProjectManager.Instance.AddDataset(composite);
            Logger.Log($"Created composite: {composite.Name}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to create composite: {ex.Message}");
        }
    }

    private void DrawStitching(ImageDataset dataset)
    {
        ImGui.TextWrapped("Stitch multiple satellite images together into a mosaic.");
        ImGui.Spacing();

        var satelliteImages = ProjectManager.Instance.LoadedDatasets
            .OfType<ImageDataset>()
            .Where(d => d.HasTag(ImageTag.Satellite))
            .ToList();

        ImGui.Text($"Satellite images loaded: {satelliteImages.Count}");

        if (satelliteImages.Count < 2)
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "Load at least 2 satellite images to stitch.");
            return;
        }

        ImGui.Separator();

        // Stitching mode
        ImGui.Text("Stitching Method:");

        var gridLayout = ImGui.RadioButton("Grid Layout", true);
        ImGui.SameLine();
        var geoRef = ImGui.RadioButton("Georeferenced", false);
        ImGui.SameLine();
        var autoAlign = ImGui.RadioButton("Auto-Align", false);

        ImGui.Spacing();

        // Grid parameters
        if (gridLayout)
        {
            ImGui.Text("Grid Columns:");
            ImGui.SetNextItemWidth(150);
            ImGui.SliderInt("##Columns", ref _stitchColumns, 1, 10);
        }

        // Blend mode
        ImGui.Text("Blend Mode:");
        ImGui.SetNextItemWidth(150);
        ImGui.Combo("##BlendMode", ref _blendModeIndex, _blendModes, _blendModes.Length);

        ImGui.Spacing();
        ImGui.Separator();

        // Stitch button
        if (ImGui.Button("Stitch Images", new Vector2(200, 30)))
            StitchImages(satelliteImages, gridLayout, geoRef, autoAlign);

        ImGui.SameLine();

        ImGui.TextDisabled($"({satelliteImages.Count} images)");
    }

    private void StitchImages(List<ImageDataset> images, bool gridLayout, bool geoRef, bool autoAlign)
    {
        try
        {
            var blendMode = (BlendMode)_blendModeIndex;
            ImageDataset stitched;

            if (geoRef)
                stitched = ImageStitcher.StitchGeoreferenced(images, blendMode);
            else if (autoAlign)
                stitched = ImageStitcher.StitchWithAlignment(images, blendMode);
            else
                stitched = ImageStitcher.StitchGrid(images, _stitchColumns, blendMode);

            ProjectManager.Instance.AddDataset(stitched);
            Logger.Log($"Stitched {images.Count} images into mosaic: {stitched.Width}x{stitched.Height}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to stitch images: {ex.Message}");
        }
    }

    private void DrawColorCorrection(ImageDataset dataset)
    {
        ImGui.TextWrapped("Adjust color, brightness, contrast, and other image properties.");
        ImGui.Spacing();

        var changed = false;

        // Brightness
        ImGui.Text("Brightness:");
        ImGui.SetNextItemWidth(200);
        changed |= ImGui.SliderFloat("##Brightness", ref _brightness, -1f, 1f, "%.2f");

        // Contrast
        ImGui.Text("Contrast:");
        ImGui.SetNextItemWidth(200);
        changed |= ImGui.SliderFloat("##Contrast", ref _contrast, 0f, 2f, "%.2f");

        // Gamma
        ImGui.Text("Gamma:");
        ImGui.SetNextItemWidth(200);
        changed |= ImGui.SliderFloat("##Gamma", ref _gamma, 0.1f, 3f, "%.2f");

        // Saturation
        ImGui.Text("Saturation:");
        ImGui.SetNextItemWidth(200);
        changed |= ImGui.SliderFloat("##Saturation", ref _saturation, 0f, 2f, "%.2f");

        ImGui.Spacing();
        ImGui.Separator();

        // Auto corrections
        changed |= ImGui.Checkbox("Auto White Balance", ref _autoBalance);
        changed |= ImGui.Checkbox("Auto Contrast", ref _autoContrast);

        ImGui.Spacing();
        ImGui.Separator();

        // Buttons
        if (ImGui.Button("Apply Correction")) ApplyColorCorrection(dataset);

        ImGui.SameLine();

        if (ImGui.Button("Reset to Defaults")) ResetCorrection();

        ImGui.SameLine();

        if (ImGui.Button("Preview"))
        {
            ApplyColorCorrection(dataset, true);
            Logger.Log("Created temporary preview dataset. You can delete it when finished.");
        }
    }

    private void DrawAdvancedProcessing(ImageDataset dataset)
    {
        ImGui.TextWrapped("Advanced atmospheric and enhancement processing.");
        ImGui.Spacing();

        // Dehaze
        ImGui.Text("Atmospheric Haze Removal:");
        ImGui.SetNextItemWidth(200);
        ImGui.SliderFloat("##Dehaze", ref _dehazeStrength, 0f, 1f, "%.2f");

        if (ImGui.Button("Apply Dehaze")) ApplyDehaze(dataset);

        ImGui.Spacing();
        ImGui.Separator();

        // Sharpen
        ImGui.Text("Sharpening:");
        ImGui.SetNextItemWidth(200);
        ImGui.SliderFloat("##Sharpen", ref _sharpenAmount, 0f, 2f, "%.2f");

        if (ImGui.Button("Apply Sharpen")) ApplySharpen(dataset);

        ImGui.Spacing();
        ImGui.Separator();

        // Histogram equalization
        if (ImGui.Button("Histogram Equalization")) ApplyHistogramEqualization(dataset);

        ImGui.SameLine();
        ImGui.TextDisabled("(Improves contrast)");
    }

    private void ApplyColorCorrection(ImageDataset dataset, bool isPreview = false)
    {
        try
        {
            var correction = GetCurrentCorrection();
            var corrected = ColorCorrectionProcessor.ApplyCorrection(dataset, correction);

            if (isPreview) corrected.Name = $"[Preview] {dataset.Name}";

            ProjectManager.Instance.AddDataset(corrected);
            Logger.Log($"Applied color correction to {dataset.Name}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to apply correction: {ex.Message}");
        }
    }

    private void ApplyDehaze(ImageDataset dataset)
    {
        try
        {
            if (dataset.ImageData == null)
                dataset.Load();

            var dehazed =
                ColorCorrectionProcessor.Dehaze(dataset.ImageData, dataset.Width, dataset.Height, _dehazeStrength);

            var result = new ImageDataset($"{dataset.Name}_Dehazed", "")
            {
                Width = dataset.Width,
                Height = dataset.Height,
                ImageData = dehazed,
                Tags = dataset.Tags
            };

            ProjectManager.Instance.AddDataset(result);
            Logger.Log($"Applied dehaze to {dataset.Name}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to apply dehaze: {ex.Message}");
        }
    }

    private void ApplySharpen(ImageDataset dataset)
    {
        try
        {
            if (dataset.ImageData == null)
                dataset.Load();

            var sharpened =
                ColorCorrectionProcessor.Sharpen(dataset.ImageData, dataset.Width, dataset.Height, _sharpenAmount);

            var result = new ImageDataset($"{dataset.Name}_Sharpened", "")
            {
                Width = dataset.Width,
                Height = dataset.Height,
                ImageData = sharpened,
                Tags = dataset.Tags
            };

            ProjectManager.Instance.AddDataset(result);
            Logger.Log($"Applied sharpening to {dataset.Name}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to apply sharpening: {ex.Message}");
        }
    }

    private void ApplyHistogramEqualization(ImageDataset dataset)
    {
        try
        {
            if (dataset.ImageData == null)
                dataset.Load();

            var equalized =
                ColorCorrectionProcessor.HistogramEqualization(dataset.ImageData, dataset.Width, dataset.Height);

            var result = new ImageDataset($"{dataset.Name}_Equalized", "")
            {
                Width = dataset.Width,
                Height = dataset.Height,
                ImageData = equalized,
                Tags = dataset.Tags
            };

            ProjectManager.Instance.AddDataset(result);
            Logger.Log($"Applied histogram equalization to {dataset.Name}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to apply histogram equalization: {ex.Message}");
        }
    }

    private ColorCorrection GetCurrentCorrection()
    {
        return new ColorCorrection
        {
            Brightness = _brightness,
            Contrast = _contrast,
            Gamma = _gamma,
            Saturation = _saturation,
            AutoBalance = _autoBalance,
            AutoContrast = _autoContrast
        };
    }

    private void ResetCorrection()
    {
        _brightness = 0f;
        _contrast = 1f;
        _gamma = 1f;
        _saturation = 1f;
        _autoBalance = false;
        _autoContrast = false;
        _dehazeStrength = 0f;
        _sharpenAmount = 0f;
    }
}