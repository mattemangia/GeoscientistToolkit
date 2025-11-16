// GeoscientistToolkit/Analysis/PNM/DualPNMGenerator.cs

using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.Pnm;

/// <summary>
/// Workflow tool for generating Dual Pore Network Models.
/// Integrates CT data (macro-pores) with SEM images (micro-pores).
/// Based on FOUBERT, DE BOEVER et al. dual porosity approach.
/// </summary>
public class DualPNMGeneratorTool
{
    // State
    private enum GenerationState
    {
        SelectCTData,
        GenerateMacroPNM,
        SelectSEMImages,
        CalibrateSEM,
        AssociateSEMToPores,
        GenerateMicroPNM,
        ConfigureCoupling,
        Complete
    }

    private GenerationState _currentState = GenerationState.SelectCTData;

    // Data
    private CtImageStackDataset _ctDataset;
    private PNMDataset _macroPNM;
    private List<SEMImageInfo> _semImages = new();
    private DualPNMDataset _dualPNM;

    // UI State
    private int _selectedCTIndex = -1;
    private PNMGeneratorOptions _macroOptions = new();
    private int _selectedSEMIndex = -1;
    private int _selectedMacroPoreID = -1;
    private DualPorosityCouplingMode _couplingMode = DualPorosityCouplingMode.Parallel;
    private string _outputName = "DualPNM";
    private string _statusMessage = "";

    // Scale calibration
    private ScaleCalibrationTool _scaleCalibrationTool = new();
    private int _calibratingImageIndex = -1;

    public bool IsOpen { get; set; }

    public void Show(ProjectManager projectManager)
    {
        if (!IsOpen) return;

        ImGui.SetNextWindowSize(new Vector2(900, 700), ImGuiCond.FirstUseEver);
        bool isOpen = IsOpen;
        if (ImGui.Begin("Dual PNM Generator", ref isOpen))
        {
            IsOpen = isOpen;
            DrawHeader();
            ImGui.Separator();

            switch (_currentState)
            {
                case GenerationState.SelectCTData:
                    DrawCTSelectionUI(projectManager);
                    break;

                case GenerationState.GenerateMacroPNM:
                    DrawMacroPNMGenerationUI(projectManager);
                    break;

                case GenerationState.SelectSEMImages:
                    DrawSEMSelectionUI(projectManager);
                    break;

                case GenerationState.CalibrateSEM:
                    DrawSEMCalibrationUI();
                    break;

                case GenerationState.AssociateSEMToPores:
                    DrawAssociationUI();
                    break;

                case GenerationState.GenerateMicroPNM:
                    DrawMicroPNMGenerationUI();
                    break;

                case GenerationState.ConfigureCoupling:
                    DrawCouplingConfigurationUI(projectManager);
                    break;

                case GenerationState.Complete:
                    DrawCompletionUI();
                    break;
            }

            ImGui.Separator();
            DrawStatusBar();
        }
        ImGui.End();
    }

    private void DrawHeader()
    {
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "DUAL PORE NETWORK MODEL GENERATOR");
        ImGui.Text("Integrates CT (macro-pores) and SEM (micro-pores) data");
        ImGui.Spacing();

        // Progress indicator
        var progressSteps = new[] { "CT", "Macro-PNM", "SEM", "Calibrate", "Associate", "Micro-PNM", "Coupling", "Done" };
        var currentStep = (int)_currentState;

