// GeoscientistToolkit/Data/TwoDGeology/Geomechanics/GeomechanicsEditingHelpers.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GeoscientistToolkit.Data.TwoDGeology.Geomechanics;

#region Snapping System

/// <summary>
/// Types of snapping available
/// </summary>
[Flags]
public enum SnapMode
{
    None = 0,
    Grid = 1,
    Node = 2,
    Vertex = 4,
    Edge = 8,
    Center = 16,
    Midpoint = 32,
    Perpendicular = 64,
    Tangent = 128,
    Intersection = 256,
    Angle = 512,
    All = Grid | Node | Vertex | Edge | Center | Midpoint | Perpendicular | Angle
}

/// <summary>
/// Result of a snap operation
/// </summary>
public struct SnapResult
{
    public Vector2 Position;
    public SnapMode SnapType;
    public bool Snapped;
    public int TargetId;  // ID of snapped object (node, primitive, etc.)
    public string Description;

    public static SnapResult NoSnap(Vector2 pos) => new()
    {
        Position = pos,
        SnapType = SnapMode.None,
        Snapped = false,
        TargetId = -1,
        Description = string.Empty
    };
}

/// <summary>
/// Comprehensive snapping system for geometric editing
/// </summary>
public class SnappingSystem
{
    #region Properties

    /// <summary>
    /// Enabled snap modes
    /// </summary>
    public SnapMode EnabledModes { get; set; } = SnapMode.Grid | SnapMode.Node | SnapMode.Vertex;

    /// <summary>
    /// Grid spacing for grid snapping
    /// </summary>
    public float GridSpacing { get; set; } = 1.0f;

    /// <summary>
    /// Snap tolerance in world units
    /// </summary>
    public float SnapTolerance { get; set; } = 0.5f;

    /// <summary>
    /// Angle snap increment in degrees
    /// </summary>
    public float AngleSnapIncrement { get; set; } = 15f;

    /// <summary>
    /// Whether snapping is globally enabled
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Reference point for angle snapping
    /// </summary>
    public Vector2? AngleSnapOrigin { get; set; }

    #endregion

    #region Snap Methods

    /// <summary>
    /// Snap a point using all enabled modes
    /// </summary>
    public SnapResult Snap(Vector2 point, FEMMesh2D mesh, PrimitiveManager2D primitives)
    {
        if (!IsEnabled) return SnapResult.NoSnap(point);

        SnapResult best = SnapResult.NoSnap(point);
        float bestDist = SnapTolerance;

        // Try each snap mode in priority order
        if (EnabledModes.HasFlag(SnapMode.Node))
        {
            var result = SnapToNode(point, mesh);
            if (result.Snapped)
            {
                float dist = Vector2.Distance(point, result.Position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = result;
                }
            }
        }

        if (EnabledModes.HasFlag(SnapMode.Vertex))
        {
            var result = SnapToVertex(point, primitives);
            if (result.Snapped)
            {
                float dist = Vector2.Distance(point, result.Position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = result;
                }
            }
        }

        if (EnabledModes.HasFlag(SnapMode.Center))
        {
            var result = SnapToCenter(point, primitives);
            if (result.Snapped)
            {
                float dist = Vector2.Distance(point, result.Position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = result;
                }
            }
        }

        if (EnabledModes.HasFlag(SnapMode.Midpoint))
        {
            var result = SnapToMidpoint(point, primitives);
            if (result.Snapped)
            {
                float dist = Vector2.Distance(point, result.Position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = result;
                }
            }
        }

        if (EnabledModes.HasFlag(SnapMode.Edge))
        {
            var result = SnapToEdge(point, primitives);
            if (result.Snapped)
            {
                float dist = Vector2.Distance(point, result.Position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = result;
                }
            }
        }

        if (EnabledModes.HasFlag(SnapMode.Intersection))
        {
            var result = SnapToIntersection(point, primitives);
            if (result.Snapped)
            {
                float dist = Vector2.Distance(point, result.Position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = result;
                }
            }
        }

        // Grid snap has lowest priority (fallback)
        if (!best.Snapped && EnabledModes.HasFlag(SnapMode.Grid))
        {
            best = SnapToGrid(point);
        }

        return best;
    }

    /// <summary>
    /// Snap to grid
    /// </summary>
    public SnapResult SnapToGrid(Vector2 point)
    {
        float snappedX = MathF.Round(point.X / GridSpacing) * GridSpacing;
        float snappedY = MathF.Round(point.Y / GridSpacing) * GridSpacing;

        return new SnapResult
        {
            Position = new Vector2(snappedX, snappedY),
            SnapType = SnapMode.Grid,
            Snapped = true,
            TargetId = -1,
            Description = "Grid"
        };
    }

    /// <summary>
    /// Snap to mesh node
    /// </summary>
    public SnapResult SnapToNode(Vector2 point, FEMMesh2D mesh)
    {
        if (mesh == null) return SnapResult.NoSnap(point);

        float bestDist = SnapTolerance;
        FEMNode2D bestNode = null;

        foreach (var node in mesh.Nodes)
        {
            float dist = Vector2.Distance(point, node.InitialPosition);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestNode = node;
            }
        }

        if (bestNode != null)
        {
            return new SnapResult
            {
                Position = bestNode.InitialPosition,
                SnapType = SnapMode.Node,
                Snapped = true,
                TargetId = bestNode.Id,
                Description = $"Node {bestNode.Id}"
            };
        }

