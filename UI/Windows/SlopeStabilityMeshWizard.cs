// GeoscientistToolkit/UI/Windows/SlopeStabilityMeshWizard.cs
// Wizard for creating 3D meshes from point cloud data for slope stability analysis
// Based on MATLAB code by Francesco Ottaviani (Università degli Studi di Urbino Carlo Bo)

using System.Numerics;
using GeoscientistToolkit.Analysis.SlopeStability;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Windows;

/// <summary>
/// Wizard window for generating 3D meshes from point cloud data.
/// Creates Mesh3D datasets suitable for slope stability simulations.
/// </summary>
public class SlopeStabilityMeshWizard
{
    private enum WizardStep
    {
        SelectInput,
        ConfigureParameters,
        Processing,
        Results
    }

    private bool _isOpen;
    private WizardStep _currentStep = WizardStep.SelectInput;

    // File dialogs
    private readonly ImGuiFileDialog _inputFileDialog;
    private readonly ImGuiExportFileDialog _outputFileDialog;

    // Input settings
    private string _inputFilePath = "";
    private string _outputFilePath = "";
    private string _datasetName = "SlopeModel";

    // Parameters (matching PointCloudMeshGenerator.MeshGenerationParameters)
    private float _gridStep = 2.0f;
    private float _maxEdgeLength = 4.0f;
    private float _zDeep = 20.0f;
    private float _planeDistance = 50.0f;
    private float _weldingAngle = 40.0f;
    private float _peakThreshold = 0.005f;
    private bool _enableWelding = false;
    private bool _enableInterpolation = true;
    private bool _enableDownsampling = true;
    private int _interpolationMethod = 1; // 0=Linear, 1=Nearest, 2=Natural
    private float _rotationAngle = 0.0f;
    private bool _translateToOrigin = false;
    private bool _createSolidMesh = true;
    private float _nodataValue = -9999.0f;

    // Processing state
    private bool _isProcessing;
    private float _progress;
    private string _progressMessage = "";
    private Task _processingTask;
    private PointCloudMeshGenerator.MeshGenerationResult _result;
    private string _errorMessage = "";

    // Preview data
    private int _previewPointCount;
    private Vector3 _previewMin;
    private Vector3 _previewMax;

    public SlopeStabilityMeshWizard()
    {
        _inputFileDialog = new ImGuiFileDialog("SlopeMeshInputDlg", FileDialogType.OpenFile, "Select Point Cloud File");
        _outputFileDialog = new ImGuiExportFileDialog("SlopeMeshOutputDlg", "Save Mesh As");
        _outputFileDialog.SetExtensions(
            (".obj", "Wavefront OBJ"),
            (".stl", "STL Stereolithography")
        );
    }

    public void Show()
    {
        _isOpen = true;
        _currentStep = WizardStep.SelectInput;
        _errorMessage = "";
        _result = null;
        _progress = 0;
        _progressMessage = "";
        _isProcessing = false;
    }

