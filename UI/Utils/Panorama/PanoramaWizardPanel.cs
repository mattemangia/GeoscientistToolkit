// GeoscientistToolkit/UI/Panorama/PanoramaWizardPanel.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Business.Panorama;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util; // Added for the static Logger
using ImGuiNET;
using StbImageSharp;
using Veldrid;

namespace GeoscientistToolkit.UI.Panorama;

/// <summary>
/// A self-contained UI component for manually selecting control points between two images.
/// This class uses Veldrid for texture management and rendering within an ImGui modal.
/// </summary>
internal class ManualLinkEditor : IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ResourceFactory _resourceFactory;
    // CORRECTED: Use the project's ImGuiController
    private readonly ImGuiController _imGuiController;

    private struct VeldridTextureBinding : IDisposable
    {
        public readonly IntPtr ImGuiBinding;
        private readonly Texture _texture;
        private readonly TextureView _textureView;
        // CORRECTED: Use the project's ImGuiController
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
    private PanoramaImage _image1;
    private PanoramaImage _image2;
    private VeldridTextureBinding? _binding1;
    private VeldridTextureBinding? _binding2;

    private readonly List<(Vector2 P1, Vector2 P2)> _points = new();
    private Vector2? _pendingPoint;

    private Vector2 _pan1 = Vector2.Zero;
    private float _zoom1 = 1.0f;
    private Vector2 _pan2 = Vector2.Zero;
    private float _zoom2 = 1.0f;

    public Action<PanoramaImage, PanoramaImage, List<(Vector2 P1, Vector2 P2)>> OnConfirm;

    // CORRECTED: Constructor now uses the project's ImGuiController
    public ManualLinkEditor(GraphicsDevice graphicsDevice, ImGuiController imGuiController)
    {
        _graphicsDevice = graphicsDevice;
        _resourceFactory = graphicsDevice.ResourceFactory;
        _imGuiController = imGuiController;
    }

    public void Open(PanoramaImage image1, PanoramaImage image2)
    {
        _image1 = image1;
        _image2 = image2;
        
        DisposeBindings();
        _binding1 = CreateTextureBindingForImage(image1.Dataset);
        _binding2 = CreateTextureBindingForImage(image2.Dataset);
        
        _points.Clear();
        _pendingPoint = null;
        _pan1 = _pan2 = Vector2.Zero;
        _zoom1 = _zoom2 = 1.0f;
        
        IsOpen = true;
        ImGui.OpenPopup("Manual Link Editor");
    }
    
    public void Draw()
    {
        if (!IsOpen) return;
        
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(1400, 800), ImGuiCond.Appearing);

        if (ImGui.BeginPopupModal("Manual Link Editor", ref IsOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize))
        {
            if (_binding1 == null || _binding2 == null)
            {
                ImGui.TextColored(new Vector4(1,0,0,1), "Error: Could not load image textures. Check file paths and format.");
                if (ImGui.Button("Close")) IsOpen = false;
                ImGui.EndPopup();
                return;
            }

            ImGui.Text("Select at least 4 corresponding points on both images. Use mouse wheel to zoom and middle mouse button to pan.");
            ImGui.Separator();

            ImGui.Columns(3, "EditorLayout", true);
            DrawImagePanel(1, _image1.Dataset, _binding1.Value, ref _pan1, ref _zoom1);
            ImGui.NextColumn();
            DrawImagePanel(2, _image2.Dataset, _binding2.Value, ref _pan2, ref _zoom2);
            ImGui.NextColumn();
            DrawControlsPanel();
            ImGui.NextColumn();
            ImGui.Columns(1);
            ImGui.Separator();
            
            bool canConfirm = _points.Count >= 4;
            if (!canConfirm) ImGui.BeginDisabled();
            if (ImGui.Button("Confirm Links", new Vector2(120, 0)))
            {
                OnConfirm?.Invoke(_image1, _image2, new List<(Vector2 P1, Vector2 P2)>(_points));
                IsOpen = false;
            }
            if (!canConfirm) ImGui.EndDisabled();

            if(ImGui.IsItemHovered() && !canConfirm) ImGui.SetTooltip("At least 4 point pairs are required.");
            
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0))) IsOpen = false;

            ImGui.EndPopup();
        }
        
        if (!IsOpen) DisposeBindings();
    }

    private void DrawImagePanel(int id, Data.Image.ImageDataset dataset, VeldridTextureBinding binding, ref Vector2 pan, ref float zoom)
    {
        ImGui.Text(dataset.Name);
        ImGui.BeginChild($"ImagePanel{id}", Vector2.Zero, ImGuiChildFlags.Border, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        var panelTopLeft = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        
        if (ImGui.IsWindowHovered())
        {
            var io = ImGui.GetIO();
            if (io.MouseWheel != 0)
            {
                var mousePosInPanel = io.MousePos - panelTopLeft;
                var oldZoom = zoom;
                zoom *= (io.MouseWheel > 0) ? 1.1f : 1 / 1.1f;
                zoom = Math.Clamp(zoom, 0.1f, 10.0f);
                pan = mousePosInPanel - zoom / oldZoom * (mousePosInPanel - pan);
            }
            if (ImGui.IsMouseDown(ImGuiMouseButton.Middle)) pan += io.MouseDelta;
        }
        
        var imgTopLeft = panelTopLeft + pan;
        var imgBottomRight = imgTopLeft + new Vector2(dataset.Width, dataset.Height) * zoom;
        drawList.AddImage(binding.ImGuiBinding, imgTopLeft, imgBottomRight);

        foreach (var (p, index) in _points.Select((p, i) => (p, i)))
        {
            var point = (id == 1) ? p.P1 : p.P2;
            var screenPos = panelTopLeft + pan + point * zoom;
            drawList.AddText(screenPos + new Vector2(8, -8), 0xFFFFFFFF, (index + 1).ToString());
            drawList.AddCircleFilled(screenPos, 5, 0xFF00FF00, 12);
            drawList.AddCircle(screenPos, 5, 0xFF000000, 12, 2f);
        }

        if (_pendingPoint.HasValue && id == 1)
        {
             var screenPos = panelTopLeft + pan + _pendingPoint.Value * zoom;
             drawList.AddCircleFilled(screenPos, 5, 0xFF00FFFF, 12);
        }

        if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            var imageCoords = (ImGui.GetMousePos() - (panelTopLeft + pan)) / zoom;
            if (imageCoords.X >= 0 && imageCoords.Y >= 0 && imageCoords.X <= dataset.Width && imageCoords.Y <= dataset.Height)
            {
                if (id == 1) _pendingPoint = imageCoords;
                else if (id == 2 && _pendingPoint.HasValue)
                {
                    _points.Add((_pendingPoint.Value, imageCoords));
                    _pendingPoint = null;
                }
            }
        }
        ImGui.EndChild();
    }

    private void DrawControlsPanel()
    {
        ImGui.Text("Control Points");
        if (ImGui.BeginChild("PointsList", new Vector2(0, -50), ImGuiChildFlags.Border))
        {
            if (_pendingPoint.HasValue) ImGui.TextColored(new Vector4(1,1,0,1), $"Pair {_points.Count + 1}: Click on right image...");
            else ImGui.Text($"Pair {_points.Count + 1}: Click on left image...");
            ImGui.Separator();

            int pointToRemove = -1;
            for (int i = 0; i < _points.Count; i++)
            {
                var (p1, p2) = _points[i];
                ImGui.Text($"#{i+1}: ({p1.X:F0},{p1.Y:F0}) -> ({p2.X:F0},{p2.Y:F0})");
                ImGui.SameLine();
                if (ImGui.SmallButton($"X##{i}")) pointToRemove = i;
            }
            if (pointToRemove != -1) _points.RemoveAt(pointToRemove);
        }
        ImGui.EndChild();
    }

    private VeldridTextureBinding? CreateTextureBindingForImage(Data.Image.ImageDataset dataset)
    {
        // CORRECTED: Check for pre-loaded image data, same as the fix in PhotogrammetryWizardPanel.
        if (dataset?.ImageData == null)
        {
            Logger.LogError($"Cannot create texture binding: Image data not loaded for {dataset?.Name ?? "null"}");
            return null;
        }

        var texture = _resourceFactory.CreateTexture(TextureDescription.Texture2D(
            (uint)dataset.Width, (uint)dataset.Height, 1, 1,
            PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));

        // CORRECTED: Use the pre-loaded RGBA data directly, removing file I/O and the 'unsafe' block.
        _graphicsDevice.UpdateTexture(texture, dataset.ImageData, 0, 0, 0, (uint)dataset.Width, (uint)dataset.Height, 1, 0, 0);

        var textureView = _resourceFactory.CreateTextureView(texture);
        var imGuiBinding = _imGuiController.GetOrCreateImGuiBinding(_resourceFactory, textureView);

        return new VeldridTextureBinding(texture, textureView, imGuiBinding, _imGuiController);
    }
    
    private void DisposeBindings()
    {
        _binding1?.Dispose();
        _binding2?.Dispose();
        _binding1 = null;
        _binding2 = null;
    }

    public void Dispose() => DisposeBindings();
}


