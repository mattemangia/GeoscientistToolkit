// GeoscientistToolkit/Analysis/Transform/TransformOverlay.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Data.CtImageStack;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.Transform
{
    /// <summary>
    /// Handles interactive overlay rendering and manipulation for the TransformTool.
    /// </summary>
    public class TransformOverlay
    {
        private enum HandleType { None, TranslateX, TranslateY, TranslateZ, RotateX, RotateY, RotateZ, Scale }
        
        private readonly TransformTool _tool;
        public CtImageStackDataset Dataset { get; }

        // Interaction state
        private HandleType _activeHandle = HandleType.None;
        private HandleType _hoveredHandle = HandleType.None;
        private Vector2 _dragStartMousePos;
        private Vector3 _dragStartTranslation;
        private Vector3 _dragStartRotation;
        private Vector3 _dragStartScale;
        
        // Handle definitions
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

            uint color = _activeHandle != HandleType.None ? 0xFF00FFFF : 0xFFFFA500; // Cyan when active, Orange otherwise

            // Draw edges
            DrawEdge(dl, screenCorners[0], screenCorners[1], color); DrawEdge(dl, screenCorners[1], screenCorners[2], color);
            DrawEdge(dl, screenCorners[2], screenCorners[3], color); DrawEdge(dl, screenCorners[3], screenCorners[0], color);
            DrawEdge(dl, screenCorners[4], screenCorners[5], color); DrawEdge(dl, screenCorners[5], screenCorners[6], color);
            DrawEdge(dl, screenCorners[6], screenCorners[7], color); DrawEdge(dl, screenCorners[7], screenCorners[4], color);
            DrawEdge(dl, screenCorners[0], screenCorners[4], color); DrawEdge(dl, screenCorners[1], screenCorners[5], color);
            DrawEdge(dl, screenCorners[2], screenCorners[6], color); DrawEdge(dl, screenCorners[3], screenCorners[7], color);

            // Update and draw handles
            UpdateAndDrawHandles(dl, corners, viewIndex, imagePos, imageSize);
        }

        public bool HandleMouseInput(Vector2 mousePos, Vector2 imagePos, Vector2 imageSize,
            int imageWidth, int imageHeight, int viewIndex, bool clicked, bool dragging, bool released)
        {
            if (clicked)
            {
                _hoveredHandle = HitTestHandles(mousePos);
                if (_hoveredHandle != HandleType.None)
                {
                    _activeHandle = _hoveredHandle;
                    _dragStartMousePos = mousePos;
                    // Store initial transform values
                    _dragStartTranslation = _tool._translation;
                    _dragStartRotation = _tool._rotation;
                    _dragStartScale = _tool._scale;
                    return true;
                }
            }

            if (dragging && _activeHandle != HandleType.None)
            {
                var mouseDelta = mousePos - _dragStartMousePos;
                var worldDelta = new Vector3(mouseDelta.X / imageSize.X * Dataset.Width, mouseDelta.Y / imageSize.Y * Dataset.Height, 0);

                switch (_activeHandle)
                {
                    case HandleType.TranslateX:
                        _tool.SetTranslation(_dragStartTranslation + GetAxisVectorForView(viewIndex, HandleType.TranslateX, worldDelta));
                        break;
                    case HandleType.TranslateY:
                         _tool.SetTranslation(_dragStartTranslation + GetAxisVectorForView(viewIndex, HandleType.TranslateY, worldDelta));
                        break;
                    case HandleType.TranslateZ:
                         _tool.SetTranslation(_dragStartTranslation + GetAxisVectorForView(viewIndex, HandleType.TranslateZ, worldDelta));
                        break;
                    case HandleType.RotateX:
                        _tool.SetRotation(_dragStartRotation + new Vector3(mouseDelta.Y * 0.5f, 0, 0));
                        break;
                    case HandleType.RotateY:
                         _tool.SetRotation(_dragStartRotation + new Vector3(0, -mouseDelta.X * 0.5f, 0));
                        break;
                    case HandleType.RotateZ:
                         _tool.SetRotation(_dragStartRotation + new Vector3(0, 0, -mouseDelta.X * 0.5f));
                        break;
                    case HandleType.Scale:
                        float scaleFactor = 1.0f + (mouseDelta.X - mouseDelta.Y) * 0.005f;
                        _tool.SetScale(_dragStartScale * scaleFactor);
                        break;
                }
                return true;
            }

            if (released)
            {
                if (_activeHandle != HandleType.None)
                {
                    _activeHandle = HandleType.None;
                    return true; // Consume the release event
                }
            }
            
            // Update hover state if not dragging
            if (_activeHandle == HandleType.None)
            {
                _hoveredHandle = HitTestHandles(mousePos);
                if (_hoveredHandle != HandleType.None)
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                }
            }
            
            return _activeHandle != HandleType.None;
        }

        private void UpdateAndDrawHandles(ImDrawListPtr dl, Vector3[] corners, int viewIndex, Vector2 imagePos, Vector2 imageSize)
        {
            _handlePositions3D.Clear();
            _handlePositions2D.Clear();

            // Midpoints of faces (for translation)
            _handlePositions3D[HandleType.TranslateX] = (corners[1] + corners[2] + corners[5] + corners[6]) * 0.25f; // Right face
            _handlePositions3D[HandleType.TranslateY] = (corners[2] + corners[3] + corners[6] + corners[7]) * 0.25f; // Top face
            _handlePositions3D[HandleType.TranslateZ] = (corners[4] + corners[5] + corners[6] + corners[7]) * 0.25f; // Front face

            // Midpoints of edges (for rotation)
            _handlePositions3D[HandleType.RotateZ] = (corners[0] + corners[1]) * 0.5f; // Bottom-front edge
            _handlePositions3D[HandleType.RotateY] = (corners[3] + corners[7]) * 0.5f; // Top-left edge
            _handlePositions3D[HandleType.RotateX] = (corners[0] + corners[4]) * 0.5f; // Left-front edge

            // One corner for uniform scale
            _handlePositions3D[HandleType.Scale] = corners[6]; // Top-right-front corner
            
            foreach (var kvp in _handlePositions3D)
            {
                var handleType = kvp.Key;
                var pos3D = kvp.Value;
                var pos2D = ProjectToView(pos3D, viewIndex, imagePos, imageSize);
                _handlePositions2D[handleType] = pos2D;

                uint color = GetHandleColor(handleType);
                dl.AddCircleFilled(pos2D, HandleSize, color);
                dl.AddCircle(pos2D, HandleSize, 0xFFFFFFFF, 12, 1.5f);
            }
        }
        
        private HandleType HitTestHandles(Vector2 mousePos)
        {
            // Iterate backwards so scale handle (drawn last) is picked first
            foreach (var kvp in _handlePositions2D.Reverse())
            {
                if (Vector2.Distance(mousePos, kvp.Value) < HandleSize + 2)
                {
                    return kvp.Key;
                }
            }
            return HandleType.None;
        }

        private uint GetHandleColor(HandleType type)
        {
            if (type == _activeHandle || type == _hoveredHandle) return 0xFF00FFFF; // Cyan highlight
            return type switch
            {
                HandleType.TranslateX => 0xFF0000FF, // Red
                HandleType.TranslateY => 0xFF00FF00, // Green
                HandleType.TranslateZ => 0xFFFF0000, // Blue
                HandleType.RotateX => 0xFF00008B,    // Dark Red
                HandleType.RotateY => 0xFF008B00,    // Dark Green
                HandleType.RotateZ => 0xFF8B0000,    // Dark Blue
                HandleType.Scale => 0xFFFFFFFF,      // White
                _ => 0xFF808080
            };
        }
        
        private Vector3 GetAxisVectorForView(int viewIndex, HandleType handle, Vector3 screenDelta)
        {
            return viewIndex switch
            {
                0 => handle switch // XY View
                {
                    HandleType.TranslateX => new Vector3(screenDelta.X, 0, 0),
                    HandleType.TranslateY => new Vector3(0, screenDelta.Y, 0),
                    _ => Vector3.Zero
                },
                1 => handle switch // XZ View
                {
                    HandleType.TranslateX => new Vector3(screenDelta.X, 0, 0),
                    HandleType.TranslateZ => new Vector3(0, 0, screenDelta.Y), // Mouse Y moves Z
                    _ => Vector3.Zero
                },
                2 => handle switch // YZ View
                {
                    HandleType.TranslateY => new Vector3(0, screenDelta.X, 0), // Mouse X moves Y
                    HandleType.TranslateZ => new Vector3(0, 0, screenDelta.Y), // Mouse Y moves Z
                    _ => Vector3.Zero
                },
                _ => Vector3.Zero
            };
        }

        private Vector3[] GetBoundingBoxCorners(Vector3 min, Vector3 max, Matrix4x4 transform)
        {
            var corners = new Vector3[8];
            corners[0] = Vector3.Transform(new Vector3(min.X, min.Y, min.Z), transform);
            corners[1] = Vector3.Transform(new Vector3(max.X, min.Y, min.Z), transform);
            corners[2] = Vector3.Transform(new Vector3(max.X, max.Y, min.Z), transform);
            corners[3] = Vector3.Transform(new Vector3(min.X, max.Y, min.Z), transform);
            corners[4] = Vector3.Transform(new Vector3(min.X, min.Y, max.Z), transform);
            corners[5] = Vector3.Transform(new Vector3(max.X, min.Y, max.Z), transform);
            corners[6] = Vector3.Transform(new Vector3(max.X, max.Y, max.Z), transform);
            corners[7] = Vector3.Transform(new Vector3(min.X, max.Y, max.Z), transform);
            return corners;
        }

        private Vector2 ProjectToView(Vector3 point3d, int viewIndex, Vector2 imagePos, Vector2 imageSize)
        {
            Vector2 point2d;
            Vector2 normPos;

            switch (viewIndex)
            {
                case 0: // XY View
                    point2d = new Vector2(point3d.X, point3d.Y);
                    normPos = point2d / new Vector2(Dataset.Width, Dataset.Height);
                    break;
                case 1: // XZ View
                    point2d = new Vector2(point3d.X, point3d.Z);
                    normPos = point2d / new Vector2(Dataset.Width, Dataset.Depth);
                    break;
                case 2: // YZ View
                    point2d = new Vector2(point3d.Y, point3d.Z);
                    normPos = point2d / new Vector2(Dataset.Height, Dataset.Depth);
                    break;
                default:
                    return Vector2.Zero;
            }
            
            // This projection does not account for viewer zoom and pan.
            // A more robust implementation would pass these from CtCombinedViewer.
            return imagePos + new Vector2(normPos.X * imageSize.X, normPos.Y * imageSize.Y);
        }

        // --- FIX: ADDED MISSING HELPER METHOD ---
        private void DrawEdge(ImDrawListPtr dl, Vector2 p1, Vector2 p2, uint color)
        {
            dl.AddLine(p1, p2, color, 1.5f);
        }
    }
}