// GeoscientistToolkit/UI/Photogrammetry/PhotogrammetryWizardPanel.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.IO;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Business.Photogrammetry;
using GeoscientistToolkit.Business.Panorama;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using Veldrid;

namespace GeoscientistToolkit.UI.Photogrammetry;

/// <summary>
/// Editor for placing and editing Ground Control Points on images
/// </summary>
internal class GroundControlPointEditor : IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ResourceFactory _resourceFactory;
    private readonly ImGuiRenderer _imGuiRenderer;

    private struct VeldridTextureBinding : IDisposable
    {
        public readonly IntPtr ImGuiBinding;
        private readonly Texture _texture;
        private readonly TextureView _textureView;
        private readonly ImGuiRenderer _renderer;
        private bool _disposed;

        public VeldridTextureBinding(Texture texture, TextureView view, IntPtr binding, ImGuiRenderer renderer)
        {
            _texture = texture;
            _textureView = view;
            ImGuiBinding = binding;
            _renderer = renderer;
            _disposed = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _renderer.RemoveImGuiBinding(_textureView);
            _textureView.Dispose();
            _texture.Dispose();
            _disposed = true;
        }
    }

    public bool IsOpen;
    private PhotogrammetryImage _currentImage;
    private VeldridTextureBinding? _imageBinding;

    private Vector2 _pan = Vector2.Zero;
    private float _zoom = 1.0f;
    private GroundControlPoint _selectedGCP;
    private bool _isPlacingNew;
    private string _gcpName = "GCP";
    private string _gcpX = "";
    private string _gcpY = "";
    private string _gcpZ = "";

    public Action<PhotogrammetryImage, GroundControlPoint> OnGCPUpdated;
    public Action<PhotogrammetryImage, GroundControlPoint> OnGCPRemoved;

    public GroundControlPointEditor(GraphicsDevice graphicsDevice, ImGuiRenderer imGuiRenderer)
    {
        _graphicsDevice = graphicsDevice;
        _resourceFactory = graphicsDevice.ResourceFactory;
        _imGuiRenderer = imGuiRenderer;
    }

    public void Open(PhotogrammetryImage image)
    {
        _currentImage = image;
        DisposeBinding();
        _imageBinding = CreateTextureBinding(_currentImage.Dataset);
        _pan = Vector2.Zero;
        _zoom = 1.0f;
        _selectedGCP = null;
        _isPlacingNew = false;
        IsOpen = true;
        ImGui.OpenPopup("Ground Control Point Editor");
    }

    private VeldridTextureBinding? CreateTextureBinding(Data.Image.ImageDataset dataset)
    {
        if (dataset?.ImageData == null) return null;

        var texture = _resourceFactory.CreateTexture(TextureDescription.Texture2D(
            (uint)dataset.Width, (uint)dataset.Height, 1, 1,
            PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));

        // The ImageLoader always provides RGBA data, so we can use it directly.
        _graphicsDevice.UpdateTexture(texture, dataset.ImageData, 0, 0, 0, (uint)dataset.Width, (uint)dataset.Height, 1, 0, 0);

        var textureView = _resourceFactory.CreateTextureView(texture);
        var imGuiBinding = _imGuiRenderer.GetOrCreateImGuiBinding(_resourceFactory, textureView);

        return new VeldridTextureBinding(texture, textureView, imGuiBinding, _imGuiRenderer);
    }

    public void Draw()
    {
        if (!IsOpen) return;

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(1200, 700), ImGuiCond.Appearing);

        if (ImGui.BeginPopupModal("Ground Control Point Editor", ref IsOpen, ImGuiWindowFlags.NoCollapse))
        {
            if (_currentImage == null || _imageBinding == null)
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), "Error: Could not load image.");
                if (ImGui.Button("Close")) IsOpen = false;
                ImGui.EndPopup();
                return;
            }

            ImGui.Text($"Image: {_currentImage.Dataset.Name}");
            if (_currentImage.IsGeoreferenced)
            {
                ImGui.TextColored(new Vector4(0, 1, 0, 1),
                    $"Georeferenced: Lat={_currentImage.Latitude:F6}, Lon={_currentImage.Longitude:F6}, Alt={_currentImage.Altitude?.ToString("F2") ?? "N/A"}");
            }
            ImGui.Separator();

            ImGui.Columns(2, "GCPLayout", true);
            DrawImagePanel();
            ImGui.NextColumn();
            DrawControlPanel();
            ImGui.Columns(1);

            ImGui.Separator();
            if (ImGui.Button("Close", new Vector2(120, 0))) IsOpen = false;

            ImGui.EndPopup();
        }

        if (!IsOpen) DisposeBinding();
    }

    private void DrawImagePanel()
    {
        ImGui.Text("Click on image to place/select GCP. Mouse wheel to zoom, middle button to pan.");
        ImGui.BeginChild("ImagePanel", Vector2.Zero, ImGuiChildFlags.Border, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        var panelTopLeft = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        if (ImGui.IsWindowHovered())
        {
            var io = ImGui.GetIO();
            if (io.MouseWheel != 0)
            {
                var mousePosInPanel = io.MousePos - panelTopLeft;
                var oldZoom = _zoom;
                _zoom *= (io.MouseWheel > 0) ? 1.1f : 1 / 1.1f;
                _zoom = Math.Clamp(_zoom, 0.1f, 10.0f);
                _pan = mousePosInPanel - _zoom / oldZoom * (mousePosInPanel - _pan);
            }
            if (ImGui.IsMouseDown(ImGuiMouseButton.Middle)) _pan += io.MouseDelta;
        }

        var imgTopLeft = panelTopLeft + _pan;
        var imgBottomRight = imgTopLeft + new Vector2(_currentImage.Dataset.Width, _currentImage.Dataset.Height) * _zoom;
        drawList.AddImage(_imageBinding.Value.ImGuiBinding, imgTopLeft, imgBottomRight);

        // Draw existing GCPs
        foreach (var gcp in _currentImage.GroundControlPoints)
        {
            var screenPos = panelTopLeft + _pan + gcp.ImagePosition * _zoom;
            var color = gcp == _selectedGCP ? 0xFFFFFF00 : (gcp.IsConfirmed ? 0xFF00FF00 : 0xFF0000FF);
            drawList.AddCircleFilled(screenPos, 6, color, 12);
            drawList.AddCircle(screenPos, 6, 0xFF000000, 12, 2f);
            drawList.AddText(screenPos + new Vector2(10, -10), 0xFFFFFFFF, gcp.Name);
        }

        if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            var imageCoords = (ImGui.GetMousePos() - (panelTopLeft + _pan)) / _zoom;
            if (imageCoords.X >= 0 && imageCoords.Y >= 0 &&
                imageCoords.X <= _currentImage.Dataset.Width && imageCoords.Y <= _currentImage.Dataset.Height)
            {
                HandleImageClick(imageCoords);
            }
        }

        ImGui.EndChild();
    }

    private void HandleImageClick(Vector2 imageCoords)
    {
        foreach (var gcp in _currentImage.GroundControlPoints)
        {
            var dist = Vector2.Distance(gcp.ImagePosition, imageCoords);
            if (dist < 20 / _zoom)
            {
                _selectedGCP = gcp;
                _gcpName = gcp.Name;
                _gcpX = gcp.WorldPosition?.X.ToString("F3") ?? "";
                _gcpY = gcp.WorldPosition?.Y.ToString("F3") ?? "";
                _gcpZ = gcp.WorldPosition?.Z.ToString("F3") ?? "";
                _isPlacingNew = false;
                return;
            }
        }

        if (_isPlacingNew)
        {
            var newGCP = new GroundControlPoint
            {
                Name = _gcpName,
                ImagePosition = imageCoords
            };
            _currentImage.GroundControlPoints.Add(newGCP);
            _selectedGCP = newGCP;
            _isPlacingNew = false;
            OnGCPUpdated?.Invoke(_currentImage, newGCP);
        }
    }

    private void DrawControlPanel()
    {
        ImGui.Text("Ground Control Points");
        ImGui.Separator();

        if (ImGui.Button("Add New GCP", new Vector2(-1, 0)))
        {
            _isPlacingNew = true;
            _selectedGCP = null;
            _gcpName = $"GCP_{_currentImage.GroundControlPoints.Count + 1}";
            _gcpX = ""; _gcpY = ""; _gcpZ = "";
        }

        if (_isPlacingNew)
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "Click on image to place GCP");

        ImGui.Separator();

        if (_selectedGCP != null)
        {
            ImGui.Text("Selected GCP:");
            ImGui.InputText("Name", ref _gcpName, 64);
            ImGui.Text("World Coordinates:");
            ImGui.InputText("X (or Longitude)", ref _gcpX, 32);
            ImGui.InputText("Y (or Latitude)", ref _gcpY, 32);
            ImGui.InputText("Z (or Altitude)", ref _gcpZ, 32);

            if (ImGui.Button("Update Coordinates", new Vector2(-1, 0)))
            {
                if (float.TryParse(_gcpX, out float x) &&
                    float.TryParse(_gcpY, out float y) &&
                    float.TryParse(_gcpZ, out float z))
                {
                    _selectedGCP.Name = _gcpName;
                    _selectedGCP.WorldPosition = new Vector3(x, y, z);
                    OnGCPUpdated?.Invoke(_currentImage, _selectedGCP);
                }
            }

            if (ImGui.Button("Remove GCP", new Vector2(-1, 0)))
            {
                OnGCPRemoved?.Invoke(_currentImage, _selectedGCP);
                _selectedGCP = null;
            }
        }

        ImGui.Separator();
        ImGui.Text($"Total GCPs: {_currentImage.GroundControlPoints.Count}");
        ImGui.Text($"Confirmed: {_currentImage.GroundControlPoints.Count(g => g.IsConfirmed)}");
    }

    private void DisposeBinding()
    {
        _imageBinding?.Dispose();
        _imageBinding = null;
    }

    public void Dispose() => DisposeBinding();
}