        return SnapResult.NoSnap(point);
    }

    /// <summary>
    /// Snap to primitive vertex
    /// </summary>
    public SnapResult SnapToVertex(Vector2 point, PrimitiveManager2D primitives)
    {
        if (primitives == null) return SnapResult.NoSnap(point);

        float bestDist = SnapTolerance;
        Vector2 bestVertex = point;
        int bestPrimId = -1;

        foreach (var prim in primitives.Primitives)
        {
            var vertices = prim.GetVertices();
            foreach (var v in vertices)
            {
                float dist = Vector2.Distance(point, v);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestVertex = v;
                    bestPrimId = prim.Id;
                }
            }
        }

        if (bestPrimId >= 0)
        {
            return new SnapResult
            {
                Position = bestVertex,
                SnapType = SnapMode.Vertex,
                Snapped = true,
                TargetId = bestPrimId,
                Description = "Vertex"
            };
        }

        return SnapResult.NoSnap(point);
    }

    /// <summary>
    /// Snap to primitive center
    /// </summary>
    public SnapResult SnapToCenter(Vector2 point, PrimitiveManager2D primitives)
    {
        if (primitives == null) return SnapResult.NoSnap(point);

        float bestDist = SnapTolerance;
        Vector2 bestCenter = point;
        int bestPrimId = -1;

        foreach (var prim in primitives.Primitives)
        {
            float dist = Vector2.Distance(point, prim.Position);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestCenter = prim.Position;
                bestPrimId = prim.Id;
            }
        }

        if (bestPrimId >= 0)
        {
            return new SnapResult
            {
                Position = bestCenter,
                SnapType = SnapMode.Center,
                Snapped = true,
                TargetId = bestPrimId,
                Description = "Center"
            };
        }

        return SnapResult.NoSnap(point);
    }

    /// <summary>
    /// Snap to edge midpoint
    /// </summary>
    public SnapResult SnapToMidpoint(Vector2 point, PrimitiveManager2D primitives)
    {
        if (primitives == null) return SnapResult.NoSnap(point);

        float bestDist = SnapTolerance;
        Vector2 bestMidpoint = point;
        int bestPrimId = -1;

        foreach (var prim in primitives.Primitives)
        {
            var vertices = prim.GetVertices();
            for (int i = 0; i < vertices.Count; i++)
            {
                int j = (i + 1) % vertices.Count;
                var midpoint = (vertices[i] + vertices[j]) / 2;

                float dist = Vector2.Distance(point, midpoint);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestMidpoint = midpoint;
                    bestPrimId = prim.Id;
                }
            }
        }

        if (bestPrimId >= 0)
        {
            return new SnapResult
            {
                Position = bestMidpoint,
                SnapType = SnapMode.Midpoint,
                Snapped = true,
                TargetId = bestPrimId,
                Description = "Midpoint"
            };
        }

        return SnapResult.NoSnap(point);
    }

    /// <summary>
    /// Snap to nearest point on edge
    /// </summary>
    public SnapResult SnapToEdge(Vector2 point, PrimitiveManager2D primitives)
    {
        if (primitives == null) return SnapResult.NoSnap(point);

        float bestDist = SnapTolerance;
        Vector2 bestPoint = point;
        int bestPrimId = -1;

        foreach (var prim in primitives.Primitives)
        {
            var vertices = prim.GetVertices();
            for (int i = 0; i < vertices.Count; i++)
            {
                int j = (i + 1) % vertices.Count;
                var closest = ClosestPointOnSegment(point, vertices[i], vertices[j]);

                float dist = Vector2.Distance(point, closest);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestPoint = closest;
                    bestPrimId = prim.Id;
                }
            }
        }

        if (bestPrimId >= 0)
        {
            return new SnapResult
            {
                Position = bestPoint,
                SnapType = SnapMode.Edge,
                Snapped = true,
                TargetId = bestPrimId,
                Description = "Edge"
            };
        }

        return SnapResult.NoSnap(point);
    }

    /// <summary>
    /// Snap to edge intersection
    /// </summary>
    public SnapResult SnapToIntersection(Vector2 point, PrimitiveManager2D primitives)
    {
        if (primitives == null) return SnapResult.NoSnap(point);

        var allEdges = new List<(Vector2 a, Vector2 b, int primId)>();

        foreach (var prim in primitives.Primitives)
        {
            var vertices = prim.GetVertices();
            for (int i = 0; i < vertices.Count; i++)
            {
                int j = (i + 1) % vertices.Count;
                allEdges.Add((vertices[i], vertices[j], prim.Id));
            }
        }

        float bestDist = SnapTolerance;
        Vector2 bestIntersection = point;
        bool found = false;

        // Check all edge pairs for intersections
        for (int i = 0; i < allEdges.Count; i++)
        {
            for (int j = i + 1; j < allEdges.Count; j++)
            {
                if (TryGetIntersection(allEdges[i].a, allEdges[i].b,
                                       allEdges[j].a, allEdges[j].b,
                                       out Vector2 intersection))
                {
                    float dist = Vector2.Distance(point, intersection);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestIntersection = intersection;
                        found = true;
                    }
                }
            }
        }

        if (found)
        {
            return new SnapResult
            {
                Position = bestIntersection,
                SnapType = SnapMode.Intersection,
                Snapped = true,
                TargetId = -1,
                Description = "Intersection"
            };
        }

        return SnapResult.NoSnap(point);
    }

    /// <summary>
    /// Snap angle to increments
    /// </summary>
    public float SnapAngle(float angle)
    {
        if (!EnabledModes.HasFlag(SnapMode.Angle)) return angle;

        return MathF.Round(angle / AngleSnapIncrement) * AngleSnapIncrement;
    }

    /// <summary>
    /// Snap angle from reference point
    /// </summary>
    public Vector2 SnapAngleFromOrigin(Vector2 origin, Vector2 point)
    {
        if (!EnabledModes.HasFlag(SnapMode.Angle)) return point;

        var delta = point - origin;
        float length = delta.Length();
        if (length < 0.001f) return point;

        float angle = MathF.Atan2(delta.Y, delta.X) * 180f / MathF.PI;
        float snappedAngle = SnapAngle(angle) * MathF.PI / 180f;

        return origin + new Vector2(MathF.Cos(snappedAngle), MathF.Sin(snappedAngle)) * length;
    }

    #endregion

    #region Helper Methods

    private static Vector2 ClosestPointOnSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float t = Vector2.Dot(point - a, ab) / Vector2.Dot(ab, ab);
        t = Math.Clamp(t, 0, 1);
        return a + ab * t;
    }

    private static bool TryGetIntersection(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2, out Vector2 intersection)
    {
        intersection = Vector2.Zero;

        var d1 = a2 - a1;
        var d2 = b2 - b1;

        float cross = d1.X * d2.Y - d1.Y * d2.X;
        if (MathF.Abs(cross) < 1e-10f) return false; // Parallel

        var d = b1 - a1;
        float t = (d.X * d2.Y - d.Y * d2.X) / cross;
        float u = (d.X * d1.Y - d.Y * d1.X) / cross;

        if (t >= 0 && t <= 1 && u >= 0 && u <= 1)
        {
            intersection = a1 + d1 * t;
            return true;
        }

        return false;
    }

    #endregion
}

#endregion

#region Transform Handles

/// <summary>
/// Types of transform handles
/// </summary>
public enum HandleType
{
    None,
    Move,
    RotateTopLeft,
    RotateTopRight,
    RotateBottomLeft,
    RotateBottomRight,
    ScaleTop,
    ScaleBottom,
    ScaleLeft,
    ScaleRight,
    ScaleTopLeft,
    ScaleTopRight,
    ScaleBottomLeft,
    ScaleBottomRight
}

/// <summary>
/// Transform handle for interactive manipulation
/// </summary>
public struct TransformHandle
{
    public HandleType Type;
    public Vector2 Position;
    public float Size;
    public bool IsHovered;

    public bool ContainsPoint(Vector2 point)
    {
        return Vector2.Distance(point, Position) <= Size;
    }
}

/// <summary>
/// System for rendering and interacting with transform handles
/// </summary>
public class TransformHandleSystem
{
    #region Properties

    /// <summary>
    /// Size of handle in screen pixels
    /// </summary>
    public float HandleSize { get; set; } = 8f;

    /// <summary>
    /// Currently active handle
    /// </summary>
    public HandleType ActiveHandle { get; private set; } = HandleType.None;

    /// <summary>
    /// Currently hovered handle
    /// </summary>
    public HandleType HoveredHandle { get; private set; } = HandleType.None;

    /// <summary>
    /// Whether user is currently dragging a handle
    /// </summary>
    public bool IsDragging { get; private set; }

    /// <summary>
    /// Starting position when drag began
    /// </summary>
    public Vector2 DragStartPosition { get; private set; }

    /// <summary>
    /// Original primitive state when drag began
    /// </summary>
    public PrimitiveState DragStartState { get; private set; }

    #endregion

    #region Public Methods

    /// <summary>
    /// Get all handles for a primitive
    /// </summary>
    public List<TransformHandle> GetHandles(GeometricPrimitive2D primitive, float zoom)
    {
        if (primitive == null) return new List<TransformHandle>();

        var handles = new List<TransformHandle>();
        var (min, max) = primitive.GetBoundingBox();
        var center = (min + max) / 2;
        float handleSize = HandleSize / zoom;

        // Move handle (center)
        handles.Add(new TransformHandle
        {
            Type = HandleType.Move,
            Position = center,
            Size = handleSize * 1.5f
        });

        // Scale handles (corners)
        handles.Add(new TransformHandle
        {
            Type = HandleType.ScaleTopLeft,
            Position = new Vector2(min.X, max.Y),
            Size = handleSize
        });
        handles.Add(new TransformHandle
        {
            Type = HandleType.ScaleTopRight,
            Position = new Vector2(max.X, max.Y),
            Size = handleSize
        });
        handles.Add(new TransformHandle
        {
            Type = HandleType.ScaleBottomLeft,
            Position = new Vector2(min.X, min.Y),
            Size = handleSize
        });
        handles.Add(new TransformHandle
        {
            Type = HandleType.ScaleBottomRight,
            Position = new Vector2(max.X, min.Y),
            Size = handleSize
        });

        // Scale handles (edges)
        handles.Add(new TransformHandle
        {
            Type = HandleType.ScaleTop,
            Position = new Vector2(center.X, max.Y),
            Size = handleSize
        });
        handles.Add(new TransformHandle
        {
            Type = HandleType.ScaleBottom,
            Position = new Vector2(center.X, min.Y),
            Size = handleSize
        });
        handles.Add(new TransformHandle
        {
            Type = HandleType.ScaleLeft,
            Position = new Vector2(min.X, center.Y),
            Size = handleSize
        });
        handles.Add(new TransformHandle
        {
            Type = HandleType.ScaleRight,
            Position = new Vector2(max.X, center.Y),
            Size = handleSize
        });

        // Rotate handles (outside corners)
        float rotateOffset = handleSize * 2f;
        handles.Add(new TransformHandle
        {
            Type = HandleType.RotateTopLeft,
            Position = new Vector2(min.X - rotateOffset, max.Y + rotateOffset),
            Size = handleSize
        });
        handles.Add(new TransformHandle
        {
            Type = HandleType.RotateTopRight,
            Position = new Vector2(max.X + rotateOffset, max.Y + rotateOffset),
            Size = handleSize
        });
        handles.Add(new TransformHandle
        {
            Type = HandleType.RotateBottomLeft,
            Position = new Vector2(min.X - rotateOffset, min.Y - rotateOffset),
            Size = handleSize
        });
        handles.Add(new TransformHandle
        {
            Type = HandleType.RotateBottomRight,
            Position = new Vector2(max.X + rotateOffset, min.Y - rotateOffset),
            Size = handleSize
        });

        return handles;
    }

