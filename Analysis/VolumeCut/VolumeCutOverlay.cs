// GAIA/Analysis/VolumeCut/VolumeCutOverlay.cs

using System.Numerics;
using GAIA.Data.CtImageStack;
using ImGuiNET;

namespace GAIA.Analysis.VolumeCut;

/// <summary>
///     Interactive cross-section overlay for the Volume Cut tool: draws the cut shape on the
///     three orthogonal slice views and lets the user adjust it by dragging handles.
/// </summary>
public sealed class VolumeCutOverlay
{
    private const float HandleR = 6f;
    private const uint ShapeColor = 0xFF2BD4FF;   // amber-free cyan
    private const uint ShapeColorDim = 0x662BD4FF;
    private readonly Dictionary<Handle, Vector2> _handles = new();
    private readonly VolumeCutState _state;
    private Handle _active = Handle.None;
    private int _activeView = -1;
    private Vector2 _dragStartMouse;
    private VolumeCutState _dragStartSnapshot;

    public VolumeCutOverlay(VolumeCutState state, CtImageStackDataset dataset)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        Dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
    }

    public CtImageStackDataset Dataset { get; }

    private static int UAxis(int view) => view == 2 ? 1 : 0;
    private static int VAxis(int view) => view == 0 ? 1 : 2;
    private static int ThirdAxis(int view) => view switch { 0 => 2, 1 => 1, _ => 0 };

    private int AxisLength(int axis) => axis switch { 0 => Dataset.Width, 1 => Dataset.Height, _ => Dataset.Depth };

    public void DrawOnSlice(ImDrawListPtr dl, int viewIndex, Vector2 imagePos, Vector2 imageSize,
        int imageWidth, int imageHeight, int sliceX, int sliceY, int sliceZ)
    {
        if (!_state.ShowOverlay) return;
        if (imageSize.X <= 0 || imageSize.Y <= 0 || imageWidth <= 0 || imageHeight <= 0) return;
        _handles.Clear();
        BuildAndDraw(dl, viewIndex, imagePos, imageSize, SliceOf(viewIndex, sliceX, sliceY, sliceZ), true);
    }

    public bool HandleMouseInput(Vector2 mousePos, Vector2 imagePos, Vector2 imageSize,
        int imageWidth, int imageHeight, int viewIndex, bool clicked, bool dragging, bool released,
        int sliceX, int sliceY, int sliceZ)
    {
        if (!_state.ShowOverlay) return false;
        if (imageSize.X <= 0 || imageSize.Y <= 0 || imageWidth <= 0 || imageHeight <= 0) return false;

        _handles.Clear();
        BuildAndDraw(default, viewIndex, imagePos, imageSize, SliceOf(viewIndex, sliceX, sliceY, sliceZ), false);

        if (clicked)
        {
            _active = Hit(mousePos);
            if (_active != Handle.None)
            {
                _activeView = viewIndex;
                _dragStartMouse = mousePos;
                _dragStartSnapshot = Snapshot();
                return true;
            }
        }

        if (dragging && _active != Handle.None && _activeView == viewIndex)
        {
            var deltaPx = mousePos - _dragStartMouse;
            var du = deltaPx.X * (imageWidth / MathF.Max(1f, imageSize.X));
            var dv = deltaPx.Y * (imageHeight / MathF.Max(1f, imageSize.Y));
            ApplyDrag(viewIndex, du, dv, SliceOf(viewIndex, sliceX, sliceY, sliceZ));
            _state.ClampTo(Dataset.Width, Dataset.Height, Dataset.Depth);
            return true;
        }

        if (released && _active != Handle.None)
        {
            _active = Handle.None;
            _activeView = -1;
            return true;
        }

        if (_active == Handle.None && Hit(mousePos) != Handle.None)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        return _active != Handle.None;
    }

    private static int SliceOf(int view, int sliceX, int sliceY, int sliceZ) =>
        view switch { 0 => sliceZ, 1 => sliceY, _ => sliceX };

    private void BuildAndDraw(ImDrawListPtr dl, int view, Vector2 imagePos, Vector2 imageSize, int slice, bool draw)
    {
        var u = UAxis(view);
        var v = VAxis(view);
        var third = ThirdAxis(view);
        var uLen = AxisLength(u);
        var vLen = AxisLength(v);

        Vector2 ToPx(float uu, float vv) => imagePos + new Vector2(uu / uLen * imageSize.X, vv / vLen * imageSize.Y);

        switch (_state.Shape)
        {
            case VolumeCutShapeKind.Box:
            {
                var onSlice = slice >= VolumeCutState.Component(_state.BoxMin, third) &&
                              slice <= VolumeCutState.Component(_state.BoxMax, third);
                var minU = VolumeCutState.Component(_state.BoxMin, u);
                var maxU = VolumeCutState.Component(_state.BoxMax, u);
                var minV = VolumeCutState.Component(_state.BoxMin, v);
                var maxV = VolumeCutState.Component(_state.BoxMax, v);
                var tl = ToPx(minU, minV);
                var br = ToPx(maxU, maxV);
                if (draw) dl.AddRect(tl, br, onSlice ? ShapeColor : ShapeColorDim, 0, ImDrawFlags.None, 2f);

                _handles[Handle.Center] = (tl + br) * 0.5f;
                _handles[Handle.UMin] = new Vector2(tl.X, (tl.Y + br.Y) * 0.5f);
                _handles[Handle.UMax] = new Vector2(br.X, (tl.Y + br.Y) * 0.5f);
                _handles[Handle.VMin] = new Vector2((tl.X + br.X) * 0.5f, tl.Y);
                _handles[Handle.VMax] = new Vector2((tl.X + br.X) * 0.5f, br.Y);
                break;
            }
            case VolumeCutShapeKind.Sphere:
            {
                var offset = slice - VolumeCutState.Component(_state.SphereCenter, third);
                var remaining = _state.SphereRadius * _state.SphereRadius - offset * offset;
                var centerPx = ToPx(VolumeCutState.Component(_state.SphereCenter, u),
                    VolumeCutState.Component(_state.SphereCenter, v));
                _handles[Handle.Center] = centerPx;
                if (remaining > 0)
                {
                    var inPlane = MathF.Sqrt(remaining);
                    var radiusPx = inPlane / uLen * imageSize.X;
                    if (draw) dl.AddCircle(centerPx, radiusPx, ShapeColor, 64, 2f);
                    _handles[Handle.Radius] = centerPx + new Vector2(radiusPx, 0);
                }
                else if (draw)
                {
                    dl.AddCircle(centerPx, 4f, ShapeColorDim, 12, 1.5f);
                }

                break;
            }
            default: // Cylinder
            {
                if (_state.CylinderAxis == third)
                {
                    // circular cross-section
                    var onSlice = slice >= _state.CylinderAxisMin && slice <= _state.CylinderAxisMax;
                    var centerPx = ToPx(VolumeCutState.Component(_state.CylinderCenter, u),
                        VolumeCutState.Component(_state.CylinderCenter, v));
                    var radiusPx = _state.CylinderRadius / uLen * imageSize.X;
                    if (draw) dl.AddCircle(centerPx, radiusPx, onSlice ? ShapeColor : ShapeColorDim, 64, 2f);
                    _handles[Handle.Center] = centerPx;
                    _handles[Handle.Radius] = centerPx + new Vector2(radiusPx, 0);
                }
                else
                {
                    // lateral rectangle: extent along the cylinder axis, chord across it
                    var crossAxis = 3 - _state.CylinderAxis - third; // remaining in-plane axis
                    var offset = slice - VolumeCutState.Component(_state.CylinderCenter, third);
                    var remaining = _state.CylinderRadius * _state.CylinderRadius - offset * offset;
                    if (remaining <= 0) break;
                    var halfChord = MathF.Sqrt(remaining);
                    var crossCenter = VolumeCutState.Component(_state.CylinderCenter, crossAxis);
                    var axisIsU = _state.CylinderAxis == u;

                    var tl = axisIsU
                        ? ToPx(_state.CylinderAxisMin, crossCenter - halfChord)
                        : ToPx(crossCenter - halfChord, _state.CylinderAxisMin);
                    var br = axisIsU
                        ? ToPx(_state.CylinderAxisMax, crossCenter + halfChord)
                        : ToPx(crossCenter + halfChord, _state.CylinderAxisMax);
                    if (draw) dl.AddRect(tl, br, ShapeColor, 0, ImDrawFlags.None, 2f);

                    _handles[Handle.Center] = (tl + br) * 0.5f;
                    if (axisIsU)
                    {
                        _handles[Handle.AxisMin] = new Vector2(tl.X, (tl.Y + br.Y) * 0.5f);
                        _handles[Handle.AxisMax] = new Vector2(br.X, (tl.Y + br.Y) * 0.5f);
                        _handles[Handle.Radius] = new Vector2((tl.X + br.X) * 0.5f, br.Y);
                    }
                    else
                    {
                        _handles[Handle.AxisMin] = new Vector2((tl.X + br.X) * 0.5f, tl.Y);
                        _handles[Handle.AxisMax] = new Vector2((tl.X + br.X) * 0.5f, br.Y);
                        _handles[Handle.Radius] = new Vector2(br.X, (tl.Y + br.Y) * 0.5f);
                    }
                }

                break;
            }
        }

        if (draw) DrawHandles(dl);
    }

    private void ApplyDrag(int view, float du, float dv, int slice)
    {
        var u = UAxis(view);
        var v = VAxis(view);
        var third = ThirdAxis(view);
        var s = _dragStartSnapshot;

        switch (_state.Shape)
        {
            case VolumeCutShapeKind.Box:
                switch (_active)
                {
                    case Handle.Center:
                    {
                        var size = s.BoxMax - s.BoxMin;
                        var min = s.BoxMin;
                        VolumeCutState.SetComponent(ref min, u, VolumeCutState.Component(s.BoxMin, u) + du);
                        VolumeCutState.SetComponent(ref min, v, VolumeCutState.Component(s.BoxMin, v) + dv);
                        _state.BoxMin = min;
                        _state.BoxMax = min + size;
                        break;
                    }
                    case Handle.UMin:
                        SetBoxEdge(u, true, VolumeCutState.Component(s.BoxMin, u) + du);
                        _state.ApplyBoxAspect(s, u);
                        break;
                    case Handle.UMax:
                        SetBoxEdge(u, false, VolumeCutState.Component(s.BoxMax, u) + du);
                        _state.ApplyBoxAspect(s, u);
                        break;
                    case Handle.VMin:
                        SetBoxEdge(v, true, VolumeCutState.Component(s.BoxMin, v) + dv);
                        _state.ApplyBoxAspect(s, v);
                        break;
                    case Handle.VMax:
                        SetBoxEdge(v, false, VolumeCutState.Component(s.BoxMax, v) + dv);
                        _state.ApplyBoxAspect(s, v);
                        break;
                }

                break;

            case VolumeCutShapeKind.Sphere:
                if (_active == Handle.Center)
                {
                    var center = s.SphereCenter;
                    VolumeCutState.SetComponent(ref center, u, VolumeCutState.Component(s.SphereCenter, u) + du);
                    VolumeCutState.SetComponent(ref center, v, VolumeCutState.Component(s.SphereCenter, v) + dv);
                    _state.SphereCenter = center;
                }
                else if (_active == Handle.Radius)
                {
                    var offset = slice - VolumeCutState.Component(s.SphereCenter, third);
                    var startInPlane = MathF.Sqrt(MathF.Max(0,
                        s.SphereRadius * s.SphereRadius - offset * offset));
                    var inPlane = MathF.Max(1f, startInPlane + du);
                    _state.SphereRadius = MathF.Sqrt(inPlane * inPlane + offset * offset);
                }

                break;

            default: // Cylinder
                if (_state.CylinderAxis == third)
                {
                    if (_active == Handle.Center)
                    {
                        var center = s.CylinderCenter;
                        VolumeCutState.SetComponent(ref center, u,
                            VolumeCutState.Component(s.CylinderCenter, u) + du);
                        VolumeCutState.SetComponent(ref center, v,
                            VolumeCutState.Component(s.CylinderCenter, v) + dv);
                        _state.CylinderCenter = center;
                    }
                    else if (_active == Handle.Radius)
                    {
                        _state.CylinderRadius = MathF.Max(1f, s.CylinderRadius + du);
                        _state.ApplyCylinderAspectFromRadius(s);
                    }
                }
                else
                {
                    var crossAxis = 3 - _state.CylinderAxis - third;
                    var axisIsU = _state.CylinderAxis == u;
                    var dAxis = axisIsU ? du : dv;
                    var dCross = axisIsU ? dv : du;
                    switch (_active)
                    {
                        case Handle.Center:
                        {
                            _state.CylinderAxisMin = s.CylinderAxisMin + dAxis;
                            _state.CylinderAxisMax = s.CylinderAxisMax + dAxis;
                            var center = s.CylinderCenter;
                            VolumeCutState.SetComponent(ref center, crossAxis,
                                VolumeCutState.Component(s.CylinderCenter, crossAxis) + dCross);
                            _state.CylinderCenter = center;
                            break;
                        }
                        case Handle.AxisMin:
                            _state.CylinderAxisMin = MathF.Min(s.CylinderAxisMin + dAxis,
                                _state.CylinderAxisMax - 1);
                            _state.ApplyCylinderAspectFromExtent(s);
                            break;
                        case Handle.AxisMax:
                            _state.CylinderAxisMax = MathF.Max(s.CylinderAxisMax + dAxis,
                                _state.CylinderAxisMin + 1);
                            _state.ApplyCylinderAspectFromExtent(s);
                            break;
                        case Handle.Radius:
                        {
                            var offset = slice - VolumeCutState.Component(s.CylinderCenter, third);
                            var startChord = MathF.Sqrt(MathF.Max(0,
                                s.CylinderRadius * s.CylinderRadius - offset * offset));
                            var chord = MathF.Max(1f, startChord + dCross);
                            _state.CylinderRadius = MathF.Sqrt(chord * chord + offset * offset);
                            _state.ApplyCylinderAspectFromRadius(s);
                            break;
                        }
                    }
                }

                break;
        }
    }

    private void SetBoxEdge(int axis, bool isMin, float value)
    {
        if (isMin)
        {
            var min = _state.BoxMin;
            VolumeCutState.SetComponent(ref min, axis,
                MathF.Min(value, VolumeCutState.Component(_state.BoxMax, axis) - 1));
            _state.BoxMin = min;
        }
        else
        {
            var max = _state.BoxMax;
            VolumeCutState.SetComponent(ref max, axis,
                MathF.Max(value, VolumeCutState.Component(_state.BoxMin, axis) + 1));
            _state.BoxMax = max;
        }
    }

    private VolumeCutState Snapshot() => new()
    {
        Shape = _state.Shape,
        BoxMin = _state.BoxMin,
        BoxMax = _state.BoxMax,
        CylinderAxis = _state.CylinderAxis,
        CylinderCenter = _state.CylinderCenter,
        CylinderRadius = _state.CylinderRadius,
        CylinderAxisMin = _state.CylinderAxisMin,
        CylinderAxisMax = _state.CylinderAxisMax,
        SphereCenter = _state.SphereCenter,
        SphereRadius = _state.SphereRadius
    };

    private void DrawHandles(ImDrawListPtr dl)
    {
        foreach (var kv in _handles)
        {
            dl.AddCircleFilled(kv.Value, HandleR, ShapeColor);
            dl.AddCircle(kv.Value, HandleR, 0xFFFFFFFF, 16, 1.5f);
        }
    }

    private Handle Hit(Vector2 mouse)
    {
        foreach (var kv in _handles)
            if (Vector2.Distance(mouse, kv.Value) <= HandleR + 2)
                return kv.Key;
        return Handle.None;
    }

    private enum Handle
    {
        None,
        Center,
        UMin,
        UMax,
        VMin,
        VMax,
        AxisMin,
        AxisMax,
        Radius
    }
}