public class PhotogrammetryWizardPanel : BasePanel
{
    private readonly PhotogrammetryJob _job;
    private readonly List<string> _logBuffer = new();
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ImGuiRenderer _imGuiRenderer;

    private readonly GroundControlPointEditor _gcpEditor;
    private readonly ImGuiExportFileDialog _exportDialog;

    private PhotogrammetryImage _imageToDiscard;
    private PhotogrammetryImage _groupLinkImage1;
    private PhotogrammetryImage _groupLinkImage2;

    private DenseCloudOptions _denseCloudOptions = new();
    private MeshOptions _meshOptions = new();
    private OrthomosaicOptions _orthoOptions = new();
    private DEMOptions _demOptions = new();
    private bool _showOrthoDialog;
    private bool _showDemDialog;

    private bool _showDenseCloudDialog;
    private bool _showMeshDialog;
    private bool _addToProject = true;

    public bool IsOpen { get; private set; }
    public string Title { get; }

    public PhotogrammetryWizardPanel(DatasetGroup imageGroup, GraphicsDevice graphicsDevice, ImGuiRenderer imGuiRenderer)
        : base($"Photogrammetry: {imageGroup.Name}", new Vector2(900, 700))
    {
        Title = $"Photogrammetry: {imageGroup.Name}";
        _graphicsDevice = graphicsDevice;
        _imGuiRenderer = imGuiRenderer;

        _job = new PhotogrammetryJob(imageGroup);
        _job.Service.StartProcessingAsync();

        _exportDialog = new ImGuiExportFileDialog("photogrammetryExport", "Export");

        _gcpEditor = new GroundControlPointEditor(graphicsDevice, imGuiRenderer);
        _gcpEditor.OnGCPUpdated = (img, gcp) => _job.Service.AddOrUpdateGroundControlPoint(img, gcp);
        _gcpEditor.OnGCPRemoved = (img, gcp) => _job.Service.RemoveGroundControlPoint(img, gcp);
    }

