// GeoscientistToolkit/UI/Panorama/PanoramaWizardPanel.cs

using System.Numerics;
using GeoscientistToolkit.Business.Panorama;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using Veldrid;

namespace GeoscientistToolkit.UI.Panorama;

/// <summary>
///     A self-contained UI component for manually selecting control points between two images.
///     This class uses Veldrid for texture management and rendering within an ImGui modal.
/// </summary>
internal class ManualLinkEditor : IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ImGuiController _imGuiController;
    private readonly List<(Vector2 P1, Vector2 P2)> _points = new();
    private readonly ResourceFactory _resourceFactory;
    private VeldridTextureBinding? _binding1;
    private VeldridTextureBinding? _binding2;
    private PanoramaImage _image1;
    private PanoramaImage _image2;

    private Vector2 _pan1 = Vector2.Zero;
    private Vector2 _pan2 = Vector2.Zero;
    private Vector2? _pendingPoint;
    private float _zoom1 = 1.0f;
    private float _zoom2 = 1.0f;

    public bool IsOpen;

    public Action<PanoramaImage, PanoramaImage, List<(Vector2 P1, Vector2 P2)>> OnConfirm;
    
    public ManualLinkEditor(GraphicsDevice graphicsDevice, ImGuiController imGuiController)
    {
        _graphicsDevice = graphicsDevice;
        _resourceFactory = graphicsDevice.ResourceFactory;
        _imGuiController = imGuiController;
    }

    public void Dispose()
    {
        DisposeBindings();
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

        if (ImGui.BeginPopupModal("Manual Link Editor", ref IsOpen,
                ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize))
        {
            if (_binding1 == null || _binding2 == null)
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1),
                    "Error: Could not load image textures. Check file paths and format.");
                if (ImGui.Button("Close")) IsOpen = false;
                ImGui.EndPopup();
                return;
            }

            ImGui.Text(
                "Select at least 4 corresponding points on both images. Use mouse wheel to zoom and middle mouse button to pan.");
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

            var canConfirm = _points.Count >= 4;
            if (!canConfirm) ImGui.BeginDisabled();
            if (ImGui.Button("Confirm Links", new Vector2(120, 0)))
            {
                // UI change must happen before invoking the action that might change the parent's state
                IsOpen = false;
                OnConfirm?.Invoke(_image1, _image2, new List<(Vector2 P1, Vector2 P2)>(_points));
            }

            if (!canConfirm) ImGui.EndDisabled();

            if (ImGui.IsItemHovered() && !canConfirm) ImGui.SetTooltip("At least 4 point pairs are required.");

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0))) IsOpen = false;

            ImGui.EndPopup();
        }

        if (!IsOpen) DisposeBindings();
    }

    private void DrawImagePanel(int id, ImageDataset dataset, VeldridTextureBinding binding, ref Vector2 pan,
        ref float zoom)
    {
        ImGui.Text(dataset.Name);
        ImGui.BeginChild($"ImagePanel{id}", Vector2.Zero, ImGuiChildFlags.Border,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        var panelTopLeft = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        if (ImGui.IsWindowHovered())
        {
            var io = ImGui.GetIO();
            if (io.MouseWheel != 0)
            {
                var mousePosInPanel = io.MousePos - panelTopLeft;
                var oldZoom = zoom;
                zoom *= io.MouseWheel > 0 ? 1.1f : 1 / 1.1f;
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
            var point = id == 1 ? p.P1 : p.P2;
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
            if (imageCoords.X >= 0 && imageCoords.Y >= 0 && imageCoords.X <= dataset.Width &&
                imageCoords.Y <= dataset.Height)
            {
                if (id == 1)
                {
                    _pendingPoint = imageCoords;
                }
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
            if (_pendingPoint.HasValue)
                ImGui.TextColored(new Vector4(1, 1, 0, 1), $"Pair {_points.Count + 1}: Click on right image...");
            else ImGui.Text($"Pair {_points.Count + 1}: Click on left image...");
            ImGui.Separator();

            var pointToRemove = -1;
            for (var i = 0; i < _points.Count; i++)
            {
                var (p1, p2) = _points[i];
                ImGui.Text($"#{i + 1}: ({p1.X:F0},{p1.Y:F0}) -> ({p2.X:F0},{p2.Y:F0})");
                ImGui.SameLine();
                if (ImGui.SmallButton($"X##{i}")) pointToRemove = i;
            }

            if (pointToRemove != -1) _points.RemoveAt(pointToRemove);
        }

        ImGui.EndChild();
    }

    private VeldridTextureBinding? CreateTextureBindingForImage(ImageDataset dataset)
    {
        if (dataset?.ImageData == null)
        {
            Logger.LogError($"Cannot create texture binding: Image data not loaded for {dataset?.Name ?? "null"}");
            return null;
        }

        var texture = _resourceFactory.CreateTexture(TextureDescription.Texture2D(
            (uint)dataset.Width, (uint)dataset.Height, 1, 1,
            PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        
        _graphicsDevice.UpdateTexture(texture, dataset.ImageData, 0, 0, 0, (uint)dataset.Width, (uint)dataset.Height, 1,
            0, 0);

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
}

public class PanoramaWizardPanel : BasePanel
{
    private ImGuiExportFileDialog _exportFileDialog;
    private readonly PanoramaStitchJob _job;
    private readonly List<string> _logBuffer = new();
    private readonly ManualLinkEditor _manualLinkEditor;
    private PanoramaImage _groupLinkImage1;
    private PanoramaImage _groupLinkImage2;

    private PanoramaImage _imageToDiscard;
    private bool _previewFitRequested = true;
    // *** FIX 2: Add a "dirty" flag to force preview recalculation ***
    private bool _previewLayoutIsDirty = true; 
    
    private Vector2 _previewPan = Vector2.Zero;
    private float _previewZoom = 1.0f;
    
    public PanoramaWizardPanel(DatasetGroup imageGroup, GraphicsDevice graphicsDevice, ImGuiController imGuiController)
        : base($"Panorama Wizard: {imageGroup.Name}", new Vector2(800, 600))
    {
        Title = $"Panorama Wizard: {imageGroup.Name}";

        _job = new PanoramaStitchJob(imageGroup);
        _job.Service.StartProcessingAsync();

        _exportFileDialog = new ImGuiExportFileDialog("panoramaExport", "Export Panorama");
        _exportFileDialog.SetExtensions(new ImGuiExportFileDialog.ExtensionOption(".png", "PNG Image"),
            new ImGuiExportFileDialog.ExtensionOption(".jpg", "JPEG Image"));

        _manualLinkEditor = new ManualLinkEditor(graphicsDevice, imGuiController);
        _manualLinkEditor.OnConfirm = (img1, img2, points) =>
        {
            Logger.Log($"Confirmed {points.Count} manual links for {img1.Dataset.Name} and {img2.Dataset.Name}.");
            _job.Service.AddManualLinkAndRecompute(img1, img2, points);
            _previewLayoutIsDirty = true; // Mark the layout as dirty
            _groupLinkImage1 = null;      // Reset selections
            _groupLinkImage2 = null;
        };
    }

    public bool IsOpen { get; private set; }
    public string Title { get; }

    public void Open()
    {
        IsOpen = true;
    }

    public void Submit()
    {
        var pOpen = IsOpen;
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
            _previewLayoutIsDirty = true; // Mark the layout as dirty
        }

        if (_exportFileDialog.Submit())
            if (!string.IsNullOrEmpty(_exportFileDialog.SelectedPath))
                _job.Service.StartBlendingAsync(_exportFileDialog.SelectedPath);

        var service = _job.Service;
        ImGui.ProgressBar(service.Progress, new Vector2(-1, 0), service.StatusMessage);

        // *** FIX 1: Tightly scope the ManualLinkEditor's Draw call to its specific state ***
        // This prevents it from being drawn during state transitions, avoiding the crash.
        if (service.State == PanoramaState.AwaitingManualInput)
        {
            _manualLinkEditor.Draw();
        }

        if (ImGui.BeginTabBar("WizardTabs"))
        {
            if (ImGui.BeginTabItem("Processing Log"))
            {
                DrawLog();
                ImGui.EndTabItem();
            }

            if (service.State == PanoramaState.AwaitingManualInput && ImGui.BeginTabItem("Manual Correction"))
            {
                DrawManualInputUI();
                ImGui.EndTabItem();
            }

            if (service.State >= PanoramaState.ReadyForPreview && ImGui.BeginTabItem("Preview & Stitch"))
            {
                DrawPreviewUI();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        if (service.State == PanoramaState.Failed)
            ImGui.TextColored(new Vector4(1, 0, 0, 1), "Process failed. Check log.");
        if (service.State == PanoramaState.Completed) ImGui.TextColored(new Vector4(0, 1, 0, 1), "Panorama created!");
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
        var stitchGroups = _job.Service.StitchGroups;
        var imagesWithNoFeatures = _job.Service.Images.Where(img => img.Features.KeyPoints.Count < 20).ToList();

        if (imagesWithNoFeatures.Any())
            if (ImGui.CollapsingHeader($"Images with too few features ({imagesWithNoFeatures.Count})###NoFeatureHeader",
                    ImGuiTreeNodeFlags.DefaultOpen))
                foreach (var image in imagesWithNoFeatures)
                {
                    ImGui.BulletText($"{image.Dataset.Name}: Discard this image.");
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"Discard##{image.Id}")) _imageToDiscard = image;
                }

        if (stitchGroups.Count > 1)
            if (ImGui.CollapsingHeader($"Unconnected Image Groups ({stitchGroups.Count})###GroupHeader",
                    ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.TextWrapped("Select one image from two different groups to create a manual link between them.");
                ImGui.Columns(2, "GroupSelectors", false);
                DrawGroupSelectionUI(stitchGroups, 1, ref _groupLinkImage1, ref _groupLinkImage2);
                ImGui.NextColumn();
                DrawGroupSelectionUI(stitchGroups, 2, ref _groupLinkImage2, ref _groupLinkImage1);
                ImGui.NextColumn();
                ImGui.Columns(1);

                var canLink = _groupLinkImage1 != null && _groupLinkImage2 != null;
                if (!canLink) ImGui.BeginDisabled();
                if (ImGui.Button("Create Manual Link...")) _manualLinkEditor.Open(_groupLinkImage1, _groupLinkImage2);
                if (!canLink) ImGui.EndDisabled();
            }
    }
    
    private void DrawGroupSelectionUI(List<StitchGroup> groups, int id, ref PanoramaImage selected,
        ref PanoramaImage otherSelected)
    {
        ImGui.BeginChild($"GroupList{id}", Vector2.Zero, ImGuiChildFlags.Border);
        foreach (var group in groups)
        {
            var isOtherGroup = otherSelected != null && group.Images.Contains(otherSelected);
            if (isOtherGroup) ImGui.BeginDisabled();
            
            foreach (var image in group.Images)
                if (ImGui.Selectable(image.Dataset.Name, selected == image))
                    selected = image;

            if (isOtherGroup) ImGui.EndDisabled();
            ImGui.Separator();
        }

        ImGui.EndChild();
    }

    private void DrawPreviewUI()
    {
        if (ImGui.Button("Fit View"))
        {
            _previewFitRequested = true;
        }
        ImGui.SameLine();
        bool isBlending = _job.Service.State == PanoramaState.Blending;
        if (isBlending) ImGui.BeginDisabled();
        if (ImGui.Button("Create Panorama..."))
        {
            _exportFileDialog.Open();
        }
        if (isBlending) ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextDisabled("(Pan: Middle Mouse Button, Zoom: Mouse Wheel)");

        ImGui.BeginChild("PreviewArea", Vector2.Zero, ImGuiChildFlags.Border, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
    
        var canvasPos = ImGui.GetCursorScreenPos();
        var canvasSize = ImGui.GetContentRegionAvail();
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(canvasPos, canvasPos + canvasSize, 0xFF202020); // Background
    
        if (_job.Service.TryBuildPreviewLayout(out var quads, out var bounds) && quads.Any())
        {
            var boundsSize = new Vector2(bounds.MaxX - bounds.MinX, bounds.MaxY - bounds.MinY);

            // *** FIX 2: Check the dirty flag to force a refit ***
            if (_previewFitRequested || _previewLayoutIsDirty || boundsSize.X <= 0 || boundsSize.Y <= 0)
            {
                if (boundsSize.X > 0 && boundsSize.Y > 0)
                {
                    float zoomX = canvasSize.X / boundsSize.X;
                    float zoomY = canvasSize.Y / boundsSize.Y;
                    _previewZoom = Math.Min(zoomX, zoomY) * 0.9f;

                    var boundsCenter = new Vector2(bounds.MinX + boundsSize.X * 0.5f, bounds.MinY + boundsSize.Y * 0.5f);
                    _previewPan = canvasPos + canvasSize * 0.5f - boundsCenter * _previewZoom;
                }
                _previewFitRequested = false;
                _previewLayoutIsDirty = false; // Reset the flag
            }

            if (ImGui.IsWindowHovered())
            {
                if (ImGui.IsMouseDown(ImGuiMouseButton.Middle))
                {
                    _previewPan += ImGui.GetIO().MouseDelta;
                }
                if (ImGui.GetIO().MouseWheel != 0)
                {
                    float scale = ImGui.GetIO().MouseWheel > 0 ? 1.1f : 1 / 1.1f;
                    var mousePosInCanvas = ImGui.GetMousePos() - canvasPos;
                    var worldPos = (mousePosInCanvas - _previewPan) / _previewZoom;
                    _previewZoom *= scale;
                    _previewPan = mousePosInCanvas - worldPos * _previewZoom;
                }
            }

            drawList.PushClipRect(canvasPos, canvasPos + canvasSize, true);
            foreach (var (img, quad) in quads)
            {
                var p1 = quad[0] * _previewZoom + _previewPan;
                var p2 = quad[1] * _previewZoom + _previewPan;
                var p3 = quad[2] * _previewZoom + _previewPan;
                var p4 = quad[3] * _previewZoom + _previewPan;
            
                drawList.AddQuad(p1, p2, p3, p4, 0xFFFFFFFF, 1.0f);
            }
            drawList.PopClipRect();
        }
        else
        {
            drawList.AddText(canvasPos + new Vector2(10, 10), 0xFF00FFFF, "Building preview...");
        }

        ImGui.EndChild();
    }
}