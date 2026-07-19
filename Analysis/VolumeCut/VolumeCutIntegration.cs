// GAIA/Analysis/VolumeCut/VolumeCutIntegration.cs

using System.Numerics;
using GAIA.Data.CtImageStack;
using ImGuiNET;

namespace GAIA.Analysis.VolumeCut;

/// <summary>
///     Connects the Volume Cut tool to the CT viewers: cross-section overlays with handles on
///     the 2D slice views, and a wireframe with draggable handles in the 3D viewport.
/// </summary>
public static class VolumeCutIntegration
{
    private static readonly Dictionary<CtImageStackDataset, VolumeCutTool> _activeTools = new();

    // 3D drag state
    private static Handle3D _active3D = Handle3D.None;
    private static Vector2 _drag3DStartMouse;
    private static VolumeCutState _drag3DSnapshot;

    public static void RegisterTool(CtImageStackDataset dataset, VolumeCutTool tool)
    {
        if (dataset == null || tool == null) return;
        _activeTools[dataset] = tool;
    }

    public static void UnregisterTool(CtImageStackDataset dataset)
    {
        if (dataset == null) return;
        _activeTools.Remove(dataset);
    }

    public static VolumeCutTool GetActiveTool(CtImageStackDataset dataset) =>
        dataset != null && _activeTools.TryGetValue(dataset, out var tool) ? tool : null;

    // ---------------- 2D slice views ----------------

    public static void DrawOverlay(CtImageStackDataset dataset, ImDrawListPtr dl, int viewIndex,
        Vector2 imagePos, Vector2 imageSize, int imageWidth, int imageHeight, int sliceX, int sliceY, int sliceZ)
    {
        var tool = GetActiveTool(dataset);
        if (tool?.Overlay == null || !tool.State.ShowOverlay) return;
        tool.Overlay.DrawOnSlice(dl, viewIndex, imagePos, imageSize, imageWidth, imageHeight,
            sliceX, sliceY, sliceZ);
    }

    public static bool HandleMouseInput(CtImageStackDataset dataset, Vector2 mousePos,
        Vector2 imagePos, Vector2 imageSize, int imageWidth, int imageHeight, int viewIndex,
        bool clicked, bool dragging, bool released, int sliceX, int sliceY, int sliceZ)
    {
        var tool = GetActiveTool(dataset);
        if (tool?.Overlay == null || !tool.State.ShowOverlay) return false;
        var handled = tool.Overlay.HandleMouseInput(mousePos, imagePos, imageSize, imageWidth, imageHeight,
            viewIndex, clicked, dragging, released, sliceX, sliceY, sliceZ);
        if (handled && released) tool.RefreshPreview();
        return handled;
    }

    // ---------------- 3D viewport ----------------

