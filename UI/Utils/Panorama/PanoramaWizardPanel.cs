// GeoscientistToolkit/UI/Utils/Panorama/PanoramaWizardPanel.cs
//
// ==========================================================================================
// FINAL VERSION:
// 1. Implemented a clipping rectangle on the canvas to provide a true crop/zoom experience.
// 2. Added panning via left-click-and-drag for intuitive positioning of the panorama
//    within the viewport.
// 3. Added export size control.
// 4. Implemented side-by-side manual control point addition.
// 5. Added a checkbox to enable/disable image preview with textures.
// 6. CORRECTED: Replaced Guid-based texture dictionary key with a direct object reference
//    to resolve compilation errors with 'ds.Id'.
// ==========================================================================================

// ==========================================================================================
// FIX IMPLEMENTED:
// 1. Moved texture creation from the constructor to a just-in-time method (`EnsureTexturesAreLoaded`)
//    that runs after the stitching service has loaded the image data, resolving the root cause
//    of textures not appearing.
// 2. The side-by-side "Manual Link" view now correctly respects the "Enable Image Preview"
//    checkbox, making the UI behavior consistent.
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
        private bool _enableTexturePreview = false;
        private readonly ImGuiExportFileDialog _fileDialog;
        private int _exportWidth = 8192;


        // Canvas & Projection Controls
        private Vector2 _canvasMin, _canvasMax;
        private Vector2 _pan = Vector2.Zero;
        private float _zoom = 1.0f;
        private bool _fittedOnce = false;
        private float _previewStraighten = 0.0f;

        // Filtri render (solo UI)
        private readonly HashSet<Guid> _hiddenImages = new();

        // Manual link UI
        private int _selImgA = 0;
        private int _selImgB = 1;
        private readonly List<(Vector2 P1, Vector2 P2)> _manualPairs = new();
        private Vector2? _pendingImagePoint;
        private Vector2? _pendingScreenPoint;
        private float _p1x, _p1y, _p2x, _p2y;
        private string _manualMsg = "";
        private readonly Dictionary<Dataset, TextureManager> _textureManagers = new();
        private Guid _texturesForJobId = Guid.Empty; // FIX: Tracks textures for the current job


        // ────────── Costruttori ──────────
        public PanoramaWizardPanel(DatasetGroup group)
        {
            _group = group ?? throw new ArgumentNullException(nameof(group));
            Title = $"Panorama Wizard: {_group?.Name ?? "Group"}";
            IsOpen = true;
            _job = new PanoramaStitchJob(_group);
            _fileDialog = new ImGuiExportFileDialog("panoramaExport", "Export Panorama");
            _fileDialog.SetExtensions((".png", "PNG Image"), (".jpg", "JPEG Image"));

            // FIX: Texture creation is removed from here. It was executing before image data was loaded.
            // It is now handled by EnsureTexturesAreLoaded().
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
        public void Open() => IsOpen = true;
        public void Close()
        {
            // Stop showing the panel first so nothing else draws/allocates
            IsOpen = false;

            // 1) Stop the pipeline and dispose heavy service state
            try
            {
                _job?.Service?.Cancel();
                _job?.Service?.Dispose();   // requires methods added below
            }
            catch { /* ignore on close */ }

            // 2) Dispose GPU textures (Veldrid/ImGui)
            try
            {
                foreach (var tm in _textureManagers.Values)
                    tm?.Dispose();
            }
            finally
            {
                _textureManagers.Clear();
            }

            // 3) Drop UI/state references so GC can reclaim memory
            try
            {
                _hiddenImages.Clear();
                _manualPairs.Clear();
                _manualMsg = "";
                _pendingImagePoint = null;
                _pendingScreenPoint = null;
            }
            catch { /* ignore */ }

            _job = null;

            // 4) (Optional) compact LOH once after closing a big workspace
            PanoramaStitchingService.ForceGcCompaction();
        }

        private void Recompute()
        {
            // FIXED: Cancel and properly dispose the resources of the old job first.
            _job?.Service?.Cancel();
            _job?.Service?.Dispose();

            _hiddenImages.Clear();
            _manualMsg = "";
            _manualPairs.Clear();
            _pendingImagePoint = null;
            _pendingScreenPoint = null;

            // Now, create the new job and start processing.
            _job = new PanoramaStitchJob(_group);
            _ = _job.Service.StartProcessingAsync();
            _fittedOnce = false;
        }

        public void Submit()
        {
            var s = _job.Service.State;
            if (s != PanoramaState.ReadyForPreview && s != PanoramaState.Completed)
            {
                Recompute();
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
                // FIX: Load textures just-in-time after image data is available.
                EnsureTexturesAreLoaded();

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
            if (!openRef && IsOpen)
            {
                Close();
                return;
            }
            if (_fileDialog.Submit())
            {
                if (!string.IsNullOrWhiteSpace(_fileDialog.SelectedPath))
                {
                    _ = _job.Service.StartBlendingAsync(_fileDialog.SelectedPath, _exportWidth);
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
                Recompute();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) _job.Service.Cancel();
            ImGui.SameLine();
            ImGui.Checkbox("Show Only Main Group", ref _showOnlyMainGroup);
            ImGui.SameLine();
            ImGui.Checkbox("Enable Tooltip", ref _enableTooltips);
            ImGui.SameLine();
            ImGui.Checkbox("Enable Image Preview", ref _enableTexturePreview);
        }

        private void DrawLogTab()
        {
            ImGui.BeginChild("log_scroller", new Vector2(0, -4), ImGuiChildFlags.Border, ImGuiWindowFlags.None);
            foreach (var msg in _job.Service.Logs)
                ImGui.TextUnformatted(msg);
            ImGui.EndChild();
        }

        /// <summary>
        /// Draws the interactive preview and stitching controls tab.
        /// </summary>
        private void DrawPreviewTab()
        {
            // --- HEADER CONTROLS ---
            if (ImGui.Button("Fit View")) { _fittedOnce = false; }
            ImGui.SameLine();
            ImGui.TextDisabled("(Pan: Drag or MMB, Zoom: Wheel/Slider)"); // UI Hint Updated
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

            // --- PROJECTION & CROP CONTROLS ---
            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X * 0.4f);
            if (canExport)
            {
                ImGui.SliderFloat("FOV/Zoom", ref _zoom, 0.1f, 10.0f, "%.2fx");
                ImGui.SameLine();
                ImGui.SliderFloat("Straighten", ref _previewStraighten, -1.0f, 1.0f);
                ImGui.SameLine();
                ImGui.InputInt("Export Width", ref _exportWidth, 128, 512);

            }
            else // Show disabled sliders
            {
                ImGui.BeginDisabled();
                float disabledZoom = 1.0f, disabledStraighten = 0.0f;
                int disabledWidth = _exportWidth;
                ImGui.SliderFloat("FOV/Zoom", ref disabledZoom, 0.1f, 10.0f, "%.2fx");
                ImGui.SameLine();
                ImGui.SliderFloat("Straighten", ref disabledStraighten, -1.0f, 1.0f);
                ImGui.SameLine();
                ImGui.InputInt("Export Width", ref disabledWidth);
                ImGui.EndDisabled();
            }
            ImGui.PopItemWidth();
            ImGui.Separator();

            // --- CANVAS SETUP ---
            var dl = ImGui.GetWindowDrawList();
            _canvasMin = ImGui.GetCursorScreenPos();
            var avail = ImGui.GetContentRegionAvail();
            _canvasMax = _canvasMin + new Vector2(Math.Max(32, avail.X), Math.Max(200, avail.Y - 6));

            ImGui.InvisibleButton("pano_canvas", _canvasMax - _canvasMin, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonMiddle | ImGuiButtonFlags.MouseButtonRight);
            bool hovered = ImGui.IsItemHovered();

            HandlePanZoom(hovered);

            dl.AddRectFilled(_canvasMin, _canvasMax, ImGui.GetColorU32(new Vector4(0.08f, 0.08f, 0.08f, 1f)));
            dl.AddRect(_canvasMin, _canvasMax, ImGui.GetColorU32(new Vector4(0.35f, 0.35f, 0.35f, 1f)));

            dl.PushClipRect(_canvasMin, _canvasMax, true);

            // --- DRAWING LOGIC ---
            if (_job.Service.TryBuildPreviewLayout(out var quads, out var bounds))
            {
                if (!_fittedOnce) FitViewTo(bounds);

                DrawBounds(dl, bounds);

                foreach (var (img, pts) in quads)
                {
                    if (_hiddenImages.Contains(img.Id)) continue;

                    var screenPts = new Vector2[4];
                    for (int i = 0; i < 4; i++)
                    {
                        screenPts[i] = ToScreen(pts[i], bounds);
                    }

                    if (_enableTexturePreview && _textureManagers.TryGetValue(img.Dataset, out var tm))
                    {
                        var textureId = tm.GetImGuiTextureId();
                        if (textureId != IntPtr.Zero)
                        {
                            dl.AddImageQuad(textureId, screenPts[0], screenPts[1], screenPts[2], screenPts[3],
                                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
                        }
                    }
                    else
                    {
                        uint col = ImGui.GetColorU32(new Vector4(0.9f, 0.9f, 0.9f, 1f));
                        for (int i = 0; i < 4; i++)
                        {
                            dl.AddLine(screenPts[i], screenPts[(i + 1) % 4], col, 1.0f);
                        }
                    }
                    
                    // --- BEGIN: DRAW IMAGE NAME ---
                    // Calculate the visual center of the quad on the screen.
                    var center = (screenPts[0] + screenPts[1] + screenPts[2] + screenPts[3]) * 0.25f;

                    // Only draw the text if the center of the image is visible on the canvas.
                    if (PointInCanvas(center))
                    {
                        var text = img.Dataset.Name;
                        var textSize = ImGui.CalcTextSize(text);
                        
                        // Position the text in the center of the quad.
                        var textPos = center - textSize * 0.5f;

                        // Add a simple shadow for readability by drawing the text in black first, offset by 1 pixel.
                        dl.AddText(textPos + new Vector2(1, 1), ImGui.GetColorU32(new Vector4(0, 0, 0, 0.85f)), text);
                        
                        // Draw the main text in white.
                        dl.AddText(textPos, ImGui.GetColorU32(new Vector4(1, 1, 1, 1f)), text);
                    }
                    // --- END: DRAW IMAGE NAME ---


                    if (_enableTooltips && hovered)
                    {
                        var tooltipCenter = ToScreen((pts[0] + pts[2]) * 0.5f, bounds);
                        if (PointInCanvas(tooltipCenter) && (ImGui.GetMousePos() - tooltipCenter).Length() < 14f)
                        {
                            ImGui.BeginTooltip();
                            ImGui.TextUnformatted(img.Dataset.Name);
                            ImGui.TextDisabled($"{img.Dataset.Width}×{img.Dataset.Height}");
                            ImGui.EndTooltip();
                        }
                    }
                }

                // --- CONTEXT MENU ---
                if (hovered && ImGui.IsMouseReleased(ImGuiMouseButton.Right))
                    ImGui.OpenPopup("pano_ctx");

                if (ImGui.BeginPopup("pano_ctx"))
                {
                    if (ImGui.MenuItem("Recompute"))
                    {
                        Recompute();
                    }
                    ImGui.Separator();
                    if (ImGui.BeginMenu("Hide/Show Images"))
                    {
                        foreach (var img in _job.Service.GetImages())
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
                var hint = _job.Service.State == PanoramaState.Idle || _job.Service.State == PanoramaState.Failed ? "Run the pipeline to see the preview layout." : "Processing... Please wait.";
                var sz = ImGui.CalcTextSize(hint);
                var mid = _canvasMin + (_canvasMax - _canvasMin - sz) * 0.5f;
                dl.AddText(mid, ImGui.GetColorU32(new Vector4(0.6f, 0.6f, 0.6f, 1f)), hint);
            }

            dl.PopClipRect();
        }

        private void DrawManualLinkTab()
        {
            var imgs = _job.Service.GetImages();
            if (imgs.Count < 2) { ImGui.TextDisabled("Load and run at least 2 images first."); return; }

            if (_selImgA >= imgs.Count) _selImgA = 0;
            if (_selImgB >= imgs.Count) _selImgB = Math.Min(1, imgs.Count - 1);
            if (_selImgA == _selImgB && imgs.Count > 1) _selImgB = (_selImgA + 1) % imgs.Count;

            var imgA = imgs[_selImgA];
            var imgB = imgs[_selImgB];

            if (ImGui.BeginCombo("Image A", imgA.Dataset.Name))
            {
                for (int i = 0; i < imgs.Count; i++)
                {
                    if (ImGui.Selectable(imgs[i].Dataset.Name, i == _selImgA)) _selImgA = i;
                }
                ImGui.EndCombo();
            }
            ImGui.SameLine();
            if (ImGui.BeginCombo("Image B", imgB.Dataset.Name))
            {
                for (int i = 0; i < imgs.Count; i++)
                {
                    if (i != _selImgA && ImGui.Selectable(imgs[i].Dataset.Name, i == _selImgB)) _selImgB = i;
                }
                ImGui.EndCombo();
            }

            ImGui.Separator();

            if (ImGui.Button("Refine with Manual Points") && _manualPairs.Count > 0)
            {
                // --- REFINEMENT LOGIC IMPLEMENTED HERE ---
                if (_manualPairs.Count < 8)
                {
                    _manualMsg = $"Error: At least 8 point pairs are required. You have {_manualPairs.Count}.";
                }
                else
                {
                    _manualMsg = $"Refining with {_manualPairs.Count} points...";
                    
                    // Create a copy of the list to pass to the async method
                    var pairsCopy = new List<(Vector2, Vector2)>(_manualPairs);

                    // Call the service asynchronously and update the UI message on completion
                    _ = _job.Service.RefineWithManualPointsAsync(imgA, imgB, pairsCopy)
                        .ContinueWith(task =>
                        {
                            if (task.IsFaulted)
                            {
                                _manualMsg = "Refinement failed. See log for details.";
                            }
                            else
                            {
                                _manualMsg = "Refinement complete! Preview has been updated.";
                                _fittedOnce = false; // Force preview to re-fit to the new layout
                            }
                        }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Clear Points"))
            {
                _manualPairs.Clear();
                _pendingImagePoint = null;
                _pendingScreenPoint = null;
                _manualMsg = "Points cleared.";
            }
            ImGui.Text(_manualMsg);

            ImGui.Columns(2, "manual_link_cols", true);

            // Image A
            DrawImageForManualLinking(imgA, 0);
            ImGui.NextColumn();

            // Image B
            DrawImageForManualLinking(imgB, 1);
            ImGui.Columns(1);
        }
        private void DrawImageForManualLinking(PanoramaImage img, int panelIndex)
        {
            ImGui.Text(img.Dataset.Name);
            var avail = ImGui.GetContentRegionAvail();
            var canvasSize = new Vector2(avail.X, avail.X * ((float)img.Dataset.Height / img.Dataset.Width));
            var canvasMin = ImGui.GetCursorScreenPos();
            var canvasMax = canvasMin + canvasSize;
            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(canvasMin, canvasMax, 0xFF202020);

            // FIX: This view now respects the "Enable Image Preview" checkbox for consistency.
            if (_enableTexturePreview && _textureManagers.TryGetValue(img.Dataset, out var tm))
            {
                var textureId = tm.GetImGuiTextureId();
                if (textureId != IntPtr.Zero)
                {
                    dl.AddImage(textureId, canvasMin, canvasMax);
                }
            }
            dl.AddRect(canvasMin, canvasMax, 0xFF808080);

            bool isHovered = ImGui.IsMouseHoveringRect(canvasMin, canvasMax);
            if (isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                var mousePos = ImGui.GetMousePos();
                var localPos = (mousePos - canvasMin) / canvasSize;
                var imagePos = new Vector2(localPos.X * img.Dataset.Width, localPos.Y * img.Dataset.Height);

                if (panelIndex == 0)
                {
                    _pendingImagePoint = imagePos;
                    _pendingScreenPoint = mousePos;
                }
                else if (_pendingImagePoint.HasValue)
                {
                    _manualPairs.Add((_pendingImagePoint.Value, imagePos));
                    _pendingImagePoint = null;
                    _pendingScreenPoint = null;
                }
            }

            // Draw existing points
            foreach (var pair in _manualPairs)
            {
                var p = (panelIndex == 0) ? pair.P1 : pair.P2;
                var screenPos = canvasMin + new Vector2(p.X / img.Dataset.Width, p.Y / img.Dataset.Height) * canvasSize;
                dl.AddCircleFilled(screenPos, 4, 0xFF00FF00);
                dl.AddCircle(screenPos, 5, 0xFFFFFFFF);
            }

            // If a point is selected in the left image, draw a line to the cursor in the right image.
            if (_pendingScreenPoint.HasValue && panelIndex == 1 && isHovered)
            {
                dl.AddLine(_pendingScreenPoint.Value, ImGui.GetMousePos(), 0xFF00FFFF);
            }

            // Also draw the pending point in the left image so the user knows it's selected.
            if (_pendingScreenPoint.HasValue && panelIndex == 0)
            {
                dl.AddCircleFilled(_pendingScreenPoint.Value, 4, 0xFF00FFFF);
                dl.AddCircle(_pendingScreenPoint.Value, 5, 0xFFFFFFFF);
            }
        }

        /// <summary>
        /// FIX: Creates GPU textures for the current job's images, but only after the service has loaded them.
        /// This prevents trying to create textures from null data.
        /// </summary>
        private void EnsureTexturesAreLoaded()
        {
            // Check if the job is ready and if we haven't already loaded textures for this job instance.
            if (_job != null && _job.Service.State >= PanoramaState.DetectingFeatures && _texturesForJobId != _job.Id)
            {
                // Dispose old textures to prevent leaks when recomputing.
                foreach (var tm in _textureManagers.Values)
                {
                    tm?.Dispose();
                }
                _textureManagers.Clear();

                // Get the list of images that the service successfully loaded.
                var loadedImages = _job.Service.GetImages();
                foreach (var panoramaImage in loadedImages)
                {
                    if (panoramaImage.Dataset is ImageDataset imageDs && imageDs.ImageData != null)
                    {
                        _textureManagers[imageDs] = TextureManager.CreateFromPixelData(
                            imageDs.ImageData,
                            (uint)imageDs.Width,
                            (uint)imageDs.Height
                        );
                    }
                }
                // Mark that we've created textures for this job ID to avoid redundant work.
                _texturesForJobId = _job.Id;
            }
        }


        private void DrawBounds(ImGuiNET.ImDrawListPtr dl, (float xmin, float ymin, float xmax, float ymax) b)
        {
            var p0 = ToScreen(new Vector2(b.xmin, b.ymin), b);
            var p1 = ToScreen(new Vector2(b.xmax, b.ymin), b);
            var p2 = ToScreen(new Vector2(b.xmax, b.ymax), b);
            var p3 = ToScreen(new Vector2(b.xmin, b.ymax), b);
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

            _pan = screenCenter - _canvasMin - (worldCenter * _zoom);
            _fittedOnce = true;
        }

        private void HandlePanZoom(bool hovered)
        {
            if (!hovered) return;

            // Panning with either Left or Middle mouse button
            if (ImGui.IsMouseDragging(ImGuiMouseButton.Left) || ImGui.IsMouseDragging(ImGuiMouseButton.Middle))
            {
                _pan += ImGui.GetIO().MouseDelta;
            }

            // Zooming with mouse wheel
            float wheel = ImGui.GetIO().MouseWheel;
            if (Math.Abs(wheel) > float.Epsilon)
            {
                var mouse = ImGui.GetMousePos();
                var worldPosBeforeZoom = ScreenToWorld(mouse);

                float oldZoom = _zoom;
                _zoom *= MathF.Pow(1.1f, wheel);
                _zoom = Math.Clamp(_zoom, 0.05f, 20f);

                _pan += worldPosBeforeZoom * (oldZoom - _zoom);
            }
        }

        private Vector2 ToScreen(Vector2 world, (float xmin, float ymin, float xmax, float ymax) bounds)
        {
            Vector2 finalWorldPos = world;

            if (Math.Abs(_previewStraighten) > 0.001f && bounds.xmax > bounds.xmin)
            {
                float worldWidth = bounds.xmax - bounds.xmin;
                float worldCenterX = bounds.xmin + worldWidth * 0.5f;
                float normalizedX = (world.X - worldCenterX) / (worldWidth * 0.5f);
                float warpFactor = normalizedX * normalizedX;
                float yOffset = world.Y * warpFactor * _previewStraighten;
                finalWorldPos = new Vector2(world.X, world.Y - yOffset);
            }


            return _canvasMin + _pan + finalWorldPos * _zoom;
        }

        private Vector2 ScreenToWorld(Vector2 screen)
        {
            return (screen - _canvasMin - _pan) / Math.Max(1e-6f, _zoom);
        }

        private bool PointInCanvas(Vector2 p) =>
            p.X >= _canvasMin.X && p.X <= _canvasMax.X && p.Y >= _canvasMin.Y && p.Y <= _canvasMax.Y;

        private bool TryCall_AddManualLinkAndRecompute(PanoramaImage a, PanoramaImage b, List<(Vector2 P1, Vector2 P2)> pairs)
        {
            return false;
        }

        private bool TryCall_RemoveImage(PanoramaImage img)
        {
            return false;
        }
    }
}