    public void Open() => IsOpen = true;

    public void Submit()
    {
        bool pOpen = IsOpen;
        base.Submit(ref pOpen);
        IsOpen = pOpen;

        if (!IsOpen)
        {
            _job.Service.Cancel();
            _gcpEditor.Dispose();
        }
    }

    protected override void DrawContent()
    {
        UpdateLogs();

        if (_imageToDiscard != null)
        {
            _job.Service.RemoveImage(_imageToDiscard);
            _imageToDiscard = null;
        }

        _gcpEditor.Draw();

        var service = _job.Service;
        ImGui.ProgressBar(service.Progress, new Vector2(-1, 0), service.StatusMessage);
        
        // --- Extra actions toolbar ---
        ImGui.Separator();
        if (ImGui.Button("Build Dense Cloud...", new Vector2(200, 0))) _showDenseCloudDialog = true;
        ImGui.SameLine();
        if (ImGui.Button("Build Mesh...", new Vector2(200, 0))) _showMeshDialog = true;
        ImGui.SameLine();
        if (ImGui.Button("Build Orthomosaic...", new Vector2(200, 0))) _showOrthoDialog = true;
        ImGui.SameLine();
        if (ImGui.Button("Build DEM...", new Vector2(200, 0))) _showDemDialog = true;
        ImGui.Separator();

        if (ImGui.BeginTabBar("PhotogrammetryTabs"))
        {
            if (ImGui.BeginTabItem("Processing Log")) { DrawLog(); ImGui.EndTabItem(); }
            if (service.State == PhotogrammetryState.AwaitingManualInput && ImGui.BeginTabItem("Manual Input")) { DrawManualInputUI(); ImGui.EndTabItem(); }
            if (service.State >= PhotogrammetryState.ComputingSparseReconstruction && ImGui.BeginTabItem("Reconstruction")) { DrawReconstructionUI(); ImGui.EndTabItem(); }
            if (service.SparseCloud != null && ImGui.BeginTabItem("Sparse Cloud")) { DrawPointCloudView(service.SparseCloud); ImGui.EndTabItem(); }
            if (service.DenseCloud != null && ImGui.BeginTabItem("Dense Cloud")) { DrawPointCloudView(service.DenseCloud); ImGui.EndTabItem(); }
            if (service.GeneratedMesh != null && ImGui.BeginTabItem("Mesh")) { DrawMeshView(); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }

        if (service.State == PhotogrammetryState.Failed)
            ImGui.TextColored(new Vector4(1, 0, 0, 1), "Process failed. Check log.");

        DrawBuildDialogs();
        HandleExportDialog();
    }

    private void UpdateLogs()
    {
        while (_job.Service.Logs.TryDequeue(out var log))
        {
            _logBuffer.Add(log);
            if (_logBuffer.Count > 500) _logBuffer.RemoveAt(0);
        }
    }

    private void DrawLog()
    {
        ImGui.BeginChild("LogRegion", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);
        ImGui.TextUnformatted(string.Join("\n", _logBuffer));
        if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY()) ImGui.SetScrollHereY(1.0f);
        ImGui.EndChild();
    }

