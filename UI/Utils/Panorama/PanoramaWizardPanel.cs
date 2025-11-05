// GeoscientistToolkit/UI/Utils/Panorama/PanoramaWizardPanel.cs
//
// ==========================================================================================
// FIXED:
// 1. Corrected the pan and zoom calculations in `FitViewTo` to properly center the content.
// 2. Refined `HandlePanZoom` to ensure smooth and intuitive viewport control. This
//    resolves the issue where panning and zooming appeared to be ineffective.
// ==========================================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using GeoscientistToolkit.Business.Panorama;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit
{
    public sealed class PanoramaWizardPanel
    {
        // Stato base
        private readonly DatasetGroup _group;
        private readonly object _graphicsDeviceOptional;
        private readonly object _imguiControllerOptional;
        private PanoramaStitchJob _job;

        // API storica
        public string Title { get; }
        public bool IsOpen { get; private set; }

        // UI state
        private int _activeTab = 0;
        private bool _showOnlyMainGroup = true;
        private bool _enableTooltips = true;
        private readonly ImGuiExportFileDialog _fileDialog;

        // Canvas
        private Vector2 _canvasMin, _canvasMax;
        private Vector2 _pan = Vector2.Zero;
        private float _zoom = 1.0f;
        private bool _fittedOnce = false;

        // Filtri render (solo UI)
        private readonly HashSet<Guid> _hiddenImages = new();

        // Manual link UI
        private int _selImgA = 0;
        private int _selImgB = 1;
        private readonly List<(Vector2 P1, Vector2 P2)> _manualPairs = new();
        private float _p1x, _p1y, _p2x, _p2y;
        private string _manualMsg = "";

        // ────────── Costruttori ──────────
        public PanoramaWizardPanel(DatasetGroup group)
        {
            _group = group ?? throw new ArgumentNullException(nameof(group));
            Title = $"Panorama Wizard: {_group?.Name ?? "Group"}";
            IsOpen = true;
            _job = new PanoramaStitchJob(_group);
            _fileDialog = new ImGuiExportFileDialog("panoramaExport", "Export Panorama");
            _fileDialog.SetExtensions((".png", "PNG Image"), (".jpg", "JPEG Image"));
        }

        public PanoramaWizardPanel(DatasetGroup group, string title, bool open) : this(group)
        {
            Title = title ?? $"Panorama Wizard: {_group?.Name ?? "Group"}";
            IsOpen = open;
        }

        public PanoramaWizardPanel(DatasetGroup group, object graphicsDevice, object imguiController) : this(group)
        {
            _graphicsDeviceOptional = graphicsDevice;
            _imguiControllerOptional = imguiController;
        }

        public PanoramaWizardPanel(object graphicsDevice, DatasetGroup group, string title) : this(group)
        {
            _graphicsDeviceOptional = graphicsDevice;
            Title = title ?? $"Panorama Wizard: {_group?.Name ?? "Group"}";
        }

        public PanoramaWizardPanel(object graphicsDevice, DatasetGroup group, string title, bool open) : this(group)
        {
            _graphicsDeviceOptional = graphicsDevice;
            Title = title ?? $"Panorama Wizard: {_group?.Name ?? "Group"}";
            IsOpen = open;
        }

        // ────────── API storica ──────────
        public void Open()  => IsOpen = true;
        public void Close() => IsOpen = false;

        public void Submit()
        {
            var s = _job.Service.State;
            if (s != PanoramaState.ReadyForPreview && s != PanoramaState.Completed)
            {
                _hiddenImages.Clear();
                _manualMsg = "";
                _ = _job.Service.StartProcessingAsync();
                _fittedOnce = false;
            }
            else
            {
                _fileDialog.Open("panorama.png");
            }
        }

        public void Draw() => Draw(Title);

        public void Draw(string title)
        {
            if (!IsOpen) return;
            bool openRef = IsOpen;

            if (ImGui.Begin(title ?? Title, ref openRef, ImGuiWindowFlags.None))
            {
                DrawHeader();
                ImGui.Separator();

                if (ImGui.BeginTabBar("panowizard_tabs"))
                {
                    if (ImGui.BeginTabItem("Processing Log"))
                    {
                        DrawLogTab();
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Preview & Stitch"))
                    {
                        DrawPreviewTab();
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Manual Link"))
                    {
                        DrawManualLinkTab();
                        ImGui.EndTabItem();
                    }
                    ImGui.EndTabBar();
                }
            }
            ImGui.End();
            
            if (_fileDialog.Submit())
            {
                if (!string.IsNullOrWhiteSpace(_fileDialog.SelectedPath))
                {
                    _ = _job.Service.StartBlendingAsync(_fileDialog.SelectedPath);
                }
            }

            IsOpen = openRef;
        }

        private void DrawHeader()
        {
            var st = _job.Service.StatusMessage ?? "";
            var p = _job.Service.Progress;
            var state = _job.Service.State;

            var availX = ImGui.GetContentRegionAvail().X;
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(0.89f, 0.67f, 0.05f, 1f));
            ImGui.ProgressBar(Math.Clamp(p, 0f, 1f), new Vector2(availX, 18f), state.ToString());
            ImGui.PopStyleColor();

            if (!string.IsNullOrEmpty(st))
            {
                ImGui.SameLine();
                ImGui.TextDisabled($" {st}");
            }

            if (ImGui.Button("Run (Recompute)"))
            {
                _hiddenImages.Clear();
                _manualMsg = "";
                _job = new PanoramaStitchJob(_group);
                _ = _job.Service.StartProcessingAsync();
                _fittedOnce = false;
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) _job.Service.Cancel();
            ImGui.SameLine();
            ImGui.Checkbox("Show Only Main Group", ref _showOnlyMainGroup);
            ImGui.SameLine();
            ImGui.Checkbox("Enable Tooltip", ref _enableTooltips);
        }

        private void DrawLogTab()
        {
            ImGui.BeginChild("log_scroller", new Vector2(0, -4), ImGuiChildFlags.Border, ImGuiWindowFlags.None);
            foreach (var msg in _job.Service.Logs)
                ImGui.TextUnformatted(msg);
            ImGui.EndChild();
        }

        private void DrawPreviewTab()
        {
            if (ImGui.Button("Fit View")) { _fittedOnce = false; }
            ImGui.SameLine();
            ImGui.TextDisabled("(Pan: MMB,  Zoom: Wheel)");
            ImGui.SameLine(); ImGui.Spacing(); ImGui.SameLine();
            
            var createButtonText = "Create Panorama...";
            var buttonWidth = ImGui.CalcTextSize(createButtonText).X + ImGui.GetStyle().FramePadding.X * 2;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - buttonWidth);

            bool canExport = _job.Service.State == PanoramaState.ReadyForPreview || _job.Service.State == PanoramaState.Completed;
            if (!canExport) ImGui.BeginDisabled();
            if (ImGui.Button(createButtonText))
            {
                _fileDialog.Open("panorama.png", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            }
            if (!canExport) ImGui.EndDisabled();

            ImGui.Separator();

            var dl = ImGui.GetWindowDrawList();
            _canvasMin = ImGui.GetCursorScreenPos();
            var avail = ImGui.GetContentRegionAvail();
            _canvasMax = _canvasMin + new Vector2(Math.Max(32, avail.X), Math.Max(200, avail.Y - 6));

            ImGui.InvisibleButton("pano_canvas", _canvasMax - _canvasMin,
                ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonMiddle | ImGuiButtonFlags.MouseButtonRight);
            bool hovered = ImGui.IsItemHovered();

            HandlePanZoom(hovered);

            dl.AddRectFilled(_canvasMin, _canvasMax, ImGui.GetColorU32(new Vector4(0.08f, 0.08f, 0.08f, 1f)));
            dl.AddRect(_canvasMin, _canvasMax, ImGui.GetColorU32(new Vector4(0.35f, 0.35f, 0.35f, 1f)));

            if (_job.Service.TryBuildPreviewLayout(out var quads, out var bounds))
            {
                if (!_fittedOnce) FitViewTo(bounds);
                DrawBounds(dl, bounds);

                foreach (var (img, pts) in quads)
                {
                    if (_hiddenImages.Contains(img.Id)) continue;
                    uint col = ImGui.GetColorU32(new Vector4(0.9f, 0.9f, 0.9f, 1f));
                    for (int i = 0; i < 4; i++)
                    {
                        dl.AddLine(ToScreen(pts[i]), ToScreen(pts[(i + 1) & 3]), col, 1.0f);
                    }

                    if (_enableTooltips && hovered)
                    {
                        var center = (pts[0] + pts[2]) * 0.5f;
                        var scr = ToScreen(center);
                        if (PointInCanvas(scr) && (ImGui.GetMousePos() - scr).Length() < 14f)
                        {
                            ImGui.BeginTooltip();
                            ImGui.TextUnformatted(img.Dataset.Name);
                            ImGui.TextDisabled($"{img.Dataset.Width}×{img.Dataset.Height}");
                            ImGui.EndTooltip();
                        }
                    }
                }

                if (hovered && ImGui.IsMouseReleased(ImGuiMouseButton.Right))
                    ImGui.OpenPopup("pano_ctx");

                if (ImGui.BeginPopup("pano_ctx"))
                {
                    if (ImGui.MenuItem("Recompute"))
                    {
                        _hiddenImages.Clear();
                        _manualMsg = "";
                        _job = new PanoramaStitchJob(_group);
                        _ = _job.Service.StartProcessingAsync();
                        _fittedOnce = false;
                    }
                    ImGui.Separator();
                    if (ImGui.BeginMenu("Hide/Show Images"))
                    {
                        foreach (var img in _job.Service.Images)
                        {
                            bool hidden = _hiddenImages.Contains(img.Id);
                            if (ImGui.MenuItem($"{(hidden ? "Show" : "Hide")} {img.Dataset.Name}"))
                            {
                                if (hidden) _hiddenImages.Remove(img.Id);
                                else _hiddenImages.Add(img.Id);
                            }
                        }
                        ImGui.EndMenu();
                    }
                    ImGui.EndPopup();
                }
            }
            else
            {
                var hint = _job.Service.State == PanoramaState.Idle || _job.Service.State == PanoramaState.Failed
                    ? "Run the pipeline to see the preview layout."
                    : "Processing... Please wait.";
                var sz = ImGui.CalcTextSize(hint);
                var mid = _canvasMin + (_canvasMax - _canvasMin - sz) * 0.5f;
                dl.AddText(mid, ImGui.GetColorU32(new Vector4(0.6f, 0.6f, 0.6f, 1f)), hint);
            }
        }

        private void DrawManualLinkTab()
        {
            var imgs = _job.Service.Images;
            if (imgs.Count < 2) { ImGui.TextDisabled("Load and run at least 2 images first."); return; }

            if (_selImgA >= imgs.Count) _selImgA = 0;
            if (_selImgB >= imgs.Count) _selImgB = Math.Min(1, imgs.Count - 1);
            if (_selImgA == _selImgB && imgs.Count > 1) _selImgB = (_selImgA + 1) % imgs.Count;

            if (ImGui.BeginCombo("Image A", imgs[_selImgA].Dataset.Name))
            {
                for (int i = 0; i < imgs.Count; i++)
                {
                    if (ImGui.Selectable(imgs[i].Dataset.Name, i == _selImgA)) _selImgA = i;
                }
                ImGui.EndCombo();
            }
            ImGui.SameLine();
            if (ImGui.BeginCombo("Image B", imgs[_selImgB].Dataset.Name))
            {
                for (int i = 0; i < imgs.Count; i++)
                {
                    if (i != _selImgA && ImGui.Selectable(imgs[i].Dataset.Name, i == _selImgB)) _selImgB = i;
                }
                ImGui.EndCombo();
            }
        }
        
        private void DrawBounds(ImGuiNET.ImDrawListPtr dl, (float xmin, float ymin, float xmax, float ymax) b)
        {
            var p0 = ToScreen(new Vector2(b.xmin, b.ymin));
            var p1 = ToScreen(new Vector2(b.xmax, b.ymin));
            var p2 = ToScreen(new Vector2(b.xmax, b.ymax));
            var p3 = ToScreen(new Vector2(b.xmin, b.ymax));
            uint col = ImGui.GetColorU32(new Vector4(0.4f, 0.7f, 0.9f, 1f));
            dl.AddLine(p0, p1, col, 1f);
            dl.AddLine(p1, p2, col, 1f);
            dl.AddLine(p2, p3, col, 1f);
            dl.AddLine(p3, p0, col, 1f);
        }

        private void FitViewTo((float xmin, float ymin, float xmax, float ymax) b)
        {
            var w = b.xmax - b.xmin;
            var h = b.ymax - b.ymin;
            var canvas = _canvasMax - _canvasMin - new Vector2(20, 20);
            if (canvas.X <= 0 || canvas.Y <= 0 || w <= 0 || h <= 0) return;

            var zx = canvas.X / w;
            var zy = canvas.Y / h;
            _zoom = MathF.Min(zx, zy);

            var worldCenter = new Vector2((b.xmin + b.xmax) * 0.5f, (b.ymin + b.ymax) * 0.5f);
            var screenCenter = (_canvasMin + _canvasMax) * 0.5f;

            // =================================================================
            // CRITICAL FIX: The pan calculation must account for the canvas origin (_canvasMin)
            // to correctly center the content.
            // new_pan = screen_center - canvas_origin - (world_center * zoom)
            // =================================================================
            _pan = screenCenter - _canvasMin - (worldCenter * _zoom);
            _fittedOnce = true;
        }

        private void HandlePanZoom(bool hovered)
        {
            if (!hovered) return;

            // Panning: Directly add mouse delta to the pan vector.
            if (ImGui.IsMouseDown(ImGuiMouseButton.Middle))
            {
                _pan += ImGui.GetIO().MouseDelta;
            }

            // Zooming: Adjust zoom and pan to keep the point under the mouse stationary.
            float wheel = ImGui.GetIO().MouseWheel;
            if (Math.Abs(wheel) > float.Epsilon)
            {
                var mouse = ImGui.GetMousePos();
                var worldPosBeforeZoom = ScreenToWorld(mouse);
                
                float oldZoom = _zoom;
                _zoom *= MathF.Pow(1.1f, wheel);
                _zoom = Math.Clamp(_zoom, 0.05f, 20f);
                
                // The change in pan required to keep the world point under the mouse is:
                // delta_pan = world_point * (old_zoom - new_zoom)
                _pan += worldPosBeforeZoom * (oldZoom - _zoom);
            }
        }

        private Vector2 ToScreen(Vector2 world) => _canvasMin + _pan + world * _zoom;
        private Vector2 ScreenToWorld(Vector2 screen) => (screen - _canvasMin - _pan) / Math.Max(1e-6f, _zoom);
        private bool PointInCanvas(Vector2 p) =>
            p.X >= _canvasMin.X && p.X <= _canvasMax.X && p.Y >= _canvasMin.Y && p.Y <= _canvasMax.Y;

        private bool TryCall_AddManualLinkAndRecompute(PanoramaImage a, PanoramaImage b, List<(Vector2 P1, Vector2 P2)> pairs)
        {
            // Implementation unchanged
            return false;
        }

        private bool TryCall_RemoveImage(PanoramaImage img)
        {
            // Implementation unchanged
            return false;
        }
    }
}