// GeoscientistToolkit/Analysis/RockCoreExtractor/RockCoreOverlay.cs

using System.Numerics;
using GeoscientistToolkit.Data.CtImageStack;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.RockCoreExtractor;

/// <summary>
///     Interactive overlay for the Rock Core tool with defensive guards.
/// </summary>
public class RockCoreOverlay
{
    private const float HandleR = 7f;
    private readonly Dictionary<Handle, Vector2> _handles = new();
    private readonly RockCoreExtractorTool _tool;
    private Handle _active = Handle.None;
    private Vector2 _dragStartCenter;
    private float _dragStartLength, _dragStartDiameter, _dragStartStart;
    private Vector2 _dragStartMouse;

    public RockCoreOverlay(RockCoreExtractorTool tool, CtImageStackDataset dataset)
    {
        _tool = tool ?? throw new ArgumentNullException(nameof(tool));
        Dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
    }

    public CtImageStackDataset Dataset { get; }

    public void DrawOnSlice(ImDrawListPtr dl, int viewIndex, Vector2 imagePos, Vector2 imageSize,
        int imageWidth, int imageHeight, int sliceX, int sliceY, int sliceZ)
    {
        if (!_tool.ShowPreview) return;
        if (imageSize.X <= 0 || imageSize.Y <= 0 || imageWidth <= 0 || imageHeight <= 0) return;

        var p = _tool.GetCoreParameters();
        _handles.Clear();

        // Decide circular vs lateral in this view
        var circularHere = IsCircularView(p.View, viewIndex);

        if (circularHere)
        {
            // circle in this view
            var centerPx = imagePos + new Vector2(p.Center.X * imageSize.X, p.Center.Y * imageSize.Y);
            var radiusPx = p.Diameter * 0.5f * (imageSize.X / Math.Max(1, Dataset.Width));
            dl.AddCircle(centerPx, radiusPx, 0xFF00FF00, 64, 2f);

            _handles[Handle.Center] = centerPx;
            _handles[Handle.RadiusPos] = centerPx + new Vector2(radiusPx, 0);
            _handles[Handle.RadiusNeg] = centerPx - new Vector2(radiusPx, 0);

            DrawHandles(dl);
        }
        else
        {
            // lateral rectangle (length along the lateral axis, diameter as thickness)
            BuildLateralRectangle(p, viewIndex, imagePos, imageSize, out var tl, out var br);
            dl.AddRect(tl, br, 0xFF00FF00, 0, ImDrawFlags.None, 2f);
            DrawHandles(dl);
        }
    }