    /// <summary>Appends the wireframe of the cut shape to the 3D viewer's overlay line batch.</summary>
    public static void BuildOverlayLines(CtImageStackDataset dataset, Vector3 volumeScale,
        Action<Vector3, Vector3, Vector3, float> addLine)
    {
        var tool = GetActiveTool(dataset);
        if (tool == null || !tool.State.ShowOverlay) return;
        var state = tool.State;
        var color = new Vector3(0.16f, 0.83f, 1f);
        Vector3 W(Vector3 voxel) => VoxelToWorld(voxel, dataset, volumeScale);

        switch (state.Shape)
        {
            case VolumeCutShapeKind.Box:
            {
                var lo = state.BoxMin;
                var hi = state.BoxMax;
                Vector3 C(int i) => W(new Vector3((i & 1) != 0 ? hi.X : lo.X, (i & 2) != 0 ? hi.Y : lo.Y,
                    (i & 4) != 0 ? hi.Z : lo.Z));
                int[] edges = { 0, 1, 1, 3, 3, 2, 2, 0, 4, 5, 5, 7, 7, 6, 6, 4, 0, 4, 1, 5, 2, 6, 3, 7 };
                for (var i = 0; i < edges.Length; i += 2) addLine(C(edges[i]), C(edges[i + 1]), color, 0.95f);
                break;
            }
            case VolumeCutShapeKind.Sphere:
                for (var plane = 0; plane < 3; plane++)
                    AddVoxelCircle(state.SphereCenter, state.SphereRadius, plane, W, addLine, color);
                break;
            default:
            {
                var axis = state.CylinderAxis;
                var capMin = state.CylinderCenter;
                var capMax = state.CylinderCenter;
                VolumeCutState.SetComponent(ref capMin, axis, state.CylinderAxisMin);
                VolumeCutState.SetComponent(ref capMax, axis, state.CylinderAxisMax);
                AddVoxelCircle(capMin, state.CylinderRadius, axis, W, addLine, color);
                AddVoxelCircle(capMax, state.CylinderRadius, axis, W, addLine, color);
                var (u, v) = axis switch { 0 => (1, 2), 1 => (0, 2), _ => (0, 1) };
                for (var k = 0; k < 4; k++)
                {
                    var angle = k * MathF.PI / 2;
                    var a = capMin;
                    var b = capMax;
                    var du = MathF.Cos(angle) * state.CylinderRadius;
                    var dv = MathF.Sin(angle) * state.CylinderRadius;
                    VolumeCutState.SetComponent(ref a, u, VolumeCutState.Component(a, u) + du);
                    VolumeCutState.SetComponent(ref a, v, VolumeCutState.Component(a, v) + dv);
                    VolumeCutState.SetComponent(ref b, u, VolumeCutState.Component(b, u) + du);
                    VolumeCutState.SetComponent(ref b, v, VolumeCutState.Component(b, v) + dv);
                    addLine(W(a), W(b), color, 0.85f);
                }

                break;
            }
        }
    }

    /// <summary>Draws the draggable handle markers over the 3D viewport image.</summary>
    public static void DrawViewport(CtImageStackDataset dataset, ImDrawListPtr dl, Matrix4x4 viewProj,
        Vector2 origin, Vector2 size, Vector3 volumeScale)
    {
        var tool = GetActiveTool(dataset);
        if (tool == null || !tool.State.ShowOverlay) return;
        foreach (var (_, voxel, _) in EnumerateHandles(tool.State))
        {
            if (!Project(viewProj, origin, size, VoxelToWorld(voxel, dataset, volumeScale), out var screen))
                continue;
            dl.AddCircleFilled(screen, 6f, 0xFF2BD4FF);
            dl.AddCircle(screen, 6f, 0xFFFFFFFF, 16, 1.5f);
        }
    }

    /// <summary>
    ///     3D handle interaction. Returns true while a handle is hot so the viewer suppresses
    ///     camera rotation for that drag.
    /// </summary>
    public static bool HandleViewportInput(CtImageStackDataset dataset, Vector2 mousePos, Vector2 origin,
        Vector2 size, Matrix4x4 viewProj, Matrix4x4 view, Vector3 volumeScale,
        bool clicked, bool dragging, bool released)
    {
        var tool = GetActiveTool(dataset);
        if (tool == null || !tool.State.ShowOverlay) return false;
        var state = tool.State;

        if (clicked && _active3D == Handle3D.None)
        {
            foreach (var (handle, voxel, _) in EnumerateHandles(state))
            {
                if (!Project(viewProj, origin, size, VoxelToWorld(voxel, dataset, volumeScale), out var screen) ||
                    Vector2.Distance(mousePos, screen) > 8f) continue;
                _active3D = handle;
                _drag3DStartMouse = mousePos;
                _drag3DSnapshot = Clone(state);
                return true;
            }
        }

        if (dragging && _active3D != Handle3D.None)
        {
            var deltaPx = mousePos - _drag3DStartMouse;
            DragHandle(dataset, state, _active3D, deltaPx, viewProj, view, origin, size, volumeScale);
            state.ClampTo(dataset.Width, dataset.Height, dataset.Depth);
            return true;
        }

        if (released && _active3D != Handle3D.None)
        {
            _active3D = Handle3D.None;
            tool.RefreshPreview();
            return true;
        }

        return _active3D != Handle3D.None;
    }