    /// <summary>
    /// Update hover state based on mouse position
    /// </summary>
    public HandleType UpdateHover(Vector2 worldPos, GeometricPrimitive2D primitive, float zoom)
    {
        HoveredHandle = HandleType.None;

        if (primitive == null || IsDragging) return HoveredHandle;

        var handles = GetHandles(primitive, zoom);
        foreach (var handle in handles)
        {
            if (handle.ContainsPoint(worldPos))
            {
                HoveredHandle = handle.Type;
                break;
            }
        }

        return HoveredHandle;
    }

    /// <summary>
    /// Begin dragging a handle
    /// </summary>
    public void BeginDrag(Vector2 worldPos, GeometricPrimitive2D primitive, float zoom)
    {
        if (primitive == null) return;

        var handles = GetHandles(primitive, zoom);
        foreach (var handle in handles)
        {
            if (handle.ContainsPoint(worldPos))
            {
                ActiveHandle = handle.Type;
                IsDragging = true;
                DragStartPosition = worldPos;
                DragStartState = new PrimitiveState(primitive);
                break;
            }
        }
    }

    /// <summary>
    /// Update drag with new mouse position
    /// </summary>
    public void UpdateDrag(Vector2 worldPos, GeometricPrimitive2D primitive, SnappingSystem snapping)
    {
        if (!IsDragging || primitive == null) return;

        // Apply snapping if enabled
        if (snapping != null && snapping.IsEnabled)
        {
            var snapResult = snapping.Snap(worldPos, null, null);
            if (snapResult.Snapped)
            {
                worldPos = snapResult.Position;
            }
        }

        var delta = worldPos - DragStartPosition;

        switch (ActiveHandle)
        {
            case HandleType.Move:
                primitive.Position = DragStartState.Position + delta;
                break;

            case HandleType.ScaleTopLeft:
            case HandleType.ScaleTopRight:
            case HandleType.ScaleBottomLeft:
            case HandleType.ScaleBottomRight:
            case HandleType.ScaleTop:
            case HandleType.ScaleBottom:
            case HandleType.ScaleLeft:
            case HandleType.ScaleRight:
                ApplyScale(primitive, worldPos);
                break;

            case HandleType.RotateTopLeft:
            case HandleType.RotateTopRight:
            case HandleType.RotateBottomLeft:
            case HandleType.RotateBottomRight:
                ApplyRotation(primitive, worldPos, snapping);
                break;
        }
    }

    /// <summary>
    /// End dragging
    /// </summary>
    public void EndDrag()
    {
        ActiveHandle = HandleType.None;
        IsDragging = false;
    }

    /// <summary>
    /// Cancel drag and restore original state
    /// </summary>
    public void CancelDrag(GeometricPrimitive2D primitive)
    {
        if (IsDragging && primitive != null && DragStartState != null)
        {
            DragStartState.ApplyTo(primitive);
        }
        EndDrag();
    }

    #endregion

    #region Private Methods

    private void ApplyScale(GeometricPrimitive2D primitive, Vector2 worldPos)
    {
        if (primitive is RectanglePrimitive rect)
        {
            ApplyScaleToRect(rect, worldPos);
        }
        else if (primitive is CirclePrimitive circle)
        {
            ApplyScaleToCircle(circle, worldPos);
        }
        else if (primitive is EllipsePrimitive ellipse)
        {
            ApplyScaleToEllipse(ellipse, worldPos);
        }
    }

    private void ApplyScaleToRect(RectanglePrimitive rect, Vector2 worldPos)
    {
        var center = DragStartState.Position;
        var delta = worldPos - center;

        switch (ActiveHandle)
        {
            case HandleType.ScaleTopLeft:
                rect.Width = Math.Max(0.1, DragStartState.Width - (worldPos.X - DragStartPosition.X));
                rect.Height = Math.Max(0.1, DragStartState.Height + (worldPos.Y - DragStartPosition.Y));
                rect.Position = center + new Vector2((float)(DragStartState.Width - rect.Width) / 2,
                                                      (float)(rect.Height - DragStartState.Height) / 2);
                break;
            case HandleType.ScaleTopRight:
                rect.Width = Math.Max(0.1, DragStartState.Width + (worldPos.X - DragStartPosition.X));
                rect.Height = Math.Max(0.1, DragStartState.Height + (worldPos.Y - DragStartPosition.Y));
                rect.Position = center + new Vector2((float)(rect.Width - DragStartState.Width) / 2,
                                                      (float)(rect.Height - DragStartState.Height) / 2);
                break;
            case HandleType.ScaleBottomLeft:
                rect.Width = Math.Max(0.1, DragStartState.Width - (worldPos.X - DragStartPosition.X));
                rect.Height = Math.Max(0.1, DragStartState.Height - (worldPos.Y - DragStartPosition.Y));
                rect.Position = center + new Vector2((float)(DragStartState.Width - rect.Width) / 2,
                                                      (float)(DragStartState.Height - rect.Height) / 2);
                break;
            case HandleType.ScaleBottomRight:
                rect.Width = Math.Max(0.1, DragStartState.Width + (worldPos.X - DragStartPosition.X));
                rect.Height = Math.Max(0.1, DragStartState.Height - (worldPos.Y - DragStartPosition.Y));
                rect.Position = center + new Vector2((float)(rect.Width - DragStartState.Width) / 2,
                                                      (float)(DragStartState.Height - rect.Height) / 2);
                break;
            case HandleType.ScaleTop:
                rect.Height = Math.Max(0.1, DragStartState.Height + (worldPos.Y - DragStartPosition.Y));
                rect.Position = center + new Vector2(0, (float)(rect.Height - DragStartState.Height) / 2);
                break;
            case HandleType.ScaleBottom:
                rect.Height = Math.Max(0.1, DragStartState.Height - (worldPos.Y - DragStartPosition.Y));
                rect.Position = center + new Vector2(0, (float)(DragStartState.Height - rect.Height) / 2);
                break;
            case HandleType.ScaleLeft:
                rect.Width = Math.Max(0.1, DragStartState.Width - (worldPos.X - DragStartPosition.X));
                rect.Position = center + new Vector2((float)(DragStartState.Width - rect.Width) / 2, 0);
                break;
            case HandleType.ScaleRight:
                rect.Width = Math.Max(0.1, DragStartState.Width + (worldPos.X - DragStartPosition.X));
                rect.Position = center + new Vector2((float)(rect.Width - DragStartState.Width) / 2, 0);
                break;
        }
    }

    private void ApplyScaleToCircle(CirclePrimitive circle, Vector2 worldPos)
    {
        float dist = Vector2.Distance(worldPos, DragStartState.Position);
        circle.Radius = Math.Max(0.1, dist);
    }

    private void ApplyScaleToEllipse(EllipsePrimitive ellipse, Vector2 worldPos)
    {
        var center = DragStartState.Position;
        var delta = worldPos - center;

        switch (ActiveHandle)
        {
            case HandleType.ScaleLeft:
            case HandleType.ScaleRight:
                ellipse.SemiAxisA = Math.Max(0.1, Math.Abs(delta.X));
                break;
            case HandleType.ScaleTop:
            case HandleType.ScaleBottom:
                ellipse.SemiAxisB = Math.Max(0.1, Math.Abs(delta.Y));
                break;
            default:
                ellipse.SemiAxisA = Math.Max(0.1, Math.Abs(delta.X));
                ellipse.SemiAxisB = Math.Max(0.1, Math.Abs(delta.Y));
                break;
        }
    }

