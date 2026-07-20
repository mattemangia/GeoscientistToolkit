// GAIA/Analysis/VolumeCut/VolumeCutState.cs

using System.Numerics;

namespace GAIA.Analysis.VolumeCut;

public enum VolumeCutShapeKind { Box, Cylinder, Sphere }

public enum VolumeCutKeepMode { KeepInside, KeepOutside }

/// <summary>
///     Analytic description of a volume cut region. All coordinates are in voxels.
///     Every shape resolves each (y, z) row to at most one contiguous X interval, so applying
///     a cut is span fills at memset speed, streamed slice-by-slice without any mask volume.
/// </summary>
public sealed class VolumeCutState
{
    public VolumeCutShapeKind Shape = VolumeCutShapeKind.Box;
    public VolumeCutKeepMode KeepMode = VolumeCutKeepMode.KeepInside;
    public bool ApplyToGrayscale = true;
    public bool ApplyToLabels = true;
    public bool ShowOverlay = true;

    /// <summary>When set, the cut also resizes the dataset to the bounding box of the kept region,
    /// so no empty (black) border is left around it. See <see cref="GetCropBounds" />.</summary>
    public bool CropToRegion;

    /// <summary>When set, resize handles scale the whole shape proportionally: a box drag
    /// rescales the other two axes about their centers, and cylinder radius/extent stay linked.
    /// Moves are unaffected; a sphere is inherently uniform.</summary>
    public bool LockAspectRatio;

    // Box (inclusive voxel bounds)
    public Vector3 BoxMin;
    public Vector3 BoxMax;

    // Cylinder: axis 0=X, 1=Y, 2=Z; the two in-plane components of Center are the circle
    // center, the axis component is unused. AxisMin/AxisMax bound the extent along the axis.
    public int CylinderAxis = 2;
    public Vector3 CylinderCenter;
    public float CylinderRadius = 1;
    public float CylinderAxisMin;
    public float CylinderAxisMax;

    // Sphere
    public Vector3 SphereCenter;
    public float SphereRadius = 1;