    private static IEnumerable<(Handle3D handle, Vector3 voxel, int axis)> EnumerateHandles(VolumeCutState state)
    {
        switch (state.Shape)
        {
            case VolumeCutShapeKind.Box:
            {
                var c = (state.BoxMin + state.BoxMax) * 0.5f;
                yield return (Handle3D.Center, c, -1);
                for (var axis = 0; axis < 3; axis++)
                {
                    var lo = c;
                    var hi = c;
                    VolumeCutState.SetComponent(ref lo, axis, VolumeCutState.Component(state.BoxMin, axis));
                    VolumeCutState.SetComponent(ref hi, axis, VolumeCutState.Component(state.BoxMax, axis));
                    yield return (Handle3D.FaceMinX + axis * 2, lo, axis);
                    yield return (Handle3D.FaceMinX + axis * 2 + 1, hi, axis);
                }

                break;
            }
            case VolumeCutShapeKind.Sphere:
            {
                yield return (Handle3D.Center, state.SphereCenter, -1);
                var r = state.SphereCenter;
                r.X += state.SphereRadius;
                yield return (Handle3D.Radius, r, 0);
                break;
            }
            default:
            {
                var axis = state.CylinderAxis;
                var mid = state.CylinderCenter;
                VolumeCutState.SetComponent(ref mid, axis, (state.CylinderAxisMin + state.CylinderAxisMax) * 0.5f);
                yield return (Handle3D.Center, mid, -1);
                var capMin = mid;
                var capMax = mid;
                VolumeCutState.SetComponent(ref capMin, axis, state.CylinderAxisMin);
                VolumeCutState.SetComponent(ref capMax, axis, state.CylinderAxisMax);
                yield return (Handle3D.CapMin, capMin, axis);
                yield return (Handle3D.CapMax, capMax, axis);
                var crossAxis = axis == 0 ? 1 : 0;
                var radiusPoint = mid;
                VolumeCutState.SetComponent(ref radiusPoint, crossAxis,
                    VolumeCutState.Component(mid, crossAxis) + state.CylinderRadius);
                yield return (Handle3D.Radius, radiusPoint, crossAxis);
                break;
            }
        }
    }

    private static VolumeCutState Clone(VolumeCutState state) => new()
    {
        Shape = state.Shape,
        BoxMin = state.BoxMin,
        BoxMax = state.BoxMax,
        CylinderAxis = state.CylinderAxis,
        CylinderCenter = state.CylinderCenter,
        CylinderRadius = state.CylinderRadius,
        CylinderAxisMin = state.CylinderAxisMin,
        CylinderAxisMax = state.CylinderAxisMax,
        SphereCenter = state.SphereCenter,
        SphereRadius = state.SphereRadius
    };

