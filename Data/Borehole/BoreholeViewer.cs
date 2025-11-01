// GeoscientistToolkit/UI/Borehole/BoreholeViewer.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Borehole
{
    /// <summary>
    /// Borehole/well log viewer:
    /// - Single vertical scrollbar on the right drives Y (depth) for both content and left depth scale.
    /// - Single horizontal scrollbar at the bottom drives X for header & body.
    /// - Legend is a separate floating ImGui window (movable/resizable).
    /// </summary>
    public class BoreholeViewer : IDatasetViewer, IDisposable
    {
        private readonly BoreholeDataset _dataset;

        // colors
        private readonly Vector4 _textColor      = new(0.90f, 0.90f, 0.90f, 1.00f);
        private readonly Vector4 _mutedText      = new(0.75f, 0.75f, 0.75f, 1.00f);
        private readonly Vector4 _depthTextColor = new(0.85f, 0.85f, 0.85f, 1.00f);
        private readonly Vector4 _gridColor      = new(0.30f, 0.30f, 0.30f, 0.50f);

        // layout
        private const float HeaderHeight    = 30f;
        private const float DepthScaleWidth = 56f; // label padding included
        private const float BottomBarHeight = 18f;

        private readonly float _lithologyColumnWidth = 150f;
        private readonly float _trackSpacing = 10f;

        // depth range
        private bool  _autoScaleDepth = true;
        private float _depthStart;
        private float _depthEnd;

        // toggles
        private bool _showDepthGrid       = true;
        private bool _showLithologyNames  = true;
        private bool _showParameterValues = true;
        private bool _showLegend          = true;

        // Legend window initial placement (first frame only)
        private bool   _legendInit  = false;
        private Vector2 _legendInitPos  = new(60f, 60f);
        private Vector2 _legendInitSize = new(320f, 240f);

        public BoreholeViewer(BoreholeDataset dataset)
        {
            _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));

            if (_dataset.LithologyUnits.Any())
            {
                _depthStart = _dataset.LithologyUnits.Min(u => u.DepthFrom);
                _depthEnd   = _dataset.LithologyUnits.Max(u => u.DepthTo);
            }
            else
            {
                _depthStart = 0;
                _depthEnd   = Math.Max(_dataset.TotalDepth, 1f);
            }
        }

        public void DrawToolbarControls()
        {
            ImGui.Text("Depth Range:");
            ImGui.SameLine();

            if (ImGui.Checkbox("Auto", ref _autoScaleDepth))
            {
                if (_autoScaleDepth && _dataset.LithologyUnits.Any())
                {
                    _depthStart = _dataset.LithologyUnits.Min(u => u.DepthFrom);
                    _depthEnd   = _dataset.LithologyUnits.Max(u => u.DepthTo);
                }
            }

            if (!_autoScaleDepth)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(80);
                ImGui.DragFloat("##StartDepth", ref _depthStart, 0.1f, 0, _dataset.TotalDepth, "%.1f m");

                ImGui.SameLine(); ImGui.Text("to");

                ImGui.SameLine();
                ImGui.SetNextItemWidth(80);
                ImGui.DragFloat("##EndDepth", ref _depthEnd, 0.1f, _depthStart, _dataset.TotalDepth, "%.1f m");
            }

            ImGui.SameLine(); ImGui.Separator();
            ImGui.SameLine(); ImGui.Checkbox("Grid",   ref _showDepthGrid);
            ImGui.SameLine(); ImGui.Checkbox("Names",  ref _showLithologyNames);
            ImGui.SameLine(); ImGui.Checkbox("Values", ref _showParameterValues);
            ImGui.SameLine(); ImGui.Checkbox("Legend (window)", ref _showLegend);
        }

        public void DrawContent(ref float zoom, ref Vector2 pan)
        {
            var availAll = ImGui.GetContentRegionAvail();
            if (availAll.X < 5 || availAll.Y < 5) return;

            bool reset = ImGui.IsKeyPressed(ImGuiKey.R);
            if (reset) zoom = 1f;

            var visibleTracks   = _dataset.ParameterTracks.Values.Where(t => t.IsVisible).ToList();
            float trackWidth    = _dataset.TrackWidth;
            float tracksWidth   = visibleTracks.Count > 0 ? visibleTracks.Count * trackWidth + (visibleTracks.Count - 1) * _trackSpacing : 0f;
            float contentWidthX = _lithologyColumnWidth + (tracksWidth > 0 ? _trackSpacing : 0f) + tracksWidth;

            float row2Height = availAll.Y - HeaderHeight - BottomBarHeight;
            if (row2Height < 1f) row2Height = 1f;

            float depthRange     = Math.Max(1f, _depthEnd - _depthStart);
            float pixelsPerMeter = Math.Max(0.0001f, (row2Height - 20f) / depthRange * zoom);
            float gridInterval   = GetAdaptiveGridInterval(pixelsPerMeter);

            float maxDepthMeters = Math.Max(_dataset.TotalDepth, 10_000f);
            float totalHeight    = Math.Max(1f, (maxDepthMeters - _depthStart) * pixelsPerMeter);

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

            var origin   = ImGui.GetCursorScreenPos();
            var fullSize = availAll;

            // Frozen backgrounds (header strip + left depth bar)
            var dlRoot = ImGui.GetWindowDrawList();
            dlRoot.AddRectFilled(origin, origin + new Vector2(fullSize.X, HeaderHeight),
                                 ImGui.GetColorU32(new Vector4(0.20f, 0.20f, 0.20f, 1)));
            dlRoot.AddRectFilled(origin, origin + new Vector2(DepthScaleWidth, fullSize.Y),
                                 ImGui.GetColorU32(new Vector4(0.15f, 0.15f, 0.15f, 1)));

            // ---------------- Bottom horizontal scrollbar (single X) ----------------
            ImGui.SetCursorScreenPos(origin + new Vector2(DepthScaleWidth, HeaderHeight + row2Height));
            ImGui.BeginChild("BottomHSB",
                new Vector2(fullSize.X - DepthScaleWidth, BottomBarHeight),
                ImGuiChildFlags.None,
                ImGuiWindowFlags.AlwaysHorizontalScrollbar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

            if (reset) ImGui.SetScrollX(0);

            var bottomAvail       = ImGui.GetContentRegionAvail();
            float bottomContentW  = Math.Max(contentWidthX, bottomAvail.X + 1f);
            ImGui.Dummy(new Vector2(bottomContentW, 1f));
            float globalScrollX   = ImGui.GetScrollX();
            ImGui.EndChild();

            // ---------------- Header (reads X from bottom; no scrollbars) ----------------
            // Corner
            ImGui.SetCursorScreenPos(origin);
            ImGui.BeginChild("CornerFrozen", new Vector2(DepthScaleWidth, HeaderHeight), ImGuiChildFlags.None,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            ImGui.EndChild();

            // Header row
            ImGui.SetCursorScreenPos(origin + new Vector2(DepthScaleWidth, 0));
            ImGui.BeginChild("HeaderView",
                new Vector2(fullSize.X - DepthScaleWidth, HeaderHeight),
                ImGuiChildFlags.None,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

            var headerPos      = ImGui.GetCursorScreenPos();
            var headerViewport = ImGui.GetContentRegionAvail();
            {
                var dl = ImGui.GetWindowDrawList();
                var clipMin = headerPos;
                var clipMax = headerPos + new Vector2(headerViewport.X, HeaderHeight);
                dl.PushClipRect(clipMin, clipMax, true);

                float x = headerPos.X - globalScrollX;
                DrawLithologyHeader(dl, new Vector2(x, headerPos.Y), _lithologyColumnWidth);
                x += _lithologyColumnWidth;

                if (tracksWidth > 0)
                {
                    x += _trackSpacing;
                    foreach (var t in visibleTracks)
                    {
                        DrawTrackHeader(dl, t, new Vector2(x, headerPos.Y), _dataset.TrackWidth);
                        x += _dataset.TrackWidth + _trackSpacing;
                    }
                }

                dl.PopClipRect();
            }
            ImGui.EndChild();

            // ---------------- Body (single vertical scrollbar; source of Y) ----------------
            ImGui.SetCursorScreenPos(origin + new Vector2(DepthScaleWidth, HeaderHeight));
            ImGui.BeginChild("BodyVSB",
                new Vector2(fullSize.X - DepthScaleWidth, row2Height),
                ImGuiChildFlags.None,
                ImGuiWindowFlags.AlwaysVerticalScrollbar | ImGuiWindowFlags.NoScrollbar);

            if (reset) ImGui.SetScrollY(0);

            var bodyPos      = ImGui.GetCursorScreenPos();    // capture BEFORE dummy
            var bodyViewport = ImGui.GetContentRegionAvail(); // capture BEFORE dummy

            // define vertical scroll range
            ImGui.Dummy(new Vector2(Math.Max(1f, bodyViewport.X), Math.Max(totalHeight, bodyViewport.Y + 1f)));
            float globalScrollY = ImGui.GetScrollY();

            // Visible depth window (derived from single source of truth: globalScrollY)
            float visTopDepth    = _depthStart + globalScrollY / pixelsPerMeter;
            float visBottomDepth = visTopDepth + bodyViewport.Y / pixelsPerMeter;

            {
                var dl = ImGui.GetWindowDrawList();
                var clipMin = bodyPos;
                var clipMax = bodyPos + bodyViewport;
                dl.PushClipRect(clipMin, clipMax, true);

                // visible band background
                float xLeft  = bodyPos.X - globalScrollX;
                float xRight = xLeft + contentWidthX;
                dl.AddRectFilled(new Vector2(xLeft, bodyPos.Y),
                                 new Vector2(xRight, bodyPos.Y + bodyViewport.Y),
                                 ImGui.GetColorU32(new Vector4(0.10f, 0.10f, 0.10f, 1.0f)));

                // content origin aligned to shared scrolls
                float originX = xLeft;
                float originY = bodyPos.Y - globalScrollY;

                // lithology column
                DrawLithologyVisible(dl, new Vector2(originX, originY),
                    _lithologyColumnWidth, pixelsPerMeter, gridInterval, visTopDepth, visBottomDepth);

                float x = originX + _lithologyColumnWidth;

                // tracks
                if (tracksWidth > 0)
                {
                    x += _trackSpacing;
                    foreach (var t in visibleTracks)
                    {
                        DrawTrackVisible(dl, t, new Vector2(x, originY),
                            _dataset.TrackWidth, pixelsPerMeter, gridInterval, visTopDepth, visBottomDepth);
                        x += _dataset.TrackWidth + _trackSpacing;
                    }
                }

                dl.PopClipRect();
            }
            ImGui.EndChild();

            // ---------------- Left depth scale (no bar; hard-locked to body Y) ----------------
            ImGui.SetCursorScreenPos(origin + new Vector2(0, HeaderHeight));
            ImGui.BeginChild("DepthView",
                new Vector2(DepthScaleWidth, row2Height),
                ImGuiChildFlags.None,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

            var depthInnerPos = ImGui.GetCursorScreenPos();     // BEFORE dummy
            var depthViewport = ImGui.GetContentRegionAvail();  // BEFORE dummy

            ImGui.Dummy(new Vector2(1f, Math.Max(totalHeight, depthViewport.Y + 1f)));
            ImGui.SetScrollY(globalScrollY); // hard lock to body

            {
                var dl = ImGui.GetWindowDrawList();
                var clipMin = depthInnerPos;
                var clipMax = depthInnerPos + depthViewport;
                dl.PushClipRect(clipMin, clipMax, true);

                float originY = depthInnerPos.Y - globalScrollY;

                DrawDepthVisible(dl, new Vector2(depthInnerPos.X, originY),
                    pixelsPerMeter, gridInterval, visTopDepth, visBottomDepth);

                dl.PopClipRect();
            }
            ImGui.EndChild();

            // frozen separators
            var dlSep = ImGui.GetWindowDrawList();
            dlSep.AddLine(origin + new Vector2(DepthScaleWidth - 1, 0), origin + new Vector2(DepthScaleWidth - 1, fullSize.Y), ImGui.GetColorU32(ImGuiCol.Separator));
            dlSep.AddLine(origin + new Vector2(0, HeaderHeight - 1), origin + new Vector2(fullSize.X, HeaderHeight - 1), ImGui.GetColorU32(ImGuiCol.Separator));

            ImGui.PopStyleVar();

            // ---------------- Legend as a separate window ----------------
            if (_showLegend)
            {
                if (!_legendInit)
                {
                    ImGui.SetNextWindowPos(_legendInitPos, ImGuiCond.FirstUseEver);
                    ImGui.SetNextWindowSize(_legendInitSize, ImGuiCond.FirstUseEver);
                    _legendInit = true;
                }

                var flags =
                    ImGuiWindowFlags.NoSavedSettings |
                    ImGuiWindowFlags.AlwaysAutoResize |
                    ImGuiWindowFlags.NoCollapse;

                ImGui.Begin("Borehole Legend", ref _showLegend, flags);

                // tracks
                if (visibleTracks.Count > 0)
                {
                    ImGui.TextColored(_mutedText, "Tracks");
                    if (ImGui.BeginTable("tbl_tracks", 2, ImGuiTableFlags.SizingFixedFit))
                    {
                        ImGui.TableSetupColumn("Swatch", ImGuiTableColumnFlags.WidthFixed, 20);
                        ImGui.TableSetupColumn("Label",  ImGuiTableColumnFlags.WidthStretch);

                        foreach (var t in visibleTracks)
                        {
                            ImGui.TableNextRow();

                            // swatch
                            ImGui.TableSetColumnIndex(0);
                            DrawColorSwatch(t.Color);

                            // label
                            ImGui.TableSetColumnIndex(1);
                            var label = string.IsNullOrWhiteSpace(t.Unit) ? t.Name : $"{t.Name} [{t.Unit}]";
                            ImGui.TextUnformatted(label);
                        }

                        ImGui.EndTable();
                    }
                }

                // lithologies (unique types, derive color from first matching unit or fallback)
                var lithoTypes = _dataset.LithologyUnits
                    .Select(u => u.LithologyType)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct()
                    .OrderBy(s => s)
                    .ToList();

                if (lithoTypes.Count > 0)
                {
                    if (visibleTracks.Count > 0) ImGui.Separator();
                    ImGui.TextColored(_mutedText, "Lithologies");

                    if (ImGui.BeginTable("tbl_litho", 2, ImGuiTableFlags.SizingFixedFit))
                    {
                        ImGui.TableSetupColumn("Swatch", ImGuiTableColumnFlags.WidthFixed, 20);
                        ImGui.TableSetupColumn("Label",  ImGuiTableColumnFlags.WidthStretch);

                        foreach (var lt in lithoTypes)
                        {
                            var first = _dataset.LithologyUnits.FirstOrDefault(u => u.LithologyType == lt);
                            var col = first != null ? first.Color : GetDefaultLithologyColor(lt);

                            ImGui.TableNextRow();

                            // swatch (with a faint hatch overlay)
                            ImGui.TableSetColumnIndex(0);
                            DrawColorSwatch(col, hatch: true);

                            // label
                            ImGui.TableSetColumnIndex(1);
                            ImGui.TextUnformatted(lt);
                        }

                        ImGui.EndTable();
                    }
                }

                ImGui.End(); // Borehole Legend
            }

            HandleInput(ref zoom);
        }

        private float GetAdaptiveGridInterval(float ppm)
        {
            if (ppm <= 0) return 1000f;
            const float targetPx = 80f;
            var d   = targetPx / ppm;
            var p10 = Math.Pow(10, Math.Floor(Math.Log10(d)));
            var n   = d / p10;
            if (n < 1.5) return (float)(1 * p10);
            if (n < 3.5) return (float)(2 * p10);
            if (n < 7.5) return (float)(5 * p10);
            return (float)(10 * p10);
        }

        public void Dispose() { }

        // ---------------- Legend helpers ----------------

        private void DrawColorSwatch(Vector4 col, bool hatch = false)
        {
            // Draw a small 14x12 swatch occupying the current cursor row height.
            var dl = ImGui.GetWindowDrawList();
            var p  = ImGui.GetCursorScreenPos();
            var sz = new Vector2(14f, 12f);

            dl.AddRectFilled(p, p + sz, ImGui.GetColorU32(col), 2f);
            dl.AddRect(p, p + sz, ImGui.GetColorU32(new Vector4(0, 0, 0, 1)), 2f);

            if (hatch)
            {
                for (float xx = p.X; xx < p.X + sz.X; xx += 4f)
                    dl.AddLine(new Vector2(xx, p.Y), new Vector2(xx + 4f, p.Y + sz.Y), ImGui.GetColorU32(new Vector4(0, 0, 0, 0.25f)));
            }

            ImGui.Dummy(sz);
        }

        private Vector4 GetDefaultLithologyColor(string lithologyType)
        {
            // Conservative, readable defaults if no unit is available to sample from
            return lithologyType switch
            {
                "Sandstone"    => new Vector4(0.80f, 0.70f, 0.55f, 1.0f),
                "Shale"        => new Vector4(0.45f, 0.45f, 0.40f, 1.0f),
                "Limestone"    => new Vector4(0.85f, 0.85f, 0.80f, 1.0f),
                "Clay"         => new Vector4(0.65f, 0.55f, 0.45f, 1.0f),
                "Siltstone"    => new Vector4(0.70f, 0.65f, 0.55f, 1.0f),
                "Conglomerate" => new Vector4(0.65f, 0.65f, 0.60f, 1.0f),
                "Granite"      => new Vector4(0.65f, 0.60f, 0.55f, 1.0f),
                "Basalt"       => new Vector4(0.40f, 0.35f, 0.30f, 1.0f),
                "Dolomite"     => new Vector4(0.80f, 0.75f, 0.70f, 1.0f),
                "Sand"         => new Vector4(0.85f, 0.80f, 0.65f, 1.0f),
                "Soil"         => new Vector4(0.55f, 0.45f, 0.35f, 1.0f),
                _              => new Vector4(0.60f, 0.60f, 0.60f, 1.0f),
            };
        }

        // ---------------- drawing helpers ----------------

        private void DrawDepthVisible(ImDrawListPtr dl, Vector2 pos, float ppm, float step, float top, float bottom)
        {
            float start = (float)Math.Floor(top / step) * step - step;
            float end   = (float)Math.Ceiling(bottom / step) * step + step;

            for (float d = Math.Max(0, start); d <= end; d += step)
            {
                float y = pos.Y + (d - _depthStart) * ppm;

                // tick
                dl.AddLine(new Vector2(pos.X + DepthScaleWidth - 12, y),
                           new Vector2(pos.X + DepthScaleWidth - 2,  y),
                           ImGui.GetColorU32(_textColor), 1f);

                // label
                string label = step >= 1000 ? $"{d / 1000f:F1} km" : $"{d:0} m";
                var ts = ImGui.CalcTextSize(label);
                dl.AddText(new Vector2(pos.X + DepthScaleWidth - ts.X - 14, y - ts.Y * 0.5f),
                           ImGui.GetColorU32(_depthTextColor),
                           label);
            }
        }

        private void DrawLithologyHeader(ImDrawListPtr dl, Vector2 pos, float width)
        {
            var t = "Lithology";
            var s = ImGui.CalcTextSize(t);
            dl.AddText(pos + new Vector2((width - s.X) * 0.5f, (HeaderHeight - s.Y) * 0.5f),
                       ImGui.GetColorU32(_textColor), t);
        }

        private void DrawTrackHeader(ImDrawListPtr dl, ParameterTrack track, Vector2 pos, float width)
        {
            var t = track.Name; var ts = ImGui.CalcTextSize(t);
            dl.AddText(pos + new Vector2((width - ts.X) * 0.5f, 2), ImGui.GetColorU32(_textColor), t);
            var u = string.IsNullOrWhiteSpace(track.Unit) ? "" : $"({track.Unit})";
            if (!string.IsNullOrEmpty(u))
            {
                var us = ImGui.CalcTextSize(u);
                dl.AddText(pos + new Vector2((width - us.X) * 0.5f, 14), ImGui.GetColorU32(new Vector4(0.6f, 0.6f, 0.6f, 1)), u);
            }
        }

        private void DrawLithologyVisible(ImDrawListPtr dl, Vector2 origin, float width,
                                          float ppm, float step, float top, float bottom)
        {
            float yTop = origin.Y + (top    - _depthStart) * ppm;
            float yBot = origin.Y + (bottom - _depthStart) * ppm;

            dl.AddRect(origin + new Vector2(0, yTop - origin.Y),
                       origin + new Vector2(width, yBot - origin.Y),
                       ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 1)), 0, ImDrawFlags.None, 1f);

            foreach (var u in _dataset.LithologyUnits)
            {
                if (u.DepthTo < top || u.DepthFrom > bottom) continue;

                float y1 = origin.Y + (u.DepthFrom - _depthStart) * ppm;
                float y2 = origin.Y + (u.DepthTo   - _depthStart) * ppm;

                float y1c = Math.Max(y1, yTop);
                float y2c = Math.Min(y2, yBot);
                if (y2c <= y1c + 0.5f) continue;

                DrawLithologyPattern(dl, new Vector2(origin.X, y1c),
                                     new Vector2(width, y2c - y1c),
                                     u.Color, GetPatternForLithology(u.LithologyType));

                dl.AddRect(new Vector2(origin.X, y1), new Vector2(origin.X + width, y2),
                           ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 1)), 0, ImDrawFlags.None, 1f);

                if (_showLithologyNames && (y2 - y1) > 20f)
                {
                    var name = u.Name; var ts = ImGui.CalcTextSize(name);
                    if (ts.Y < (y2 - y1) - 4f)
                        dl.AddText(new Vector2(origin.X + (width - ts.X) * 0.5f, y1 + ((y2 - y1) - ts.Y) * 0.5f),
                                   ImGui.GetColorU32(new Vector4(0, 0, 0, 1)), name);
                }
            }

            if (_showDepthGrid)
            {
                float start = (float)Math.Floor(top / step) * step - step;
                float end   = (float)Math.Ceiling(bottom / step) * step + step;
                for (float d = Math.Max(0, start); d <= end; d += step)
                {
                    float y = origin.Y + (d - _depthStart) * ppm;
                    dl.AddLine(new Vector2(origin.X, y), new Vector2(origin.X + width, y), ImGui.GetColorU32(_gridColor), 1f);
                }
            }
        }

        private void DrawTrackVisible(ImDrawListPtr dl, ParameterTrack track, Vector2 origin,
                                      float width, float ppm, float step, float top, float bottom)
        {
            float yTop = origin.Y + (top    - _depthStart) * ppm;
            float yBot = origin.Y + (bottom - _depthStart) * ppm;

            dl.AddRectFilled(new Vector2(origin.X, yTop), new Vector2(origin.X + width, yBot),
                             ImGui.GetColorU32(new Vector4(0.10f, 0.10f, 0.10f, 1)));
            dl.AddRect(new Vector2(origin.X, yTop), new Vector2(origin.X + width, yBot),
                       ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 1)), 0, ImDrawFlags.None, 1f);

            if (track.Points.Count >= 2)
            {
                var pts = track.Points.OrderBy(p => p.Depth).ToList();
                float margin = (bottom - top) * 0.2f;
                float fromD  = Math.Max(0, top - margin);
                float toD    = bottom + margin;

                for (int i = 0; i < pts.Count - 1; i++)
                {
                    var p1 = pts[i]; var p2 = pts[i + 1];
                    if (Math.Max(p1.Depth, p2.Depth) < fromD || Math.Min(p1.Depth, p2.Depth) > toD) continue;

                    float y1 = origin.Y + (p1.Depth - _depthStart) * ppm;
                    float y2 = origin.Y + (p2.Depth - _depthStart) * ppm;

                    float x1, x2;
                    if (track.IsLogarithmic)
                    {
                        var logMin = (float)Math.Log10(Math.Max(track.MinValue, 0.001f));
                        var logMax = (float)Math.Log10(Math.Max(track.MaxValue, 0.001f));
                        var v1 = (float)Math.Log10(Math.Max(p1.Value, 0.001f));
                        var v2 = (float)Math.Log10(Math.Max(p2.Value, 0.001f));
                        x1 = origin.X + ((v1 - logMin) / Math.Max(1e-6f, (logMax - logMin))) * width;
                        x2 = origin.X + ((v2 - logMin) / Math.Max(1e-6f, (logMax - logMin))) * width;
                    }
                    else
                    {
                        x1 = origin.X + ((p1.Value - track.MinValue) / Math.Max(1e-6f, (track.MaxValue - track.MinValue))) * width;
                        x2 = origin.X + ((p2.Value - track.MinValue) / Math.Max(1e-6f, (track.MaxValue - track.MinValue))) * width;
                    }

                    x1 = Math.Clamp(x1, origin.X, origin.X + width);
                    x2 = Math.Clamp(x2, origin.X, origin.X + width);

                    dl.AddLine(new Vector2(x1, y1), new Vector2(x2, y2), ImGui.GetColorU32(track.Color), 2f);
                }
            }

            if (_showDepthGrid)
            {
                float start = (float)Math.Floor(top / step) * step - step;
                float end   = (float)Math.Ceiling(bottom / step) * step + step;
                for (float d = Math.Max(0, start); d <= end; d += step)
                {
                    float y = origin.Y + (d - _depthStart) * ppm;
                    dl.AddLine(new Vector2(origin.X, y), new Vector2(origin.X + width, y), ImGui.GetColorU32(_gridColor), 1f);
                }
            }
        }

        private void DrawLithologyPattern(ImDrawListPtr dl, Vector2 pos, Vector2 size, Vector4 color, LithologyPattern pattern)
        {
            var c  = ImGui.GetColorU32(color);
            var pc = ImGui.GetColorU32(new Vector4(color.X * 0.7f, color.Y * 0.7f, color.Z * 0.7f, color.W));
            dl.AddRectFilled(pos, pos + size, c);

            switch (pattern)
            {
                case LithologyPattern.Dots:
                    for (float yy = 0; yy < size.Y; yy += 8)
                        for (float xx = 0; xx < size.X; xx += 8)
                            dl.AddCircleFilled(pos + new Vector2(xx + 4, yy + 4), 1.5f, pc);
                    break;
                case LithologyPattern.HorizontalLines:
                    for (float yy = 0; yy < size.Y; yy += 6)
                        dl.AddLine(pos + new Vector2(0, yy), pos + new Vector2(size.X, yy), pc, 1.5f);
                    break;
                case LithologyPattern.VerticalLines:
                    for (float xx = 0; xx < size.X; xx += 6)
                        dl.AddLine(pos + new Vector2(xx, 0), pos + new Vector2(xx, size.Y), pc, 1.5f);
                    break;
                case LithologyPattern.Diagonal:
                    for (float i = -size.Y; i < size.X + size.Y; i += 8)
                        dl.AddLine(pos + new Vector2(i, 0), pos + new Vector2(i + size.Y, size.Y), pc, 1.5f);
                    break;
                case LithologyPattern.Crosses:
                    for (float yy = 0; yy < size.Y; yy += 10)
                        for (float xx = 0; xx < size.X; xx += 10)
                        {
                            var c0 = pos + new Vector2(xx + 5, yy + 5);
                            dl.AddLine(c0 - new Vector2(3, 0), c0 + new Vector2(3, 0), pc, 1.5f);
                            dl.AddLine(c0 - new Vector2(0, 3), c0 + new Vector2(0, 3), pc, 1.5f);
                        }
                    break;
                case LithologyPattern.Sand:
                    var rnd = new Random(0);
                    for (int i = 0; i < (int)(size.X * size.Y / 20); i++)
                    {
                        float xx = (float)rnd.NextDouble() * size.X;
                        float yy = (float)rnd.NextDouble() * size.Y;
                        dl.AddCircleFilled(pos + new Vector2(xx, yy), 1f, pc);
                    }
                    break;
                case LithologyPattern.Bricks:
                    for (float yy = 0; yy < size.Y; yy += 10)
                    {
                        float off = ((int)(yy / 10) % 2 == 0) ? 0f : 15f;
                        for (float xx = -15f; xx < size.X; xx += 30)
                            dl.AddRect(pos + new Vector2(xx + off, yy), pos + new Vector2(xx + off + 28, yy + 8), pc);
                    }
                    break;
                case LithologyPattern.Limestone:
                    rnd = new Random(1);
                    for (int i = 0; i < (int)(size.X * size.Y / 30); i++)
                    {
                        float xx = (float)rnd.NextDouble() * size.X;
                        float yy = (float)rnd.NextDouble() * size.Y;
                        float r  = (float)rnd.NextDouble() * 2 + 1;
                        dl.AddCircle(pos + new Vector2(xx, yy), r, pc);
                    }
                    break;
                case LithologyPattern.Solid:
                default:
                    break;
            }
        }

        private LithologyPattern GetPatternForLithology(string lithologyType)
        {
            if (_dataset.LithologyPatterns.TryGetValue(lithologyType, out var pattern))
                return pattern;
            return LithologyPattern.Solid;
        }

        private void HandleInput(ref float zoom)
        {
            var io = ImGui.GetIO();
            if (!ImGui.IsWindowHovered()) return;

            if (io.MouseWheel != 0)
            {
                const float zf = 1.2f;
                zoom = io.MouseWheel > 0 ? zoom * zf : zoom / zf;
                zoom = Math.Clamp(zoom, 0.01f, 20f);
            }
        }
    }
}
