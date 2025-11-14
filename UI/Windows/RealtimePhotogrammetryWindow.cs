// GeoscientistToolkit/UI/Windows/RealtimePhotogrammetryWindow.cs

using GeoscientistToolkit.Analysis.Photogrammetry;
using GeoscientistToolkit.Settings;
using ImGuiNET;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.IO;
using Veldrid;
using GeoscientistToolkit.UI.Utils;

using GeoscientistToolkit.Util;
namespace GeoscientistToolkit.UI.Windows;

/// <summary>
/// Real-time photogrammetry window with live video feed and reconstruction.
/// </summary>
public class RealtimePhotogrammetryWindow : IDisposable
{
    private bool _isOpen;
    private PhotogrammetryPipeline _pipeline;
    private PhotogrammetryPipeline.PipelineConfig _config;

    // UI state
    private bool _isCapturing;
    private bool _isPaused;
    private int _selectedCameraIndex;
    private string _videoFilePath = "";
    private bool _useVideoFile;

    // Configuration UI
    private string _depthModelPath = "";
    private string _superPointModelPath = "";
    private string _lightGlueModelPath = "";
    private bool _useGpu = false;
    private int _depthModelType = 0; // 0=MiDaS Small, 1=DPT Small, 2=ZoeDepth
    private int _keyframeInterval = 10;
    private int _targetWidth = 640;
    private int _targetHeight = 480;

    // Camera intrinsics
    private float _focalLengthX = 500;
    private float _focalLengthY = 500;
    private float _principalPointX = 320;
    private float _principalPointY = 240;

    // Georeferencing
    private bool _showGeoreferencing;
    private string _gcpName = "GCP_";
    private Vector3 _gcpLocal = Vector3.Zero;
    private Vector3 _gcpWorld = Vector3.Zero;
    private float _gcpAccuracy = 1.0f;
    private bool _refineWithAltitude = false;

    // Visualization
    private IntPtr _frameTextureId = IntPtr.Zero;
    private IntPtr _depthTextureId = IntPtr.Zero;
    private Mat _currentFrameDisplay;
    private Mat _currentDepthDisplay;

    // Statistics
    private List<float> _processingTimes = new();
    private const int MaxStatsSamples = 100;

    // File dialogs
    private readonly ImGuiFileDialog _videoFileDialog;
    private readonly ImGuiExportFileDialog _exportPointCloudDialog;
    private readonly ImGuiExportFileDialog _exportMeshDialog;
    private readonly ImGuiExportFileDialog _exportCameraPathDialog;

    public RealtimePhotogrammetryWindow()
    {
        // Initialize file dialogs
        _videoFileDialog = new ImGuiFileDialog("SelectVideoFile", FileDialogType.OpenFile, "Select Video File");
        _exportPointCloudDialog = new ImGuiExportFileDialog("ExportPointCloud", "Export Point Cloud");
        _exportPointCloudDialog.SetExtensions(
            new ImGuiExportFileDialog.ExtensionOption(".ply", "PLY Format"),
            new ImGuiExportFileDialog.ExtensionOption(".xyz", "XYZ Format"),
            new ImGuiExportFileDialog.ExtensionOption(".obj", "OBJ Format")
        );

        _exportMeshDialog = new ImGuiExportFileDialog("ExportMesh", "Export Mesh");
        _exportMeshDialog.SetExtensions(
            new ImGuiExportFileDialog.ExtensionOption(".obj", "Wavefront OBJ")
        );

        _exportCameraPathDialog = new ImGuiExportFileDialog("ExportCameraPath", "Export Camera Path");
        _exportCameraPathDialog.SetExtensions(
            new ImGuiExportFileDialog.ExtensionOption(".txt", "Text File"),
            new ImGuiExportFileDialog.ExtensionOption(".csv", "CSV File")
        );

        LoadFromSettings();

        _config = new PhotogrammetryPipeline.PipelineConfig
        {
            TargetWidth = _targetWidth,
            TargetHeight = _targetHeight,
            UseGpu = _useGpu,
            KeyframeInterval = _keyframeInterval,
            DepthModelPath = _depthModelPath,
            SuperPointModelPath = _superPointModelPath,
            LightGlueModelPath = _lightGlueModelPath,
            FocalLengthX = _focalLengthX,
            FocalLengthY = _focalLengthY,
            PrincipalPointX = _principalPointX,
            PrincipalPointY = _principalPointY
        };
    }

