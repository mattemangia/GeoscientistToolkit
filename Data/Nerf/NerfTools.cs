// GeoscientistToolkit/Data/Nerf/NerfTools.cs

using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Data.Nerf;

/// <summary>
/// Tools panel for NeRF dataset operations including training, import, and export.
/// </summary>
public class NerfTools : IDatasetTools
{
    private NerfDataset _currentDataset;
    private NerfTrainer _trainer;

    // Import state
    private string _importFolderPath = "";
    private string _importVideoPath = "";
    private string _colmapPath = "";
    private int _keyframeInterval = 15;
    private bool _isImporting = false;
    private float _importProgress = 0f;

    // Training configuration UI state
    private bool _showAdvancedConfig = false;

    // Export state
    private string _exportPath = "";
    private int _exportWidth = 1920;
    private int _exportHeight = 1080;
    private int _exportFrameCount = 120;
    private bool _isExporting = false;
    private float _exportProgress = 0f;

    // Mesh/PointCloud export settings
    private int _meshResolution = 128;
    private float _densityThreshold = 10f;
    private bool _bakeTexture = true;
    private int _textureResolution = 1024;
    private int _pointCloudResolution = 128;
    private int _meshFormat = 0; // 0=OBJ, 1=PLY, 2=STL
    private int _pointCloudFormat = 0; // 0=PLY, 1=XYZ, 2=OBJ

    // Live capture state
    private bool _isCapturing = false;
    private int _captureDeviceIndex = 0;
    private int _capturedFrameCount = 0;