    private void ApplyRotation(GeometricPrimitive2D primitive, Vector2 worldPos, SnappingSystem snapping)
    {
        var center = DragStartState.Position;

        float startAngle = MathF.Atan2(DragStartPosition.Y - center.Y, DragStartPosition.X - center.X);
        float currentAngle = MathF.Atan2(worldPos.Y - center.Y, worldPos.X - center.X);

        float deltaAngle = (currentAngle - startAngle) * 180f / MathF.PI;

        // Apply angle snapping
        if (snapping != null && snapping.EnabledModes.HasFlag(SnapMode.Angle))
        {
            deltaAngle = snapping.SnapAngle(deltaAngle);
        }

        primitive.Rotation = DragStartState.Rotation + deltaAngle;
    }

    #endregion
}

/// <summary>
/// Stores primitive state for undo/redo and drag operations
/// </summary>
public class PrimitiveState
{
    public Vector2 Position { get; set; }
    public double Rotation { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Radius { get; set; }
    public double SemiAxisA { get; set; }
    public double SemiAxisB { get; set; }
    public List<Vector2> Vertices { get; set; }

    public PrimitiveState() { }

    public PrimitiveState(GeometricPrimitive2D primitive)
    {
        Position = primitive.Position;
        Rotation = primitive.Rotation;

        if (primitive is RectanglePrimitive rect)
        {
            Width = rect.Width;
            Height = rect.Height;
        }
        else if (primitive is CirclePrimitive circle)
        {
            Radius = circle.Radius;
        }
        else if (primitive is EllipsePrimitive ellipse)
        {
            SemiAxisA = ellipse.SemiAxisA;
            SemiAxisB = ellipse.SemiAxisB;
        }
        else if (primitive is PolygonPrimitive poly)
        {
            Vertices = new List<Vector2>(poly.LocalVertices);
        }
    }

    public void ApplyTo(GeometricPrimitive2D primitive)
    {
        primitive.Position = Position;
        primitive.Rotation = Rotation;

        if (primitive is RectanglePrimitive rect)
        {
            rect.Width = Width;
            rect.Height = Height;
        }
        else if (primitive is CirclePrimitive circle)
        {
            circle.Radius = Radius;
        }
        else if (primitive is EllipsePrimitive ellipse)
        {
            ellipse.SemiAxisA = SemiAxisA;
            ellipse.SemiAxisB = SemiAxisB;
        }
        else if (primitive is PolygonPrimitive poly && Vertices != null)
        {
            poly.LocalVertices = new List<Vector2>(Vertices);
        }
    }
}

#endregion

#region Undo/Redo System

/// <summary>
/// Types of editing operations that can be undone
/// </summary>
public enum EditOperationType
{
    AddPrimitive,
    RemovePrimitive,
    ModifyPrimitive,
    AddJointSet,
    RemoveJointSet,
    ModifyJointSet,
    AddNode,
    RemoveNode,
    ModifyBoundaryCondition,
    ApplyLoad,
    Multiple
}

/// <summary>
/// Single edit operation that can be undone
/// </summary>
public abstract class EditOperation
{
    public EditOperationType Type { get; protected set; }
    public string Description { get; protected set; }
    public DateTime Timestamp { get; } = DateTime.Now;

    public abstract void Undo(EditContext context);
    public abstract void Redo(EditContext context);
}

/// <summary>
/// Context containing all editable objects
/// </summary>
public class EditContext
{
    public PrimitiveManager2D Primitives { get; set; }
    public JointSetManager JointSets { get; set; }
    public FEMMesh2D Mesh { get; set; }
    public TwoDGeomechanicalSimulator Simulator { get; set; }
}

/// <summary>
/// Operation for adding a primitive
/// </summary>
public class AddPrimitiveOperation : EditOperation
{
    private readonly GeometricPrimitive2D _primitive;
    private int _primitiveId;

    public AddPrimitiveOperation(GeometricPrimitive2D primitive)
    {
        Type = EditOperationType.AddPrimitive;
        _primitive = primitive.Clone();
        Description = $"Add {primitive.Type}";
    }

    public override void Undo(EditContext context)
    {
        context.Primitives.RemovePrimitive(_primitiveId);
    }

    public override void Redo(EditContext context)
    {
        _primitiveId = context.Primitives.AddPrimitive(_primitive.Clone());
    }
}

/// <summary>
/// Operation for removing a primitive
/// </summary>
public class RemovePrimitiveOperation : EditOperation
{
    private readonly GeometricPrimitive2D _primitive;
    private readonly int _primitiveId;

    public RemovePrimitiveOperation(GeometricPrimitive2D primitive)
    {
        Type = EditOperationType.RemovePrimitive;
        _primitive = primitive.Clone();
        _primitiveId = primitive.Id;
        Description = $"Remove {primitive.Type}";
    }

    public override void Undo(EditContext context)
    {
        var restored = _primitive.Clone();
        restored.Id = _primitiveId;
        context.Primitives.Primitives.Add(restored);
    }

    public override void Redo(EditContext context)
    {
        context.Primitives.RemovePrimitive(_primitiveId);
    }
}

/// <summary>
/// Operation for modifying a primitive
/// </summary>
public class ModifyPrimitiveOperation : EditOperation
{
    private readonly int _primitiveId;
    private readonly PrimitiveState _beforeState;
    private readonly PrimitiveState _afterState;

    public ModifyPrimitiveOperation(GeometricPrimitive2D primitive, PrimitiveState beforeState)
    {
        Type = EditOperationType.ModifyPrimitive;
        _primitiveId = primitive.Id;
        _beforeState = beforeState;
        _afterState = new PrimitiveState(primitive);
        Description = $"Modify {primitive.Type}";
    }

    public override void Undo(EditContext context)
    {
        var prim = context.Primitives.GetPrimitive(_primitiveId);
        if (prim != null)
        {
            _beforeState.ApplyTo(prim);
        }
    }

    public override void Redo(EditContext context)
    {
        var prim = context.Primitives.GetPrimitive(_primitiveId);
        if (prim != null)
        {
            _afterState.ApplyTo(prim);
        }
    }
}

/// <summary>
/// Compound operation containing multiple operations
/// </summary>
public class CompoundOperation : EditOperation
{
    private readonly List<EditOperation> _operations = new();

    public CompoundOperation(string description)
    {
        Type = EditOperationType.Multiple;
        Description = description;
    }

    public void AddOperation(EditOperation op)
    {
        _operations.Add(op);
    }

    public override void Undo(EditContext context)
    {
        // Undo in reverse order
        for (int i = _operations.Count - 1; i >= 0; i--)
        {
            _operations[i].Undo(context);
        }
    }

    public override void Redo(EditContext context)
    {
        foreach (var op in _operations)
        {
            op.Redo(context);
        }
    }
}

/// <summary>
/// Manages undo/redo history
/// </summary>
public class UndoRedoManager
{
    private readonly Stack<EditOperation> _undoStack = new();
    private readonly Stack<EditOperation> _redoStack = new();
    private readonly EditContext _context;

    /// <summary>
    /// Maximum number of operations to keep
    /// </summary>
    public int MaxHistorySize { get; set; } = 100;

    /// <summary>
    /// Whether undo is available
    /// </summary>
    public bool CanUndo => _undoStack.Count > 0;

    /// <summary>
    /// Whether redo is available
    /// </summary>
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>
    /// Description of next undo operation
    /// </summary>
    public string UndoDescription => CanUndo ? _undoStack.Peek().Description : string.Empty;

    /// <summary>
    /// Description of next redo operation
    /// </summary>
    public string RedoDescription => CanRedo ? _redoStack.Peek().Description : string.Empty;