    public void Show()
    {
        _isOpen = true;
    }

    public void Hide()
    {
        _isOpen = false;
        StopCapture();
    }

    public void Draw()
    {
        if (!_isOpen) return;

        ImGui.SetNextWindowSize(new Vector2(1400, 900), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Real-time Photogrammetry", ref _isOpen, ImGuiWindowFlags.MenuBar))
        {
            DrawMenuBar();

            if (ImGui.BeginTabBar("PhotogrammetryTabs"))
            {
                if (ImGui.BeginTabItem("Configuration"))
                {
                    DrawConfigurationTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Capture"))
                {
                    DrawCaptureTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Keyframes"))
                {
                    DrawKeyframesTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Georeferencing"))
                {
                    DrawGeoreferencingTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Statistics"))
                {
                    DrawStatisticsTab();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }
        ImGui.End();

        // Handle file dialogs
        HandleFileDialogs();

        if (!_isOpen)
        {
            StopCapture();
        }
    }

    private void LoadFromSettings()
    {
        var settings = SettingsManager.Instance.Settings.Photogrammetry;

        _depthModelPath = settings.DepthModelPath;
        _superPointModelPath = settings.SuperPointModelPath;
        _lightGlueModelPath = settings.LightGlueModelPath;
        _useGpu = settings.UseGpuAcceleration;
        _depthModelType = settings.DepthModelType;
        _keyframeInterval = settings.KeyframeInterval;
        _targetWidth = settings.TargetWidth;
        _targetHeight = settings.TargetHeight;
        _focalLengthX = settings.FocalLengthX;
        _focalLengthY = settings.FocalLengthY;
        _principalPointX = settings.PrincipalPointX;
        _principalPointY = settings.PrincipalPointY;
    }

    private void HandleFileDialogs()
    {
        if (_videoFileDialog.Submit())
        {
            _videoFilePath = _videoFileDialog.SelectedPath;
        }

        if (_exportPointCloudDialog.Submit())
        {
            var format = Path.GetExtension(_exportPointCloudDialog.SelectedPath).ToLower() switch
            {
                ".ply" => PointCloudExporter.ExportFormat.PLY,
                ".xyz" => PointCloudExporter.ExportFormat.XYZ,
                ".obj" => PointCloudExporter.ExportFormat.OBJ,
                _ => PointCloudExporter.ExportFormat.PLY
            };

            ExportPointCloudInternal(_exportPointCloudDialog.SelectedPath, format);
        }

        if (_exportMeshDialog.Submit())
        {
            ExportMeshInternal(_exportMeshDialog.SelectedPath);
        }

        if (_exportCameraPathDialog.Submit())
        {
            ExportCameraPathInternal(_exportCameraPathDialog.SelectedPath);
        }
    }

    private void DrawMenuBar()
    {
        if (ImGui.BeginMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("Load Configuration..."))
                {
                    // TODO: Load config from file
                }

                if (ImGui.MenuItem("Save Configuration..."))
                {
                    // TODO: Save config to file
                }

                ImGui.Separator();

                if (ImGui.MenuItem("Export Point Cloud..."))
                {
                    var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "pointcloud.ply");
                    _exportPointCloudDialog.Open("pointcloud", Path.GetDirectoryName(defaultPath));
                }

                if (ImGui.MenuItem("Export Mesh..."))
                {
                    var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "mesh.obj");
                    _exportMeshDialog.Open("mesh", Path.GetDirectoryName(defaultPath));
                }

                if (ImGui.MenuItem("Export Camera Path..."))
                {
                    var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "camera_path.txt");
                    _exportCameraPathDialog.Open("camera_path", Path.GetDirectoryName(defaultPath));
                }

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Tools"))
            {
                if (ImGui.MenuItem("Reset Pipeline"))
                {
                    _pipeline?.Reset();
                    Logger.Log("Photogrammetry pipeline reset");
                }

                if (ImGui.MenuItem("Clear Keyframes"))
                {
                    _pipeline?.KeyframeManager.Clear();
                    Logger.Log("Keyframes cleared");
                }

                ImGui.EndMenu();
            }

            ImGui.EndMenuBar();
        }
    }

    private void DrawConfigurationTab()
    {
        ImGui.TextColored(new Vector4(0.3f, 0.8f, 1.0f, 1.0f), "Pipeline Configuration");
        ImGui.Separator();

        // Model paths
        ImGui.Text("Model Paths:");
        ImGui.Text("Models are configured in Settings (Edit → Settings → Photogrammetry)");
        ImGui.Text($"Depth Model: {(string.IsNullOrEmpty(_depthModelPath) ? "Not set" : Path.GetFileName(_depthModelPath))}");
        ImGui.Text($"SuperPoint: {(string.IsNullOrEmpty(_superPointModelPath) ? "Not set" : Path.GetFileName(_superPointModelPath))}");
        ImGui.Text($"LightGlue: {(string.IsNullOrEmpty(_lightGlueModelPath) ? "Not set (optional)" : Path.GetFileName(_lightGlueModelPath))}");

        ImGui.Separator();

        // Hardware settings
        ImGui.Text("Hardware:");
        ImGui.Checkbox("Use GPU Acceleration (CUDA)", ref _useGpu);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Requires CUDA-capable GPU and CUDA toolkit");
        }

        ImGui.Separator();

        // Depth model type
        ImGui.Text("Depth Model Type:");
        string[] depthModels = { "MiDaS Small (Fast)", "DPT Small (Balanced)", "ZoeDepth (Metric-aware, Slow)" };
        ImGui.Combo("##DepthModelType", ref _depthModelType, depthModels, depthModels.Length);

        ImGui.Separator();

        // Processing parameters
        ImGui.Text("Processing Parameters:");
        ImGui.SliderInt("Keyframe Interval", ref _keyframeInterval, 1, 30);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Create a keyframe every N frames");
        }

        ImGui.SliderInt("Target Width", ref _targetWidth, 320, 1920);
        ImGui.SliderInt("Target Height", ref _targetHeight, 240, 1080);

        ImGui.Separator();

        // Camera intrinsics
        ImGui.Text("Camera Intrinsics (K matrix):");
        ImGui.DragFloat("Focal Length X", ref _focalLengthX, 1.0f, 100, 2000);
        ImGui.DragFloat("Focal Length Y", ref _focalLengthY, 1.0f, 100, 2000);
        ImGui.DragFloat("Principal Point X", ref _principalPointX, 1.0f, 0, 2000);
        ImGui.DragFloat("Principal Point Y", ref _principalPointY, 1.0f, 0, 2000);

        if (ImGui.Button("Auto-estimate from resolution"))
        {
            _focalLengthX = _targetWidth * 0.8f;
            _focalLengthY = _targetHeight * 0.8f;
            _principalPointX = _targetWidth / 2.0f;
            _principalPointY = _targetHeight / 2.0f;
        }

        ImGui.Separator();

        // Initialize button
        if (ImGui.Button("Initialize Pipeline", new Vector2(200, 40)))
        {
            InitializePipeline();
        }

        if (_pipeline != null && _pipeline.IsInitialized)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.0f, 1.0f), "Pipeline Ready");
        }
    }

    private void DrawCaptureTab()
    {
        ImGui.TextColored(new Vector4(0.3f, 0.8f, 1.0f, 1.0f), "Video Capture");
        ImGui.Separator();

        // Video source selection
        ImGui.Text("Video Source:");
        ImGui.RadioButton("Webcam/Camera", ref _useVideoFile, false);
        ImGui.SameLine();
        ImGui.RadioButton("Video File", ref _useVideoFile, true);

        ImGui.Separator();

        if (!_useVideoFile)
        {
            // Camera selection
            if (_pipeline != null && _pipeline.VideoCapture != null)
            {
                var cameras = _pipeline.VideoCapture.AvailableCameras;

                if (cameras.Count > 0)
                {
                    ImGui.Text("Available Cameras:");
                    for (int i = 0; i < cameras.Count; i++)
                    {
                        var cam = cameras[i];
                        if (ImGui.RadioButton($"Camera {cam.Index}: {cam.Width}x{cam.Height} @ {cam.Fps}fps", _selectedCameraIndex == cam.Index))
                        {
                            _selectedCameraIndex = cam.Index;
                        }
                    }
                }
                else
                {
                    ImGui.TextColored(new Vector4(1.0f, 0.5f, 0.0f, 1.0f), "No cameras detected");
                }
            }
        }
        else
        {
            // Video file selection
            ImGui.InputText("Video File Path", ref _videoFilePath, 512);
            ImGui.SameLine();
            if (ImGui.Button("Browse..."))
            {
                _videoFileDialog.Open(Path.GetDirectoryName(_videoFilePath), new[] { ".mp4", ".avi", ".mov", ".mkv", ".wmv" });
            }
        }

        ImGui.Separator();

        // Capture controls
        if (!_isCapturing)
        {
            if (ImGui.Button("Start Capture", new Vector2(150, 40)))
            {
                StartCapture();
            }
        }
        else
        {
            if (ImGui.Button(_isPaused ? "Resume" : "Pause", new Vector2(150, 40)))
            {
                _isPaused = !_isPaused;
            }

            ImGui.SameLine();

            if (ImGui.Button("Stop", new Vector2(150, 40)))
            {
                StopCapture();
            }
        }

        ImGui.Separator();

        // Live view
        ImGui.Text("Live View:");

        if (_currentFrameDisplay != null && !_currentFrameDisplay.Empty())
        {
            DisplayMat("Frame", _currentFrameDisplay, new Vector2(640, 480));
        }
        else
        {
            ImGui.Text("No video feed");
        }

        ImGui.SameLine();

        if (_currentDepthDisplay != null && !_currentDepthDisplay.Empty())
        {
            DisplayMat("Depth", _currentDepthDisplay, new Vector2(640, 480));
        }

        // Process frames if capturing
        if (_isCapturing && !_isPaused && _pipeline != null)
        {
            ProcessNextFrame();
        }
    }

    private void DrawKeyframesTab()
    {
        ImGui.TextColored(new Vector4(0.3f, 0.8f, 1.0f, 1.0f), "Keyframes");
        ImGui.Separator();

        if (_pipeline == null || _pipeline.KeyframeManager == null)
        {
            ImGui.Text("Initialize pipeline first");
            return;
        }

        var keyframes = _pipeline.KeyframeManager.Keyframes;
        ImGui.Text($"Total Keyframes: {keyframes.Count}");

        ImGui.Separator();

        if (ImGui.BeginTable("KeyframesTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Frame ID");
            ImGui.TableSetupColumn("Timestamp");
            ImGui.TableSetupColumn("3D Points");
            ImGui.TableSetupColumn("Actions");
            ImGui.TableHeadersRow();

            foreach (var kf in keyframes)
            {
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.Text($"{kf.FrameId}");

                ImGui.TableNextColumn();
                ImGui.Text($"{kf.Timestamp:HH:mm:ss}");

                ImGui.TableNextColumn();
                ImGui.Text($"{kf.Points3D.Count}");

                ImGui.TableNextColumn();
                if (ImGui.SmallButton($"View##{kf.FrameId}"))
                {
                    // TODO: View keyframe
                }
            }

            ImGui.EndTable();
        }

        ImGui.Separator();

        if (ImGui.Button("Perform Bundle Adjustment"))
        {
            _pipeline.KeyframeManager.PerformBundleAdjustment();
        }
    }

    private void DrawGeoreferencingTab()
    {
        ImGui.TextColored(new Vector4(0.3f, 0.8f, 1.0f, 1.0f), "Georeferencing with Ground Control Points");
        ImGui.Separator();

        if (_pipeline == null || _pipeline.Georeferencing == null)
        {
            ImGui.Text("Initialize pipeline first");
            return;
        }

        var georef = _pipeline.Georeferencing;
        var gcps = georef.GroundControlPoints;

        ImGui.Text($"Ground Control Points: {gcps.Count}");

        if (gcps.Count < 3)
        {
            ImGui.TextColored(new Vector4(1.0f, 0.5f, 0.0f, 1.0f), "Need at least 3 GCPs for georeferencing");
        }

        ImGui.Separator();

        // Add GCP
        ImGui.Text("Add Ground Control Point:");
        ImGui.InputText("Name", ref _gcpName, 64);

        ImGui.DragFloat3("Local Position (x,y,z)", ref _gcpLocal, 0.1f);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Position in the local reconstruction coordinate system");
        }

        ImGui.DragFloat3("World Position (E,N,Alt)", ref _gcpWorld, 0.1f);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Position in world coordinates (e.g., UTM Easting, Northing, Altitude)");
        }

        ImGui.DragFloat("Accuracy (m)", ref _gcpAccuracy, 0.01f, 0.01f, 100.0f);

        if (ImGui.Button("Add GCP"))
        {
            georef.AddGcp(_gcpName, _gcpLocal, _gcpWorld, _gcpAccuracy);
            _gcpName = $"GCP_{gcps.Count + 1}";
        }

        ImGui.Separator();

        // GCP list
        if (ImGui.BeginTable("GCPTable", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Name");
            ImGui.TableSetupColumn("Local");
            ImGui.TableSetupColumn("World");
            ImGui.TableSetupColumn("Accuracy");
            ImGui.TableSetupColumn("Active");
            ImGui.TableSetupColumn("Actions");
            ImGui.TableHeadersRow();

            for (int i = 0; i < gcps.Count; i++)
            {
                var gcp = gcps[i];
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.Text(gcp.Name);

                ImGui.TableNextColumn();
                ImGui.Text($"({gcp.LocalPosition.X:F2}, {gcp.LocalPosition.Y:F2}, {gcp.LocalPosition.Z:F2})");

                ImGui.TableNextColumn();
                ImGui.Text($"({gcp.WorldPosition.X:F2}, {gcp.WorldPosition.Y:F2}, {gcp.WorldPosition.Z:F2})");

                ImGui.TableNextColumn();
                ImGui.Text($"{gcp.Accuracy:F2} m");

                ImGui.TableNextColumn();
                bool isActive = gcp.IsActive;
                if (ImGui.Checkbox($"##{i}", ref isActive))
                {
                    gcp.IsActive = isActive;
                }

                ImGui.TableNextColumn();
                if (ImGui.SmallButton($"Remove##{i}"))
                {
                    georef.RemoveGcp(gcp);
                }
            }

            ImGui.EndTable();
        }

        ImGui.Separator();

        // Compute transform
        ImGui.Checkbox("Refine with Altitude", ref _refineWithAltitude);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Additional refinement using altitude information for better vertical accuracy");
        }

        if (ImGui.Button("Compute Georeferencing Transform", new Vector2(300, 40)))
        {
            var transform = georef.ComputeTransform(_refineWithAltitude);

            if (transform != null)
            {
                ImGui.OpenPopup("Transform Results");
            }
        }

        // Transform results popup
        if (ImGui.BeginPopupModal("Transform Results", ImGuiWindowFlags.AlwaysAutoResize))
        {
            var transform = georef.ComputeTransform(_refineWithAltitude);
            if (transform != null)
            {
                ImGui.Text($"GCPs Used: {transform.NumGcpsUsed}");
                ImGui.Text($"RMS Error: {transform.RmsError:F3} meters");
                ImGui.Text($"Scale: {transform.Scale:F6}");
                ImGui.Text($"Translation: ({transform.Translation.X:F3}, {transform.Translation.Y:F3}, {transform.Translation.Z:F3})");
                ImGui.Text($"Rotation: ({transform.Rotation.X:F3}, {transform.Rotation.Y:F3}, {transform.Rotation.Z:F3}, {transform.Rotation.W:F3})");
            }

            if (ImGui.Button("OK", new Vector2(120, 0)))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void DrawStatisticsTab()
    {
        ImGui.TextColored(new Vector4(0.3f, 0.8f, 1.0f, 1.0f), "Statistics");
        ImGui.Separator();

        if (_pipeline == null)
        {
            ImGui.Text("Initialize pipeline first");
            return;
        }

        ImGui.Text($"Frames Processed: {_pipeline.FrameCount}");
        ImGui.Text($"Average Processing Time: {_pipeline.AverageProcessingTime:F2} ms");

        if (_processingTimes.Count > 0)
        {
            float avgFps = 1000.0f / (_processingTimes.Count > 0 ? _processingTimes[^1] : 1.0f);
            ImGui.Text($"Current FPS: {avgFps:F1}");
        }

        ImGui.Separator();

        ImGui.Text($"Total Keyframes: {_pipeline.KeyframeManager.Keyframes.Count}");
        ImGui.Text($"Total GCPs: {_pipeline.Georeferencing.GroundControlPoints.Count}");

        ImGui.Separator();

        // Processing time plot
        if (_processingTimes.Count > 0)
        {
            ImGui.PlotLines("Processing Time (ms)", ref _processingTimes.ToArray()[0],
                _processingTimes.Count, 0, "", 0, 100, new Vector2(0, 80));
        }
    }

    private void InitializePipeline()
    {
        try
        {
            _config = new PhotogrammetryPipeline.PipelineConfig
            {
                DepthModelPath = _depthModelPath,
                DepthModelType = (DepthEstimator.DepthModelType)_depthModelType,
                SuperPointModelPath = _superPointModelPath,
                LightGlueModelPath = _lightGlueModelPath,
                UseGpu = _useGpu,
                KeyframeInterval = _keyframeInterval,
                TargetWidth = _targetWidth,
                TargetHeight = _targetHeight,
                FocalLengthX = _focalLengthX,
                FocalLengthY = _focalLengthY,
                PrincipalPointX = _principalPointX,
                PrincipalPointY = _principalPointY
            };

            _pipeline?.Dispose();
            _pipeline = new PhotogrammetryPipeline(_config);

            Logger.Log("Photogrammetry pipeline initialized successfully");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to initialize pipeline: {ex.Message}");
        }
    }

    private void StartCapture()
    {
        if (_pipeline == null || !_pipeline.IsInitialized)
        {
            Logger.LogError("Initialize pipeline first");
            return;
        }

        bool success = false;

        if (_useVideoFile)
        {
            success = _pipeline.VideoCapture.OpenFile(_videoFilePath);
        }
        else
        {
            success = _pipeline.VideoCapture.OpenCamera(_selectedCameraIndex, _targetWidth, _targetHeight);
        }

        if (success)
        {
            _isCapturing = true;
            _isPaused = false;
            Logger.Log("Video capture started");
        }
    }

    private void StopCapture()
    {
        if (_pipeline != null)
        {
            _pipeline.VideoCapture.CloseCamera();
        }

        _isCapturing = false;
        _isPaused = false;

        _currentFrameDisplay?.Dispose();
        _currentFrameDisplay = null;
        _currentDepthDisplay?.Dispose();
        _currentDepthDisplay = null;

        Logger.Log("Video capture stopped");
    }

    private void ProcessNextFrame()
    {
        if (_pipeline.VideoCapture.CaptureFrame(out Mat frame))
        {
            var result = _pipeline.ProcessFrame(frame);

            if (result != null && result.Success)
            {
                // Update display
                _currentFrameDisplay?.Dispose();
                _currentFrameDisplay = result.PreprocessedFrame?.Clone();

                if (result.DepthMap != null)
                {
                    _currentDepthDisplay?.Dispose();
                    _currentDepthDisplay = NormalizeDepthForDisplay(result.DepthMap);
                }

                // Update statistics
                _processingTimes.Add((float)result.ProcessingTimeMs);
                if (_processingTimes.Count > MaxStatsSamples)
                {
                    _processingTimes.RemoveAt(0);
                }
            }

            frame.Dispose();
        }
    }

    private Mat NormalizeDepthForDisplay(Mat depth)
    {
        Mat normalized = new Mat();
        Cv2.Normalize(depth, normalized, 0, 255, NormTypes.MinMax);
        normalized.ConvertTo(normalized, MatType.CV_8UC1);
        Cv2.ApplyColorMap(normalized, normalized, ColormapTypes.Jet);
        return normalized;
    }

    private void DisplayMat(string label, Mat image, Vector2 size)
    {
        // This is a placeholder - you'll need to implement texture upload to GPU
        // using your graphics backend (Veldrid in this case)
        ImGui.BeginChild(label, size, true);
        ImGui.Text($"{label}: {image.Width}x{image.Height}");
        // TODO: Display image texture
        ImGui.EndChild();
    }

    private void ExportPointCloudInternal(string filePath, PointCloudExporter.ExportFormat format)
    {
        if (_pipeline == null || _pipeline.KeyframeManager.Keyframes.Count == 0)
        {
            Logger.LogWarning("No keyframes to export");
            return;
        }

        try
        {
            var geoTransform = _pipeline.Georeferencing.GroundControlPoints.Count >= 3
                ? _pipeline.Georeferencing.ComputeTransform()
                : null;

            bool success = PointCloudExporter.ExportKeyframes(
                _pipeline.KeyframeManager.Keyframes,
                filePath,
                format,
                exportColors: true,
                geoTransform: geoTransform);

            if (success)
            {
                Logger.Log($"Successfully exported point cloud to {filePath}");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export point cloud: {ex.Message}");
        }
    }

    private void ExportMeshInternal(string filePath)
    {
        if (_pipeline == null || _pipeline.KeyframeManager.Keyframes.Count == 0)
        {
            Logger.LogWarning("No keyframes to export as mesh");
            return;
        }

        try
        {
            // For now, export as point cloud in OBJ format
            // Full mesh reconstruction would require TSDF fusion or similar
            var geoTransform = _pipeline.Georeferencing.GroundControlPoints.Count >= 3
                ? _pipeline.Georeferencing.ComputeTransform()
                : null;

            bool success = PointCloudExporter.ExportKeyframes(
                _pipeline.KeyframeManager.Keyframes,
                filePath,
                PointCloudExporter.ExportFormat.OBJ,
                exportColors: true,
                geoTransform: geoTransform);

            if (success)
            {
                Logger.Log($"Successfully exported mesh to {filePath}");
                Logger.LogWarning("Note: Full mesh reconstruction (TSDF fusion) not yet implemented. Exported as point cloud.");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export mesh: {ex.Message}");
        }
    }

    private void ExportCameraPathInternal(string filePath)
    {
        if (_pipeline == null || _pipeline.KeyframeManager.Keyframes.Count == 0)
        {
            Logger.LogWarning("No keyframes to export camera path");
            return;
        }

        try
        {
            bool success = PointCloudExporter.ExportCameraPath(
                _pipeline.KeyframeManager.Keyframes,
                filePath);

            if (success)
            {
                Logger.Log($"Successfully exported camera path to {filePath}");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export camera path: {ex.Message}");
        }
    }

    public void Dispose()
    {
        StopCapture();
        _pipeline?.Dispose();
        _currentFrameDisplay?.Dispose();
        _currentDepthDisplay?.Dispose();
    }
}