    public static float Component(Vector3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    public static void SetComponent(ref Vector3 v, int axis, float value)
    {
        if (axis == 0) v.X = value;
        else if (axis == 1) v.Y = value;
        else v.Z = value;
    }

    public void ResetToVolume(int width, int height, int depth)
    {
        BoxMin = new Vector3(width * 0.25f, height * 0.25f, depth * 0.25f);
        BoxMax = new Vector3(width * 0.75f, height * 0.75f, depth * 0.75f);
        CylinderAxis = 2;
        CylinderCenter = new Vector3(width * 0.5f, height * 0.5f, depth * 0.5f);
        CylinderRadius = Math.Min(width, height) * 0.35f;
        CylinderAxisMin = depth * 0.1f;
        CylinderAxisMax = depth * 0.9f;
        SphereCenter = new Vector3(width * 0.5f, height * 0.5f, depth * 0.5f);
        SphereRadius = Math.Min(width, Math.Min(height, depth)) * 0.35f;
    }

    public void ClampTo(int width, int height, int depth)
    {
        // A dataset that is still loading reports 0 dimensions; keep every clamp range valid.
        var max = Vector3.Max(Vector3.One, new Vector3(width - 1, height - 1, depth - 1));
        var largest = Math.Max(1, Math.Max(width, Math.Max(height, depth)));
        BoxMin = Vector3.Clamp(BoxMin, Vector3.Zero, max);
        BoxMax = Vector3.Clamp(BoxMax, BoxMin, max);
        CylinderCenter = Vector3.Clamp(CylinderCenter, Vector3.Zero, max);
        CylinderRadius = Math.Clamp(CylinderRadius, 1, largest);
        var axisLength = Math.Max(1, (CylinderAxis switch { 0 => width, 1 => height, _ => depth }) - 1);
        CylinderAxisMin = Math.Clamp(CylinderAxisMin, 0, axisLength);
        CylinderAxisMax = Math.Clamp(CylinderAxisMax, CylinderAxisMin, axisLength);
        SphereCenter = Vector3.Clamp(SphereCenter, Vector3.Zero, max);
        SphereRadius = Math.Clamp(SphereRadius, 1, largest);
    }

    /// <summary>
    ///     Inclusive voxel bounds of the region kept by the cut, for the optional crop-to-region.
    ///     Keep-inside crops to the shape's bounding box (a sphere/cylinder crops to the box that
    ///     contains it); keep-outside keeps the whole exterior, so the bounds span the full volume
    ///     and no crop reduction happens. All values are clamped inside the volume.
    /// </summary>
    public (int X0, int Y0, int Z0, int X1, int Y1, int Z1) GetCropBounds(int width, int height, int depth)
    {
        if (KeepMode == VolumeCutKeepMode.KeepOutside)
            return (0, 0, 0, width - 1, height - 1, depth - 1);

        Vector3 lo, hi;
        switch (Shape)
        {
            case VolumeCutShapeKind.Box:
                lo = BoxMin;
                hi = BoxMax;
                break;
            case VolumeCutShapeKind.Sphere:
                lo = SphereCenter - new Vector3(SphereRadius);
                hi = SphereCenter + new Vector3(SphereRadius);
                break;
            default:
                // Cylinder: full radius on the two cross axes, [AxisMin, AxisMax] along the axis.
                lo = CylinderCenter - new Vector3(CylinderRadius);
                hi = CylinderCenter + new Vector3(CylinderRadius);
                SetComponent(ref lo, CylinderAxis, CylinderAxisMin);
                SetComponent(ref hi, CylinderAxis, CylinderAxisMax);
                break;
        }

        // Round to the exact voxels the cut keeps — ceil(min)/floor(max), matching
        // TryGetRowInsideSpan (start = ceil(min), end = floor(max) + 1). Using floor/ceil here
        // instead would make the crop box one voxel wider than the kept region on each side,
        // leaving a black frame around the result.
        var x0 = Math.Clamp((int)MathF.Ceiling(lo.X), 0, width - 1);
        var y0 = Math.Clamp((int)MathF.Ceiling(lo.Y), 0, height - 1);
        var z0 = Math.Clamp((int)MathF.Ceiling(lo.Z), 0, depth - 1);
        var x1 = Math.Clamp((int)MathF.Floor(hi.X), x0, width - 1);
        var y1 = Math.Clamp((int)MathF.Floor(hi.Y), y0, height - 1);
        var z1 = Math.Clamp((int)MathF.Floor(hi.Z), z0, depth - 1);
        return (x0, y0, z0, x1, y1, z1);
    }

    /// <summary>Whether a voxel lies inside the cut shape (independent of keep mode).</summary>
    public bool InsideShape(float x, float y, float z)
    {
        switch (Shape)
        {
            case VolumeCutShapeKind.Box:
                return x >= BoxMin.X && x <= BoxMax.X && y >= BoxMin.Y && y <= BoxMax.Y &&
                       z >= BoxMin.Z && z <= BoxMax.Z;
            case VolumeCutShapeKind.Sphere:
            {
                var dx = x - SphereCenter.X;
                var dy = y - SphereCenter.Y;
                var dz = z - SphereCenter.Z;
                return dx * dx + dy * dy + dz * dz <= SphereRadius * SphereRadius;
            }
            default:
            {
                var position = new Vector3(x, y, z);
                var along = Component(position, CylinderAxis);
                if (along < CylinderAxisMin || along > CylinderAxisMax) return false;
                var (u, v) = CylinderAxis switch
                {
                    0 => (1, 2),
                    1 => (0, 2),
                    _ => (0, 1)
                };
                var du = Component(position, u) - Component(CylinderCenter, u);
                var dv = Component(position, v) - Component(CylinderCenter, v);
                return du * du + dv * dv <= CylinderRadius * CylinderRadius;
            }
        }
    }

    /// <summary>
    ///     The contiguous X interval [start, end) of voxels inside the shape on row (y, z).
    ///     Returns false when the row does not intersect the shape.
    /// </summary>
    public bool TryGetRowInsideSpan(int y, int z, int width, out int start, out int end)
    {
        start = end = 0;
        float minX, maxX;
        switch (Shape)
        {
            case VolumeCutShapeKind.Box:
                if (y < BoxMin.Y || y > BoxMax.Y || z < BoxMin.Z || z > BoxMax.Z) return false;
                minX = BoxMin.X;
                maxX = BoxMax.X;
                break;
            case VolumeCutShapeKind.Sphere:
            {
                var dy = y - SphereCenter.Y;
                var dz = z - SphereCenter.Z;
                var remaining = SphereRadius * SphereRadius - dy * dy - dz * dz;
                if (remaining < 0) return false;
                var dx = MathF.Sqrt(remaining);
                minX = SphereCenter.X - dx;
                maxX = SphereCenter.X + dx;
                break;
            }
            default:
                switch (CylinderAxis)
                {
                    case 0: // extent along X; circle in (Y, Z)
                    {
                        var dy = y - CylinderCenter.Y;
                        var dz = z - CylinderCenter.Z;
                        if (dy * dy + dz * dz > CylinderRadius * CylinderRadius) return false;
                        minX = CylinderAxisMin;
                        maxX = CylinderAxisMax;
                        break;
                    }
                    case 1: // extent along Y; circle in (X, Z)
                    {
                        if (y < CylinderAxisMin || y > CylinderAxisMax) return false;
                        var dz = z - CylinderCenter.Z;
                        var remaining = CylinderRadius * CylinderRadius - dz * dz;
                        if (remaining < 0) return false;
                        var dx = MathF.Sqrt(remaining);
                        minX = CylinderCenter.X - dx;
                        maxX = CylinderCenter.X + dx;
                        break;
                    }
                    default: // extent along Z; circle in (X, Y)
                    {
                        if (z < CylinderAxisMin || z > CylinderAxisMax) return false;
                        var dy = y - CylinderCenter.Y;
                        var remaining = CylinderRadius * CylinderRadius - dy * dy;
                        if (remaining < 0) return false;
                        var dx = MathF.Sqrt(remaining);
                        minX = CylinderCenter.X - dx;
                        maxX = CylinderCenter.X + dx;
                        break;
                    }
                }

                break;
        }

        start = Math.Max(0, (int)MathF.Ceiling(minX));
        end = Math.Min(width, (int)MathF.Floor(maxX) + 1);
        return end > start;
    }

    /// <summary>Fast slice classification so untouched slices can be skipped entirely.</summary>
    public (bool AnyInside, bool FullyInside) ClassifySlice(int z, int width, int height)
    {
        switch (Shape)
        {
            case VolumeCutShapeKind.Box:
            {
                if (z < BoxMin.Z || z > BoxMax.Z) return (false, false);
                var full = BoxMin.X <= 0 && BoxMax.X >= width - 1 && BoxMin.Y <= 0 && BoxMax.Y >= height - 1;
                return (true, full);
            }
            case VolumeCutShapeKind.Sphere:
            {
                var dz = z - SphereCenter.Z;
                if (MathF.Abs(dz) > SphereRadius) return (false, false);
                var slabRadiusSq = SphereRadius * SphereRadius - dz * dz;
                return (true, FarthestCornerSq(SphereCenter.X, SphereCenter.Y, width, height) <= slabRadiusSq);
            }
            default:
                switch (CylinderAxis)
                {
                    case 2:
                    {
                        if (z < CylinderAxisMin || z > CylinderAxisMax) return (false, false);
                        var full = FarthestCornerSq(CylinderCenter.X, CylinderCenter.Y, width, height) <=
                                   CylinderRadius * CylinderRadius;
                        return (true, full);
                    }
                    case 1:
                    {
                        var dz = z - CylinderCenter.Z;
                        if (MathF.Abs(dz) > CylinderRadius) return (false, false);
                        return (true, false);
                    }
                    default:
                    {
                        var dz = z - CylinderCenter.Z;
                        if (MathF.Abs(dz) > CylinderRadius) return (false, false);
                        return (true, false);
                    }
                }
        }
    }

    private static float FarthestCornerSq(float cx, float cy, int width, int height)
    {
        var dx = MathF.Max(cx, width - 1 - cx);
        var dy = MathF.Max(cy, height - 1 - cy);
        return dx * dx + dy * dy;
    }

    #region Aspect-ratio-locked resizing

    /// <summary>
    ///     After a box edge on <paramref name="draggedAxis" /> was resized, rescales the other two
    ///     axes about their drag-start centers by the same factor. <paramref name="dragStart" />
    ///     holds the geometry captured when the drag began.
    /// </summary>
    public void ApplyBoxAspect(VolumeCutState dragStart, int draggedAxis)
    {
        if (!LockAspectRatio) return;
        var startSize = Component(dragStart.BoxMax, draggedAxis) - Component(dragStart.BoxMin, draggedAxis);
        if (startSize <= 0) return;
        var factor = (Component(BoxMax, draggedAxis) - Component(BoxMin, draggedAxis)) / startSize;
        for (var axis = 0; axis < 3; axis++)
        {
            if (axis == draggedAxis) continue;
            var center = (Component(dragStart.BoxMin, axis) + Component(dragStart.BoxMax, axis)) * 0.5f;
            var half = MathF.Max(0.5f,
                (Component(dragStart.BoxMax, axis) - Component(dragStart.BoxMin, axis)) * 0.5f * factor);
            var min = BoxMin;
            var max = BoxMax;
            SetComponent(ref min, axis, center - half);
            SetComponent(ref max, axis, center + half);
            BoxMin = min;
            BoxMax = max;
        }
    }

    /// <summary>After the cylinder radius changed, rescales the axis extent about its center.</summary>
    public void ApplyCylinderAspectFromRadius(VolumeCutState dragStart)
    {
        if (!LockAspectRatio || dragStart.CylinderRadius <= 0) return;
        var factor = CylinderRadius / dragStart.CylinderRadius;
        var center = (dragStart.CylinderAxisMin + dragStart.CylinderAxisMax) * 0.5f;
        var half = MathF.Max(0.5f, (dragStart.CylinderAxisMax - dragStart.CylinderAxisMin) * 0.5f * factor);
        CylinderAxisMin = center - half;
        CylinderAxisMax = center + half;
    }

    /// <summary>After the cylinder extent changed, rescales the radius by the same factor.</summary>
    public void ApplyCylinderAspectFromExtent(VolumeCutState dragStart)
    {
        if (!LockAspectRatio) return;
        var startExtent = dragStart.CylinderAxisMax - dragStart.CylinderAxisMin;
        if (startExtent <= 0) return;
        var factor = (CylinderAxisMax - CylinderAxisMin) / startExtent;
        CylinderRadius = MathF.Max(1f, dragStart.CylinderRadius * factor);
    }

    #endregion
}