    public void Draw()
    {
        if (!_isOpen) return;

        ImGui.SetNextWindowSize(new Vector2(700, 600), ImGuiCond.FirstUseEver);
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));

        if (ImGui.Begin("Slope Stability Model Wizard###SlopeStabilityMeshWizard", ref _isOpen,
            ImGuiWindowFlags.NoCollapse))
        {
            DrawHeader();
            ImGui.Separator();

            switch (_currentStep)
            {
                case WizardStep.SelectInput:
                    DrawSelectInputStep();
                    break;
                case WizardStep.ConfigureParameters:
                    DrawConfigureParametersStep();
                    break;
                case WizardStep.Processing:
                    DrawProcessingStep();
                    break;
                case WizardStep.Results:
                    DrawResultsStep();
                    break;
            }

            ImGui.End();
        }

        // Handle file dialogs
        if (_inputFileDialog.Submit())
        {
            _inputFilePath = _inputFileDialog.SelectedPath;
            PreviewPointCloud();
        }

        if (_outputFileDialog.Submit())
        {
            _outputFilePath = _outputFileDialog.SelectedPath;
        }
    }

    private void DrawHeader()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.8f, 1.0f, 1.0f));
        ImGui.Text("Slope Stability Model Wizard");
        ImGui.PopStyleColor();

        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text("Generate 3D mesh models from point cloud data for slope stability analysis.");
            ImGui.Text("Based on algorithms by Francesco Ottaviani, Universita degli Studi di Urbino Carlo Bo.");
            ImGui.EndTooltip();
        }

        ImGui.Spacing();

        // Step indicator
        var steps = new[] { "1. Select Input", "2. Configure", "3. Processing", "4. Results" };
        var stepIdx = (int)_currentStep;

        float totalWidth = ImGui.GetContentRegionAvail().X;
        float stepWidth = totalWidth / steps.Length;

        for (int i = 0; i < steps.Length; i++)
        {
            if (i > 0) ImGui.SameLine();

            var isActive = i == stepIdx;
            var isCompleted = i < stepIdx;

            if (isActive)
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 1.0f, 0.4f, 1.0f));
            else if (isCompleted)
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.8f, 0.5f, 1.0f));
            else
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));

            ImGui.SetCursorPosX(i * stepWidth + (stepWidth - ImGui.CalcTextSize(steps[i]).X) / 2);
            ImGui.Text(steps[i]);
            ImGui.PopStyleColor();
        }
    }

    private void DrawSelectInputStep()
    {
        ImGui.Spacing();
        ImGui.Text("Step 1: Select Input Point Cloud");
        ImGui.Separator();
        ImGui.Spacing();

        // Input file selection
        ImGui.Text("Input Point Cloud File:");
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 100);
        ImGui.InputText("##InputPath", ref _inputFilePath, 512, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine();
        if (ImGui.Button("Browse...", new Vector2(90, 0)))
        {
            _inputFileDialog.Open(null, new[] { ".txt", ".xyz", ".csv", ".pts", ".asc" });
        }

        ImGui.Spacing();
        ImGui.TextWrapped("Supported formats: XYZ, TXT, CSV, PTS, ASC (space/tab/comma delimited)");
        ImGui.TextWrapped("Expected columns: X Y Z [R G B] (RGB values optional)");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Dataset name
        ImGui.Text("Dataset Name:");
        ImGui.SetNextItemWidth(300);
        ImGui.InputText("##DatasetName", ref _datasetName, 128);

        // Preview info
        if (!string.IsNullOrEmpty(_inputFilePath) && File.Exists(_inputFilePath))
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.9f, 0.5f, 1.0f));
            ImGui.Text("Point Cloud Preview:");
            ImGui.PopStyleColor();

            ImGui.BulletText($"File: {Path.GetFileName(_inputFilePath)}");
            ImGui.BulletText($"Points: {_previewPointCount:N0}");
            if (_previewPointCount > 0)
            {
                ImGui.BulletText($"X Range: {_previewMin.X:F2} to {_previewMax.X:F2}");
                ImGui.BulletText($"Y Range: {_previewMin.Y:F2} to {_previewMax.Y:F2}");
                ImGui.BulletText($"Z Range: {_previewMin.Z:F2} to {_previewMax.Z:F2}");
            }
        }

        // Error message
        if (!string.IsNullOrEmpty(_errorMessage))
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.4f, 0.4f, 1.0f));
            ImGui.TextWrapped(_errorMessage);
            ImGui.PopStyleColor();
        }

        ImGui.Spacing();
        DrawNavigationButtons(
            canBack: false,
            canNext: !string.IsNullOrEmpty(_inputFilePath) && File.Exists(_inputFilePath) && _previewPointCount > 0,
            onNext: () => _currentStep = WizardStep.ConfigureParameters
        );
    }

    private void PreviewPointCloud()
    {
        _previewPointCount = 0;
        _errorMessage = "";

        if (string.IsNullOrEmpty(_inputFilePath) || !File.Exists(_inputFilePath))
        {
            _errorMessage = "File not found";
            return;
        }

        try
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            int count = 0;

            using var reader = new StreamReader(_inputFilePath);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#") || line.StartsWith("//"))
                    continue;

                var parts = line.Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    if (float.TryParse(parts[0], out var x) &&
                        float.TryParse(parts[1], out var y) &&
                        float.TryParse(parts[2], out var z))
                    {
                        count++;
                        var p = new Vector3(x, y, z);
                        min = Vector3.Min(min, p);
                        max = Vector3.Max(max, p);

                        // Limit preview to avoid long processing
                        if (count >= 100000) break;
                    }
                }
            }

            _previewPointCount = count;
            _previewMin = min;
            _previewMax = max;

            if (count < 3)
                _errorMessage = "File contains fewer than 3 valid points";
        }
        catch (Exception ex)
        {
            _errorMessage = $"Error reading file: {ex.Message}";
        }
    }

    private void DrawConfigureParametersStep()
    {
        ImGui.Spacing();
        ImGui.Text("Step 2: Configure Mesh Generation Parameters");
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.BeginChild("ParametersScroll", new Vector2(0, ImGui.GetContentRegionAvail().Y - 50),
            ImGuiChildFlags.None))
        {
            // Grid and Sampling
            if (ImGui.CollapsingHeader("Grid & Sampling", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();

                ImGui.Checkbox("Enable Downsampling", ref _enableDownsampling);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Reduce point density using grid averaging");

                if (_enableDownsampling)
                {
                    ImGui.SetNextItemWidth(150);
                    ImGui.DragFloat("Grid Step", ref _gridStep, 0.1f, 0.1f, 50.0f, "%.2f m");
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Grid cell size for downsampling (meters)");
                }

                ImGui.SetNextItemWidth(150);
                ImGui.DragFloat("Nodata Value", ref _nodataValue, 1.0f, -99999.0f, 0.0f, "%.0f");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Z value to treat as nodata/invalid");

                ImGui.Unindent();
            }

            // Triangulation
            if (ImGui.CollapsingHeader("Triangulation", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();

                ImGui.SetNextItemWidth(150);
                ImGui.DragFloat("Max Edge Length", ref _maxEdgeLength, 0.1f, 0.1f, 100.0f, "%.2f m");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Maximum allowed triangle edge length. Larger triangles will be filtered out.");

                ImGui.Unindent();
            }

            // Solid Mesh
            if (ImGui.CollapsingHeader("Solid Mesh", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();

                ImGui.Checkbox("Create Solid Mesh", ref _createSolidMesh);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Create a closed solid with bottom surface and sides");

                if (_createSolidMesh)
                {
                    ImGui.SetNextItemWidth(150);
                    ImGui.DragFloat("Base Depth (Z Deep)", ref _zDeep, 0.5f, 0.1f, 500.0f, "%.1f m");
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Depth below minimum Z for the solid bottom");
                }

                ImGui.Unindent();
            }

            // Interpolation
            if (ImGui.CollapsingHeader("Interpolation"))
            {
                ImGui.Indent();

                ImGui.Checkbox("Enable Interpolation", ref _enableInterpolation);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Interpolate surface to fill gaps");

                if (_enableInterpolation)
                {
                    ImGui.SetNextItemWidth(150);
                    string[] methods = { "Linear", "Nearest", "Natural" };
                    ImGui.Combo("Method", ref _interpolationMethod, methods, methods.Length);
                }

                ImGui.Unindent();
            }

            // Mesh Welding
            if (ImGui.CollapsingHeader("Mesh Welding"))
            {
                ImGui.Indent();

                ImGui.Checkbox("Enable Mesh Welding", ref _enableWelding);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Smooth sharp peaks and fix mesh artifacts");

                if (_enableWelding)
                {
                    ImGui.SetNextItemWidth(150);
                    ImGui.DragFloat("Welding Angle", ref _weldingAngle, 1.0f, 1.0f, 89.0f, "%.0f deg");

                    ImGui.SetNextItemWidth(150);
                    ImGui.DragFloat("Peak Threshold", ref _peakThreshold, 0.0001f, 0.0001f, 1.0f, "%.4f");
                }

                ImGui.Unindent();
            }

            // Transformation
            if (ImGui.CollapsingHeader("Transformation"))
            {
                ImGui.Indent();

                ImGui.SetNextItemWidth(150);
                ImGui.DragFloat("Rotation (Z axis)", ref _rotationAngle, 1.0f, -180.0f, 180.0f, "%.1f deg");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Rotate point cloud around Z axis");

                ImGui.Checkbox("Translate to Origin", ref _translateToOrigin);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Move mesh centroid to origin (0, 0, 0)");

                ImGui.Unindent();
            }

            // Output
            if (ImGui.CollapsingHeader("Output", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();

                ImGui.Text("Output File Path:");
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 100);
                ImGui.InputText("##OutputPath", ref _outputFilePath, 512, ImGuiInputTextFlags.ReadOnly);
                ImGui.SameLine();
                if (ImGui.Button("Save As...", new Vector2(90, 0)))
                {
                    var defaultName = string.IsNullOrEmpty(_datasetName) ? "slope_model" : _datasetName;
                    var defaultDir = string.IsNullOrEmpty(_inputFilePath)
                        ? Environment.CurrentDirectory
                        : Path.GetDirectoryName(_inputFilePath);
                    _outputFileDialog.Open(defaultName, defaultDir);
                }

                ImGui.Unindent();
            }

            ImGui.EndChild();
        }

        DrawNavigationButtons(
            canBack: true,
            canNext: !string.IsNullOrEmpty(_outputFilePath),
            onBack: () => _currentStep = WizardStep.SelectInput,
            onNext: StartProcessing
        );
    }

    private void StartProcessing()
    {
        _currentStep = WizardStep.Processing;
        _isProcessing = true;
        _progress = 0;
        _progressMessage = "Starting...";
        _errorMessage = "";
        _result = null;

        _processingTask = Task.Run(() =>
        {
            try
            {
                var parameters = new PointCloudMeshGenerator.MeshGenerationParameters
                {
                    GridStep = _gridStep,
                    MaxEdgeLength = _maxEdgeLength,
                    ZDeep = _zDeep,
                    PlaneDistance = _planeDistance,
                    WeldingAngle = _weldingAngle,
                    PeakThreshold = _peakThreshold,
                    EnableWelding = _enableWelding,
                    EnableInterpolation = _enableInterpolation,
                    EnableDownsampling = _enableDownsampling,
                    Interpolation = (PointCloudMeshGenerator.InterpolationMethod)_interpolationMethod,
                    RotationAngleDegrees = _rotationAngle,
                    TranslateToOrigin = _translateToOrigin,
                    CreateSolidMesh = _createSolidMesh,
                    NodataValue = _nodataValue
                };

                var generator = new PointCloudMeshGenerator(parameters);
                generator.SetProgressCallback((msg, prog) =>
                {
                    _progressMessage = msg;
                    _progress = prog;
                });

                _result = generator.GenerateFromFile(_inputFilePath);

                if (_result.Success)
                {
                    // Save to file
                    SaveMesh();

                    // Create dataset
                    CreateDataset();
                }

                _isProcessing = false;
                _currentStep = WizardStep.Results;
            }
            catch (Exception ex)
            {
                _errorMessage = ex.Message;
                _isProcessing = false;
                _currentStep = WizardStep.Results;
                Logger.LogError($"[SlopeStabilityMeshWizard] Processing error: {ex}");
            }
        });
    }

    private void SaveMesh()
    {
        if (_result == null || !_result.Success) return;

        var ext = Path.GetExtension(_outputFilePath).ToLowerInvariant();
        if (ext == ".stl")
            SaveSTL();
        else
            SaveOBJ();
    }

    private void SaveOBJ()
    {
        using var writer = new StreamWriter(_outputFilePath);
        var culture = System.Globalization.CultureInfo.InvariantCulture;

        writer.WriteLine("# Generated by Geoscientist Toolkit - Slope Stability Mesh Wizard");
        writer.WriteLine("# Based on algorithms by Francesco Ottaviani, Universita degli Studi di Urbino Carlo Bo");
        writer.WriteLine($"# Vertices: {_result.Vertices.Count}");
        writer.WriteLine($"# Faces: {_result.Faces.Count}");
        writer.WriteLine();

        foreach (var v in _result.Vertices)
            writer.WriteLine($"v {v.X.ToString(culture)} {v.Y.ToString(culture)} {v.Z.ToString(culture)}");

        writer.WriteLine();

        foreach (var f in _result.Faces)
            writer.WriteLine($"f {f[0] + 1} {f[1] + 1} {f[2] + 1}");
    }

    private void SaveSTL()
    {
        using var writer = new StreamWriter(_outputFilePath);
        var culture = System.Globalization.CultureInfo.InvariantCulture;

        writer.WriteLine("solid slope_model");

        foreach (var face in _result.Faces)
        {
            var v0 = _result.Vertices[face[0]];
            var v1 = _result.Vertices[face[1]];
            var v2 = _result.Vertices[face[2]];

            // Calculate normal
            var edge1 = v1 - v0;
            var edge2 = v2 - v0;
            var normal = Vector3.Normalize(Vector3.Cross(edge1, edge2));

            if (!float.IsFinite(normal.X)) normal = Vector3.UnitZ;

            writer.WriteLine($"  facet normal {normal.X.ToString(culture)} {normal.Y.ToString(culture)} {normal.Z.ToString(culture)}");
            writer.WriteLine("    outer loop");
            writer.WriteLine($"      vertex {v0.X.ToString(culture)} {v0.Y.ToString(culture)} {v0.Z.ToString(culture)}");
            writer.WriteLine($"      vertex {v1.X.ToString(culture)} {v1.Y.ToString(culture)} {v1.Z.ToString(culture)}");
            writer.WriteLine($"      vertex {v2.X.ToString(culture)} {v2.Y.ToString(culture)} {v2.Z.ToString(culture)}");
            writer.WriteLine("    endloop");
            writer.WriteLine("  endfacet");
        }

        writer.WriteLine("endsolid slope_model");
    }

    private void CreateDataset()
    {
        if (_result == null || !_result.Success) return;

        VeldridManager.RunOnMainThread(() =>
        {
            try
            {
                var dataset = new Mesh3DDataset(_datasetName, _outputFilePath);
                dataset.Load();

                ProjectManager.Instance.CurrentProject.AddDataset(dataset);
                Logger.Log($"[SlopeStabilityMeshWizard] Created dataset: {_datasetName}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[SlopeStabilityMeshWizard] Failed to create dataset: {ex.Message}");
            }
        });
    }

    private void DrawProcessingStep()
    {
        ImGui.Spacing();
        ImGui.Text("Step 3: Processing");
        ImGui.Separator();
        ImGui.Spacing();

        var center = ImGui.GetContentRegionAvail() / 2;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + center.Y / 2 - 50);

        // Center the progress display
        var progressText = $"{_progressMessage}";
        var textWidth = ImGui.CalcTextSize(progressText).X;
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - textWidth) / 2);
        ImGui.Text(progressText);

        ImGui.Spacing();
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - 400) / 2);
        ImGui.ProgressBar(_progress, new Vector2(400, 25), $"{_progress:P0}");

        ImGui.Spacing();
        ImGui.Spacing();

        if (_isProcessing)
        {
            var spinnerText = "Processing";
            for (int i = 0; i < ((int)(ImGui.GetTime() * 3) % 4); i++)
                spinnerText += ".";

            textWidth = ImGui.CalcTextSize(spinnerText).X;
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - textWidth) / 2);
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1.0f), spinnerText);
        }
    }

    private void DrawResultsStep()
    {
        ImGui.Spacing();
        ImGui.Text("Step 4: Results");
        ImGui.Separator();
        ImGui.Spacing();

        if (_result != null && _result.Success)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.3f, 1.0f, 0.3f, 1.0f));
            ImGui.Text("Mesh generation completed successfully!");
            ImGui.PopStyleColor();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text("Statistics:");
            ImGui.BulletText($"Original points: {_result.OriginalPointCount:N0}");
            ImGui.BulletText($"Filtered points: {_result.FilteredPointCount:N0}");
            ImGui.BulletText($"Mesh vertices: {_result.Vertices.Count:N0}");
            ImGui.BulletText($"Mesh faces: {_result.Faces.Count:N0}");

            ImGui.Spacing();

            ImGui.Text("Bounding Box:");
            ImGui.BulletText($"Min: ({_result.BoundingBoxMin.X:F2}, {_result.BoundingBoxMin.Y:F2}, {_result.BoundingBoxMin.Z:F2})");
            ImGui.BulletText($"Max: ({_result.BoundingBoxMax.X:F2}, {_result.BoundingBoxMax.Y:F2}, {_result.BoundingBoxMax.Z:F2})");
            ImGui.BulletText($"Center: ({_result.Center.X:F2}, {_result.Center.Y:F2}, {_result.Center.Z:F2})");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text("Output:");
            ImGui.BulletText($"File: {_outputFilePath}");
            ImGui.BulletText($"Dataset: {_datasetName}");

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.6f, 0.8f, 1.0f, 1.0f),
                "The mesh has been added to your project and is ready for slope stability simulations.");
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.4f, 0.4f, 1.0f));
            ImGui.Text("Mesh generation failed!");
            ImGui.PopStyleColor();

            ImGui.Spacing();

            if (_result != null)
                ImGui.TextWrapped($"Error: {_result.StatusMessage}");

            if (!string.IsNullOrEmpty(_errorMessage))
                ImGui.TextWrapped($"Details: {_errorMessage}");
        }

        ImGui.Spacing();
        ImGui.Spacing();

        // Close button
        var buttonWidth = 120f;
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - buttonWidth) / 2);

        if (ImGui.Button("Close", new Vector2(buttonWidth, 30)))
        {
            _isOpen = false;
        }

        ImGui.SameLine();
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() + buttonWidth + 20) / 2);

        if (_result != null && _result.Success)
        {
            if (ImGui.Button("Create Another", new Vector2(buttonWidth, 30)))
            {
                _currentStep = WizardStep.SelectInput;
                _result = null;
                _errorMessage = "";
            }
        }
    }

    private void DrawNavigationButtons(bool canBack, bool canNext, Action onBack = null, Action onNext = null)
    {
        ImGui.Separator();
        ImGui.Spacing();

        var buttonWidth = 100f;
        var spacing = 10f;
        var totalWidth = buttonWidth * 2 + spacing;
        var startX = (ImGui.GetWindowWidth() - totalWidth) / 2;

        ImGui.SetCursorPosX(startX);

        if (!canBack) ImGui.BeginDisabled();
        if (ImGui.Button("Back", new Vector2(buttonWidth, 30)))
            onBack?.Invoke();
        if (!canBack) ImGui.EndDisabled();

        ImGui.SameLine();

        if (!canNext) ImGui.BeginDisabled();
        if (ImGui.Button("Next", new Vector2(buttonWidth, 30)))
            onNext?.Invoke();
        if (!canNext) ImGui.EndDisabled();
    }
}