public class PanoramaWizardPanel : BasePanel
{
    private readonly PanoramaStitchJob _job;
    private readonly List<string> _logBuffer = new();
    private readonly ImGuiExportFileDialog _exportFileDialog;
    private readonly ManualLinkEditor _manualLinkEditor;

    private PanoramaImage _imageToDiscard;
    private PanoramaImage _groupLinkImage1;
    private PanoramaImage _groupLinkImage2;

    public bool IsOpen { get; private set; }
    public string Title { get; }

    // CORRECTED: Constructor now uses the project's ImGuiController
    public PanoramaWizardPanel(DatasetGroup imageGroup, GraphicsDevice graphicsDevice, ImGuiController imGuiController) 
        : base($"Panorama Wizard: {imageGroup.Name}", new Vector2(800, 600))
    {
        Title = $"Panorama Wizard: {imageGroup.Name}";
       
        _job = new PanoramaStitchJob(imageGroup);
        _job.Service.StartProcessingAsync();
        
        _exportFileDialog = new ImGuiExportFileDialog("panoramaExport", "Export Panorama");
        _exportFileDialog.SetExtensions(new ImGuiExportFileDialog.ExtensionOption(".png", "PNG Image"), new ImGuiExportFileDialog.ExtensionOption(".jpg", "JPEG Image"));
        
        _manualLinkEditor = new ManualLinkEditor(graphicsDevice, imGuiController);
        _manualLinkEditor.OnConfirm = (img1, img2, points) =>
        {
            Logger.Log($"Confirmed {points.Count} manual links for {img1.Dataset.Name} and {img2.Dataset.Name}.");
            // CORRECTED: Call the service method to add the manual link and re-run the analysis.
            _job.Service.AddManualLinkAndRecompute(img1, img2, points);
        };
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
            _manualLinkEditor.Dispose();
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
        if (_exportFileDialog.Submit())
        {
            if (!string.IsNullOrEmpty(_exportFileDialog.SelectedPath))
            {
                _job.Service.StartBlendingAsync(_exportFileDialog.SelectedPath);
            }
        }
        _manualLinkEditor.Draw();

        var service = _job.Service;
        ImGui.ProgressBar(service.Progress, new Vector2(-1, 0), service.StatusMessage);

        if (ImGui.BeginTabBar("WizardTabs"))
        {
            if (ImGui.BeginTabItem("Processing Log")) { DrawLog(); ImGui.EndTabItem(); }
            if (service.State == PanoramaState.AwaitingManualInput && ImGui.BeginTabItem("Manual Correction")) { DrawManualInputUI(); ImGui.EndTabItem(); }
            if ((service.State >= PanoramaState.ReadyForPreview) && ImGui.BeginTabItem("Preview & Stitch")) { DrawPreviewUI(); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }

        if (service.State == PanoramaState.Failed) ImGui.TextColored(new Vector4(1,0,0,1), "Process failed. Check log.");
        if (service.State == PanoramaState.Completed) ImGui.TextColored(new Vector4(0,1,0,1), "Panorama created!");
    }

    private void UpdateLogs()
    {
        while(_job.Service.Logs.TryDequeue(out var log))
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
        var stitchGroups = _job.Service.StitchGroups;
        var imagesWithNoFeatures = _job.Service.Images.Where(img => img.Features.KeyPoints.Count < 20).ToList();

        if (imagesWithNoFeatures.Any())
        {
            if (ImGui.CollapsingHeader($"Images with too few features ({imagesWithNoFeatures.Count})###NoFeatureHeader", ImGuiTreeNodeFlags.DefaultOpen))
            {
                foreach (var image in imagesWithNoFeatures)
                {
                    ImGui.BulletText($"{image.Dataset.Name}: Discard this image.");
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"Discard##{image.Id}")) _imageToDiscard = image;
                }
            }
        }
        if (stitchGroups.Count > 1)
        {
             if (ImGui.CollapsingHeader($"Unconnected Image Groups ({stitchGroups.Count})###GroupHeader", ImGuiTreeNodeFlags.DefaultOpen))
             {
                ImGui.TextWrapped("Select one image from two different groups to create a manual link between them.");
                ImGui.Columns(2, "GroupSelectors", false);
                DrawGroupSelectionUI(stitchGroups, 1, ref _groupLinkImage1, ref _groupLinkImage2);
                ImGui.NextColumn();
                DrawGroupSelectionUI(stitchGroups, 2, ref _groupLinkImage2, ref _groupLinkImage1);
                ImGui.NextColumn();
                ImGui.Columns(1);
                
                bool canLink = _groupLinkImage1 != null && _groupLinkImage2 != null;
                if (!canLink) ImGui.BeginDisabled();
                if (ImGui.Button("Create Manual Link...")) _manualLinkEditor.Open(_groupLinkImage1, _groupLinkImage2);
                if (!canLink) ImGui.EndDisabled();
             }
        }
    }
    
    private void DrawGroupSelectionUI(List<StitchGroup> groups, int id, ref PanoramaImage selected, ref PanoramaImage otherSelected)
    {
        ImGui.BeginChild($"GroupList{id}", Vector2.Zero, ImGuiChildFlags.Border);
        foreach(var group in groups)
        {
            bool isOtherGroup = otherSelected == null || !group.Images.Contains(otherSelected);
            if (!isOtherGroup) ImGui.BeginDisabled();
            foreach (var image in group.Images)
            {
                if (ImGui.Selectable(image.Dataset.Name, selected == image)) selected = image;
            }
            if (!isOtherGroup) ImGui.EndDisabled();
            ImGui.Separator();
        }
        ImGui.EndChild();
    }

    private void DrawPreviewUI()
{
    ImGui.TextWrapped("All images are linked. Preview shows the aligned images. Click 'Stitch Panorama' to begin the final blending process.");
    
    ImGui.BeginChild("PreviewRegion", new Vector2(-1, ImGui.GetContentRegionAvail().Y - 100), ImGuiChildFlags.Border);
    
    var drawList = ImGui.GetWindowDrawList();
    var p0 = ImGui.GetCursorScreenPos();
    var availSize = ImGui.GetContentRegionAvail();
    
    // Draw background
    drawList.AddRectFilled(p0, p0 + availSize, ImGui.GetColorU32(ImGuiCol.FrameBg));
    
    // Compute preview bounds
    var components = _job.Service.StitchGroups;
    if (components.Count > 0 && components[0].Images.Count > 0)
    {
        var mainGroup = components[0];
        
        // Simple preview: show image boundaries as overlapping rectangles
        float scale = 0.1f; // Scale down for preview
        var offset = new Vector2(availSize.X / 2, availSize.Y / 2);
        
        foreach (var image in mainGroup.Images)
        {
            // Draw a rectangle for each image showing its relative position
            var imgSize = new Vector2(image.Dataset.Width * scale, image.Dataset.Height * scale);
            var imgPos = p0 + offset - imgSize / 2;
            
            // Draw semi-transparent filled rect
            uint color = ImGui.GetColorU32(new Vector4(0.3f, 0.5f, 0.7f, 0.3f));
            drawList.AddRectFilled(imgPos, imgPos + imgSize, color);
            
            // Draw border
            drawList.AddRect(imgPos, imgPos + imgSize, ImGui.GetColorU32(ImGuiCol.Text), 0, ImDrawFlags.None, 1);
            
            // Draw image name
            drawList.AddText(imgPos + new Vector2(5, 5), ImGui.GetColorU32(ImGuiCol.Text), image.Dataset.Name);
        }
        
        // Show statistics
        ImGui.SetCursorScreenPos(p0 + new Vector2(10, availSize.Y - 30));
        ImGui.Text($"Images: {mainGroup.Images.Count} | Estimated size: {_job.Service.GetMemoryRequirementString()}");
    }
    else
    {
        ImGui.SetCursorScreenPos(p0 + new Vector2(10, 10));
        ImGui.Text("No preview available - ensure images are properly linked.");
    }
    
    ImGui.EndChild();
    ImGui.Separator();

    bool isBusy = _job.Service.State == PanoramaState.Blending || _job.Service.State == PanoramaState.Completed;
    if (isBusy) ImGui.BeginDisabled();
    if (ImGui.Button("Stitch Panorama", new Vector2(-1, 40)))
    {
        _exportFileDialog.Open($"{_job.ImageGroup.Name}_panorama");
    }
    if (isBusy) ImGui.EndDisabled();
}
}