    private static void DragHandle(CtImageStackDataset dataset, VolumeCutState state, Handle3D handle,
        Vector2 deltaPx, Matrix4x4 viewProj, Matrix4x4 view, Vector2 origin, Vector2 size, Vector3 volumeScale)
    {
        var snap = _drag3DSnapshot;
        if (snap == null) return;
        switch (state.Shape)
        {
            case VolumeCutShapeKind.Box:
            {
                var startCenter = (snap.BoxMin + snap.BoxMax) * 0.5f;
                if (handle == Handle3D.Center)
                {
                    var moved = CameraPlaneDelta(dataset, startCenter, deltaPx, viewProj, view, origin, size,
                        volumeScale);
                    state.BoxMin = snap.BoxMin + moved;
                    state.BoxMax = snap.BoxMax + moved;
                }
                else
                {
                    var axis = (handle - Handle3D.FaceMinX) / 2;
                    var isMin = ((handle - Handle3D.FaceMinX) & 1) == 0;
                    var face = startCenter;
                    VolumeCutState.SetComponent(ref face, axis, isMin
                        ? VolumeCutState.Component(snap.BoxMin, axis)
                        : VolumeCutState.Component(snap.BoxMax, axis));
                    var deltaVox = AxisDelta(dataset, face, axis, deltaPx, viewProj, origin, size, volumeScale);
                    if (isMin)
                    {
                        var min = state.BoxMin;
                        VolumeCutState.SetComponent(ref min, axis, MathF.Min(
                            VolumeCutState.Component(snap.BoxMin, axis) + deltaVox,
                            VolumeCutState.Component(snap.BoxMax, axis) - 1));
                        state.BoxMin = min;
                    }
                    else
                    {
                        var max = state.BoxMax;
                        VolumeCutState.SetComponent(ref max, axis, MathF.Max(
                            VolumeCutState.Component(snap.BoxMax, axis) + deltaVox,
                            VolumeCutState.Component(snap.BoxMin, axis) + 1));
                        state.BoxMax = max;
                    }
                }

                break;
            }
            case VolumeCutShapeKind.Sphere:
                if (handle == Handle3D.Center)
                {
                    var moved = CameraPlaneDelta(dataset, snap.SphereCenter, deltaPx, viewProj, view, origin,
                        size, volumeScale);
                    state.SphereCenter = snap.SphereCenter + moved;
                }
                else
                {
                    var anchor = snap.SphereCenter;
                    anchor.X += snap.SphereRadius;
                    var deltaVox = AxisDelta(dataset, anchor, 0, deltaPx, viewProj, origin, size, volumeScale);
                    state.SphereRadius = MathF.Max(1f, snap.SphereRadius + deltaVox);
                }

                break;

            default:
            {
                var axis = state.CylinderAxis;
                var mid = snap.CylinderCenter;
                VolumeCutState.SetComponent(ref mid, axis, (snap.CylinderAxisMin + snap.CylinderAxisMax) * 0.5f);
                switch (handle)
                {
                    case Handle3D.Center:
                    {
                        var moved = CameraPlaneDelta(dataset, mid, deltaPx, viewProj, view, origin, size,
                            volumeScale);
                        var center = snap.CylinderCenter + moved;
                        VolumeCutState.SetComponent(ref center, axis,
                            VolumeCutState.Component(snap.CylinderCenter, axis));
                        state.CylinderCenter = center;
                        var axisShift = VolumeCutState.Component(moved, axis);
                        state.CylinderAxisMin = snap.CylinderAxisMin + axisShift;
                        state.CylinderAxisMax = snap.CylinderAxisMax + axisShift;
                        break;
                    }
                    case Handle3D.CapMin:
                    case Handle3D.CapMax:
                    {
                        var isMin = handle == Handle3D.CapMin;
                        var cap = mid;
                        VolumeCutState.SetComponent(ref cap, axis,
                            isMin ? snap.CylinderAxisMin : snap.CylinderAxisMax);
                        var deltaVox = AxisDelta(dataset, cap, axis, deltaPx, viewProj, origin, size, volumeScale);
                        if (isMin)
                            state.CylinderAxisMin = MathF.Min(snap.CylinderAxisMin + deltaVox,
                                snap.CylinderAxisMax - 1);
                        else
                            state.CylinderAxisMax = MathF.Max(snap.CylinderAxisMax + deltaVox,
                                snap.CylinderAxisMin + 1);
                        break;
                    }
                    case Handle3D.Radius:
                    {
                        var crossAxis = axis == 0 ? 1 : 0;
                        var anchor = mid;
                        VolumeCutState.SetComponent(ref anchor, crossAxis,
                            VolumeCutState.Component(anchor, crossAxis) + snap.CylinderRadius);
                        var deltaVox = AxisDelta(dataset, anchor, crossAxis, deltaPx, viewProj, origin, size,
                            volumeScale);
                        state.CylinderRadius = MathF.Max(1f, snap.CylinderRadius + deltaVox);
                        break;
                    }
                }

                break;
            }
        }
    }

    /// <summary>Voxel-space delta for a drag constrained to one voxel axis.</summary>
    private static float AxisDelta(CtImageStackDataset dataset, Vector3 anchorVoxel, int axis, Vector2 deltaPx,
        Matrix4x4 viewProj, Vector2 origin, Vector2 size, Vector3 volumeScale)
    {
        const float epsilon = 4f;
        var step = anchorVoxel;
        VolumeCutState.SetComponent(ref step, axis, VolumeCutState.Component(anchorVoxel, axis) + epsilon);
        if (!Project(viewProj, origin, size, VoxelToWorld(anchorVoxel, dataset, volumeScale), out var a) ||
            !Project(viewProj, origin, size, VoxelToWorld(step, dataset, volumeScale), out var b))
            return 0;
        var screenDir = b - a;
        var lengthSq = screenDir.LengthSquared();
        if (lengthSq < 1e-4f) return 0;
        return Vector2.Dot(deltaPx, screenDir) / lengthSq * epsilon;
    }

