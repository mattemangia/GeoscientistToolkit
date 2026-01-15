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
    private readonly ImGuiController _imGuiController;

    private struct VeldridTextureBinding : IDisposable
    {
        public readonly IntPtr ImGuiBinding;
        private readonly Texture _texture;
        private readonly TextureView _textureView;
        private readonly ImGuiController _renderer;
        private bool _disposed;

        public VeldridTextureBinding(Texture texture, TextureView view, IntPtr binding, ImGuiController renderer)
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

    public GroundControlPointEditor(GraphicsDevice graphicsDevice, ImGuiController imGuiController)
    {
        _graphicsDevice = graphicsDevice;
        _resourceFactory = graphicsDevice.ResourceFactory;
        _imGuiController = imGuiController;
    }

    public void Open(PhotogrammetryImage image)
    {
        _currentImage = image;
        DisposeBinding();
        
        if (_currentImage?.Dataset?.ImageData == null)
        {
            Util.Logger.LogError($"Cannot open GCP editor: Image data not loaded for {_currentImage?.Dataset?.Name ?? "null"}");
            IsOpen = false;
            return;
        }
        
        _imageBinding = CreateTextureBinding(_currentImage.Dataset);
        if (_imageBinding == null)
        {
            Util.Logger.LogError($"Failed to create texture binding for {_currentImage.Dataset.Name}");
            IsOpen = false;
            return;
        }
        
        _pan = Vector2.Zero;
        _zoom = 1.0f;
        _selectedGCP = null;
        _isPlacingNew = false;
        IsOpen = true;
    }

    private VeldridTextureBinding? CreateTextureBinding(Data.Image.ImageDataset dataset)
    {
        if (dataset?.ImageData == null) return null;

        var texture = _resourceFactory.CreateTexture(TextureDescription.Texture2D(
            (uint)dataset.Width, (uint)dataset.Height, 1, 1,
            PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));

        _graphicsDevice.UpdateTexture(texture, dataset.ImageData, 0, 0, 0, (uint)dataset.Width, (uint)dataset.Height, 1, 0, 0);

        var textureView = _resourceFactory.CreateTextureView(texture);
        var imGuiBinding = _imGuiController.GetOrCreateImGuiBinding(_resourceFactory, textureView);

        return new VeldridTextureBinding(texture, textureView, imGuiBinding, _imGuiController);
    }

    public void Draw()
    {
        if (!IsOpen) return;

        if (!ImGui.IsPopupOpen("Ground Control Point Editor"))
        {
            ImGui.OpenPopup("Ground Control Point Editor");
        }

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
    private readonly ImGuiController _imGuiController;

    private readonly GroundControlPointEditor _gcpEditor;
    private readonly ImGuiExportFileDialog _exportDialog;

    private PhotogrammetryImage _imageToDiscard;
    private PhotogrammetryImage _groupLinkImage1;
    private PhotogrammetryImage _groupLinkImage2;

    private DenseCloudOptions _denseCloudOptions = new();
    private MeshOptions _meshOptions = new();
    private TextureOptions _textureOptions = new();
    private OrthomosaicOptions _orthoOptions = new();
    private DEMOptions _demOptions = new();
    private bool _showOrthoDialog;
    private bool _showDemDialog;
    private bool _showTextureDialog;

    // Point cloud visualization options
    private int _pointCloudProjectionMode = 0; // 0=XY, 1=XZ, 2=YZ
    private bool _pointCloudUseDepthColor = true;

    private bool _showDenseCloudDialog;
    private bool _showMeshDialog;
    private bool _addToProject = true;

    public bool IsOpen { get; private set; }
    public string Title { get; }

    public PhotogrammetryWizardPanel(DatasetGroup imageGroup, GraphicsDevice graphicsDevice, ImGuiController imGuiController)
        : base($"Photogrammetry: {imageGroup.Name}", new Vector2(900, 700))
    {
        Title = $"Photogrammetry: {imageGroup.Name}";
        _graphicsDevice = graphicsDevice;
        _imGuiController = imGuiController;

        _job = new PhotogrammetryJob(imageGroup);
        _job.Service.StartProcessingAsync();

        _exportDialog = new ImGuiExportFileDialog("photogrammetryExport", "Export");

        _gcpEditor = new GroundControlPointEditor(graphicsDevice, imGuiController);
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
            if (ImGui.BeginTabItem("Processing Log")) 
            { 
                DrawLog(); 
                ImGui.EndTabItem(); 
            }
            
            if (service.State == PhotogrammetryState.AwaitingManualInput && ImGui.BeginTabItem("Manual Input")) 
            { 
                DrawManualInputUI(); 
                ImGui.EndTabItem(); 
            }
            
            if (service.Graph != null && ImGui.BeginTabItem("Image Groups")) 
            { 
                DrawImageGroupsVisualization(); 
                ImGui.EndTabItem(); 
            }
            
            if ((service.State >= PhotogrammetryState.ComputingSparseReconstruction || 
                 (service.ImageGroups != null && service.ImageGroups.Count == 1)) && 
                ImGui.BeginTabItem("Reconstruction")) 
            { 
                DrawReconstructionUI(); 
                ImGui.EndTabItem(); 
            }
            
            if (service.State >= PhotogrammetryState.ComputingSparseReconstruction || service.SparseCloud != null)
            {
                if (ImGui.BeginTabItem("Sparse Cloud"))
                {
                    if (service.SparseCloud != null)
                        DrawPointCloudView(service.SparseCloud);
                    else
                        DrawSparseCloudPreview();
                    ImGui.EndTabItem();
                }
            }
            
            if (service.SparseCloud != null || service.DenseCloud != null)
            {
                if (ImGui.BeginTabItem("Dense Cloud"))
                {
                    if (service.DenseCloud != null)
                        DrawPointCloudView(service.DenseCloud);
                    else
                        DrawDenseCloudPreview();
                    ImGui.EndTabItem();
                }
            }
            
            if (service.GeneratedMesh != null && ImGui.BeginTabItem("Mesh")) 
            { 
                DrawMeshView(); 
                ImGui.EndTabItem(); 
            }
            
            if (ImGui.BeginTabItem("GCP Management"))
            {
                DrawGCPManagementTab();
                ImGui.EndTabItem();
            }
            
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
        var imagesWithNoFeatures = _job.Service.Images.Where(img => 
            img.SiftFeatures == null || img.SiftFeatures.KeyPoints.Count < 20).ToList();

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
                ImGui.TextWrapped("Your images are in disconnected groups. Select one image from each group to link them manually.");
                
                for (int i = 0; i < imageGroups.Count; i++)
                {
                    var grp = imageGroups[i];
                    if (ImGui.TreeNode($"Group {i + 1} ({grp.Images.Count} images)###grp{i}"))
                    {
                        foreach (var img in grp.Images)
                        {
                            bool isSelected1 = _groupLinkImage1 == img;
                            bool isSelected2 = _groupLinkImage2 == img;
                            string suffix = isSelected1 ? " [Selected as Image 1]" : isSelected2 ? " [Selected as Image 2]" : "";
                            
                            ImGui.Text($"• {img.Dataset.Name}{suffix}");
                            ImGui.SameLine();
                            
                            if (_groupLinkImage1 != img)
                            {
                                if (ImGui.SmallButton($"Select 1##{img.Id}"))
                                {
                                    _groupLinkImage1 = img;
                                    _logBuffer.Add($"[{DateTime.Now:HH:mm:ss}] Selected {img.Dataset.Name} as Image 1 for manual linking");
                                }
                            }
                            
                            if (_groupLinkImage1 != null && _groupLinkImage1 != img && _groupLinkImage2 != img)
                            {
                                ImGui.SameLine();
                                if (ImGui.SmallButton($"Select 2##{img.Id}"))
                                {
                                    _groupLinkImage2 = img;
                                    _logBuffer.Add($"[{DateTime.Now:HH:mm:ss}] Selected {img.Dataset.Name} as Image 2 for manual linking");
                                }
                            }
                        }
                        ImGui.TreePop();
                    }
                }
                
                ImGui.Separator();
                
                if (_groupLinkImage1 != null || _groupLinkImage2 != null)
                {
                    ImGui.Text("Selected for Linking:");
                    if (_groupLinkImage1 != null)
                        ImGui.BulletText($"Image 1: {_groupLinkImage1.Dataset.Name}");
                    if (_groupLinkImage2 != null)
                        ImGui.BulletText($"Image 2: {_groupLinkImage2.Dataset.Name}");
                    
                    if (_groupLinkImage1 != null && _groupLinkImage2 != null)
                    {
                        if (ImGui.Button("Link Selected Images", new Vector2(200, 30)))
                        {
                            _logBuffer.Add($"[{DateTime.Now:HH:mm:ss}] Manually linking {_groupLinkImage1.Dataset.Name} and {_groupLinkImage2.Dataset.Name}");
                            _job.Service.ManuallyLinkImages(_groupLinkImage1, _groupLinkImage2);
                            _groupLinkImage1 = null;
                            _groupLinkImage2 = null;
                        }
                        ImGui.SameLine();
                        if (ImGui.Button("Clear Selection", new Vector2(150, 30)))
                        {
                            _groupLinkImage1 = null;
                            _groupLinkImage2 = null;
                        }
                    }
                }
                else
                {
                    ImGui.TextColored(new Vector4(1, 1, 0, 1), "Select one image from each group you want to link.");
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
                    {
                        // Ensure image is loaded before opening GCP editor
                        image.Dataset.Load();
                        _gcpEditor.Open(image);
                    }
                    ImGui.TreePop();
                }
            }
        }
        
        ImGui.Separator();
        ImGui.Separator();
        
        ImGui.TextColored(new Vector4(0, 1, 0, 1), "Actions:");
        
        var currentGroups = _job.Service.ImageGroups;
        bool canProceed = currentGroups.Count == 1 || imagesWithNoFeatures.Count == 0;
        
        if (currentGroups.Count > 1)
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), $"⚠ Warning: {currentGroups.Count} disconnected groups detected. Link them or discard isolated images.");
        }
        
        if (!canProceed)
        {
            ImGui.BeginDisabled();
        }
        
        if (ImGui.Button("Continue Processing", new Vector2(200, 40)))
        {
            _logBuffer.Add($"[{DateTime.Now:HH:mm:ss}] Resuming photogrammetry processing...");
            _job.Service.ContinueAfterManualInput();
        }
        ImGui.SameLine();
        ImGui.TextWrapped("Continue to camera alignment and sparse reconstruction");
        
        if (!canProceed)
        {
            ImGui.EndDisabled();
            ImGui.TextColored(new Vector4(1, 0, 0, 1), "Cannot proceed: Resolve disconnected groups first.");
        }
        
        ImGui.Separator();
        
        if (ImGui.Button("Force Continue (Skip Unmatched)", new Vector2(200, 30)))
        {
            _logBuffer.Add($"[{DateTime.Now:HH:mm:ss}] Force continuing, skipping unmatched images...");
            _job.Service.ForceContinueProcessing();
        }
        ImGui.SameLine();
        ImGui.TextWrapped("Continue with largest group only");
    }

    private void DrawReconstructionUI()
    {
        var service = _job.Service;

        ImGui.TextWrapped("Build 3D reconstruction products:");
        ImGui.Separator();

        bool hasSparse = service.SparseCloud != null;
        if (hasSparse)
            ImGui.TextColored(new Vector4(0, 1, 0, 1), $"Sparse Cloud: {service.SparseCloud.Points.Count} points");
        else
        {
            if (ImGui.Button("Build Sparse Cloud", new Vector2(-1, 30)))
                _ = service.BuildSparseCloudAsync();
        }

        ImGui.Separator();

        bool hasDense = service.DenseCloud != null;
        if (hasDense)
            ImGui.TextColored(new Vector4(0, 1, 0, 1), $"Dense Cloud: {service.DenseCloud.Points.Count} points");
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
            ImGui.TextColored(new Vector4(0, 1, 0, 1), $"Mesh: {service.GeneratedMesh.VertexCount} vertices");
        else
        {
            bool canBuild = hasSparse || hasDense;
            if (!canBuild) ImGui.BeginDisabled();
            if (ImGui.Button("Build Mesh...", new Vector2(-1, 30)))
                _showMeshDialog = true;
            if (!canBuild) ImGui.EndDisabled();
        }

        ImGui.Separator();

        bool hasTexture = hasMesh && !string.IsNullOrEmpty(service.GeneratedMesh?.TexturePath);
        if (hasTexture)
            ImGui.TextColored(new Vector4(0, 1, 0, 1), $"Texture: {Path.GetFileName(service.GeneratedMesh.TexturePath)}");
        else
        {
            bool canBuildTexture = hasMesh && service.Images?.Count > 0;
            if (!canBuildTexture) ImGui.BeginDisabled();
            if (ImGui.Button("Build Texture...", new Vector2(-1, 30)))
                _showTextureDialog = true;
            if (!canBuildTexture) ImGui.EndDisabled();
        }
    }

    private void DrawPointCloudView(PhotogrammetryPointCloud cloud)
    {
        ImGui.Text($"Points: {cloud.Points.Count}");
        ImGui.Text($"Type: {(cloud.IsDense ? "Dense" : "Sparse")}");
        ImGui.Text($"Bounds: {cloud.BoundingBoxMin} to {cloud.BoundingBoxMax}");
        
        ImGui.Spacing();
        
        // Visualization controls
        ImGui.Text("Visualization:");
        ImGui.SameLine();
        if (ImGui.RadioButton("XY (Top)", ref _pointCloudProjectionMode, 0)) { }
        ImGui.SameLine();
        if (ImGui.RadioButton("XZ (Front)", ref _pointCloudProjectionMode, 1)) { }
        ImGui.SameLine();
        if (ImGui.RadioButton("YZ (Side)", ref _pointCloudProjectionMode, 2)) { }
        
        ImGui.Checkbox("Depth Coloring", ref _pointCloudUseDepthColor);
        
        ImGui.Spacing();

        ImGui.BeginChild("Preview", Vector2.Zero, ImGuiChildFlags.Border);
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var size = ImGui.GetContentRegionAvail();

        if (size.X > 10 && size.Y > 10 && cloud.Points.Count > 0)
        {
            var range = cloud.BoundingBoxMax - cloud.BoundingBoxMin;
            
            // Determine which axes to use based on projection mode
            float rangeU, rangeV, rangeDepth;
            string depthAxisName;
            
            switch (_pointCloudProjectionMode)
            {
                case 0: // XY view (Top)
                    rangeU = range.X;
                    rangeV = range.Y;
                    rangeDepth = range.Z;
                    depthAxisName = "Z";
                    break;
                case 1: // XZ view (Front)
                    rangeU = range.X;
                    rangeV = range.Z;
                    rangeDepth = range.Y;
                    depthAxisName = "Y";
                    break;
                case 2: // YZ view (Side)
                default:
                    rangeU = range.Y;
                    rangeV = range.Z;
                    rangeDepth = range.X;
                    depthAxisName = "X";
                    break;
            }
            
            float scale = Math.Min(size.X / Math.Max(rangeU, 0.01f), size.Y / Math.Max(rangeV, 0.01f)) * 0.9f;

            // Sample points for display (max 5000 for performance)
            int step = Math.Max(1, cloud.Points.Count / 5000);
            
            for (int i = 0; i < cloud.Points.Count; i += step)
            {
                var pt = cloud.Points[i];
                
                // Get 2D projection coordinates based on mode
                Vector2 p2d;
                float depth;
                
                switch (_pointCloudProjectionMode)
                {
                    case 0: // XY view
                        p2d = new Vector2(
                            pt.Position.X - cloud.BoundingBoxMin.X, 
                            pt.Position.Y - cloud.BoundingBoxMin.Y);
                        depth = pt.Position.Z - cloud.BoundingBoxMin.Z;
                        break;
                    case 1: // XZ view
                        p2d = new Vector2(
                            pt.Position.X - cloud.BoundingBoxMin.X, 
                            pt.Position.Z - cloud.BoundingBoxMin.Z);
                        depth = pt.Position.Y - cloud.BoundingBoxMin.Y;
                        break;
                    case 2: // YZ view
                    default:
                        p2d = new Vector2(
                            pt.Position.Y - cloud.BoundingBoxMin.Y, 
                            pt.Position.Z - cloud.BoundingBoxMin.Z);
                        depth = pt.Position.X - cloud.BoundingBoxMin.X;
                        break;
                }
                
                var screen = pos + size / 2 + new Vector2(p2d.X, -p2d.Y) * scale;
                
                // Choose color: depth-based or original point color
                Vector4 color;
                if (_pointCloudUseDepthColor && rangeDepth > 0.01f)
                {
                    // Map depth to color: blue (near) -> cyan -> green -> yellow -> red (far)
                    float t = Math.Clamp(depth / rangeDepth, 0f, 1f);
                    color = GetDepthColor(t);
                }
                else
                {
                    // Use original point color
                    color = new Vector4(pt.Color, 1);
                }
                
                uint col = ImGui.ColorConvertFloat4ToU32(color);
                drawList.AddCircleFilled(screen, 2, col);
            }
            
            // Draw axis labels
            if (_pointCloudUseDepthColor && rangeDepth > 0.01f)
            {
                var labelPos = pos + new Vector2(10, size.Y - 60);
                drawList.AddText(labelPos, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1)), 
                    $"Depth ({depthAxisName}): {rangeDepth:F2}m");
                
                // Draw color scale
                float barWidth = 200;
                float barHeight = 20;
                var barPos = pos + new Vector2(10, size.Y - 35);
                
                for (int x = 0; x < (int)barWidth; x++)
                {
                    float t = x / barWidth;
                    uint col = ImGui.ColorConvertFloat4ToU32(GetDepthColor(t));
                    drawList.AddLine(
                        barPos + new Vector2(x, 0), 
                        barPos + new Vector2(x, barHeight), 
                        col, 2);
                }
                
                drawList.AddText(barPos + new Vector2(0, barHeight + 2), 
                    ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1)), "Near");
                drawList.AddText(barPos + new Vector2(barWidth - 20, barHeight + 2), 
                    ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1)), "Far");
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
    
    /// <summary>
    /// Get color for depth visualization: blue (near) -> cyan -> green -> yellow -> red (far)
    /// </summary>
    private Vector4 GetDepthColor(float t)
    {
        // Use a perceptually uniform color ramp
        if (t < 0.25f)
        {
            // Blue to Cyan
            float s = t / 0.25f;
            return new Vector4(0, s, 1, 1);
        }
        else if (t < 0.5f)
        {
            // Cyan to Green
            float s = (t - 0.25f) / 0.25f;
            return new Vector4(0, 1, 1 - s, 1);
        }
        else if (t < 0.75f)
        {
            // Green to Yellow
            float s = (t - 0.5f) / 0.25f;
            return new Vector4(s, 1, 0, 1);
        }
        else
        {
            // Yellow to Red
            float s = (t - 0.75f) / 0.25f;
            return new Vector4(1, 1 - s, 0, 1);
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

    private void DrawSparseCloudPreview()
    {
        ImGui.TextWrapped("The sparse point cloud will be generated from matched features between images.");
        ImGui.Separator();
        
        var service = _job.Service;
        var totalFeatures = service.Images.Sum(img => img.SiftFeatures?.KeyPoints.Count ?? 0);
        var totalMatches = 0;
        
        if (service.Graph != null)
        {
            foreach (var img in service.Images)
            {
                if (service.Graph.TryGetNeighbors(img.Id, out var neighbors))
                {
                    totalMatches += neighbors.Sum(n => n.Matches?.Count ?? 0);
                }
            }
        }
        
        ImGui.Text($"Total Features Detected: {totalFeatures:N0}");
        ImGui.Text($"Total Feature Matches: {totalMatches:N0}");
        ImGui.Text($"Estimated Sparse Points: {totalMatches / 2:N0} - {totalMatches:N0}");
        
        ImGui.Separator();
        
        if (service.State >= PhotogrammetryState.ComputingSparseReconstruction)
        {
            if (ImGui.Button("Build Sparse Cloud", new Vector2(200, 40)))
            {
                _logBuffer.Add($"[{DateTime.Now:HH:mm:ss}] Building sparse point cloud...");
                _ = service.BuildSparseCloudAsync();
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "Complete camera alignment first to build sparse cloud.");
        }
    }
    
    private void DrawDenseCloudPreview()
    {
        ImGui.TextWrapped("The dense point cloud will be generated by densifying the sparse cloud using multi-view stereo.");
        ImGui.Separator();
        
        if (_job.Service.SparseCloud != null)
        {
            ImGui.Text($"Sparse Cloud Points: {_job.Service.SparseCloud.Points.Count:N0}");
            ImGui.Text($"Estimated Dense Points: {_job.Service.SparseCloud.Points.Count * 50:N0} - {_job.Service.SparseCloud.Points.Count * 200:N0}");
            
            ImGui.Separator();
            
            if (ImGui.Button("Build Dense Cloud...", new Vector2(200, 40)))
            {
                _showDenseCloudDialog = true;
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "Build sparse cloud first.");
        }
    }
    
    private void DrawImageGroupsVisualization()
    {
        var groups = _job.Service.ImageGroups;
        
        ImGui.Text($"Total Image Groups: {groups.Count}");
        ImGui.Text($"Total Images: {_job.Service.Images.Count}");
        ImGui.Separator();
        
        ImGui.BeginChild("GroupsVis", new Vector2(-1, 300), ImGuiChildFlags.Border);
        
        var drawList = ImGui.GetWindowDrawList();
        var startPos = ImGui.GetCursorScreenPos();
        var size = ImGui.GetContentRegionAvail();
        
        if (groups.Count > 0)
        {
            float groupWidth = size.X / groups.Count;
            
            for (int i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                var groupX = startPos.X + i * groupWidth;
                
                var boxMin = new Vector2(groupX + 10, startPos.Y + 10);
                var boxMax = new Vector2(groupX + groupWidth - 10, startPos.Y + 290);
                
                uint color = group.Images.Count > 1 
                    ? ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0.7f, 0, 0.3f))
                    : ImGui.ColorConvertFloat4ToU32(new Vector4(0.7f, 0, 0, 0.3f));
                
                drawList.AddRectFilled(boxMin, boxMax, color);
                drawList.AddRect(boxMin, boxMax, ImGui.ColorConvertFloat4ToU32(Vector4.One));
                
                var textPos = new Vector2(groupX + 15, startPos.Y + 15);
                drawList.AddText(textPos, ImGui.ColorConvertFloat4ToU32(Vector4.One), $"Group {i + 1}");
                drawList.AddText(new Vector2(textPos.X, textPos.Y + 20), 
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.8f, 0.8f, 0.8f, 1)), 
                    $"{group.Images.Count} images");
                
                float y = textPos.Y + 45;
                foreach (var img in group.Images.Take(8))
                {
                    drawList.AddText(new Vector2(textPos.X, y), 
                        ImGui.ColorConvertFloat4ToU32(new Vector4(0.7f, 0.7f, 0.7f, 1)), 
                        img.Dataset.Name.Length > 15 ? img.Dataset.Name.Substring(0, 15) + "..." : img.Dataset.Name);
                    y += 20;
                }
                
                if (group.Images.Count > 8)
                {
                    drawList.AddText(new Vector2(textPos.X, y), 
                        ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 0.5f, 0.5f, 1)), 
                        $"... +{group.Images.Count - 8} more");
                }
            }
        }
        
        ImGui.EndChild();
        
        ImGui.Separator();
        
        if (groups.Count > 1)
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), 
                "⚠ Warning: Multiple disconnected groups detected. Use Manual Input tab to link groups.");
        }
        else if (groups.Count == 1)
        {
            ImGui.TextColored(new Vector4(0, 1, 0, 1), 
                "All images connected in a single group.");
        }
    }
    
    private void DrawGCPManagementTab()
    {
        ImGui.TextWrapped("Manage Ground Control Points across all images for georeferencing.");
        ImGui.Separator();
        
        bool georefEnabled = _job.Service.EnableGeoreferencing;
        if (ImGui.Checkbox("Enable Georeferencing", ref georefEnabled))
        {
            _job.Service.EnableGeoreferencing = georefEnabled;
        }

        if (!_job.Service.EnableGeoreferencing)
        {
            ImGui.TextWrapped("Georeferencing is disabled. The reconstruction will use arbitrary coordinates.");
            return;
        }

        ImGui.Separator();

        var allGCPs = new Dictionary<string, List<(PhotogrammetryImage img, GroundControlPoint gcp)>>();
        
        foreach (var img in _job.Service.Images)
        {
            foreach (var gcp in img.GroundControlPoints)
            {
                if (!allGCPs.ContainsKey(gcp.Name))
                    allGCPs[gcp.Name] = new List<(PhotogrammetryImage, GroundControlPoint)>();
                allGCPs[gcp.Name].Add((img, gcp));
            }
        }
        
        ImGui.Text($"Total Unique GCPs: {allGCPs.Count}");
        ImGui.Text($"Total GCP Observations: {_job.Service.Images.Sum(img => img.GroundControlPoints.Count)}");
        
        ImGui.Separator();
        
        if (ImGui.BeginTable("GCPTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("GCP Name", ImGuiTableColumnFlags.WidthFixed, 150);
            ImGui.TableSetupColumn("Observations", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("World Coordinates", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableHeadersRow();
            
            foreach (var kvp in allGCPs)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(kvp.Key);
                
                ImGui.TableNextColumn();
                ImGui.Text($"{kvp.Value.Count} images");
                
                ImGui.TableNextColumn();
                var firstGcp = kvp.Value.First().gcp;
                if (firstGcp.IsConfirmed && firstGcp.WorldPosition.HasValue)
                {
                    ImGui.Text($"X: {firstGcp.WorldPosition.Value.X:F2}, Y: {firstGcp.WorldPosition.Value.Y:F2}, Z: {firstGcp.WorldPosition.Value.Z:F2}");
                }
                else
                {
                    ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "Not set");
                }
                
                ImGui.TableNextColumn();
                if (ImGui.SmallButton($"Edit##{kvp.Key}"))
                {
                    var image = kvp.Value.First().img;
                    // Ensure image is loaded before opening GCP editor
                    image.Dataset.Load();
                    _gcpEditor.Open(image);
                }
            }
            
            ImGui.EndTable();
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
                _meshOptions.FaceCount = Math.Max(1000, faceCount);
            
            ImGui.SameLine();
            // Help marker
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35.0f);
                ImGui.TextUnformatted("Target number of triangles in the generated mesh. Higher values preserve more detail but increase processing time.");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }

            bool simplify = _meshOptions.SimplifyMesh;
            if (ImGui.Checkbox("Simplify", ref simplify))
                _meshOptions.SimplifyMesh = simplify;

            bool smooth = _meshOptions.SmoothNormals;
            if (ImGui.Checkbox("Smooth Normals", ref smooth))
                _meshOptions.SmoothNormals = smooth;

            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Mesh Optimization");
            
            bool optimize = _meshOptions.OptimizeMesh;
            if (ImGui.Checkbox("Optimize Mesh Quality", ref optimize))
                _meshOptions.OptimizeMesh = optimize;
            
            ImGui.SameLine();
            // Help marker
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35.0f);
                ImGui.TextUnformatted("Improves mesh quality using Gmsh-inspired algorithms:\n" +
                    "• Edge swapping (Delaunay refinement)\n" +
                    "• Laplacian smoothing\n" +
                    "• Vertex optimization\n" +
                    "• Sliver removal\n" +
                    "Recommended for production-quality meshes.");
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }

            if (_meshOptions.OptimizeMesh)
            {
                ImGui.Indent();
                
                int quality = (int)_meshOptions.OptimizationQuality;
                if (ImGui.Combo("Quality Profile", ref quality, "Fast (Preview)\0Balanced (Recommended)\0High Quality\0"))
                    _meshOptions.OptimizationQuality = (OptimizationQuality)quality;
                
                // Show profile details
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.7f, 1.0f));
                switch (_meshOptions.OptimizationQuality)
                {
                    case OptimizationQuality.Fast:
                        ImGui.TextWrapped("• 1 optimization pass\n• Basic smoothing\n• ~10-20% processing overhead\n• Good for previews");
                        break;
                    case OptimizationQuality.Balanced:
                        ImGui.TextWrapped("• 2 optimization passes\n• Enhanced smoothing & vertex optimization\n• ~30-40% processing overhead\n• Recommended for most cases");
                        break;
                    case OptimizationQuality.High:
                        ImGui.TextWrapped("• 3 optimization passes\n• Maximum quality algorithms\n• ~50-70% processing overhead\n• Best for final deliverables");
                        break;
                }
                ImGui.PopStyleColor();
                
                ImGui.Unindent();
            }

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

        if (_showTextureDialog)
        {
            ImGui.OpenPopup("Texture Options");
            _showTextureDialog = false;
        }

        bool isTexturePopupOpen = true;
        if (ImGui.BeginPopupModal("Texture Options", ref isTexturePopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Configure mesh texturing:");
            ImGui.Separator();

            int texSizeIdx = _textureOptions.TextureSize switch
            {
                512 => 0,
                1024 => 1,
                2048 => 2,
                4096 => 3,
                _ => 1
            };

            if (ImGui.Combo("Texture Resolution", ref texSizeIdx, "512 x 512\01024 x 1024\02048 x 2048\04096 x 4096\0"))
            {
                _textureOptions.TextureSize = texSizeIdx switch
                {
                    0 => 512,
                    1 => 1024,
                    2 => 2048,
                    _ => 4096
                };
            }

            ImGui.TextWrapped("Higher resolution preserves more detail but increases file size and processing time.");
            
            ImGui.Separator();
            ImGui.Checkbox("Add to project", ref _addToProject);

            if (ImGui.Button("Build", new Vector2(120, 0)))
            {
                string texturePath = Path.ChangeExtension(_job.Service.GeneratedMesh.FilePath, "_textured.obj");
                _ = _job.Service.BuildTextureAsync(_textureOptions, texturePath);
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0))) ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }

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

                foreach (var pt in cloud.Points)
                {
                    writer.Write(pt.Position.X);
                    writer.Write(pt.Position.Y);
                    writer.Write(pt.Position.Z);

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
