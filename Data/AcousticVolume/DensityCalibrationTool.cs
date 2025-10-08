// GeoscientistToolkit/UI/AcousticVolume/DensityCalibrationTool.cs

using System.Numerics;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Data.AcousticVolume;

public class DensityCalibrationTool
{
    private readonly AcousticVolumeDataset _acousticDataset;
    private readonly CtImageStackDataset _ctDataset;

    private readonly DensityVolume _densityVolume;

    private readonly List<CalibrationRegion> _regions = new();

    private int _currentSlice;
    private float _densityVariation = 0.1f;
    private float _meanDensity = 2700f;
    private CalibrationMode _mode = CalibrationMode.GrayscaleMapping;
    private int _selectedMaterialIndex;
    private bool _showPreview = true;
    private bool _showWindow;

    public DensityCalibrationTool(CtImageStackDataset ctDataset, AcousticVolumeDataset acousticDataset)
    {
        _ctDataset = ctDataset;
        _acousticDataset = acousticDataset;
        _densityVolume = new DensityVolume(ctDataset.Width, ctDataset.Height, ctDataset.Depth);
        _currentSlice = ctDataset.Depth / 2;
    }

    public void Show()
    {
        _showWindow = true;
    }

    public void Draw()
    {
        if (!_showWindow) return;

        ImGui.SetNextWindowSize(new Vector2(600, 800), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Density Calibration Tool", ref _showWindow))
        {
            DrawModeSelector();
            ImGui.Separator();

            switch (_mode)
            {
                case CalibrationMode.GrayscaleMapping:
                    DrawGrayscaleMapping();
                    break;
                case CalibrationMode.MeanDensity:
                    DrawMeanDensity();
                    break;
                case CalibrationMode.ManualSelection:
                    DrawManualSelection();
                    break;
            }

            ImGui.Separator();
            DrawPreview();
            DrawActions();
        }

        ImGui.End();
    }

    private void DrawModeSelector()
    {
        ImGui.Text("Calibration Mode:");
        if (ImGui.RadioButton("Grayscale Mapping", _mode == CalibrationMode.GrayscaleMapping))
            _mode = CalibrationMode.GrayscaleMapping;
        ImGui.SameLine();
        if (ImGui.RadioButton("Mean Density", _mode == CalibrationMode.MeanDensity))
            _mode = CalibrationMode.MeanDensity;
        ImGui.SameLine();
        if (ImGui.RadioButton("Manual Selection", _mode == CalibrationMode.ManualSelection))
            _mode = CalibrationMode.ManualSelection;
    }

    private void DrawGrayscaleMapping()
    {
        ImGui.Text("Material Library:");
        var materials = RockMaterialLibrary.Materials.Select(m => m.Name).ToArray();
        ImGui.Combo("Material", ref _selectedMaterialIndex, materials, materials.Length);

        var selectedMat = RockMaterialLibrary.Materials[_selectedMaterialIndex];
        ImGui.Text($"Density: {selectedMat.Density} kg/m³");
        ImGui.Text($"Vp: {selectedMat.Vp} m/s, Vs: {selectedMat.Vs} m/s");
        ImGui.Text($"Grayscale Range: {selectedMat.GrayscaleRange[0]}-{selectedMat.GrayscaleRange[1]}");
    }

    private void DrawMeanDensity()
    {
        ImGui.InputFloat("Mean Density (kg/m³)", ref _meanDensity, 10, 100, "%.0f");
        ImGui.SliderFloat("Density Variation", ref _densityVariation, 0.0f, 0.5f, "%.2f");
        ImGui.Text("Density will vary from " +
                   (_meanDensity * (1 - _densityVariation)).ToString("F0") + " to " +
                   (_meanDensity * (1 + _densityVariation)).ToString("F0") + " kg/m³");
    }

    private void DrawManualSelection()
    {
        ImGui.Text("Slice: " + (_currentSlice + 1));
        ImGui.SameLine();
        if (ImGui.Button("Previous")) _currentSlice = Math.Max(0, _currentSlice - 1);
        ImGui.SameLine();
        if (ImGui.Button("Next")) _currentSlice = Math.Min(_ctDataset.Depth - 1, _currentSlice + 1);

        ImGui.Text("Manual Regions:");
        if (ImGui.Button("Add Region")) _regions.Add(new CalibrationRegion());

        for (var i = 0; i < _regions.Count; i++)
        {
            ImGui.PushID(i);

            var name = _regions[i].Name;
            if (ImGui.InputText("Name", ref name, 64)) _regions[i].Name = name;

            var density = _regions[i].Density;
            if (ImGui.InputFloat("Density", ref density, 10, 100, "%.0f")) _regions[i].Density = density;

            var color = _regions[i].Color;
            if (ImGui.ColorEdit4("Color", ref color, ImGuiColorEditFlags.NoInputs)) _regions[i].Color = color;

            if (ImGui.Button("Select")) _regions[i].IsSelecting = true;
            ImGui.SameLine();
            if (ImGui.Button("Remove"))
            {
                _regions.RemoveAt(i);
                i--;
            }

            ImGui.PopID();
        }
    }

    private void DrawPreview()
    {
        ImGui.Checkbox("Show Preview", ref _showPreview);
        if (_showPreview) ImGui.Image(IntPtr.Zero, new Vector2(256, 256));
    }

    private void DrawActions()
    {
        if (ImGui.Button("Apply Calibration")) ApplyCalibration();
        ImGui.SameLine();
        if (ImGui.Button("Reset")) ResetCalibration();
    }

    private void ApplyCalibration()
    {
        Logger.Log("[DensityCalibrationTool] Starting calibration...");

        switch (_mode)
        {
            case CalibrationMode.GrayscaleMapping:
                ApplyGrayscaleMapping();
                break;
            case CalibrationMode.MeanDensity:
                ApplyMeanDensity();
                break;
            case CalibrationMode.ManualSelection:
                ApplyManualSelection();
                break;
        }

        _acousticDataset.YoungsModulusMPa = (float)(_densityVolume.GetMeanYoungsModulus() / 1e6);
        _acousticDataset.PoissonRatio = _densityVolume.GetMeanPoissonRatio();

        Logger.Log("[DensityCalibrationTool] Calibration complete");
    }

    private void ApplyGrayscaleMapping()
    {
        var selectedMat = RockMaterialLibrary.Materials[_selectedMaterialIndex];

        Parallel.For(0, _ctDataset.Depth, z =>
        {
            var graySlice = new byte[_ctDataset.Width * _ctDataset.Height];
            _ctDataset.VolumeData.ReadSliceZ(z, graySlice);

            for (var i = 0; i < graySlice.Length; i++)
            {
                var material = RockMaterialLibrary.GetMaterialByGrayscale(graySlice[i]);
                _densityVolume.SetDensity(i % _ctDataset.Width, i / _ctDataset.Width, z, material.Density);
                _densityVolume.SetMaterialProperties(i % _ctDataset.Width, i / _ctDataset.Width, z, material);
            }
        });
    }

    private void ApplyMeanDensity()
    {
        Parallel.For(0, _ctDataset.Depth, z =>
        {
            var graySlice = new byte[_ctDataset.Width * _ctDataset.Height];
            _ctDataset.VolumeData.ReadSliceZ(z, graySlice);

            for (var i = 0; i < graySlice.Length; i++)
            {
                var normalized = graySlice[i] / 255f;
                var density = _meanDensity * (1 + _densityVariation * (normalized - 0.5f) * 2);

                var x = i % _ctDataset.Width;
                var y = i / _ctDataset.Width;
                _densityVolume.SetDensity(x, y, z, density);
            }
        });
    }

    private void ApplyManualSelection()
    {
        Logger.Log("[DensityCalibrationTool] Manual selection mode not yet implemented");
    }

    private void ResetCalibration()
    {
        _densityVolume.Clear();
        Logger.Log("[DensityCalibrationTool] Calibration reset");
    }

    public DensityVolume GetDensityVolume()
    {
        return _densityVolume;
    }

    private enum CalibrationMode
    {
        GrayscaleMapping,
        MeanDensity,
        ManualSelection
    }
}

public class CalibrationRegion
{
    public string Name { get; set; } = "New Region";
    public float Density { get; set; } = 2700f;
    public Vector4 Color { get; set; } = Vector4.One;
    public bool IsSelecting { get; set; }
}