    public bool HandleMouseInput(Vector2 mousePos, Vector2 imagePos, Vector2 imageSize,
        int imageWidth, int imageHeight, int viewIndex, bool clicked, bool dragging, bool released)
    {
        if (!_tool.ShowPreview) return false;
        if (imageSize.X <= 0 || imageSize.Y <= 0 || imageWidth <= 0 || imageHeight <= 0) return false;

        var p = _tool.GetCoreParameters();
        var circularHere = IsCircularView(p.View, viewIndex);

        // Rebuild handles for this frame
        _handles.Clear();
        if (circularHere)
        {
            var centerPx = imagePos + new Vector2(p.Center.X * imageSize.X, p.Center.Y * imageSize.Y);
            var radiusPx = p.Diameter * 0.5f * (imageSize.X / Math.Max(1, Dataset.Width));
            _handles[Handle.Center] = centerPx;
            _handles[Handle.RadiusPos] = centerPx + new Vector2(radiusPx, 0);
            _handles[Handle.RadiusNeg] = centerPx - new Vector2(radiusPx, 0);
        }
        else
        {
            BuildLateralRectangle(p, viewIndex, imagePos, imageSize, out _, out _);
        }

        if (clicked)
        {
            _active = Hit(mousePos);
            if (_active != Handle.None)
            {
                _dragStartMouse = mousePos;
                _dragStartCenter = p.Center;
                _dragStartLength = p.Length;
                _dragStartDiameter = p.Diameter;
                _dragStartStart = p.StartPosition;
                return true;
            }
        }

        if (dragging && _active != Handle.None)
        {
            var d = mousePos - _dragStartMouse;

            if (circularHere)
            {
                if (_active == Handle.Center)
                {
                    var newCenter = new Vector2(
                        _dragStartCenter.X + d.X / Math.Max(1f, imageSize.X),
                        _dragStartCenter.Y + d.Y / Math.Max(1f, imageSize.Y)
                    );
                    _tool.SetCoreCenter(newCenter);
                    return true;
                }

                // radius change from horizontal motion
                var dRadiusPx = d.X;
                var dVox = 2f * dRadiusPx * (Dataset.Width / Math.Max(1f, imageSize.X));
                _tool.SetCoreDiameter(Math.Max(2f, _dragStartDiameter + (_active == Handle.RadiusPos ? dVox : -dVox)));
                return true;
            }

            // lateral: map to (U,V) where one axis is length, the other is diameter
            GetLateralAxes(p.View, viewIndex, out var lengthIsU);

            var duVox = d.X * (GetAxisWidthVox(viewIndex) / Math.Max(1f, imageSize.X));
            var dvVox = d.Y * (GetAxisHeightVox(viewIndex) / Math.Max(1f, imageSize.Y));

            switch (_active)
            {
                case Handle.Center:
                {
                    var dLen = lengthIsU ? duVox : dvVox;
                    var dRad = lengthIsU ? dvVox : duVox;

                    // move along length by changing start position, and move center along cross-axis
                    var lenVox = Math.Max(1f, _dragStartLength);
                    var newStart = _dragStartStart + dLen / Math.Max(1f, GetMaxLengthForView());
                    var newCenter = _dragStartCenter;

                    if (p.View == RockCoreExtractorTool.CircularView.XY_Circular_Z_Lateral)
                        // lateral = Z (affects start); center remains (X,Y) but we allow cross shift along V axis (mapped to Y)
                        newCenter.Y = _dragStartCenter.Y + dRad / Math.Max(1f, GetAxisHeightVox(viewIndex));
                    else if (p.View == RockCoreExtractorTool.CircularView.XZ_Circular_Y_Lateral)
                        // lateral = Y ; cross axis is X or Z depending on view; map to center.X
                        newCenter.X = _dragStartCenter.X + dRad / Math.Max(1f, GetAxisWidthVox(viewIndex));
                    else // YZ_Circular_X_Lateral
                        // lateral = X ; cross axis maps to center.Y (because circle plane is YZ)
                        newCenter.Y = _dragStartCenter.Y + dRad / Math.Max(1f, GetAxisHeightVox(viewIndex));

                    _tool.SetCoreCenter(newCenter);
                    _tool.SetCoreStartPosition(newStart);
                    return true;
                }
                case Handle.LengthPos:
                case Handle.LengthNeg:
                {
                    var dLen = lengthIsU ? duVox : dvVox;
                    if (_active == Handle.LengthNeg) dLen = -dLen;
                    _tool.SetCoreLength(Math.Max(2f, _dragStartLength + 2f * dLen));
                    return true;
                }
                case Handle.RadiusPos:
                case Handle.RadiusNeg:
                {
                    var dRad = lengthIsU ? dvVox : duVox;
                    if (_active == Handle.RadiusNeg) dRad = -dRad;
                    _tool.SetCoreDiameter(Math.Max(2f, _dragStartDiameter + 2f * dRad));
                    return true;
                }
            }
        }

        if (released && _active != Handle.None)
        {
            _active = Handle.None;
            return true;
        }

        // hover cursor
        if (_active == Handle.None && Hit(mousePos) != Handle.None) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        return _active != Handle.None;
    }

    // -------- helpers --------
    private float GetAxisWidthVox(int viewIndex)
    {
        return viewIndex switch { 0 => Dataset.Width, 1 => Dataset.Width, 2 => Dataset.Height, _ => Dataset.Width };
    }

    private float GetAxisHeightVox(int viewIndex)
    {
        return viewIndex switch { 0 => Dataset.Height, 1 => Dataset.Depth, 2 => Dataset.Depth, _ => Dataset.Height };
    }

    private bool IsCircularView(RockCoreExtractorTool.CircularView v, int view)
    {
        return (v == RockCoreExtractorTool.CircularView.XY_Circular_Z_Lateral && view == 0) ||
               (v == RockCoreExtractorTool.CircularView.XZ_Circular_Y_Lateral && view == 1) ||
               (v == RockCoreExtractorTool.CircularView.YZ_Circular_X_Lateral && view == 2);
    }

    private void GetLateralAxes(RockCoreExtractorTool.CircularView v, int view, out bool lengthIsU)
    {
        // U is horizontal axis of the view, V is vertical
        // For our lateral rectangle, we decide if length runs along U or V
        lengthIsU = v switch
        {
            RockCoreExtractorTool.CircularView.XY_Circular_Z_Lateral => false, // Z increases vertically in XZ/YZ views
            RockCoreExtractorTool.CircularView.XZ_Circular_Y_Lateral => view == 2, // in YZ, Y increases horizontally
            RockCoreExtractorTool.CircularView.YZ_Circular_X_Lateral => true, // X increases horizontally in XY/XZ
            _ => true
        };
    }