    public UndoRedoManager(EditContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Record an operation for undo
    /// </summary>
    public void RecordOperation(EditOperation operation)
    {
        _undoStack.Push(operation);
        _redoStack.Clear(); // Clear redo stack when new operation is recorded

        // Trim history if too large
        while (_undoStack.Count > MaxHistorySize)
        {
            // Remove oldest operations (need to convert to list and back)
            var ops = _undoStack.ToArray();
            _undoStack.Clear();
            for (int i = 0; i < ops.Length - 1; i++)
            {
                _undoStack.Push(ops[ops.Length - 2 - i]);
            }
        }
    }

    /// <summary>
    /// Undo the last operation
    /// </summary>
    public void Undo()
    {
        if (!CanUndo) return;

        var op = _undoStack.Pop();
        op.Undo(_context);
        _redoStack.Push(op);
    }

    /// <summary>
    /// Redo the last undone operation
    /// </summary>
    public void Redo()
    {
        if (!CanRedo) return;

        var op = _redoStack.Pop();
        op.Redo(_context);
        _undoStack.Push(op);
    }

    /// <summary>
    /// Clear all history
    /// </summary>
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}

#endregion

#region Interactive Deformation Preview

/// <summary>
/// Provides interactive deformation preview during simulation
/// </summary>
public class DeformationPreview
{
    private double[] _previewDisplacements;
    private double _previewScale = 1.0;
    private bool _isAnimating;
    private double _animationTime;
    private double _animationSpeed = 1.0;

    /// <summary>
    /// Whether preview is enabled
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Scale factor for deformation display
    /// </summary>
    public double DeformationScale
    {
        get => _previewScale;
        set => _previewScale = Math.Max(0.1, value);
    }

    /// <summary>
    /// Whether to animate between undeformed and deformed states
    /// </summary>
    public bool AnimateDeformation
    {
        get => _isAnimating;
        set
        {
            _isAnimating = value;
            if (value) _animationTime = 0;
        }
    }

    /// <summary>
    /// Animation speed (cycles per second)
    /// </summary>
    public double AnimationSpeed
    {
        get => _animationSpeed;
        set => _animationSpeed = Math.Max(0.1, value);
    }

    /// <summary>
    /// Update preview with current simulation results
    /// </summary>
    public void UpdateFromResults(SimulationResults2D results)
    {
        if (results == null) return;

        int numNodes = results.DisplacementX.Length;
        _previewDisplacements = new double[numNodes * 2];

        for (int i = 0; i < numNodes; i++)
        {
            _previewDisplacements[i * 2] = results.DisplacementX[i];
            _previewDisplacements[i * 2 + 1] = results.DisplacementY[i];
        }
    }

    /// <summary>
    /// Update animation
    /// </summary>
    public void Update(double deltaTime)
    {
        if (_isAnimating)
        {
            _animationTime += deltaTime * _animationSpeed;
        }
    }

    /// <summary>
    /// Get current animation factor (0-1)
    /// </summary>
    public double GetAnimationFactor()
    {
        if (!_isAnimating) return 1.0;

        // Smooth oscillation between 0 and 1
        return (1.0 + Math.Sin(_animationTime * 2 * Math.PI)) / 2.0;
    }

    /// <summary>
    /// Get deformed position for a node
    /// </summary>
    public Vector2 GetDeformedPosition(FEMNode2D node)
    {
        if (!IsEnabled || _previewDisplacements == null || node.Id * 2 + 1 >= _previewDisplacements.Length)
        {
            return node.InitialPosition;
        }

        double factor = GetAnimationFactor() * _previewScale;
        double ux = _previewDisplacements[node.Id * 2] * factor;
        double uy = _previewDisplacements[node.Id * 2 + 1] * factor;

        return node.InitialPosition + new Vector2((float)ux, (float)uy);
    }

    /// <summary>
    /// Get deformed vertices for a primitive based on nearby mesh deformation
    /// </summary>
    public List<Vector2> GetDeformedVertices(GeometricPrimitive2D primitive, FEMMesh2D mesh)
    {
        if (!IsEnabled || mesh == null || _previewDisplacements == null)
        {
            return primitive.GetVertices();
        }

        var vertices = primitive.GetVertices();
        var deformed = new List<Vector2>();

        foreach (var v in vertices)
        {
            // Find nearest mesh node and use its displacement
            var displacement = InterpolateDisplacement(v, mesh);
            deformed.Add(v + displacement * (float)(GetAnimationFactor() * _previewScale));
        }

        return deformed;
    }

    private Vector2 InterpolateDisplacement(Vector2 point, FEMMesh2D mesh)
    {
        // Find containing element or nearest node
        foreach (var element in mesh.Elements)
        {
            var nodes = mesh.Nodes.ToArray();
            var centroid = element.GetCentroid(nodes);

            if (Vector2.Distance(point, centroid) < element.GetArea(nodes))
            {
                // Use element average displacement
                Vector2 avgDisp = Vector2.Zero;
                foreach (int nodeId in element.NodeIds)
                {
                    if (nodeId * 2 + 1 < _previewDisplacements.Length)
                    {
                        avgDisp.X += (float)_previewDisplacements[nodeId * 2];
                        avgDisp.Y += (float)_previewDisplacements[nodeId * 2 + 1];
                    }
                }
                return avgDisp / element.NodeIds.Count;
            }
        }

        // Fallback: use nearest node
        FEMNode2D nearest = null;
        float minDist = float.MaxValue;

        foreach (var node in mesh.Nodes)
        {
            float dist = Vector2.Distance(point, node.InitialPosition);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = node;
            }
        }

        if (nearest != null && nearest.Id * 2 + 1 < _previewDisplacements.Length)
        {
            return new Vector2(
                (float)_previewDisplacements[nearest.Id * 2],
                (float)_previewDisplacements[nearest.Id * 2 + 1]);
        }

        return Vector2.Zero;
    }
}

#endregion

#region Coordinate Display and Measurement

/// <summary>
/// System for displaying coordinates and making measurements
/// </summary>
public class MeasurementSystem
{
    /// <summary>
    /// Current mouse position in world coordinates
    /// </summary>
    public Vector2 CurrentPosition { get; set; }

    /// <summary>
    /// Whether measurement mode is active
    /// </summary>
    public bool IsMeasuring { get; private set; }

    /// <summary>
    /// Start point of current measurement
    /// </summary>
    public Vector2 MeasureStart { get; private set; }

    /// <summary>
    /// All measurement points (for multi-point measurements)
    /// </summary>
    public List<Vector2> MeasurePoints { get; } = new();

    /// <summary>
    /// Current measurement type
    /// </summary>
    public MeasurementType MeasureType { get; set; } = MeasurementType.Distance;

    /// <summary>
    /// Start a measurement
    /// </summary>
    public void StartMeasurement(Vector2 point)
    {
        IsMeasuring = true;
        MeasureStart = point;
        MeasurePoints.Clear();
        MeasurePoints.Add(point);
    }

    /// <summary>
    /// Add a point to measurement
    /// </summary>
    public void AddPoint(Vector2 point)
    {
        if (IsMeasuring)
        {
            MeasurePoints.Add(point);
        }
    }

    /// <summary>
    /// End measurement
    /// </summary>
    public void EndMeasurement()
    {
        IsMeasuring = false;
    }

    /// <summary>
    /// Clear measurement
    /// </summary>
    public void ClearMeasurement()
    {
        IsMeasuring = false;
        MeasurePoints.Clear();
    }

    /// <summary>
    /// Get distance between two points
    /// </summary>
    public float GetDistance()
    {
        if (MeasurePoints.Count < 2) return 0;
        return Vector2.Distance(MeasurePoints[0], MeasurePoints[^1]);
    }

    /// <summary>
    /// Get total length of polyline
    /// </summary>
    public float GetPolylineLength()
    {
        if (MeasurePoints.Count < 2) return 0;

        float length = 0;
        for (int i = 1; i < MeasurePoints.Count; i++)
        {
            length += Vector2.Distance(MeasurePoints[i - 1], MeasurePoints[i]);
        }
        return length;
    }

    /// <summary>
    /// Get angle between three points (in degrees)
    /// </summary>
    public float GetAngle()
    {
        if (MeasurePoints.Count < 3) return 0;

        var v1 = MeasurePoints[0] - MeasurePoints[1];
        var v2 = MeasurePoints[2] - MeasurePoints[1];

        float dot = Vector2.Dot(v1, v2);
        float cross = v1.X * v2.Y - v1.Y * v2.X;

        return MathF.Atan2(cross, dot) * 180f / MathF.PI;
    }

