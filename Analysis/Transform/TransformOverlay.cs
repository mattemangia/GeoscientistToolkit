// GeoscientistToolkit/Analysis/Transform/TransformOverlay.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Data.CtImageStack;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.Transform
{
    public interface IOverlay
    {
        void DrawOnSlice(ImDrawListPtr dl, int viewIndex, Vector2 imagePos, Vector2 imageSize, int imageWidth, int imageHeight);
        bool HandleMouseInput(Vector2 mousePos, Vector2 imagePos, Vector2 imageSize, int imageWidth, int imageHeight, int viewIndex, bool clicked, bool dragging, bool released);
        CtImageStackDataset Dataset { get; }
    }

    // -------------------------- TRANSFORM OVERLAY ------------------------------
    public class TransformOverlay : IOverlay
    {
        private enum HandleType { None, TranslateX, TranslateY, TranslateZ, RotateX, RotateY, RotateZ, Scale }

        private readonly TransformTool _tool;
        public CtImageStackDataset Dataset { get; }

        private HandleType _activeHandle = HandleType.None;
        private HandleType _hoveredHandle = HandleType.None;
        private Vector2 _dragStartMousePos;
        private Vector3 _dragStartTranslation;
        private Vector3 _dragStartRotation;
        private Vector3 _dragStartScale;

        private const float HandleSize = 8f;
        private readonly Dictionary<HandleType, Vector3> _handlePositions3D = new();
        private readonly Dictionary<HandleType, Vector2> _handlePositions2D = new();

        public TransformOverlay(TransformTool tool, CtImageStackDataset dataset)
        {
            _tool = tool ?? throw new ArgumentNullException(nameof(tool));
            Dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        }

        public void DrawOnSlice(ImDrawListPtr dl, int viewIndex, Vector2 imagePos, Vector2 imageSize,
            int imageWidth, int imageHeight)
        {
            if (!_tool.ShowPreview) return;

            var transform = _tool.GetTransformMatrix();
            var (cropMin, cropMax) = _tool.GetCropBounds();

            var srcMin = new Vector3(Dataset.Width * cropMin.X, Dataset.Height * cropMin.Y, Dataset.Depth * cropMin.Z);
            var srcMax = new Vector3(Dataset.Width * cropMax.X, Dataset.Height * cropMax.Y, Dataset.Depth * cropMax.Z);

            var corners = GetBoundingBoxCorners(srcMin, srcMax, transform);
            var screenCorners = corners.Select(c => ProjectToView(c, viewIndex, imagePos, imageSize)).ToArray();

            uint color = _activeHandle != HandleType.None ? 0xFF00FFFF : 0xFFFFA500;

            // 12 edges
            DrawEdge(dl, screenCorners[0], screenCorners[1], color); DrawEdge(dl, screenCorners[1], screenCorners[2], color);
            DrawEdge(dl, screenCorners[2], screenCorners[3], color); DrawEdge(dl, screenCorners[3], screenCorners[0], color);
            DrawEdge(dl, screenCorners[4], screenCorners[5], color); DrawEdge(dl, screenCorners[5], screenCorners[6], color);
            DrawEdge(dl, screenCorners[6], screenCorners[7], color); DrawEdge(dl, screenCorners[7], screenCorners[4], color);
            DrawEdge(dl, screenCorners[0], screenCorners[4], color); DrawEdge(dl, screenCorners[1], screenCorners[5], color);
            DrawEdge(dl, screenCorners[2], screenCorners[6], color); DrawEdge(dl, screenCorners[3], screenCorners[7], color);

            ComputeHandlePositions(corners, viewIndex, imagePos, imageSize);
            foreach (var kvp in _handlePositions2D)
            {
                uint hc = GetHandleColor(kvp.Key);
                dl.AddCircleFilled(kvp.Value, HandleSize, hc);
                dl.AddCircle(kvp.Value, HandleSize, 0xFFFFFFFF, 12, 1.5f);
            }
        }

        public bool HandleMouseInput(Vector2 mousePos, Vector2 imagePos, Vector2 imageSize,
            int imageWidth, int imageHeight, int viewIndex, bool clicked, bool dragging, bool released)
        {
            if (!_tool.ShowPreview) return false;

            var transform = _tool.GetTransformMatrix();
            var (cropMin, cropMax) = _tool.GetCropBounds();
            var srcMin = new Vector3(Dataset.Width * cropMin.X, Dataset.Height * cropMin.Y, Dataset.Depth * cropMin.Z);
            var srcMax = new Vector3(Dataset.Width * cropMax.X, Dataset.Height * cropMax.Y, Dataset.Depth * cropMax.Z);
            var corners = GetBoundingBoxCorners(srcMin, srcMax, transform);
            ComputeHandlePositions(corners, viewIndex, imagePos, imageSize);

            if (clicked)
            {
                _hoveredHandle = HitTestHandles(mousePos);
                if (_hoveredHandle != HandleType.None)
                {
                    _activeHandle = _hoveredHandle;
                    _dragStartMousePos = mousePos;
                    _dragStartTranslation = _tool._translation;
                    _dragStartRotation = _tool._rotation;
                    _dragStartScale = _tool._scale;
                    return true;
                }
            }

            if (dragging && _activeHandle != HandleType.None)
            {
                var mouseDelta = mousePos - _dragStartMousePos;

                Vector3 wDelta = viewIndex switch
                {
                    0 => new Vector3(mouseDelta.X / imageSize.X * imageWidth,  mouseDelta.Y / imageSize.Y * imageHeight, 0),
                    1 => new Vector3(mouseDelta.X / imageSize.X * imageWidth,  0,                                     mouseDelta.Y / imageSize.Y * imageHeight),
                    2 => new Vector3(0,                                      mouseDelta.X / imageSize.X * imageWidth,  mouseDelta.Y / imageSize.Y * imageHeight),
                    _ => Vector3.Zero
                };

                switch (_activeHandle)
                {
                    case HandleType.TranslateX:
                    case HandleType.TranslateY:
                    case HandleType.TranslateZ:
                    {
                        var t = _dragStartTranslation + new Vector3(
                            _activeHandle == HandleType.TranslateX ? wDelta.X : 0,
                            _activeHandle == HandleType.TranslateY ? wDelta.Y : 0,
                            _activeHandle == HandleType.TranslateZ ? wDelta.Z : 0);

                        if (_tool.SnapEnabled)
                        {
                            t.X = _tool.Snap(t.X, _tool.SnapTranslationStep);
                            t.Y = _tool.Snap(t.Y, _tool.SnapTranslationStep);
                            t.Z = _tool.Snap(t.Z, _tool.SnapTranslationStep);
                        }
                        _tool.SetTranslation(t);
                        break;
                    }
                    case HandleType.RotateX:
                    case HandleType.RotateY:
                    case HandleType.RotateZ:
                    {
                        var r = _dragStartRotation + new Vector3(
                            _activeHandle == HandleType.RotateX ? mouseDelta.Y * 0.5f : 0,
                            _activeHandle == HandleType.RotateY ? -mouseDelta.X * 0.5f : 0,
                            _activeHandle == HandleType.RotateZ ? -mouseDelta.X * 0.5f : 0);

                        if (_tool.SnapEnabled)
                        {
                            r.X = _tool.Snap(r.X, _tool.SnapRotationStep);
                            r.Y = _tool.Snap(r.Y, _tool.SnapRotationStep);
                            r.Z = _tool.Snap(r.Z, _tool.SnapRotationStep);
                        }
                        _tool.SetRotation(r);
                        break;
                    }
                    case HandleType.Scale:
                    {
                        float scaleFactor = 1.0f + (mouseDelta.X - mouseDelta.Y) * 0.005f;
                        var s = _dragStartScale * scaleFactor;
                        if (_tool.SnapEnabled)
                        {
                            s.X = _tool.Snap(s.X, _tool.SnapScaleStep);
                            s.Y = _tool.Snap(s.Y, _tool.SnapScaleStep);
                            s.Z = _tool.Snap(s.Z, _tool.SnapScaleStep);
                        }
                        _tool.SetScale(s);
                        break;
                    }
                }
                return true;
            }

            if (released && _activeHandle != HandleType.None)
            {
                _activeHandle = HandleType.None;
                return true;
            }

            if (_activeHandle == HandleType.None)
            {
                _hoveredHandle = HitTestHandles(mousePos);
                if (_hoveredHandle != HandleType.None) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            }

            return _activeHandle != HandleType.None;
        }

        // ----- helpers
        private void ComputeHandlePositions(Vector3[] corners, int viewIndex, Vector2 imagePos, Vector2 imageSize)
        {
            _handlePositions3D.Clear();
            _handlePositions2D.Clear();

            _handlePositions3D[HandleType.TranslateX] = (corners[1] + corners[2] + corners[5] + corners[6]) * 0.25f;
            _handlePositions3D[HandleType.TranslateY] = (corners[2] + corners[3] + corners[6] + corners[7]) * 0.25f;
            _handlePositions3D[HandleType.TranslateZ] = (corners[4] + corners[5] + corners[6] + corners[7]) * 0.25f;

            _handlePositions3D[HandleType.RotateZ] = (corners[0] + corners[1]) * 0.5f;
            _handlePositions3D[HandleType.RotateY] = (corners[3] + corners[7]) * 0.5f;
            _handlePositions3D[HandleType.RotateX] = (corners[0] + corners[4]) * 0.5f;

            _handlePositions3D[HandleType.Scale] = corners[6];

            foreach (var kvp in _handlePositions3D)
            {
                var p = ProjectToView(kvp.Value, viewIndex, imagePos, imageSize);
                _handlePositions2D[kvp.Key] = p;
            }
        }

        private HandleType HitTestHandles(Vector2 mousePos)
        {
            foreach (var kvp in _handlePositions2D.Reverse())
            {
                if (Vector2.Distance(mousePos, kvp.Value) <= HandleSize + 2)
                    return kvp.Key;
            }
            return HandleType.None;
        }

        private uint GetHandleColor(HandleType t)
        {
            if (t == _activeHandle || t == _hoveredHandle) return 0xFF00FFFF;
            return t switch
            {
                HandleType.TranslateX => 0xFF0000FF,
                HandleType.TranslateY => 0xFF00FF00,
                HandleType.TranslateZ => 0xFFFF0000,
                HandleType.RotateX => 0xFF00008B,
                HandleType.RotateY => 0xFF008B00,
                HandleType.RotateZ => 0xFF8B0000,
                HandleType.Scale => 0xFFFFFFFF,
                _ => 0xFF808080
            };
        }

        private Vector3[] GetBoundingBoxCorners(Vector3 min, Vector3 max, Matrix4x4 t)
        {
            return new[]
            {
                Vector3.Transform(new Vector3(min.X, min.Y, min.Z), t),
                Vector3.Transform(new Vector3(max.X, min.Y, min.Z), t),
                Vector3.Transform(new Vector3(max.X, max.Y, min.Z), t),
                Vector3.Transform(new Vector3(min.X, max.Y, min.Z), t),
                Vector3.Transform(new Vector3(min.X, min.Y, max.Z), t),
                Vector3.Transform(new Vector3(max.X, min.Y, max.Z), t),
                Vector3.Transform(new Vector3(max.X, max.Y, max.Z), t),
                Vector3.Transform(new Vector3(min.X, max.Y, max.Z), t),
            };
        }

        private Vector2 ProjectToView(Vector3 p3, int viewIndex, Vector2 imagePos, Vector2 imageSize)
        {
            Vector2 uv, norm;
            switch (viewIndex)
            {
                case 0: uv = new Vector2(p3.X, p3.Y); norm = uv / new Vector2(Dataset.Width, Dataset.Height); break;
                case 1: uv = new Vector2(p3.X, p3.Z); norm = uv / new Vector2(Dataset.Width, Dataset.Depth); break;
                case 2: uv = new Vector2(p3.Y, p3.Z); norm = uv / new Vector2(Dataset.Height, Dataset.Depth); break;
                default: return Vector2.Zero;
            }
            return imagePos + new Vector2(norm.X * imageSize.X, norm.Y * imageSize.Y);
        }

        private void DrawEdge(ImDrawListPtr dl, Vector2 a, Vector2 b, uint col) => dl.AddLine(a, b, col, 1.5f);
    }

    // ---------------------------- CROP OVERLAY --------------------------------
    public class CropOverlay : IOverlay
    {
        private enum H
        {
            None,
            // Faces
            MinX, MaxX, MinY, MaxY, MinZ, MaxZ,
            // Edges (two axes)
            Edge_MinX_MinY, Edge_MinX_MaxY, Edge_MaxX_MinY, Edge_MaxX_MaxY,
            Edge_MinX_MinZ, Edge_MinX_MaxZ, Edge_MaxX_MinZ, Edge_MaxX_MaxZ,
            Edge_MinY_MinZ, Edge_MinY_MaxZ, Edge_MaxY_MinZ, Edge_MaxY_MaxZ,
            // Corners (three axes)
            Corner_MinX_MinY_MinZ, Corner_MaxX_MinY_MinZ,
            Corner_MinX_MaxY_MinZ, Corner_MaxX_MaxY_MinZ,
            Corner_MinX_MinY_MaxZ, Corner_MaxX_MinY_MaxZ,
            Corner_MinX_MaxY_MaxZ, Corner_MaxX_MaxY_MaxZ
        }

        private readonly TransformTool _tool;
        public CtImageStackDataset Dataset { get; }

        private const float HandleSize = 7f;
        private readonly Dictionary<H, Vector3> _h3 = new();
        private readonly Dictionary<H, Vector2> _h2 = new();

        private H _active = H.None;
        private H _hover = H.None;

        private Vector2 _dragStartMouse;
        private Vector3 _startMinNorm, _startMaxNorm;

        public CropOverlay(TransformTool tool, CtImageStackDataset dataset)
        {
            _tool = tool ?? throw new ArgumentNullException(nameof(tool));
            Dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        }

        public void DrawOnSlice(ImDrawListPtr dl, int viewIndex, Vector2 imagePos, Vector2 imageSize, int imageWidth, int imageHeight)
        {
            var (minN, maxN) = _tool.GetCropBounds();
            var min = new Vector3(Dataset.Width * minN.X, Dataset.Height * minN.Y, Dataset.Depth * minN.Z);
            var max = new Vector3(Dataset.Width * maxN.X, Dataset.Height * maxN.Y, Dataset.Depth * maxN.Z);

            var corners = GetBoundingBoxCorners(min, max);
            var sc = corners.Select(c => ProjectToView(c, viewIndex, imagePos, imageSize)).ToArray();

            uint col = _active != H.None ? 0xFF00FFFF : 0xFFE67E22;
            // 12 edges
            DrawEdge(dl, sc[0], sc[1], col); DrawEdge(dl, sc[1], sc[2], col);
            DrawEdge(dl, sc[2], sc[3], col); DrawEdge(dl, sc[3], sc[0], col);
            DrawEdge(dl, sc[4], sc[5], col); DrawEdge(dl, sc[5], sc[6], col);
            DrawEdge(dl, sc[6], sc[7], col); DrawEdge(dl, sc[7], sc[4], col);
            DrawEdge(dl, sc[0], sc[4], col); DrawEdge(dl, sc[1], sc[5], col);
            DrawEdge(dl, sc[2], sc[6], col); DrawEdge(dl, sc[3], sc[7], col);

            ComputeHandles(min, max, viewIndex, imagePos, imageSize);
            foreach (var kv in _h2)
            {
                uint hc = (kv.Key == _active || kv.Key == _hover) ? 0xFF00FFFF : 0xFFFFFFFF;
                dl.AddCircleFilled(kv.Value, HandleSize, hc);
                dl.AddCircle(kv.Value, HandleSize, 0xFF000000, 12, 1.5f);
            }
        }

        public bool HandleMouseInput(Vector2 mousePos, Vector2 imagePos, Vector2 imageSize,
            int imageWidth, int imageHeight, int viewIndex, bool clicked, bool dragging, bool released)
        {
            var (minN, maxN) = _tool.GetCropBounds();

            if (clicked)
            {
                _hover = Hit(mousePos);
                if (_hover != H.None)
                {
                    _active = _hover;
                    _dragStartMouse = mousePos;
                    _startMinNorm = minN;
                    _startMaxNorm = maxN;
                    return true;
                }
            }

            if (dragging && _active != H.None)
            {
                var delta = mousePos - _dragStartMouse;

                Vector3 wDelta = viewIndex switch
                {
                    0 => new Vector3(delta.X / imageSize.X * Dataset.Width,  delta.Y / imageSize.Y * Dataset.Height, 0),
                    1 => new Vector3(delta.X / imageSize.X * Dataset.Width,  0,                                    delta.Y / imageSize.Y * Dataset.Depth),
                    2 => new Vector3(0,                                    delta.X / imageSize.X * Dataset.Height, delta.Y / imageSize.Y * Dataset.Depth),
                    _ => Vector3.Zero
                };

                var minV = new Vector3(_startMinNorm.X * Dataset.Width,  _startMinNorm.Y * Dataset.Height,  _startMinNorm.Z * Dataset.Depth);
                var maxV = new Vector3(_startMaxNorm.X * Dataset.Width,  _startMaxNorm.Y * Dataset.Height,  _startMaxNorm.Z * Dataset.Depth);

                void applyAxis(ref float min, ref float max, float d, bool isMin)
                {
                    if (_tool.UniformCropFromCenter)
                    {
                        // symmetric about center for that axis
                        float c = (min + max) * 0.5f;
                        float half = (max - min) * 0.5f + (isMin ? -d : d);
                        half = MathF.Max(0.25f, half); // avoid collapse
                        min = c - half; max = c + half;
                    }
                    else
                    {
                        if (isMin) min += d; else max += d;
                    }
                }

                // Which axes are affected by the active handle?
                switch (_active)
                {
                    // Faces
                    case H.MinX: applyAxis(ref minV.X, ref maxV.X, wDelta.X, true); break;
                    case H.MaxX: applyAxis(ref minV.X, ref maxV.X, wDelta.X, false); break;
                    case H.MinY: applyAxis(ref minV.Y, ref maxV.Y, (viewIndex == 2 ? wDelta.X : wDelta.Y), true); break;
                    case H.MaxY: applyAxis(ref minV.Y, ref maxV.Y, (viewIndex == 2 ? wDelta.X : wDelta.Y), false); break;
                    case H.MinZ: applyAxis(ref minV.Z, ref maxV.Z, (viewIndex == 0 ? 0 : wDelta.Z), true); break;
                    case H.MaxZ: applyAxis(ref minV.Z, ref maxV.Z, (viewIndex == 0 ? 0 : wDelta.Z), false); break;

                    // Edges: two axes
                    case H.Edge_MinX_MinY: applyAxis(ref minV.X, ref maxV.X, wDelta.X, true); applyAxis(ref minV.Y, ref maxV.Y, (viewIndex==2?wDelta.X:wDelta.Y), true); break;
                    case H.Edge_MinX_MaxY: applyAxis(ref minV.X, ref maxV.X, wDelta.X, true); applyAxis(ref minV.Y, ref maxV.Y, (viewIndex==2?wDelta.X:wDelta.Y), false); break;
                    case H.Edge_MaxX_MinY: applyAxis(ref minV.X, ref maxV.X, wDelta.X, false); applyAxis(ref minV.Y, ref maxV.Y, (viewIndex==2?wDelta.X:wDelta.Y), true); break;
                    case H.Edge_MaxX_MaxY: applyAxis(ref minV.X, ref maxV.X, wDelta.X, false); applyAxis(ref minV.Y, ref maxV.Y, (viewIndex==2?wDelta.X:wDelta.Y), false); break;

                    case H.Edge_MinX_MinZ: applyAxis(ref minV.X, ref maxV.X, wDelta.X, true);  applyAxis(ref minV.Z, ref maxV.Z, (viewIndex==0?0:wDelta.Z), true); break;
                    case H.Edge_MinX_MaxZ: applyAxis(ref minV.X, ref maxV.X, wDelta.X, true);  applyAxis(ref minV.Z, ref maxV.Z, (viewIndex==0?0:wDelta.Z), false); break;
                    case H.Edge_MaxX_MinZ: applyAxis(ref minV.X, ref maxV.X, wDelta.X, false); applyAxis(ref minV.Z, ref maxV.Z, (viewIndex==0?0:wDelta.Z), true); break;
                    case H.Edge_MaxX_MaxZ: applyAxis(ref minV.X, ref maxV.X, wDelta.X, false); applyAxis(ref minV.Z, ref maxV.Z, (viewIndex==0?0:wDelta.Z), false); break;

                    case H.Edge_MinY_MinZ: applyAxis(ref minV.Y, ref maxV.Y, (viewIndex==2?wDelta.X:wDelta.Y), true);  applyAxis(ref minV.Z, ref maxV.Z, (viewIndex==0?0:wDelta.Z), true); break;
                    case H.Edge_MinY_MaxZ: applyAxis(ref minV.Y, ref maxV.Y, (viewIndex==2?wDelta.X:wDelta.Y), true);  applyAxis(ref minV.Z, ref maxV.Z, (viewIndex==0?0:wDelta.Z), false); break;
                    case H.Edge_MaxY_MinZ: applyAxis(ref minV.Y, ref maxV.Y, (viewIndex==2?wDelta.X:wDelta.Y), false); applyAxis(ref minV.Z, ref maxV.Z, (viewIndex==0?0:wDelta.Z), true); break;
                    case H.Edge_MaxY_MaxZ: applyAxis(ref minV.Y, ref maxV.Y, (viewIndex==2?wDelta.X:wDelta.Y), false); applyAxis(ref minV.Z, ref maxV.Z, (viewIndex==0?0:wDelta.Z), false); break;

                    // Corners: three axes
                    case H.Corner_MinX_MinY_MinZ:
                    case H.Corner_MinX_MinY_MaxZ:
                    case H.Corner_MinX_MaxY_MinZ:
                    case H.Corner_MinX_MaxY_MaxZ:
                    case H.Corner_MaxX_MinY_MinZ:
                    case H.Corner_MaxX_MinY_MaxZ:
                    case H.Corner_MaxX_MaxY_MinZ:
                    case H.Corner_MaxX_MaxY_MaxZ:
                    {
                        // Generic approach: push the respective min/max toward mouse deltas on each axis.
                        bool minX = _active.ToString().Contains("MinX");
                        bool minY = _active.ToString().Contains("MinY");
                        bool minZ = _active.ToString().Contains("MinZ");
                        applyAxis(ref minV.X, ref maxV.X, wDelta.X, minX);
                        applyAxis(ref minV.Y, ref maxV.Y, (viewIndex==2?wDelta.X:wDelta.Y), minY);
                        applyAxis(ref minV.Z, ref maxV.Z, (viewIndex==0?0:wDelta.Z), minZ);
                        break;
                    }
                }

                // Clamp and ordering
                var maxBounds = new Vector3(Dataset.Width, Dataset.Height, Dataset.Depth);
                minV = Vector3.Max(Vector3.Zero, minV);
                maxV = Vector3.Min(maxBounds, maxV);
                const float eps = 0.5f;
                if (maxV.X - minV.X < eps) { if (minV.X > 0) minV.X = maxV.X - eps; else maxV.X = minV.X + eps; }
                if (maxV.Y - minV.Y < eps) { if (minV.Y > 0) minV.Y = maxV.Y - eps; else maxV.Y = minV.Y + eps; }
                if (maxV.Z - minV.Z < eps) { if (minV.Z > 0) minV.Z = maxV.Z - eps; else maxV.Z = minV.Z + eps; }

                // Snapping (voxel steps)
                if (_tool.SnapEnabled)
                {
                    minV.X = _tool.Snap(minV.X, _tool.SnapCropVoxStepX);
                    minV.Y = _tool.Snap(minV.Y, _tool.SnapCropVoxStepY);
                    minV.Z = _tool.Snap(minV.Z, _tool.SnapCropVoxStepZ);
                    maxV.X = _tool.Snap(maxV.X, _tool.SnapCropVoxStepX);
                    maxV.Y = _tool.Snap(maxV.Y, _tool.SnapCropVoxStepY);
                    maxV.Z = _tool.Snap(maxV.Z, _tool.SnapCropVoxStepZ);
                }

                var minNew = new Vector3(minV.X / Dataset.Width, minV.Y / Dataset.Height, minV.Z / Dataset.Depth);
                var maxNew = new Vector3(maxV.X / Dataset.Width, maxV.Y / Dataset.Height, maxV.Z / Dataset.Depth);
                _tool.SetCropBounds(minNew, maxNew);
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
                return true;
            }

            if (released && _active != H.None)
            {
                _active = H.None;
                return true;
            }

            if (_active == H.None)
            {
                _hover = Hit(mousePos);
                if (_hover != H.None) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            }

            return _active != H.None;
        }

        private void ComputeHandles(Vector3 min, Vector3 max, int viewIndex, Vector2 imagePos, Vector2 imageSize)
        {
            _h3.Clear();
            _h2.Clear();

            // Faces
            _h3[H.MinX] = new(min.X, (min.Y + max.Y) * 0.5f, (min.Z + max.Z) * 0.5f);
            _h3[H.MaxX] = new(max.X, (min.Y + max.Y) * 0.5f, (min.Z + max.Z) * 0.5f);
            _h3[H.MinY] = new((min.X + max.X) * 0.5f, min.Y, (min.Z + max.Z) * 0.5f);
            _h3[H.MaxY] = new((min.X + max.X) * 0.5f, max.Y, (min.Z + max.Z) * 0.5f);
            _h3[H.MinZ] = new((min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f, min.Z);
            _h3[H.MaxZ] = new((min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f, max.Z);

            // Edges
            _h3[H.Edge_MinX_MinY] = new(min.X, min.Y, (min.Z + max.Z) * 0.5f);
            _h3[H.Edge_MinX_MaxY] = new(min.X, max.Y, (min.Z + max.Z) * 0.5f);
            _h3[H.Edge_MaxX_MinY] = new(max.X, min.Y, (min.Z + max.Z) * 0.5f);
            _h3[H.Edge_MaxX_MaxY] = new(max.X, max.Y, (min.Z + max.Z) * 0.5f);

            _h3[H.Edge_MinX_MinZ] = new(min.X, (min.Y + max.Y) * 0.5f, min.Z);
            _h3[H.Edge_MinX_MaxZ] = new(min.X, (min.Y + max.Y) * 0.5f, max.Z);
            _h3[H.Edge_MaxX_MinZ] = new(max.X, (min.Y + max.Y) * 0.5f, min.Z);
            _h3[H.Edge_MaxX_MaxZ] = new(max.X, (min.Y + max.Y) * 0.5f, max.Z);

            _h3[H.Edge_MinY_MinZ] = new((min.X + max.X) * 0.5f, min.Y, min.Z);
            _h3[H.Edge_MinY_MaxZ] = new((min.X + max.X) * 0.5f, min.Y, max.Z);
            _h3[H.Edge_MaxY_MinZ] = new((min.X + max.X) * 0.5f, max.Y, min.Z);
            _h3[H.Edge_MaxY_MaxZ] = new((min.X + max.X) * 0.5f, max.Y, max.Z);

            // Corners
            _h3[H.Corner_MinX_MinY_MinZ] = new(min.X, min.Y, min.Z);
            _h3[H.Corner_MaxX_MinY_MinZ] = new(max.X, min.Y, min.Z);
            _h3[H.Corner_MinX_MaxY_MinZ] = new(min.X, max.Y, min.Z);
            _h3[H.Corner_MaxX_MaxY_MinZ] = new(max.X, max.Y, min.Z);
            _h3[H.Corner_MinX_MinY_MaxZ] = new(min.X, min.Y, max.Z);
            _h3[H.Corner_MaxX_MinY_MaxZ] = new(max.X, min.Y, max.Z);
            _h3[H.Corner_MinX_MaxY_MaxZ] = new(min.X, max.Y, max.Z);
            _h3[H.Corner_MaxX_MaxY_MaxZ] = new(max.X, max.Y, max.Z);

            foreach (var kv in _h3)
            {
                var p = ProjectToView(kv.Value, viewIndex, imagePos, imageSize);
                _h2[kv.Key] = p;
            }
        }

        private H Hit(Vector2 mouse)
        {
            foreach (var kv in _h2.Reverse())
            {
                if (Vector2.Distance(mouse, kv.Value) <= HandleSize + 2)
                    return kv.Key;
            }
            return H.None;
        }

        private static Vector3[] GetBoundingBoxCorners(Vector3 min, Vector3 max)
        {
            return new[]
            {
                new Vector3(min.X, min.Y, min.Z),
                new Vector3(max.X, min.Y, min.Z),
                new Vector3(max.X, max.Y, min.Z),
                new Vector3(min.X, max.Y, min.Z),
                new Vector3(min.X, min.Y, max.Z),
                new Vector3(max.X, min.Y, max.Z),
                new Vector3(max.X, max.Y, max.Z),
                new Vector3(min.X, max.Y, max.Z),
            };
        }

        private Vector2 ProjectToView(Vector3 p3, int viewIndex, Vector2 imagePos, Vector2 imageSize)
        {
            Vector2 uv, norm;
            switch (viewIndex)
            {
                case 0: uv = new Vector2(p3.X, p3.Y); norm = uv / new Vector2(Dataset.Width, Dataset.Height); break;
                case 1: uv = new Vector2(p3.X, p3.Z); norm = uv / new Vector2(Dataset.Width, Dataset.Depth); break;
                case 2: uv = new Vector2(p3.Y, p3.Z); norm = uv / new Vector2(Dataset.Height, Dataset.Depth); break;
                default: return Vector2.Zero;
            }
            return imagePos + new Vector2(norm.X * imageSize.X, norm.Y * imageSize.Y);
        }

        private static void DrawEdge(ImDrawListPtr dl, Vector2 a, Vector2 b, uint col) => dl.AddLine(a, b, col, 1.5f);
    }
}