    /// <summary>Voxel-space delta for a free drag in the camera plane at the anchor's depth.</summary>
    private static Vector3 CameraPlaneDelta(CtImageStackDataset dataset, Vector3 anchorVoxel, Vector2 deltaPx,
        Matrix4x4 viewProj, Matrix4x4 view, Vector2 origin, Vector2 size, Vector3 volumeScale)
    {
        // Camera right/up in world space from the view matrix (world -> view rotation columns).
        var right = new Vector3(view.M11, view.M21, view.M31);
        var up = new Vector3(view.M12, view.M22, view.M32);
        var world = VoxelToWorld(anchorVoxel, dataset, volumeScale);
        const float epsilon = 0.02f;
        if (!Project(viewProj, origin, size, world, out var p0) ||
            !Project(viewProj, origin, size, world + right * epsilon, out var pr) ||
            !Project(viewProj, origin, size, world + up * epsilon, out var pu))
            return Vector3.Zero;
        var sr = pr - p0;
        var su = pu - p0;
        var det = sr.X * su.Y - sr.Y * su.X;
        if (MathF.Abs(det) < 1e-6f) return Vector3.Zero;
        var a = (deltaPx.X * su.Y - deltaPx.Y * su.X) / det;
        var b = (deltaPx.Y * sr.X - deltaPx.X * sr.Y) / det;
        var worldDelta = (right * a + up * b) * epsilon;
        return WorldDeltaToVoxel(worldDelta, dataset, volumeScale);
    }

    private static Vector3 VoxelToWorld(Vector3 voxel, CtImageStackDataset dataset, Vector3 volumeScale) =>
        new(voxel.X / Math.Max(1, dataset.Width) * volumeScale.X,
            voxel.Y / Math.Max(1, dataset.Height) * volumeScale.Y,
            voxel.Z / Math.Max(1, dataset.Depth) * volumeScale.Z);

    private static Vector3 WorldDeltaToVoxel(Vector3 world, CtImageStackDataset dataset, Vector3 volumeScale) =>
        new(world.X / Math.Max(1e-6f, volumeScale.X) * dataset.Width,
            world.Y / Math.Max(1e-6f, volumeScale.Y) * dataset.Height,
            world.Z / Math.Max(1e-6f, volumeScale.Z) * dataset.Depth);

    private static void AddVoxelCircle(Vector3 centerVoxel, float radius, int normalAxis,
        Func<Vector3, Vector3> toWorld, Action<Vector3, Vector3, Vector3, float> addLine, Vector3 color)
    {
        var (u, v) = normalAxis switch { 0 => (1, 2), 1 => (0, 2), _ => (0, 1) };
        const int segments = 48;
        var previous = Vector3.Zero;
        for (var i = 0; i <= segments; i++)
        {
            var angle = i * MathF.Tau / segments;
            var p = centerVoxel;
            VolumeCutState.SetComponent(ref p, u, VolumeCutState.Component(centerVoxel, u) + MathF.Cos(angle) * radius);
            VolumeCutState.SetComponent(ref p, v, VolumeCutState.Component(centerVoxel, v) + MathF.Sin(angle) * radius);
            var world = toWorld(p);
            if (i > 0) addLine(previous, world, color, 0.9f);
            previous = world;
        }
    }

    private static bool Project(Matrix4x4 viewProj, Vector2 origin, Vector2 size, Vector3 world,
        out Vector2 screen)
    {
        screen = Vector2.Zero;
        var clip = Vector4.Transform(new Vector4(world, 1f), viewProj);
        if (clip.W <= 1e-5f) return false;
        var ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
        if (ndc.X < -1.5f || ndc.X > 1.5f || ndc.Y < -1.5f || ndc.Y > 1.5f) return false;
        screen = origin + new Vector2((ndc.X * 0.5f + 0.5f) * size.X, (1f - (ndc.Y * 0.5f + 0.5f)) * size.Y);
        return true;
    }

    private enum Handle3D
    {
        None,
        Center,
        Radius,
        CapMin,
        CapMax,
        FaceMinX,
        FaceMaxX,
        FaceMinY,
        FaceMaxY,
        FaceMinZ,
        FaceMaxZ
    }
}