    /// <summary>
    /// Get area of polygon formed by measurement points
    /// </summary>
    public float GetArea()
    {
        if (MeasurePoints.Count < 3) return 0;

        float area = 0;
        int j = MeasurePoints.Count - 1;

        for (int i = 0; i < MeasurePoints.Count; i++)
        {
            area += (MeasurePoints[j].X + MeasurePoints[i].X) *
                    (MeasurePoints[j].Y - MeasurePoints[i].Y);
            j = i;
        }

        return MathF.Abs(area / 2);
    }

    /// <summary>
    /// Format coordinate for display
    /// </summary>
    public static string FormatCoordinate(Vector2 pos, int decimals = 3)
    {
        return $"({pos.X.ToString($"F{decimals}")}, {pos.Y.ToString($"F{decimals}")})";
    }

    /// <summary>
    /// Format distance for display
    /// </summary>
    public static string FormatDistance(float distance, int decimals = 3)
    {
        return $"{distance.ToString($"F{decimals}")} m";
    }

    /// <summary>
    /// Format angle for display
    /// </summary>
    public static string FormatAngle(float angle, int decimals = 1)
    {
        return $"{angle.ToString($"F{decimals}")}°";
    }

    /// <summary>
    /// Format area for display
    /// </summary>
    public static string FormatArea(float area, int decimals = 3)
    {
        return $"{area.ToString($"F{decimals}")} m²";
    }
}

/// <summary>
/// Types of measurements
/// </summary>
public enum MeasurementType
{
    Distance,
    Polyline,
    Angle,
    Area,
    Coordinates
}

#endregion

#region Deform Tool

/// <summary>
/// Types of deformation
/// </summary>
public enum DeformationType
{
    FreeForm,       // Move individual vertices freely
    Bend,           // Bend along an axis
    Twist,          // Twist around a center point
    Taper,          // Scale progressively along an axis
    Stretch,        // Non-uniform scaling
    Shear,          // Shear deformation
    Bulge,          // Bulge outward/inward
    Wave,           // Sinusoidal wave deformation
    Lattice         // FFD lattice deformation
}

/// <summary>
/// Deform handle for interactive vertex manipulation
/// </summary>
public struct DeformHandle
{
    public int Index;           // Vertex/control point index
    public Vector2 Position;    // Current position
    public Vector2 OriginalPosition; // Position before deformation
    public float Size;          // Handle size
    public bool IsSelected;
    public bool IsHovered;

    public bool ContainsPoint(Vector2 point)
    {
        return Vector2.Distance(point, Position) <= Size;
    }
}

/// <summary>
/// Lattice control point for FFD
/// </summary>
public struct LatticePoint
{
    public int I, J;            // Lattice indices
    public Vector2 Position;
    public Vector2 OriginalPosition;
    public float Weight;        // Influence weight
}

/// <summary>
/// Comprehensive deform tool for shapes, faults, joints, and domains
/// </summary>
public class DeformTool
{
    #region Fields

    private List<DeformHandle> _handles = new();
    private List<LatticePoint> _latticePoints = new();
    private List<Vector2> _originalVertices = new();
    private DeformationType _deformationType = DeformationType.FreeForm;
    private int _selectedHandleIndex = -1;
    private int _hoveredHandleIndex = -1;
    private bool _isDragging;
    private Vector2 _dragStartPos;
    private Vector2 _deformCenter;
    private Vector2 _deformAxis = Vector2.UnitX;

    // Lattice settings
    private int _latticeResolutionX = 4;
    private int _latticeResolutionY = 4;

    // Deformation parameters
    private float _bendAngle;
    private float _twistAngle;
    private float _taperFactor = 1.0f;
    private float _shearFactor;
    private float _bulgeFactor;
    private float _waveAmplitude = 0.5f;
    private float _waveFrequency = 2.0f;

    #endregion

    #region Properties

    /// <summary>
    /// Current deformation type
    /// </summary>
    public DeformationType DeformationType
    {
        get => _deformationType;
        set
        {
            _deformationType = value;
            RegenerateHandles();
        }
    }

    /// <summary>
    /// Handle size in screen pixels
    /// </summary>
    public float HandleSize { get; set; } = 6f;

    /// <summary>
    /// Whether deformation is active
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Currently selected handle index (-1 if none)
    /// </summary>
    public int SelectedHandleIndex => _selectedHandleIndex;

    /// <summary>
    /// Number of handles
    /// </summary>
    public int HandleCount => _handles.Count;

    /// <summary>
    /// Whether dragging a handle
    /// </summary>
    public bool IsDragging => _isDragging;

    /// <summary>
    /// Lattice resolution in X direction
    /// </summary>
    public int LatticeResolutionX
    {
        get => _latticeResolutionX;
        set { _latticeResolutionX = Math.Max(2, Math.Min(10, value)); }
    }

    /// <summary>
    /// Lattice resolution in Y direction
    /// </summary>
    public int LatticeResolutionY
    {
        get => _latticeResolutionY;
        set { _latticeResolutionY = Math.Max(2, Math.Min(10, value)); }
    }

    // Deformation parameters
    public float BendAngle { get => _bendAngle; set => _bendAngle = value; }
    public float TwistAngle { get => _twistAngle; set => _twistAngle = value; }
    public float TaperFactor { get => _taperFactor; set => _taperFactor = Math.Max(0.1f, value); }
    public float ShearFactor { get => _shearFactor; set => _shearFactor = value; }
    public float BulgeFactor { get => _bulgeFactor; set => _bulgeFactor = value; }
    public float WaveAmplitude { get => _waveAmplitude; set => _waveAmplitude = value; }
    public float WaveFrequency { get => _waveFrequency; set => _waveFrequency = Math.Max(0.1f, value); }

    /// <summary>
    /// All deform handles
    /// </summary>
    public IReadOnlyList<DeformHandle> Handles => _handles;

    /// <summary>
    /// Lattice points for FFD mode
    /// </summary>
    public IReadOnlyList<LatticePoint> LatticePoints => _latticePoints;

    #endregion

    #region Public Methods

    /// <summary>
    /// Begin deformation on a primitive
    /// </summary>
    public void BeginDeform(GeometricPrimitive2D primitive, float zoom)
    {
        if (primitive == null) return;

        IsActive = true;
        _originalVertices = new List<Vector2>(primitive.GetVertices());
        _deformCenter = primitive.Position;

        var (min, max) = primitive.GetBoundingBox();
        _deformAxis = Vector2.Normalize(max - min);

        GenerateHandles(primitive, zoom);
    }

    /// <summary>
    /// Begin deformation on a joint/discontinuity
    /// </summary>
    public void BeginDeformJoint(Discontinuity2D joint, float zoom)
    {
        if (joint == null) return;

        IsActive = true;
        _originalVertices = new List<Vector2>(joint.Points.Count > 0 ? joint.Points : new List<Vector2> { joint.StartPoint, joint.EndPoint });
        _deformCenter = (_originalVertices[0] + _originalVertices[^1]) / 2;
        _deformAxis = Vector2.Normalize(_originalVertices[^1] - _originalVertices[0]);

        GenerateHandlesFromPoints(_originalVertices, zoom);
    }

    /// <summary>
    /// Begin deformation on a mesh region
    /// </summary>
    public void BeginDeformRegion(FEMMesh2D mesh, List<int> nodeIds, float zoom)
    {
        if (mesh == null || nodeIds == null || nodeIds.Count == 0) return;

        IsActive = true;
        _originalVertices = nodeIds.Select(id => mesh.Nodes[id].InitialPosition).ToList();

        Vector2 center = Vector2.Zero;
        foreach (var v in _originalVertices) center += v;
        center /= _originalVertices.Count;
        _deformCenter = center;

        GenerateHandlesFromPoints(_originalVertices, zoom);
    }

    /// <summary>
    /// End deformation and apply changes
    /// </summary>
    public List<Vector2> EndDeform()
    {
        IsActive = false;
        var result = _handles.Select(h => h.Position).ToList();

        _handles.Clear();
        _latticePoints.Clear();
        _originalVertices.Clear();
        _selectedHandleIndex = -1;
        _hoveredHandleIndex = -1;

        return result;
    }