    private void DrawManualInputUI()
    {
        var imageGroups = _job.Service.ImageGroups;
        var imagesWithNoFeatures = _job.Service.Images.Where(img => img.Features.KeyPoints.Count < 20).ToList();

        if (imagesWithNoFeatures.Any())
        {
            if (ImGui.CollapsingHeader($"Images with too few features ({imagesWithNoFeatures.Count})###NoFeat", ImGuiTreeNodeFlags.DefaultOpen))
            {
                foreach (var image in imagesWithNoFeatures)
                {
                    ImGui.BulletText($"{image.Dataset.Name}");
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"Discard##{image.Id}")) _imageToDiscard = image;
                }
            }
        }

        if (imageGroups.Count > 1)
        {
            if (ImGui.CollapsingHeader($"Unconnected Groups ({imageGroups.Count})###Groups", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.TextWrapped("Select images from different groups to link them manually.");
                ImGui.Text("Group 1:"); ImGui.SameLine();
                foreach (var grp in imageGroups)
                {
                    foreach (var img in grp.Images)
                    {
                        if (ImGui.Selectable($"{img.Dataset.Name}##{img.Id}", _groupLinkImage1 == img))
                            _groupLinkImage1 = img;
                    }
                    ImGui.Separator();
                }
            }
        }

        ImGui.Separator();
        if (ImGui.CollapsingHeader("Ground Control Points###GCP", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextWrapped("Add ground control points to georeference the model.");

            foreach (var image in _job.Service.Images)
            {
                var label = $"{image.Dataset.Name} ({image.GroundControlPoints.Count} GCPs)";
                if (ImGui.TreeNode(label))
                {
                    foreach (var gcp in image.GroundControlPoints)
                    {
                        var status = gcp.IsConfirmed ? "[OK]" : "[Missing Coords]";
                        ImGui.Text($"  {gcp.Name} {status}");
                    }
                    if (ImGui.Button($"Edit GCPs##{image.Id}"))
                        _gcpEditor.Open(image);
                    ImGui.TreePop();
                }
            }
        }
    }

    private void DrawReconstructionUI()
    {
        var service = _job.Service;

        ImGui.TextWrapped("Build 3D reconstruction products:");
        ImGui.Separator();

        bool hasSparse = service.SparseCloud != null;
        if (hasSparse)
            ImGui.TextColored(new Vector4(0, 1, 0, 1), $"✓ Sparse Cloud: {service.SparseCloud.Points.Count} points");
        else
        {
            if (ImGui.Button("Build Sparse Cloud", new Vector2(-1, 30)))
                _ = service.BuildSparseCloudAsync();
        }

        ImGui.Separator();

        bool hasDense = service.DenseCloud != null;
        if (hasDense)
            ImGui.TextColored(new Vector4(0, 1, 0, 1), $"✓ Dense Cloud: {service.DenseCloud.Points.Count} points");
        else
        {
            if (!hasSparse) ImGui.BeginDisabled();
            if (ImGui.Button("Build Dense Cloud...", new Vector2(-1, 30)))
                _showDenseCloudDialog = true;
            if (!hasSparse) ImGui.EndDisabled();
        }

        ImGui.Separator();

        bool hasMesh = service.GeneratedMesh != null;
        if (hasMesh)
            ImGui.TextColored(new Vector4(0, 1, 0, 1), $"✓ Mesh: {service.GeneratedMesh.VertexCount} vertices");
        else
        {
            bool canBuild = hasSparse || hasDense;
            if (!canBuild) ImGui.BeginDisabled();
            if (ImGui.Button("Build Mesh...", new Vector2(-1, 30)))
                _showMeshDialog = true;
            if (!canBuild) ImGui.EndDisabled();
        }
    }

    private void DrawPointCloudView(PhotogrammetryPointCloud cloud)
    {
        ImGui.Text($"Points: {cloud.Points.Count}");
        ImGui.Text($"Type: {(cloud.IsDense ? "Dense" : "Sparse")}");
        ImGui.Text($"Bounds: {cloud.BoundingBoxMin} to {cloud.BoundingBoxMax}");

        ImGui.BeginChild("Preview", Vector2.Zero, ImGuiChildFlags.Border);
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var size = ImGui.GetContentRegionAvail();

        if (size.X > 10 && size.Y > 10 && cloud.Points.Count > 0)
        {
            var range = cloud.BoundingBoxMax - cloud.BoundingBoxMin;
            float scale = Math.Min(size.X / range.X, size.Y / range.Y) * 0.9f;

            foreach (var pt in cloud.Points.Take(3000))
            {
                var p2d = new Vector2(pt.Position.X, pt.Position.Y) - new Vector2(cloud.BoundingBoxMin.X, cloud.BoundingBoxMin.Y);
                var screen = pos + size / 2 + new Vector2(p2d.X, -p2d.Y) * scale;
                uint col = ImGui.ColorConvertFloat4ToU32(new Vector4(pt.Color, 1));
                drawList.AddCircleFilled(screen, 2, col);
            }
        }
        ImGui.EndChild();

        if (ImGui.Button("Export Point Cloud...", new Vector2(-1, 0)))
        {
            _exportDialog.SetExtensions(
                new ImGuiExportFileDialog.ExtensionOption(".xyz", "ASCII XYZ"),
                new ImGuiExportFileDialog.ExtensionOption(".ply", "PLY Format")
            );
            _exportDialog.Open($"{_job.ImageGroup.Name}_{(cloud.IsDense ? "dense" : "sparse")}_cloud");
        }
    }

    private void DrawMeshView()
    {
        var mesh = _job.Service.GeneratedMesh;
        ImGui.Text($"Vertices: {mesh.VertexCount}");
        ImGui.Text($"Faces: {mesh.FaceCount}");
        ImGui.Text($"Bounds: {mesh.BoundingBoxMin} to {mesh.BoundingBoxMax}");

        ImGui.Separator();
        ImGui.Checkbox("Add to project", ref _addToProject);

        if (_addToProject && mesh != null && !ProjectManager.Instance.LoadedDatasets.Contains(mesh))
        {
            if (ImGui.Button("Add Mesh to Project Now", new Vector2(-1, 0)))
            {
                ProjectManager.Instance.AddDataset(mesh);
                Logger.Log($"Added mesh to project: {mesh.Name}");
            }
        }
    }

    private void DrawBuildDialogs()
    {
        if (_showDenseCloudDialog)
        {
            ImGui.OpenPopup("Dense Cloud Options");
            _showDenseCloudDialog = false;
        }

        bool isDenseCloudPopupOpen = true;
        if (ImGui.BeginPopupModal("Dense Cloud Options", ref isDenseCloudPopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Configure dense cloud generation:");
            ImGui.Separator();

            int quality = _denseCloudOptions.Quality;
            if (ImGui.SliderInt("Quality", ref quality, 0, 4))
                _denseCloudOptions.Quality = quality;

            ImGui.Text(_denseCloudOptions.Quality switch
            {
                0 => "Lowest (fastest)", 1 => "Low", 2 => "Medium", 3 => "High", 4 => "Ultra", _ => ""
            });

            bool filterOutliers = _denseCloudOptions.FilterOutliers;
            if (ImGui.Checkbox("Filter Outliers", ref filterOutliers))
                _denseCloudOptions.FilterOutliers = filterOutliers;

            float confidence = _denseCloudOptions.ConfidenceThreshold;
            if (ImGui.SliderFloat("Confidence Threshold", ref confidence, 0f, 1f))
                _denseCloudOptions.ConfidenceThreshold = confidence;

            ImGui.Separator();
            if (ImGui.Button("Build", new Vector2(120, 0)))
            {
                _ = _job.Service.BuildDenseCloudAsync(_denseCloudOptions);
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0))) ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }

        if (_showMeshDialog)
        {
            ImGui.OpenPopup("Mesh Options");
            _showMeshDialog = false;
        }

        bool isMeshPopupOpen = true;
        if (ImGui.BeginPopupModal("Mesh Options", ref isMeshPopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Configure mesh generation:");
            ImGui.Separator();

            int src = (int)_meshOptions.Source;
            if (ImGui.Combo("Source Data", ref src, "Sparse Cloud\0Dense Cloud\0"))
                _meshOptions.Source = (MeshOptions.SourceData)src;

            int faceCount = _meshOptions.FaceCount;
            if (ImGui.InputInt("Target Faces", ref faceCount))
                _meshOptions.FaceCount = faceCount;

            bool simplify = _meshOptions.SimplifyMesh;
            if (ImGui.Checkbox("Simplify", ref simplify))
                _meshOptions.SimplifyMesh = simplify;

            bool smooth = _meshOptions.SmoothNormals;
            if (ImGui.Checkbox("Smooth Normals", ref smooth))
                _meshOptions.SmoothNormals = smooth;

            ImGui.Separator();
            ImGui.Checkbox("Add to project", ref _addToProject);

            if (ImGui.Button("Build", new Vector2(120, 0)))
            {
                _exportDialog.SetExtensions(
                    new ImGuiExportFileDialog.ExtensionOption(".obj", "Wavefront OBJ"),
                    new ImGuiExportFileDialog.ExtensionOption(".stl", "STL")
                );
                _exportDialog.Open($"{_job.ImageGroup.Name}_mesh");
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0))) ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }

        // Orthomosaic dialog
        if (_showOrthoDialog)
        {
            ImGui.OpenPopup("Orthomosaic Options");
            _showOrthoDialog = false;
        }
        bool isOrthoPopupOpen = true;
        if (ImGui.BeginPopupModal("Orthomosaic Options", ref isOrthoPopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            float gsd = _orthoOptions.GroundSamplingDistance;
            if (ImGui.InputFloat("GSD (m/px)", ref gsd)) _orthoOptions.GroundSamplingDistance = MathF.Max(1e-6f, gsd);

            int src = (int)_orthoOptions.Source;
            if (ImGui.Combo("Source", ref src, "Mesh\0PointCloud\0")) _orthoOptions.Source = (OrthomosaicOptions.SourceData)src;

            bool blend = _orthoOptions.EnableBlending;
            if (ImGui.Checkbox("Blend images", ref blend)) _orthoOptions.EnableBlending = blend;

            ImGui.Separator();
            ImGui.Checkbox("Add to project", ref _addToProject);

            if (ImGui.Button("Build", new Vector2(120, 0)))
            {
                _exportDialog.SetExtensions(
                    new ImGuiExportFileDialog.ExtensionOption(".png", "PNG Image"),
                    new ImGuiExportFileDialog.ExtensionOption(".tif", "TIFF Image")
                );
                _exportDialog.Open($"{_job.ImageGroup.Name}_orthomosaic");
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0))) ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }

        // DEM dialog
        if (_showDemDialog)
        {
            ImGui.OpenPopup("DEM Options");
            _showDemDialog = false;
        }
        bool isDEMPopupOpen = true;
        if (ImGui.BeginPopupModal("DEM Options", ref isDEMPopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            float res = _demOptions.Resolution;
            if (ImGui.InputFloat("Resolution (m/px)", ref res)) _demOptions.Resolution = MathF.Max(1e-6f, res);

            int srcDEM = (int)_demOptions.Source;
            if (ImGui.Combo("Source", ref srcDEM, "Mesh\0PointCloud\0")) _demOptions.Source = (DEMOptions.SourceData)srcDEM;

            bool fill = _demOptions.FillHoles;
            if (ImGui.Checkbox("Fill small holes", ref fill)) _demOptions.FillHoles = fill;

            bool smooth = _demOptions.SmoothSurface;
            if (ImGui.Checkbox("Smooth (box blur)", ref smooth)) _demOptions.SmoothSurface = smooth;

            ImGui.Separator();
            ImGui.Checkbox("Add to project", ref _addToProject);

            if (ImGui.Button("Build", new Vector2(120, 0)))
            {
                _exportDialog.SetExtensions(
                    new ImGuiExportFileDialog.ExtensionOption(".png", "PNG Heightmap"),
                    new ImGuiExportFileDialog.ExtensionOption(".tif", "TIFF (8-bit)")
                );
                _exportDialog.Open($"{_job.ImageGroup.Name}_dem");
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0))) ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }
    }
    
    private void HandleExportDialog()
    {
        if (_exportDialog.Submit() && !string.IsNullOrEmpty(_exportDialog.SelectedPath))
        {
            var path = _exportDialog.SelectedPath;
            var lowerPath = path.ToLower();

            // Route based on file extension
            if (lowerPath.EndsWith(".obj") || lowerPath.EndsWith(".stl"))
            {
                _ = _job.Service.BuildMeshAsync(_meshOptions, path);
            }
            else if (lowerPath.EndsWith(".xyz") || lowerPath.EndsWith(".ply"))
            {
                ExportPointCloud(path);
            }
            else if (lowerPath.EndsWith(".png") || lowerPath.EndsWith(".tif"))
            {
                if (lowerPath.Contains("orthomosaic"))
                    _ = _job.Service.BuildOrthomosaicAsync(_orthoOptions, path);
                else if (lowerPath.Contains("dem"))
                    _ = _job.Service.BuildDEMAsync(_demOptions, path);
            }
        }
    }

    private void ExportPointCloud(string path)
    {
        var cloud = _job.Service.DenseCloud ?? _job.Service.SparseCloud;
        if (cloud == null) return;

        if (path.EndsWith(".xyz", StringComparison.OrdinalIgnoreCase))
        {
            using var writer = new StreamWriter(path);
            foreach (var pt in cloud.Points)
            {
                writer.WriteLine($"{pt.Position.X} {pt.Position.Y} {pt.Position.Z} {(int)(pt.Color.X * 255)} {(int)(pt.Color.Y * 255)} {(int)(pt.Color.Z * 255)}");
            }
        }
        else if (path.EndsWith(".ply", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Create);
                using var writer = new BinaryWriter(stream);

                // --- PLY Header ---
                var header = $@"ply
format binary_little_endian 1.0
comment Generated by GeoscientistToolkit
element vertex {cloud.Points.Count}
property float x
property float y
property float z
property uchar red
property uchar green
property uchar blue
end_header
";
                writer.Write(System.Text.Encoding.ASCII.GetBytes(header));

                // --- PLY Body (Binary) ---
                foreach (var pt in cloud.Points)
                {
                    // Write position (X, Y, Z) as floats
                    writer.Write(pt.Position.X);
                    writer.Write(pt.Position.Y);
                    writer.Write(pt.Position.Z);

                    // Write color (R, G, B) as uchar
                    writer.Write((byte)(pt.Color.X * 255));
                    writer.Write((byte)(pt.Color.Y * 255));
                    writer.Write((byte)(pt.Color.Z * 255));
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to export PLY file: {ex.Message}");
            }
        }


        if (_addToProject)
        {
            Logger.Log($"Exported point cloud: {path}");
        }
    }
}