    private float GetMaxLengthForView()
    {
        return _tool.GetCoreParameters().View switch
        {
            RockCoreExtractorTool.CircularView.XY_Circular_Z_Lateral => Dataset.Depth,
            RockCoreExtractorTool.CircularView.XZ_Circular_Y_Lateral => Dataset.Height,
            RockCoreExtractorTool.CircularView.YZ_Circular_X_Lateral => Dataset.Width,
            _ => 1f
        };
    }

    private void BuildLateralRectangle(RockCoreExtractorTool.CoreParameters p, int viewIndex,
        Vector2 imagePos, Vector2 imageSize, out Vector2 tl, out Vector2 br)
    {
        GetLateralAxes(p.View, viewIndex, out var lengthIsU);

        var halfD = p.Diameter * 0.5f;
        var len = p.Length;

        // Determine center in view pixels
        float uCenter, vCenter;
        switch (viewIndex)
        {
            case 1: // XZ
                if (p.View == RockCoreExtractorTool.CircularView.XY_Circular_Z_Lateral)
                {
                    uCenter = p.Center.X * Dataset.Width;
                    vCenter = p.StartPosition * Dataset.Depth + len * 0.5f;
                }
                else // Y lateral
                {
                    uCenter = p.StartPosition * Dataset.Width + len * 0.5f;
                    vCenter = p.Center.Y * Dataset.Depth;
                }

                break;

            case 2: // YZ
                if (p.View == RockCoreExtractorTool.CircularView.XY_Circular_Z_Lateral)
                {
                    uCenter = p.Center.Y * Dataset.Height;
                    vCenter = p.StartPosition * Dataset.Depth + len * 0.5f;
                }
                else // X lateral
                {
                    uCenter = p.StartPosition * Dataset.Height + len * 0.5f;
                    vCenter = p.Center.Y * Dataset.Depth;
                }

                break;

            default: // 0 => XY (lateral for Y or X)
                if (p.View == RockCoreExtractorTool.CircularView.YZ_Circular_X_Lateral)
                {
                    uCenter = p.StartPosition * Dataset.Width + len * 0.5f;
                    vCenter = p.Center.X * Dataset.Height;
                }
                else // XZ circular → Y lateral
                {
                    uCenter = p.Center.X * Dataset.Width;
                    vCenter = p.StartPosition * Dataset.Height + len * 0.5f;
                }

                break;
        }

        var halfU = lengthIsU ? len * 0.5f : halfD;
        var halfV = lengthIsU ? halfD : len * 0.5f;

        // Convert to screen
        Vector2 uvToScreen(float u, float v)
        {
            return imagePos + new Vector2(
                u / GetAxisWidthVox(viewIndex) * imageSize.X,
                v / GetAxisHeightVox(viewIndex) * imageSize.Y
            );
        }

        tl = uvToScreen(uCenter - halfU, vCenter - halfV);
        br = uvToScreen(uCenter + halfU, vCenter + halfV);

        // Handles: center, ±length, ±radius
        _handles[Handle.Center] = uvToScreen(uCenter, vCenter);
        if (lengthIsU)
        {
            _handles[Handle.LengthNeg] = uvToScreen(uCenter - halfU, vCenter);
            _handles[Handle.LengthPos] = uvToScreen(uCenter + halfU, vCenter);
            _handles[Handle.RadiusNeg] = uvToScreen(uCenter, vCenter - halfV);
            _handles[Handle.RadiusPos] = uvToScreen(uCenter, vCenter + halfV);
        }
        else
        {
            _handles[Handle.LengthNeg] = uvToScreen(uCenter, vCenter - halfV);
            _handles[Handle.LengthPos] = uvToScreen(uCenter, vCenter + halfV);
            _handles[Handle.RadiusNeg] = uvToScreen(uCenter - halfU, vCenter);
            _handles[Handle.RadiusPos] = uvToScreen(uCenter + halfU, vCenter);
        }
    }

    private void DrawHandles(ImDrawListPtr dl)
    {
        foreach (var kv in _handles)
        {
            dl.AddCircleFilled(kv.Value, HandleR, 0xFF00FF00);
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

    // Simple handle map
    private enum Handle
    {
        None,
        Center,
        LengthPos,
        LengthNeg,
        RadiusPos,
        RadiusNeg
    }
}