        for (int i = 0; i < progressSteps.Length; i++)
        {
            if (i > 0) ImGui.SameLine();

            var color = i < currentStep ? new Vector4(0.2f, 0.8f, 0.2f, 1.0f) :
                        i == currentStep ? new Vector4(0.4f, 0.8f, 1.0f, 1.0f) :
                        new Vector4(0.5f, 0.5f, 0.5f, 1.0f);

            ImGui.TextColored(color, progressSteps[i]);
            if (i < progressSteps.Length - 1)
            {
                ImGui.SameLine();
                ImGui.Text(">");
            }
        }
    }

    private void DrawCTSelectionUI(ProjectManager projectManager)
    {
        ImGui.TextWrapped("Step 1: Select CT dataset for macro-pore network extraction");
        ImGui.Spacing();

        var ctDatasets = projectManager.Datasets
            .Where(d => d.Type == DatasetType.CtImageStack)
            .ToList();

        if (ctDatasets.Count == 0)
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "No CT datasets found in project. Please load CT data first.");
            return;
        }

        ImGui.Text("Available CT Datasets:");
        ImGui.BeginChild("CTList", new Vector2(0, 200), ImGuiChildFlags.Border);

        for (int i = 0; i < ctDatasets.Count; i++)
        {
            var dataset = ctDatasets[i] as CtImageStackDataset;
            bool isSelected = i == _selectedCTIndex;

            if (ImGui.Selectable($"{dataset.Name}##ct{i}", isSelected))
            {
                _selectedCTIndex = i;
                _ctDataset = dataset;
            }

            if (ImGui.IsItemHovered() && dataset != null)
            {
                ImGui.BeginTooltip();
                ImGui.Text($"Dimensions: {dataset.Width} x {dataset.Height} x {dataset.Depth}");
                ImGui.Text($"Voxel size: {dataset.PixelSize:F3} µm");
                ImGui.EndTooltip();
            }
        }

        ImGui.EndChild();

        ImGui.Spacing();

        if (_ctDataset != null)
        {
            ImGui.TextColored(new Vector4(0.2f, 0.8f, 0.2f, 1.0f), $"Selected: {_ctDataset.Name}");

            if (ImGui.Button("Next: Generate Macro-PNM", new Vector2(200, 0)))
            {
                _currentState = GenerationState.GenerateMacroPNM;
                _statusMessage = "Ready to generate macro-PNM from CT data";
            }
        }
    }

    private void DrawMacroPNMGenerationUI(ProjectManager projectManager)
    {
        ImGui.TextWrapped("Step 2: Generate macro-pore network from CT data");
        ImGui.Spacing();

        // Show PNM generation options
        ImGui.Text("PNM Generation Options:");
        ImGui.Separator();

        // Material selection
        if (_ctDataset != null && _ctDataset.Materials != null && _ctDataset.Materials.Count > 0)
        {
            ImGui.Text("Material ID:");
            ImGui.SameLine();
            if (ImGui.BeginCombo("##MaterialID", $"Material {_macroOptions.MaterialID}"))
            {
                foreach (var material in _ctDataset.Materials)
                {
                    bool isSelected = _macroOptions.MaterialID == material.ID;
                    if (ImGui.Selectable($"{material.Name} (ID: {material.ID})", isSelected))
                    {
                        _macroOptions.MaterialID = material.ID;
                    }
                }
                ImGui.EndCombo();
            }
        }

        // Generation mode
        ImGui.Text("Generation Mode:");
        ImGui.SameLine();
        int modeIndex = (int)_macroOptions.Mode;
        if (ImGui.Combo("##GenMode", ref modeIndex, new[] { "Conservative", "Aggressive" }, 2))
        {
            _macroOptions.Mode = (PNMGenerationMode)modeIndex;
        }

        // Neighborhood
        ImGui.Text("Neighborhood:");
        ImGui.SameLine();
        int nbhIndex = (int)_macroOptions.Neighborhood;
        if (ImGui.Combo("##Neighborhood", ref nbhIndex, new[] { "6-connected", "18-connected", "26-connected" }, 3))
        {
            _macroOptions.Neighborhood = (NeighborhoodType)nbhIndex;
        }

        ImGui.Spacing();

        if (ImGui.Button("Generate Macro-PNM", new Vector2(200, 0)))
        {
            GenerateMacroPNM(projectManager);
        }

        ImGui.SameLine();

        if (_macroPNM != null)
        {
            ImGui.TextColored(new Vector4(0.2f, 0.8f, 0.2f, 1.0f),
                $"Generated: {_macroPNM.Pores.Count} pores, {_macroPNM.Throats.Count} throats");

            ImGui.Spacing();

            if (ImGui.Button("Next: Select SEM Images", new Vector2(200, 0)))
            {
                _currentState = GenerationState.SelectSEMImages;
                _statusMessage = "Select SEM images for micro-pore analysis";
            }
        }
    }

    private void GenerateMacroPNM(ProjectManager projectManager)
    {
        if (_ctDataset == null) return;

        _statusMessage = "Generating macro-PNM from CT data...";

        try
        {
            _macroPNM = PNMGenerator.Generate(_ctDataset, _macroOptions, null);

            if (_macroPNM != null)
            {
                _macroPNM.Name = _outputName + "_Macro";
                _statusMessage = $"Macro-PNM generated: {_macroPNM.Pores.Count} pores, {_macroPNM.Throats.Count} throats";
                Logger.Log(_statusMessage);
            }
            else
            {
                _statusMessage = "Failed to generate macro-PNM";
                Logger.LogError(_statusMessage);
            }
        }
        catch (Exception ex)
        {
            _statusMessage = $"Error generating macro-PNM: {ex.Message}";
            Logger.LogError(_statusMessage);
        }
    }

    private void DrawSEMSelectionUI(ProjectManager projectManager)
    {
        ImGui.TextWrapped("Step 3: Select SEM images for micro-pore network extraction");
        ImGui.Spacing();

        var imageDatasets = projectManager.Datasets
            .Where(d => d.Type == DatasetType.SingleImage)
            .Select(d => d as ImageDataset)
            .Where(d => d != null)
            .ToList();

        ImGui.Text("Available Image Datasets:");
        ImGui.BeginChild("ImageList", new Vector2(0, 200), ImGuiChildFlags.Border);

        for (int i = 0; i < imageDatasets.Count; i++)
        {
            var dataset = imageDatasets[i];
            bool isSelected = _semImages.Any(sem => sem.ImageDataset == dataset);

            if (ImGui.Selectable($"{dataset.Name}##img{i}", isSelected))
            {
                if (isSelected)
                {
                    _semImages.RemoveAll(sem => sem.ImageDataset == dataset);
                }
                else
                {
                    _semImages.Add(new SEMImageInfo { ImageDataset = dataset });
                }
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text($"Size: {dataset.Width} x {dataset.Height}");
                if (dataset.HasTag(ImageTag.Calibrated))
                {
                    ImGui.Text($"Scale: {dataset.PixelSize:F4} µm/pixel");
                }
                else
                {
                    ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "Not calibrated");
                }
                ImGui.EndTooltip();
            }
        }

        ImGui.EndChild();

        ImGui.Spacing();
        ImGui.Text($"Selected: {_semImages.Count} images");

        if (_semImages.Count > 0)
        {
            if (ImGui.Button("Next: Calibrate SEM Images", new Vector2(200, 0)))
            {
                _currentState = GenerationState.CalibrateSEM;
                _statusMessage = "Calibrate SEM image scales";
            }
        }
    }

    private void DrawSEMCalibrationUI()
    {
        ImGui.TextWrapped("Step 4: Calibrate SEM image scales");
        ImGui.Spacing();

        ImGui.Text("SEM Images:");
        ImGui.BeginChild("SEMCalibList", new Vector2(0, 300), ImGuiChildFlags.Border);

        for (int i = 0; i < _semImages.Count; i++)
        {
            var semInfo = _semImages[i];
            ImGui.PushID(i);

            ImGui.Text($"{i + 1}. {semInfo.ImageDataset.Name}");
            ImGui.SameLine();

            if (semInfo.ImageDataset.HasTag(ImageTag.Calibrated))
            {
                ImGui.TextColored(new Vector4(0.2f, 0.8f, 0.2f, 1.0f),
                    $"[Calibrated: {semInfo.ImageDataset.PixelSize:F4} µm/pixel]");
            }
            else
            {
                ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "[Not calibrated]");
                ImGui.SameLine();
                if (ImGui.Button("Calibrate"))
                {
                    _calibratingImageIndex = i;
                    _scaleCalibrationTool.StartCalibration();
                    _statusMessage = $"Draw a line on the scale bar in image: {semInfo.ImageDataset.Name}";
                }
            }

            ImGui.PopID();
        }

        ImGui.EndChild();

        ImGui.Spacing();

        bool allCalibrated = _semImages.All(sem => sem.ImageDataset.HasTag(ImageTag.Calibrated));

        if (allCalibrated)
        {
            ImGui.TextColored(new Vector4(0.2f, 0.8f, 0.2f, 1.0f), "All images calibrated!");

            if (ImGui.Button("Next: Associate with Macro-Pores", new Vector2(250, 0)))
            {
                _currentState = GenerationState.AssociateSEMToPores;
                _statusMessage = "Associate SEM images with macro-pores";
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1),
                $"{_semImages.Count(sem => !sem.ImageDataset.HasTag(ImageTag.Calibrated))} images need calibration");
        }

        ImGui.Spacing();
        if (ImGui.Button("Skip (use existing calibration)", new Vector2(200, 0)))
        {
            _currentState = GenerationState.AssociateSEMToPores;
            _statusMessage = "Skipped calibration - using existing scales";
        }
    }

    private void DrawAssociationUI()
    {
        ImGui.TextWrapped("Step 5: Associate SEM images with macro-pores");
        ImGui.Spacing();

        ImGui.Text("For each SEM image, specify which macro-pore it represents:");
        ImGui.Separator();

        ImGui.BeginChild("AssociationList", new Vector2(0, 300), ImGuiChildFlags.Border);

        for (int i = 0; i < _semImages.Count; i++)
        {
            var semInfo = _semImages[i];
            ImGui.PushID(i);

            ImGui.Text($"{semInfo.ImageDataset.Name}");
            ImGui.SameLine(300);
            ImGui.Text("Macro-Pore ID:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            int macroPoreId = semInfo.MacroPoreID;
            if (ImGui.InputInt($"##poreID{i}", ref macroPoreId))
            {
                _semImages[i].MacroPoreID = macroPoreId;
            }

            if (semInfo.MacroPoreID > 0 && _macroPNM != null)
            {
                var pore = _macroPNM.Pores.FirstOrDefault(p => p.ID == semInfo.MacroPoreID);
                if (pore != null)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.2f, 0.8f, 0.2f, 1.0f), "✓ Valid");
                }
                else
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(1, 0.2f, 0.2f, 1.0f), "✗ Pore not found");
                }
            }

            ImGui.PopID();
        }

        ImGui.EndChild();

        ImGui.Spacing();

        bool allAssociated = _semImages.All(sem => sem.MacroPoreID > 0 &&
            _macroPNM?.Pores.Any(p => p.ID == sem.MacroPoreID) == true);

        if (allAssociated)
        {
            ImGui.TextColored(new Vector4(0.2f, 0.8f, 0.2f, 1.0f), "All images associated!");

            if (ImGui.Button("Next: Generate Micro-PNM", new Vector2(200, 0)))
            {
                _currentState = GenerationState.GenerateMicroPNM;
                _statusMessage = "Ready to generate micro-PNM from SEM images";
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "Not all images are associated with valid macro-pores");
        }
    }

    private void DrawMicroPNMGenerationUI()
    {
        ImGui.TextWrapped("Step 6: Generate micro-pore networks from SEM images");
        ImGui.Spacing();

        if (ImGui.Button("Generate All Micro-PNMs", new Vector2(200, 0)))
        {
            GenerateMicroPNMs();
        }

        ImGui.Spacing();

        if (_dualPNM != null && _dualPNM.MicroNetworks.Count > 0)
        {
            ImGui.TextColored(new Vector4(0.2f, 0.8f, 0.2f, 1.0f),
                $"Generated {_dualPNM.MicroNetworks.Count} micro-networks");
            ImGui.Text($"Total micro-pores: {_dualPNM.TotalMicroPoreCount}");
            ImGui.Text($"Total micro-throats: {_dualPNM.TotalMicroThroatCount}");

            ImGui.Spacing();

            if (ImGui.Button("Next: Configure Coupling", new Vector2(200, 0)))
            {
                _currentState = GenerationState.ConfigureCoupling;
                _statusMessage = "Configure dual porosity coupling";
            }
        }
    }

    private void GenerateMicroPNMs()
    {
        if (_macroPNM == null)
        {
            _statusMessage = "No macro-PNM available";
            return;
        }

        _statusMessage = "Generating micro-PNMs...";

        // Create dual PNM dataset
        _dualPNM = new DualPNMDataset(_outputName, "")
        {
            CTDatasetPath = _ctDataset?.FilePath
        };

        // Copy macro-PNM data
        CopyMacroPNMToDual(_macroPNM, _dualPNM);

        // Generate micro-PNM for each SEM image
        int successCount = 0;
        foreach (var semInfo in _semImages)
        {
            try
            {
                var microNet = GenerateMicroPNMFromSEM(semInfo);
                if (microNet != null)
                {
                    _dualPNM.AddMicroNetwork(semInfo.MacroPoreID, microNet);
                    _dualPNM.SEMImagePaths.Add(semInfo.ImageDataset.FilePath);
                    successCount++;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to generate micro-PNM for {semInfo.ImageDataset.Name}: {ex.Message}");
            }
        }

        _statusMessage = $"Generated {successCount}/{_semImages.Count} micro-networks";
        Logger.Log(_statusMessage);
    }

    private void CopyMacroPNMToDual(PNMDataset source, DualPNMDataset dest)
    {
        // Copy properties
        dest.VoxelSize = source.VoxelSize;
        dest.ImageWidth = source.ImageWidth;
        dest.ImageHeight = source.ImageHeight;
        dest.ImageDepth = source.ImageDepth;
        dest.Tortuosity = source.Tortuosity;
        dest.DarcyPermeability = source.DarcyPermeability;
        dest.NavierStokesPermeability = source.NavierStokesPermeability;
        dest.LatticeBoltzmannPermeability = source.LatticeBoltzmannPermeability;

        // Copy pores and throats via reflection (access private fields)
        var poresField = typeof(PNMDataset).GetField("_poresOriginal",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var throatsField = typeof(PNMDataset).GetField("_throatsOriginal",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (poresField != null && throatsField != null)
        {
            var sourcePores = poresField.GetValue(source) as List<Pore>;
            var sourceThroats = throatsField.GetValue(source) as List<Throat>;
            var destPores = poresField.GetValue(dest) as List<Pore>;
            var destThroats = throatsField.GetValue(dest) as List<Throat>;

            if (sourcePores != null && destPores != null)
            {
                destPores.Clear();
                destPores.AddRange(sourcePores);
            }

            if (sourceThroats != null && destThroats != null)
            {
                destThroats.Clear();
                destThroats.AddRange(sourceThroats);
            }
        }

        // Apply filter to update visible pores/throats
        dest.ApplyFilter(null);
    }

    private MicroPoreNetwork GenerateMicroPNMFromSEM(SEMImageInfo semInfo)
    {
        var imageDataset = semInfo.ImageDataset;

        // For now, create a placeholder micro-network
        // In a full implementation, this would:
        // 1. Segment the SEM image
        // 2. Extract pore network from segmentation
        // 3. Calculate micro-scale properties

        var microNet = new MicroPoreNetwork
        {
            MacroPoreID = semInfo.MacroPoreID,
            SEMPixelSize = imageDataset.PixelSize,
            SEMImagePath = imageDataset.FilePath,
            SEMImagePosition = Vector2.Zero
        };

        // Placeholder: Create simple micro-network
        // TODO: Implement actual PNM extraction from SEM image
        int microPoreCount = 100; // Placeholder
        for (int i = 0; i < microPoreCount; i++)
        {
            microNet.MicroPores.Add(new Pore
            {
                ID = i,
                Position = new Vector3(i * 10, 0, 0),
                Radius = 0.5f + (float)(i % 10) * 0.1f,
                Area = 1.0f,
                VolumeVoxels = 4.0f,
                VolumePhysical = 4.0f * imageDataset.PixelSize * imageDataset.PixelSize * imageDataset.PixelSize,
                Connections = 2
            });
        }

        // Calculate micro-network properties
        microNet.MicroVolume = microNet.MicroPores.Sum(p => p.VolumePhysical);
        microNet.MicroSurfaceArea = microNet.MicroPores.Sum(p => p.Area);
        microNet.MicroPorosity = 0.15f; // Placeholder
        microNet.MicroPermeability = 0.1f; // Placeholder (mD)

        Logger.Log($"Generated micro-network for macro-pore {semInfo.MacroPoreID}: " +
                  $"{microNet.MicroPores.Count} pores, {microNet.MicroPermeability:F3} mD");

        return microNet;
    }

    private void DrawCouplingConfigurationUI(ProjectManager projectManager)
    {
        ImGui.TextWrapped("Step 7: Configure dual porosity coupling");
        ImGui.Spacing();

        ImGui.Text("Coupling Mode:");
        int couplingModeInt = (int)_couplingMode;
        if (ImGui.RadioButton("Parallel (Separate flow paths)", ref couplingModeInt, (int)DualPorosityCouplingMode.Parallel))
            _couplingMode = (DualPorosityCouplingMode)couplingModeInt;
        if (ImGui.RadioButton("Series (Embedded micropores)", ref couplingModeInt, (int)DualPorosityCouplingMode.Series))
            _couplingMode = (DualPorosityCouplingMode)couplingModeInt;
        if (ImGui.RadioButton("Mass Transfer (Advanced)", ref couplingModeInt, (int)DualPorosityCouplingMode.MassTransfer))
            _couplingMode = (DualPorosityCouplingMode)couplingModeInt;

        ImGui.Spacing();
        ImGui.Separator();

        ImGui.TextWrapped("Coupling mode determines how macro and micro permeabilities are combined:");
        ImGui.BulletText("Parallel: k_eff = k_macro + α * k_micro (fastest flow)");
        ImGui.BulletText("Series: 1/k_eff = (1-α)/k_macro + α/k_micro (slowest flow)");
        ImGui.BulletText("Mass Transfer: Weighted average with mass exchange");

        ImGui.Spacing();

        if (ImGui.Button("Calculate Combined Properties", new Vector2(250, 0)))
        {
            if (_dualPNM != null)
            {
                _dualPNM.Coupling.CouplingMode = _couplingMode;
                _dualPNM.CalculateCombinedProperties();
                _statusMessage = "Combined properties calculated";
            }
        }

        if (_dualPNM != null && _dualPNM.Coupling.CombinedPermeability > 0)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("Results:");
            ImGui.Text($"Macro-permeability: {_dualPNM.Coupling.EffectiveMacroPermeability:F3} mD");
            ImGui.Text($"Micro-permeability: {_dualPNM.Coupling.EffectiveMicroPermeability:F3} mD");
            ImGui.Text($"Micro-porosity fraction: {_dualPNM.Coupling.TotalMicroPorosity:F4}");
            ImGui.TextColored(new Vector4(0.2f, 0.8f, 0.2f, 1.0f),
                $"Combined permeability: {_dualPNM.Coupling.CombinedPermeability:F3} mD");

            ImGui.Spacing();

            if (ImGui.Button("Finalize and Add to Project", new Vector2(250, 0)))
            {
                projectManager.AddDataset(_dualPNM);
                _currentState = GenerationState.Complete;
                _statusMessage = "Dual PNM added to project!";
                Logger.Log("Dual PNM dataset created and added to project");
            }
        }
    }

    private void DrawCompletionUI()
    {
        ImGui.TextColored(new Vector4(0.2f, 0.8f, 0.2f, 1.0f), "Dual PNM Generation Complete!");
        ImGui.Spacing();

        if (_dualPNM != null)
        {
            ImGui.Text(_dualPNM.GetStatisticsReport());

            ImGui.Spacing();
            ImGui.Separator();

            ImGui.Text("Output Name:");
            ImGui.SameLine();
            ImGui.InputText("##OutputName", ref _outputName, 100);

            if (ImGui.Button("Export to JSON", new Vector2(200, 0)))
            {
                var outputPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    _outputName + ".dualpnm.json");

                _dualPNM.ExportToJson(outputPath);
                _statusMessage = $"Exported to: {outputPath}";
            }

            ImGui.SameLine();

            if (ImGui.Button("Start New", new Vector2(200, 0)))
            {
                Reset();
            }
        }
    }

    private void DrawStatusBar()
    {
        if (!string.IsNullOrEmpty(_statusMessage))
        {
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), $"Status: {_statusMessage}");
        }
    }

    private void Reset()
    {
        _currentState = GenerationState.SelectCTData;
        _ctDataset = null;
        _macroPNM = null;
        _semImages.Clear();
        _dualPNM = null;
        _statusMessage = "Reset complete - ready to start new dual PNM";
    }
}

/// <summary>
/// Helper class to track SEM image information during workflow
/// </summary>
public class SEMImageInfo
{
    public ImageDataset ImageDataset { get; set; }
    public int MacroPoreID { get; set; }
}