    public void Draw(Dataset dataset)
    {
        if (dataset is not NerfDataset nerfDataset)
        {
            ImGui.TextDisabled("Invalid dataset type for NeRF tools.");
            return;
        }

        _currentDataset = nerfDataset;

        // Get or create trainer reference from viewer if available
        if (_trainer == null || _trainer != GetTrainerFromDataset(nerfDataset))
        {
            _trainer = new NerfTrainer(nerfDataset);
        }

        ImGui.Text("NeRF Tools");
        ImGui.Separator();

        // Dataset status
        DrawStatusSection();

        ImGui.Spacing();

        // Import tools
        if (ImGui.CollapsingHeader("Import Images", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawImportSection();
        }

        ImGui.Spacing();

        // Training tools
        if (ImGui.CollapsingHeader("Training", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawTrainingSection();
        }

        ImGui.Spacing();

        // Pose estimation tools
        if (ImGui.CollapsingHeader("Camera Poses"))
        {
            DrawPoseEstimationSection();
        }

        ImGui.Spacing();

        // Export tools
        if (ImGui.CollapsingHeader("Export"))
        {
            DrawExportSection();
        }
    }

    private void DrawStatusSection()
    {
        // Image collection info
        var imageCount = _currentDataset.ImageCollection?.FrameCount ?? 0;
        var posedCount = _currentDataset.ImageCollection?.GetFramesWithPoses().Count() ?? 0;

        ImGui.Text($"Images: {imageCount}");
        ImGui.SameLine();
        ImGui.Text($"| With poses: {posedCount}");

        // Training status
        var stateColor = _currentDataset.TrainingState switch
        {
            NerfTrainingState.Training => new Vector4(0.2f, 0.8f, 0.2f, 1.0f),
            NerfTrainingState.Completed => new Vector4(0.2f, 0.6f, 1.0f, 1.0f),
            NerfTrainingState.Paused => new Vector4(1.0f, 0.8f, 0.2f, 1.0f),
            NerfTrainingState.Failed => new Vector4(1.0f, 0.2f, 0.2f, 1.0f),
            _ => new Vector4(0.5f, 0.5f, 0.5f, 1.0f)
        };

        ImGui.TextColored(stateColor, $"Status: {_currentDataset.TrainingState}");

        if (_currentDataset.TrainingState == NerfTrainingState.Training ||
            _currentDataset.TrainingState == NerfTrainingState.Completed)
        {
            ImGui.Text($"Iteration: {_currentDataset.CurrentIteration}/{_currentDataset.TotalIterations}");
            ImGui.Text($"Loss: {_currentDataset.CurrentLoss:F6}");
            ImGui.Text($"PSNR: {_currentDataset.CurrentPSNR:F2} dB");
        }

        if (_currentDataset.ModelData != null)
        {
            var modelSize = _currentDataset.ModelData.GetSizeBytes();
            ImGui.Text($"Model size: {modelSize / 1024:F1} KB");
        }
    }

    private void DrawImportSection()
    {
        ImGui.BeginDisabled(_isImporting);

        // Import from folder
        ImGui.Text("Image Folder:");
        ImGui.SetNextItemWidth(-100);
        ImGui.InputText("##ImportFolder", ref _importFolderPath, 512);
        ImGui.SameLine();
        if (ImGui.Button("Browse##Folder"))
        {
            // In a real implementation, this would open a folder picker dialog
            Logger.Log("Folder browser not available - please enter path manually");
        }

        if (ImGui.Button("Import from Folder", new Vector2(-1, 0)))
        {
            _ = ImportFromFolderAsync();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Import from video
        ImGui.Text("Video File:");
        ImGui.SetNextItemWidth(-100);
        ImGui.InputText("##ImportVideo", ref _importVideoPath, 512);
        ImGui.SameLine();
        if (ImGui.Button("Browse##Video"))
        {
            Logger.Log("File browser not available - please enter path manually");
        }

        ImGui.Text("Keyframe interval:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        ImGui.InputInt("##KeyframeInterval", ref _keyframeInterval);
        _keyframeInterval = Math.Clamp(_keyframeInterval, 1, 120);

        if (ImGui.Button("Import from Video", new Vector2(-1, 0)))
        {
            _ = ImportFromVideoAsync();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Live capture
        ImGui.Text("Live Capture:");
        ImGui.SetNextItemWidth(150);
        ImGui.InputInt("Camera Index", ref _captureDeviceIndex);
        _captureDeviceIndex = Math.Max(0, _captureDeviceIndex);

        ImGui.EndDisabled();

        if (_isCapturing)
        {
            ImGui.TextColored(new Vector4(0.2f, 0.8f, 0.2f, 1.0f), $"Capturing... ({_capturedFrameCount} frames)");
            if (ImGui.Button("Stop Capture", new Vector2(-1, 0)))
            {
                StopCapture();
            }
        }
        else
        {
            ImGui.BeginDisabled(_isImporting);
            if (ImGui.Button("Start Capture", new Vector2(-1, 0)))
            {
                StartCapture();
            }
            ImGui.EndDisabled();
        }

        // Progress bar
        if (_isImporting)
        {
            ImGui.Spacing();
            ImGui.ProgressBar(_importProgress, new Vector2(-1, 20), $"Importing... {_importProgress * 100:F0}%");
        }
    }

    private void DrawTrainingSection()
    {
        var hasImages = (_currentDataset.ImageCollection?.FrameCount ?? 0) >= 2;
        var hasPoses = (_currentDataset.ImageCollection?.GetFramesWithPoses().Count() ?? 0) >= 2;
        var canTrain = hasImages && hasPoses;

        if (!hasImages)
        {
            ImGui.TextColored(new Vector4(1f, 0.5f, 0.2f, 1f), "Import at least 2 images to train");
        }
        else if (!hasPoses)
        {
            ImGui.TextColored(new Vector4(1f, 0.5f, 0.2f, 1f), "Estimate camera poses before training");
        }

        // Training configuration
        ImGui.Text("Iterations:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        var totalIter = _currentDataset.TotalIterations;
        if (ImGui.InputInt("##TotalIterations", ref totalIter))
        {
            _currentDataset.TotalIterations = Math.Clamp(totalIter, 1000, 1000000);
        }

        if (ImGui.Checkbox("Show Advanced Config", ref _showAdvancedConfig))
        {
        }

        if (_showAdvancedConfig)
        {
            var config = _currentDataset.TrainingConfig;

            ImGui.Indent();

            // Hash grid settings
            ImGui.Text("Hash Grid:");
            ImGui.SetNextItemWidth(100);

            int levels = config.HashGridLevels;
            if (ImGui.InputInt("Levels##HG", ref levels))
            {
                config.HashGridLevels = Math.Clamp(levels, 4, 24);
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);

            int features = config.HashGridFeatures;
            if (ImGui.InputInt("Features##HG", ref features))
            {
                config.HashGridFeatures = Math.Clamp(features, 1, 8);
            }

            // MLP settings
            ImGui.Text("MLP Network:");
            ImGui.SetNextItemWidth(100);

            int hiddenLayers = config.MlpHiddenLayers;
            if (ImGui.InputInt("Hidden Layers##MLP", ref hiddenLayers))
            {
                config.MlpHiddenLayers = Math.Clamp(hiddenLayers, 1, 8);
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);

            int hiddenWidth = config.MlpHiddenWidth;
            if (ImGui.InputInt("Width##MLP", ref hiddenWidth))
            {
                config.MlpHiddenWidth = Math.Clamp(hiddenWidth, 16, 256);
            }

            // Training settings
            ImGui.Text("Training:");
            ImGui.SetNextItemWidth(100);
            float lr = config.LearningRate * 1000;
            if (ImGui.InputFloat("LR (x0.001)##LR", ref lr, 0.1f, 1.0f, "%.2f"))
            {
                config.LearningRate = Math.Clamp(lr / 1000f, 1e-5f, 0.1f);
            }

            ImGui.SetNextItemWidth(100);

            int batchSize = config.BatchSize;
            if (ImGui.InputInt("Batch Size##BS", ref batchSize))
            {
                config.BatchSize = Math.Clamp(batchSize, 256, 16384);
            }

            ImGui.SetNextItemWidth(100);

            int samplesPerRay = config.SamplesPerRay;
            if (ImGui.InputInt("Samples/Ray##SPR", ref samplesPerRay))
            {
                config.SamplesPerRay = Math.Clamp(samplesPerRay, 16, 256);
            }

            bool useGpu = config.UseGPU;
            if(ImGui.Checkbox("Use GPU", ref useGpu))
            {
                config.UseGPU = useGpu;
            }

            ImGui.SameLine();

            bool useMixedPrecision = config.UseMixedPrecision;
            if(ImGui.Checkbox("Mixed Precision", ref useMixedPrecision))
            {
                config.UseMixedPrecision = useMixedPrecision;
            }

            ImGui.Unindent();
        }

        ImGui.Spacing();

        // Training controls
        ImGui.BeginDisabled(!canTrain);

        if (_currentDataset.TrainingState == NerfTrainingState.Training)
        {
            // Progress bar
            float progress = (float)_currentDataset.CurrentIteration / _currentDataset.TotalIterations;
            ImGui.ProgressBar(progress, new Vector2(-1, 20),
                $"{_currentDataset.CurrentIteration}/{_currentDataset.TotalIterations}");

            if (ImGui.Button("Pause", new Vector2(-1, 0)))
            {
                _trainer?.Pause();
            }
        }
        else if (_currentDataset.TrainingState == NerfTrainingState.Paused)
        {
            if (ImGui.Button("Resume Training", new Vector2(-1, 0)))
            {
                _trainer?.StartTraining();
            }
            if (ImGui.Button("Reset Training", new Vector2(-1, 0)))
            {
                _trainer?.Reset();
            }
        }
        else
        {
            if (ImGui.Button("Start Training", new Vector2(-1, 0)))
            {
                _trainer?.StartTraining();
            }
        }

        ImGui.EndDisabled();

        // Training history plot
        if (_currentDataset.TrainingHistory.Count > 0)
        {
            ImGui.Spacing();
            ImGui.Text("Training Progress:");

            var history = _currentDataset.TrainingHistory;
            var losses = history.Select(h => h.Loss).ToArray();
            var psnrs = history.Select(h => h.PSNR).ToArray();

            if (losses.Length > 1)
            {
                // Simple loss plot
                float minLoss = losses.Min();
                float maxLoss = losses.Max();

                var plotSize = new Vector2(-1, 80);
                ImGui.PlotLines("Loss", ref losses[0], losses.Length, 0,
                    $"Loss: {losses[^1]:F6}", minLoss, maxLoss, plotSize);

                float minPsnr = psnrs.Min();
                float maxPsnr = psnrs.Max();
                ImGui.PlotLines("PSNR", ref psnrs[0], psnrs.Length, 0,
                    $"PSNR: {psnrs[^1]:F2}dB", minPsnr, maxPsnr, plotSize);
            }
        }
    }

    private void DrawPoseEstimationSection()
    {
        var frameCount = _currentDataset.ImageCollection?.FrameCount ?? 0;
        var posedCount = _currentDataset.ImageCollection?.GetFramesWithPoses().Count() ?? 0;

        ImGui.Text($"Frames: {frameCount} | With poses: {posedCount}");

        ImGui.Spacing();

        // COLMAP import
        ImGui.Text("COLMAP Project:");
        ImGui.SetNextItemWidth(-100);
        ImGui.InputText("##ColmapPath", ref _colmapPath, 512);
        ImGui.SameLine();
        if (ImGui.Button("Browse##Colmap"))
        {
            Logger.Log("Folder browser not available - please enter path manually");
        }

        if (ImGui.Button("Import COLMAP Poses", new Vector2(-1, 0)))
        {
            _ = ImportColmapPosesAsync();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Synthetic pose generation
        ImGui.Text("Generate Synthetic Poses:");

        if (ImGui.Button("Circular Arrangement", new Vector2(-1, 0)))
        {
            _currentDataset.ImageCollection?.GenerateCircularPoses(2.0f, 0.5f);
            Logger.Log("Generated circular camera poses");
        }

        if (ImGui.Button("Hemisphere Arrangement", new Vector2(-1, 0)))
        {
            _currentDataset.ImageCollection?.GenerateHemispherePoses(2.0f, 3);
            Logger.Log("Generated hemisphere camera poses");
        }

        ImGui.Spacing();

        // Normalize scene
        if (ImGui.Button("Normalize Scene", new Vector2(-1, 0)))
        {
            _currentDataset.ImageCollection?.NormalizeScene();
            Logger.Log("Scene normalized to unit sphere");
        }
    }

    private void DrawExportSection()
    {
        var hasModel = _currentDataset.ModelData != null;

        ImGui.BeginDisabled(!hasModel || _isExporting);

        // Export path
        ImGui.Text("Export Path:");
        ImGui.SetNextItemWidth(-100);
        ImGui.InputText("##ExportPath", ref _exportPath, 512);
        ImGui.SameLine();
        if (ImGui.Button("Browse##Export"))
        {
            Logger.Log("File browser not available - please enter path manually");
        }

        ImGui.Spacing();

        // Save NeRF model
        if (ImGui.Button("Save NeRF Model (.nerfmodel)", new Vector2(-1, 0)))
        {
            if (!string.IsNullOrWhiteSpace(_exportPath))
            {
                var modelPath = Path.ChangeExtension(_exportPath, ".nerfmodel");
                _currentDataset.SaveModel(modelPath);
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // === MESH EXPORT ===
        ImGui.Text("3D Mesh Export:");

        ImGui.SetNextItemWidth(100);
        ImGui.InputInt("Resolution##Mesh", ref _meshResolution);
        _meshResolution = Math.Clamp(_meshResolution, 32, 512);

        ImGui.SetNextItemWidth(100);
        ImGui.InputFloat("Density Threshold", ref _densityThreshold, 1f, 5f, "%.1f");
        _densityThreshold = Math.Clamp(_densityThreshold, 0.1f, 100f);

        ImGui.Checkbox("Bake Texture", ref _bakeTexture);
        if (_bakeTexture)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(80);
            ImGui.InputInt("##TexRes", ref _textureResolution);
            _textureResolution = Math.Clamp(_textureResolution, 256, 4096);
            ImGui.SameLine();
            ImGui.Text("px");
        }

        string[] meshFormats = { "OBJ", "PLY", "STL" };
        ImGui.SetNextItemWidth(100);
        ImGui.Combo("Format##Mesh", ref _meshFormat, meshFormats, meshFormats.Length);

        if (ImGui.Button("Export Mesh", new Vector2(-1, 0)))
        {
            _ = ExportMeshAsync();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // === POINT CLOUD EXPORT ===
        ImGui.Text("Point Cloud Export:");

        ImGui.SetNextItemWidth(100);
        ImGui.InputInt("Resolution##PC", ref _pointCloudResolution);
        _pointCloudResolution = Math.Clamp(_pointCloudResolution, 32, 256);

        string[] pcFormats = { "PLY", "XYZ", "OBJ" };
        ImGui.SetNextItemWidth(100);
        ImGui.Combo("Format##PC", ref _pointCloudFormat, pcFormats, pcFormats.Length);

        if (ImGui.Button("Export Point Cloud", new Vector2(-1, 0)))
        {
            _ = ExportPointCloudAsync();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // === TEXTURE EXPORT ===
        ImGui.Text("Texture Export:");

        ImGui.SetNextItemWidth(100);
        int texRes = _textureResolution;
        if (ImGui.InputInt("Resolution##Tex", ref texRes))
        {
            _textureResolution = Math.Clamp(texRes, 256, 4096);
        }

        if (ImGui.Button("Export Spherical Texture", new Vector2(-1, 0)))
        {
            _ = ExportTextureAsync();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // === VIDEO EXPORT ===
        ImGui.Text("Render 360° Video:");
        ImGui.SetNextItemWidth(100);
        ImGui.InputInt("Width##RV", ref _exportWidth);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        ImGui.InputInt("Height##RV", ref _exportHeight);

        _exportWidth = Math.Clamp(_exportWidth, 64, 4096);
        _exportHeight = Math.Clamp(_exportHeight, 64, 4096);

        ImGui.SetNextItemWidth(100);
        ImGui.InputInt("Frames##RV", ref _exportFrameCount);
        _exportFrameCount = Math.Clamp(_exportFrameCount, 1, 1000);

        if (ImGui.Button("Render Video Frames", new Vector2(-1, 0)))
        {
            _ = Render360VideoAsync();
        }

        ImGui.EndDisabled();

        if (!hasModel)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Train a model first to enable export");
        }

        if (_isExporting)
        {
            ImGui.Spacing();
            ImGui.ProgressBar(_exportProgress, new Vector2(-1, 20), $"Exporting... {_exportProgress * 100:F0}%");
        }
    }

    private async Task ExportMeshAsync()
    {
        if (_currentDataset.ModelData == null || string.IsNullOrWhiteSpace(_exportPath))
        {
            Logger.LogWarning("No model or export path specified");
            return;
        }

        _isExporting = true;
        _exportProgress = 0f;

        try
        {
            string[] extensions = { ".obj", ".ply", ".stl" };
            var meshPath = Path.ChangeExtension(_exportPath, extensions[_meshFormat]);

            var settings = new MeshExportSettings
            {
                Resolution = _meshResolution,
                DensityThreshold = _densityThreshold,
                BakeTexture = _bakeTexture,
                TextureResolution = _textureResolution
            };

            var exporter = new NerfExporter(_currentDataset);
            var progress = new Progress<float>(p => _exportProgress = p);
            await exporter.ExportMeshAsync(meshPath, settings, progress);

            Logger.Log($"Mesh exported to: {meshPath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Mesh export failed: {ex.Message}");
        }
        finally
        {
            _isExporting = false;
            _exportProgress = 0f;
        }
    }

    private async Task ExportPointCloudAsync()
    {
        if (_currentDataset.ModelData == null || string.IsNullOrWhiteSpace(_exportPath))
        {
            Logger.LogWarning("No model or export path specified");
            return;
        }

        _isExporting = true;
        _exportProgress = 0f;

        try
        {
            string[] extensions = { ".ply", ".xyz", ".obj" };
            var pcPath = Path.ChangeExtension(_exportPath, extensions[_pointCloudFormat]);

            var settings = new PointCloudExportSettings
            {
                ResolutionX = _pointCloudResolution,
                ResolutionY = _pointCloudResolution,
                ResolutionZ = _pointCloudResolution,
                DensityThreshold = _densityThreshold
            };

            var exporter = new NerfExporter(_currentDataset);
            var progress = new Progress<float>(p => _exportProgress = p);
            await exporter.ExportPointCloudAsync(pcPath, settings, progress);

            Logger.Log($"Point cloud exported to: {pcPath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Point cloud export failed: {ex.Message}");
        }
        finally
        {
            _isExporting = false;
            _exportProgress = 0f;
        }
    }

    private async Task ExportTextureAsync()
    {
        if (_currentDataset.ModelData == null || string.IsNullOrWhiteSpace(_exportPath))
        {
            Logger.LogWarning("No model or export path specified");
            return;
        }

        _isExporting = true;
        _exportProgress = 0f;

        try
        {
            var texturePath = Path.ChangeExtension(_exportPath, ".png");

            var settings = new TextureExportSettings
            {
                Resolution = _textureResolution
            };

            var exporter = new NerfExporter(_currentDataset);
            var progress = new Progress<float>(p => _exportProgress = p);
            await exporter.ExportTextureAsync(texturePath, settings, progress);

            Logger.Log($"Texture exported to: {texturePath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Texture export failed: {ex.Message}");
        }
        finally
        {
            _isExporting = false;
            _exportProgress = 0f;
        }
    }

    private async Task ImportFromFolderAsync()
    {
        if (string.IsNullOrWhiteSpace(_importFolderPath))
        {
            Logger.LogWarning("Please enter a folder path");
            return;
        }

        _isImporting = true;
        _importProgress = 0f;

        try
        {
            var progress = new Progress<float>(p => _importProgress = p);
            await _currentDataset.ImportFromFolderAsync(_importFolderPath, progress);
            Logger.Log($"Imported {_currentDataset.ImageCollection.FrameCount} images");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Import failed: {ex.Message}");
        }
        finally
        {
            _isImporting = false;
            _importProgress = 0f;
        }
    }

    private async Task ImportFromVideoAsync()
    {
        if (string.IsNullOrWhiteSpace(_importVideoPath))
        {
            Logger.LogWarning("Please enter a video path");
            return;
        }

        _isImporting = true;
        _importProgress = 0f;

        try
        {
            var progress = new Progress<float>(p => _importProgress = p);
            await _currentDataset.ImportFromVideoAsync(_importVideoPath, _keyframeInterval, progress);
            Logger.Log($"Extracted {_currentDataset.ImageCollection.FrameCount} keyframes");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Import failed: {ex.Message}");
        }
        finally
        {
            _isImporting = false;
            _importProgress = 0f;
        }
    }

    private async Task ImportColmapPosesAsync()
    {
        if (string.IsNullOrWhiteSpace(_colmapPath))
        {
            Logger.LogWarning("Please enter a COLMAP project path");
            return;
        }

        try
        {
            var progress = new Progress<float>(p => _importProgress = p);
            await _currentDataset.ImageCollection.ImportColmapPosesAsync(_colmapPath, progress);
            Logger.Log($"Imported camera poses for {_currentDataset.ImageCollection.GetFramesWithPoses().Count()} frames");
        }
        catch (Exception ex)
        {
            Logger.LogError($"COLMAP import failed: {ex.Message}");
        }
    }

    private void StartCapture()
    {
        _isCapturing = true;
        _capturedFrameCount = 0;
        Logger.Log($"Starting live capture from camera {_captureDeviceIndex}");
        // Actual capture implementation would use OpenCV or similar
    }

    private void StopCapture()
    {
        _isCapturing = false;
        Logger.Log($"Stopped capture. Captured {_capturedFrameCount} frames");
    }

    private async Task Render360VideoAsync()
    {
        if (_currentDataset.ModelData == null)
        {
            Logger.LogWarning("No trained model available");
            return;
        }

        _isExporting = true;
        Logger.Log($"Rendering 360° video: {_exportWidth}x{_exportHeight}, {_exportFrameCount} frames");

        try
        {
            // Create output directory
            var outputDir = Path.Combine(Path.GetDirectoryName(_exportPath) ?? "", "render_frames");
            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            var center = _currentDataset.ImageCollection?.SceneCenter ?? Vector3.Zero;
            float radius = (_currentDataset.ImageCollection?.SceneRadius ?? 1.0f) * 2.0f;

            for (int i = 0; i < _exportFrameCount; i++)
            {
                float angle = 2 * MathF.PI * i / _exportFrameCount;
                var position = center + new Vector3(
                    radius * MathF.Cos(angle),
                    0.5f,
                    radius * MathF.Sin(angle)
                );

                // Create look-at matrix
                var forward = Vector3.Normalize(center - position);
                var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
                var up = Vector3.Cross(forward, right);

                var cameraMatrix = new Matrix4x4(
                    right.X, right.Y, right.Z, 0,
                    up.X, up.Y, up.Z, 0,
                    -forward.X, -forward.Y, -forward.Z, 0,
                    position.X, position.Y, position.Z, 1
                );

                float focalLength = _exportWidth / (2f * MathF.Tan(MathF.PI / 6f));
                var frameData = _trainer.RenderView(cameraMatrix, focalLength, _exportWidth, _exportHeight);

                if (frameData != null)
                {
                    var framePath = Path.Combine(outputDir, $"frame_{i:D5}.png");
                    await SaveImageAsync(framePath, frameData, _exportWidth, _exportHeight);
                }

                // Progress update would go here
            }

            Logger.Log($"Rendered {_exportFrameCount} frames to: {outputDir}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Render failed: {ex.Message}");
        }
        finally
        {
            _isExporting = false;
        }
    }

    private async Task SaveImageAsync(string path, byte[] data, int width, int height)
    {
        await Task.Run(() =>
        {
            try
            {
                using var stream = File.Create(path);
                var writer = new StbImageWriteSharp.ImageWriter();
                writer.WritePng(data, width, height, StbImageWriteSharp.ColorComponents.RedGreenBlue, stream);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to save frame: {ex.Message}");
            }
        });
    }

    private NerfTrainer GetTrainerFromDataset(NerfDataset dataset)
    {
        // This would ideally get the trainer from the viewer
        // For now, create a new one
        return _trainer;
    }
}