    /// <summary>
    /// Cancel deformation
    /// </summary>
    public List<Vector2> CancelDeform()
    {
        IsActive = false;
        var result = new List<Vector2>(_originalVertices);

        _handles.Clear();
        _latticePoints.Clear();
        _originalVertices.Clear();
        _selectedHandleIndex = -1;
        _hoveredHandleIndex = -1;

        return result;
    }

    /// <summary>
    /// Update hover state
    /// </summary>
    public int UpdateHover(Vector2 worldPos, float zoom)
    {
        _hoveredHandleIndex = -1;

        for (int i = 0; i < _handles.Count; i++)
        {
            var handle = _handles[i];
            handle.Size = HandleSize / zoom;
            handle.IsHovered = handle.ContainsPoint(worldPos);

            if (handle.IsHovered)
            {
                _hoveredHandleIndex = i;
            }
            _handles[i] = handle;
        }

        // Also check lattice points
        for (int i = 0; i < _latticePoints.Count; i++)
        {
            var lp = _latticePoints[i];
            if (Vector2.Distance(worldPos, lp.Position) <= HandleSize / zoom)
            {
                _hoveredHandleIndex = _handles.Count + i;
            }
        }

        return _hoveredHandleIndex;
    }

    /// <summary>
    /// Begin dragging a handle
    /// </summary>
    public bool BeginDrag(Vector2 worldPos, float zoom)
    {
        if (_hoveredHandleIndex < 0) return false;

        _isDragging = true;
        _dragStartPos = worldPos;
        _selectedHandleIndex = _hoveredHandleIndex;

        if (_selectedHandleIndex < _handles.Count)
        {
            var handle = _handles[_selectedHandleIndex];
            handle.IsSelected = true;
            _handles[_selectedHandleIndex] = handle;
        }

        return true;
    }

    /// <summary>
    /// Update drag position
    /// </summary>
    public void UpdateDrag(Vector2 worldPos, SnappingSystem snapping = null)
    {
        if (!_isDragging || _selectedHandleIndex < 0) return;

        // Apply snapping if available
        if (snapping != null && snapping.IsEnabled)
        {
            var snapResult = snapping.Snap(worldPos, null, null);
            if (snapResult.Snapped)
            {
                worldPos = snapResult.Position;
            }
        }

        if (_selectedHandleIndex < _handles.Count)
        {
            // Moving a vertex handle
            var handle = _handles[_selectedHandleIndex];
            handle.Position = worldPos;
            _handles[_selectedHandleIndex] = handle;

            // Apply deformation based on type
            ApplyDeformation();
        }
        else if (_selectedHandleIndex - _handles.Count < _latticePoints.Count)
        {
            // Moving a lattice point
            int latticeIdx = _selectedHandleIndex - _handles.Count;
            var lp = _latticePoints[latticeIdx];
            lp.Position = worldPos;
            _latticePoints[latticeIdx] = lp;

            // Recalculate deformation from lattice
            ApplyLatticeDeformation();
        }
    }

    /// <summary>
    /// End drag operation
    /// </summary>
    public void EndDrag()
    {
        _isDragging = false;

        if (_selectedHandleIndex >= 0 && _selectedHandleIndex < _handles.Count)
        {
            var handle = _handles[_selectedHandleIndex];
            handle.IsSelected = false;
            _handles[_selectedHandleIndex] = handle;
        }
    }

    /// <summary>
    /// Select all handles
    /// </summary>
    public void SelectAll()
    {
        for (int i = 0; i < _handles.Count; i++)
        {
            var handle = _handles[i];
            handle.IsSelected = true;
            _handles[i] = handle;
        }
    }

    /// <summary>
    /// Deselect all handles
    /// </summary>
    public void DeselectAll()
    {
        _selectedHandleIndex = -1;
        for (int i = 0; i < _handles.Count; i++)
        {
            var handle = _handles[i];
            handle.IsSelected = false;
            _handles[i] = handle;
        }
    }

    /// <summary>
    /// Get current deformed vertices
    /// </summary>
    public List<Vector2> GetDeformedVertices()
    {
        if (_deformationType == DeformationType.Lattice)
        {
            return CalculateLatticeDeformedVertices();
        }
        return _handles.Select(h => h.Position).ToList();
    }

    /// <summary>
    /// Apply predefined deformation with parameters
    /// </summary>
    public void ApplyParametricDeformation(DeformationType type, float parameter)
    {
        _deformationType = type;

        switch (type)
        {
            case DeformationType.Bend:
                _bendAngle = parameter;
                break;
            case DeformationType.Twist:
                _twistAngle = parameter;
                break;
            case DeformationType.Taper:
                _taperFactor = parameter;
                break;
            case DeformationType.Shear:
                _shearFactor = parameter;
                break;
            case DeformationType.Bulge:
                _bulgeFactor = parameter;
                break;
            case DeformationType.Wave:
                _waveAmplitude = parameter;
                break;
        }

        ApplyDeformation();
    }

    /// <summary>
    /// Reset to original positions
    /// </summary>
    public void ResetToOriginal()
    {
        for (int i = 0; i < _handles.Count && i < _originalVertices.Count; i++)
        {
            var handle = _handles[i];
            handle.Position = handle.OriginalPosition;
            _handles[i] = handle;
        }

        for (int i = 0; i < _latticePoints.Count; i++)
        {
            var lp = _latticePoints[i];
            lp.Position = lp.OriginalPosition;
            _latticePoints[i] = lp;
        }
    }

    #endregion

    #region Private Methods

    private void GenerateHandles(GeometricPrimitive2D primitive, float zoom)
    {
        _handles.Clear();
        var vertices = primitive.GetVertices();
        GenerateHandlesFromPoints(vertices, zoom);
    }

    private void GenerateHandlesFromPoints(List<Vector2> points, float zoom)
    {
        _handles.Clear();
        float handleSize = HandleSize / zoom;

        for (int i = 0; i < points.Count; i++)
        {
            _handles.Add(new DeformHandle
            {
                Index = i,
                Position = points[i],
                OriginalPosition = points[i],
                Size = handleSize
            });
        }

        if (_deformationType == DeformationType.Lattice)
        {
            GenerateLattice(zoom);
        }
    }

    private void GenerateLattice(float zoom)
    {
        _latticePoints.Clear();

        if (_originalVertices.Count == 0) return;

        // Calculate bounding box
        Vector2 min = _originalVertices[0];
        Vector2 max = _originalVertices[0];
        foreach (var v in _originalVertices)
        {
            min = Vector2.Min(min, v);
            max = Vector2.Max(max, v);
        }

        // Expand slightly
        Vector2 padding = (max - min) * 0.1f;
        min -= padding;
        max += padding;

        // Generate lattice grid
        for (int j = 0; j <= _latticeResolutionY; j++)
        {
            for (int i = 0; i <= _latticeResolutionX; i++)
            {
                float u = (float)i / _latticeResolutionX;
                float v = (float)j / _latticeResolutionY;
                Vector2 pos = min + new Vector2(u, v) * (max - min);

                _latticePoints.Add(new LatticePoint
                {
                    I = i,
                    J = j,
                    Position = pos,
                    OriginalPosition = pos,
                    Weight = 1.0f
                });
            }
        }
    }

    private void RegenerateHandles()
    {
        if (!IsActive) return;

        for (int i = 0; i < _handles.Count; i++)
        {
            var handle = _handles[i];
            handle.Position = handle.OriginalPosition;
            _handles[i] = handle;
        }

        if (_deformationType == DeformationType.Lattice && _latticePoints.Count == 0)
        {
            GenerateLattice(1.0f);
        }
        else if (_deformationType != DeformationType.Lattice)
        {
            _latticePoints.Clear();
        }
    }

    private void ApplyDeformation()
    {
        switch (_deformationType)
        {
            case DeformationType.FreeForm:
                // Direct manipulation - handles already updated
                break;
            case DeformationType.Bend:
                ApplyBendDeformation();
                break;
            case DeformationType.Twist:
                ApplyTwistDeformation();
                break;
            case DeformationType.Taper:
                ApplyTaperDeformation();
                break;
            case DeformationType.Stretch:
                ApplyStretchDeformation();
                break;
            case DeformationType.Shear:
                ApplyShearDeformation();
                break;
            case DeformationType.Bulge:
                ApplyBulgeDeformation();
                break;
            case DeformationType.Wave:
                ApplyWaveDeformation();
                break;
            case DeformationType.Lattice:
                ApplyLatticeDeformation();
                break;
        }
    }

    private void ApplyBendDeformation()
    {
        float angleRad = _bendAngle * MathF.PI / 180f;
        if (MathF.Abs(angleRad) < 0.001f) return;

        float radius = 1.0f / angleRad;

        for (int i = 0; i < _handles.Count; i++)
        {
            var handle = _handles[i];
            Vector2 local = handle.OriginalPosition - _deformCenter;

            // Project onto axis
            float axisProj = Vector2.Dot(local, _deformAxis);
            float perpProj = Vector2.Dot(local, new Vector2(-_deformAxis.Y, _deformAxis.X));

            // Bend calculation
            float bendAngle = axisProj / radius;
            float newAxisProj = radius * MathF.Sin(bendAngle);
            float yOffset = radius * (1 - MathF.Cos(bendAngle));

            Vector2 perpAxis = new Vector2(-_deformAxis.Y, _deformAxis.X);
            handle.Position = _deformCenter + _deformAxis * newAxisProj + perpAxis * (perpProj + yOffset);
            _handles[i] = handle;
        }
    }

    private void ApplyTwistDeformation()
    {
        float angleRad = _twistAngle * MathF.PI / 180f;

        for (int i = 0; i < _handles.Count; i++)
        {
            var handle = _handles[i];
            Vector2 local = handle.OriginalPosition - _deformCenter;

            // Calculate twist based on distance along axis
            float axisProj = Vector2.Dot(local, _deformAxis);
            float twistAmount = axisProj * angleRad;

            // Rotate around center
            float cos = MathF.Cos(twistAmount);
            float sin = MathF.Sin(twistAmount);

            Vector2 perpAxis = new Vector2(-_deformAxis.Y, _deformAxis.X);
            float perpProj = Vector2.Dot(local, perpAxis);

            handle.Position = _deformCenter + _deformAxis * axisProj +
                             perpAxis * (perpProj * cos);
            _handles[i] = handle;
        }
    }

    private void ApplyTaperDeformation()
    {
        for (int i = 0; i < _handles.Count; i++)
        {
            var handle = _handles[i];
            Vector2 local = handle.OriginalPosition - _deformCenter;

            // Calculate taper scale based on distance along axis
            float axisProj = Vector2.Dot(local, _deformAxis);
            float normalizedPos = (axisProj + 1) / 2; // Normalize to 0-1
            float scale = 1 + (normalizedPos * (_taperFactor - 1));

            Vector2 perpAxis = new Vector2(-_deformAxis.Y, _deformAxis.X);
            float perpProj = Vector2.Dot(local, perpAxis);

            handle.Position = _deformCenter + _deformAxis * axisProj + perpAxis * (perpProj * scale);
            _handles[i] = handle;
        }
    }

    private void ApplyStretchDeformation()
    {
        // Stretch is handled through corner handle manipulation
        // This uses the selected handle's delta to stretch proportionally
        if (_selectedHandleIndex < 0 || _selectedHandleIndex >= _handles.Count) return;

        var selectedHandle = _handles[_selectedHandleIndex];
        Vector2 delta = selectedHandle.Position - selectedHandle.OriginalPosition;

        for (int i = 0; i < _handles.Count; i++)
        {
            if (i == _selectedHandleIndex) continue;

            var handle = _handles[i];
            Vector2 toSelected = handle.OriginalPosition - selectedHandle.OriginalPosition;
            float dist = toSelected.Length();
            if (dist > 0.001f)
            {
                float influence = MathF.Max(0, 1 - dist / 10f);
                handle.Position = handle.OriginalPosition + delta * influence;
                _handles[i] = handle;
            }
        }
    }

    private void ApplyShearDeformation()
    {
        for (int i = 0; i < _handles.Count; i++)
        {
            var handle = _handles[i];
            Vector2 local = handle.OriginalPosition - _deformCenter;

            // Shear based on perpendicular distance
            Vector2 perpAxis = new Vector2(-_deformAxis.Y, _deformAxis.X);
            float perpProj = Vector2.Dot(local, perpAxis);

            handle.Position = handle.OriginalPosition + _deformAxis * (perpProj * _shearFactor);
            _handles[i] = handle;
        }
    }

    private void ApplyBulgeDeformation()
    {
        for (int i = 0; i < _handles.Count; i++)
        {
            var handle = _handles[i];
            Vector2 local = handle.OriginalPosition - _deformCenter;

            // Calculate bulge based on distance from center
            float dist = local.Length();
            float maxDist = 10f; // Normalize
            float normalizedDist = MathF.Min(dist / maxDist, 1);

            // Bulge factor creates outward/inward movement
            float bulgeAmount = _bulgeFactor * (1 - normalizedDist * normalizedDist);

            if (dist > 0.001f)
            {
                Vector2 dir = local / dist;
                handle.Position = handle.OriginalPosition + dir * bulgeAmount;
                _handles[i] = handle;
            }
        }
    }

    private void ApplyWaveDeformation()
    {
        for (int i = 0; i < _handles.Count; i++)
        {
            var handle = _handles[i];
            Vector2 local = handle.OriginalPosition - _deformCenter;

            // Wave along axis
            float axisProj = Vector2.Dot(local, _deformAxis);
            float waveOffset = _waveAmplitude * MathF.Sin(axisProj * _waveFrequency * MathF.PI);

            Vector2 perpAxis = new Vector2(-_deformAxis.Y, _deformAxis.X);
            handle.Position = handle.OriginalPosition + perpAxis * waveOffset;
            _handles[i] = handle;
        }
    }

    private void ApplyLatticeDeformation()
    {
        var deformedVertices = CalculateLatticeDeformedVertices();

        for (int i = 0; i < _handles.Count && i < deformedVertices.Count; i++)
        {
            var handle = _handles[i];
            handle.Position = deformedVertices[i];
            _handles[i] = handle;
        }
    }

    private List<Vector2> CalculateLatticeDeformedVertices()
    {
        if (_latticePoints.Count == 0 || _originalVertices.Count == 0)
            return new List<Vector2>(_originalVertices);

        // Calculate original lattice bounds
        Vector2 latticeMin = _latticePoints[0].OriginalPosition;
        Vector2 latticeMax = _latticePoints[0].OriginalPosition;
        foreach (var lp in _latticePoints)
        {
            latticeMin = Vector2.Min(latticeMin, lp.OriginalPosition);
            latticeMax = Vector2.Max(latticeMax, lp.OriginalPosition);
        }
        Vector2 latticeSize = latticeMax - latticeMin;

        var result = new List<Vector2>();

        foreach (var originalVertex in _originalVertices)
        {
            // Calculate normalized coordinates in lattice
            Vector2 uv = (originalVertex - latticeMin) / latticeSize;
            uv = Vector2.Clamp(uv, Vector2.Zero, Vector2.One);

            // Bilinear interpolation using Bernstein polynomials
            Vector2 deformed = Vector2.Zero;

            for (int j = 0; j <= _latticeResolutionY; j++)
            {
                for (int i = 0; i <= _latticeResolutionX; i++)
                {
                    int idx = j * (_latticeResolutionX + 1) + i;
                    if (idx >= _latticePoints.Count) continue;

                    // Bernstein basis functions
                    float bx = BernsteinBasis(_latticeResolutionX, i, uv.X);
                    float by = BernsteinBasis(_latticeResolutionY, j, uv.Y);

                    deformed += _latticePoints[idx].Position * bx * by;
                }
            }

            result.Add(deformed);
        }

        return result;
    }

    private static float BernsteinBasis(int n, int i, float t)
    {
        return BinomialCoefficient(n, i) * MathF.Pow(t, i) * MathF.Pow(1 - t, n - i);
    }

    private static float BinomialCoefficient(int n, int k)
    {
        if (k > n) return 0;
        if (k == 0 || k == n) return 1;

        int result = 1;
        for (int i = 0; i < k; i++)
        {
            result = result * (n - i) / (i + 1);
        }
        return result;
    }

    #endregion
}

#endregion
