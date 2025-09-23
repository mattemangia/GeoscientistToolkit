// GeoscientistToolkit/Analysis/RockCoreExtractor/RockCoreOverlay.cs
using System;
using System.Numerics;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.RockCoreExtractor
{
    /// <summary>
    /// Handles interactive overlay rendering for the Rock Core Extractor tool.
    /// </summary>
    public class RockCoreOverlay
    {
        private readonly RockCoreExtractorTool _tool;
        private readonly CtImageStackDataset _dataset;
        
        // Public property for dataset access
        public CtImageStackDataset Dataset => _dataset;
        
        // Interaction state
        private bool _isDraggingCenter = false;
        private bool _isDraggingDiameter = false;
        private bool _isDraggingLength = false;
        private Vector2 _dragStartPos;
        private float _dragStartValue;
        private Vector2 _dragStartCenter;

        public RockCoreOverlay(RockCoreExtractorTool tool, CtImageStackDataset dataset)
        {
            _tool = tool ?? throw new ArgumentNullException(nameof(tool));
            _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        }

        /// <summary>
        /// Draws the overlay on a slice view in the CtCombinedViewer.
        /// </summary>
        public void DrawOnSlice(ImDrawListPtr dl, int viewIndex, Vector2 imagePos, Vector2 imageSize, 
            int imageWidth, int imageHeight, int currentSliceX, int currentSliceY, int currentSliceZ)
        {
            if (!_tool.ShowPreview) return;

            var parameters = _tool.GetCoreParameters();
            var view = parameters.View;
            var coreDiameter = parameters.Diameter;
            var coreLength = parameters.Length;
            var coreCenter = parameters.Center;
            var coreStartPos = parameters.StartPosition;

            // Determine what to draw based on view index and selected core orientation
            switch (view)
            {
                case RockCoreExtractorTool.CircularView.XY_Circular_Z_Lateral:
                    if (viewIndex == 0) // XY view - show circle
                    {
                        DrawCircularCrossSection(dl, imagePos, imageSize, imageWidth, imageHeight,
                            coreCenter, coreDiameter);
                    }
                    else if (viewIndex == 1) // XZ view - show lateral rectangle
                    {
                        DrawLateralRectangle(dl, imagePos, imageSize, imageWidth, imageHeight,
                            coreCenter.X, coreStartPos, coreDiameter, coreLength, true, false);
                    }
                    else if (viewIndex == 2) // YZ view - show lateral rectangle
                    {
                        DrawLateralRectangle(dl, imagePos, imageSize, imageWidth, imageHeight,
                            coreCenter.Y, coreStartPos, coreDiameter, coreLength, false, false);
                    }
                    break;

                case RockCoreExtractorTool.CircularView.XZ_Circular_Y_Lateral:
                    if (viewIndex == 0) // XY view - show lateral rectangle
                    {
                        DrawLateralRectangle(dl, imagePos, imageSize, imageWidth, imageHeight,
                            coreCenter.X, coreStartPos, coreDiameter, coreLength, true, true);
                    }
                    else if (viewIndex == 1) // XZ view - show circle
                    {
                        DrawCircularCrossSection(dl, imagePos, imageSize, imageWidth, imageHeight,
                            coreCenter, coreDiameter);
                    }
                    else if (viewIndex == 2) // YZ view - show lateral rectangle
                    {
                        DrawLateralRectangle(dl, imagePos, imageSize, imageWidth, imageHeight,
                            coreCenter.Y, coreStartPos, coreDiameter, coreLength, false, true);
                    }
                    break;

                case RockCoreExtractorTool.CircularView.YZ_Circular_X_Lateral:
                    if (viewIndex == 0) // XY view - show lateral rectangle (vertical)
                    {
                        float startX = coreStartPos * imageWidth;
                        float endX = Math.Min(imageWidth, startX + coreLength);
                        float centerY = coreCenter.X * imageHeight; // Note: X component maps to Y in YZ view
                        float halfDiameter = coreDiameter / 2f;
                        
                        Vector2 topLeft = imagePos + new Vector2(startX, centerY - halfDiameter) * (imageSize / new Vector2(imageWidth, imageHeight));
                        Vector2 bottomRight = imagePos + new Vector2(endX, centerY + halfDiameter) * (imageSize / new Vector2(imageWidth, imageHeight));
                        DrawRectangleWithHandles(dl, topLeft, bottomRight, false, true);
                    }
                    else if (viewIndex == 1) // XZ view - show lateral rectangle (horizontal)
                    {
                        float centerZ = coreCenter.Y * imageHeight; // Note: Y component maps to Z in YZ view
                        float halfDiameter = coreDiameter / 2f;
                        float startX = coreStartPos * imageWidth;
                        float endX = Math.Min(imageWidth, startX + coreLength);
                        
                        Vector2 topLeft = imagePos + new Vector2(startX, centerZ - halfDiameter) * (imageSize / new Vector2(imageWidth, imageHeight));
                        Vector2 bottomRight = imagePos + new Vector2(endX, centerZ + halfDiameter) * (imageSize / new Vector2(imageWidth, imageHeight));
                        DrawRectangleWithHandles(dl, topLeft, bottomRight, false, true);
                    }
                    else if (viewIndex == 2) // YZ view - show circle
                    {
                        DrawCircularCrossSection(dl, imagePos, imageSize, imageWidth, imageHeight,
                            coreCenter, coreDiameter);
                    }
                    break;
            }
        }

        private void DrawCircularCrossSection(ImDrawListPtr dl, Vector2 imagePos, Vector2 imageSize,
            int imageWidth, int imageHeight, Vector2 center, float diameter)
        {
            Vector2 centerScreen = imagePos + new Vector2(center.X * imageSize.X, center.Y * imageSize.Y);
            float radiusScreen = (diameter / 2f) * (imageSize.X / imageWidth);
            
            // Draw circle
            dl.AddCircle(centerScreen, radiusScreen, 0xFF00FF00, 32, 2.0f);
            
            // Draw center handle
            dl.AddCircleFilled(centerScreen, 5, 0xFF00FF00);
            
            // Draw diameter handle
            Vector2 handlePos = centerScreen + new Vector2(radiusScreen, 0);
            dl.AddCircleFilled(handlePos, 5, 0xFF00FF00);
        }

        private void DrawLateralRectangle(ImDrawListPtr dl, Vector2 imagePos, Vector2 imageSize,
            int imageWidth, int imageHeight, float centerNorm, float startPosNorm, 
            float diameter, float length, bool isHorizontalCenter, bool isHorizontalLength)
        {
            float halfDiameter = diameter / 2f;
            Vector2 topLeft, bottomRight;
            
            if (isHorizontalCenter && !isHorizontalLength) // Horizontal center, vertical length
            {
                float centerX = centerNorm * imageWidth;
                float startZ = startPosNorm * imageHeight;
                float endZ = Math.Min(imageHeight, startZ + length);
                
                topLeft = imagePos + new Vector2(centerX - halfDiameter, startZ) * (imageSize / new Vector2(imageWidth, imageHeight));
                bottomRight = imagePos + new Vector2(centerX + halfDiameter, endZ) * (imageSize / new Vector2(imageWidth, imageHeight));
            }
            else if (!isHorizontalCenter && !isHorizontalLength) // Vertical center, vertical length
            {
                float centerY = centerNorm * imageWidth;
                float startZ = startPosNorm * imageHeight;
                float endZ = Math.Min(imageHeight, startZ + length);
                
                topLeft = imagePos + new Vector2(centerY - halfDiameter, startZ) * (imageSize / new Vector2(imageWidth, imageHeight));
                bottomRight = imagePos + new Vector2(centerY + halfDiameter, endZ) * (imageSize / new Vector2(imageWidth, imageHeight));
            }
            else if (isHorizontalCenter && isHorizontalLength) // Horizontal center, horizontal length
            {
                float centerX = centerNorm * imageWidth;
                float startY = startPosNorm * imageHeight;
                float endY = Math.Min(imageHeight, startY + length);
                
                topLeft = imagePos + new Vector2(centerX - halfDiameter, startY) * (imageSize / new Vector2(imageWidth, imageHeight));
                bottomRight = imagePos + new Vector2(centerX + halfDiameter, endY) * (imageSize / new Vector2(imageWidth, imageHeight));
            }
            else // Vertical center, horizontal length
            {
                float centerY = centerNorm * imageHeight;
                float startX = startPosNorm * imageWidth;
                float endX = Math.Min(imageWidth, startX + length);
                
                topLeft = imagePos + new Vector2(startX, centerY - halfDiameter) * (imageSize / new Vector2(imageWidth, imageHeight));
                bottomRight = imagePos + new Vector2(endX, centerY + halfDiameter) * (imageSize / new Vector2(imageWidth, imageHeight));
            }
            
            DrawRectangleWithHandles(dl, topLeft, bottomRight, isHorizontalCenter, isHorizontalLength);
        }

        private void DrawRectangleWithHandles(ImDrawListPtr dl, Vector2 topLeft, Vector2 bottomRight, 
            bool isHorizontalCenter, bool isHorizontalLength)
        {
            // Draw rectangle
            dl.AddRect(topLeft, bottomRight, 0xFF00FF00, 0, ImDrawFlags.None, 2.0f);
            
            // Draw corner handles for resizing
            dl.AddCircleFilled(topLeft, 4, 0xFF00FF00);
            dl.AddCircleFilled(bottomRight, 4, 0xFF00FF00);
            dl.AddCircleFilled(new Vector2(topLeft.X, bottomRight.Y), 4, 0xFF00FF00);
            dl.AddCircleFilled(new Vector2(bottomRight.X, topLeft.Y), 4, 0xFF00FF00);
            
            // Draw center handle
            Vector2 center = (topLeft + bottomRight) * 0.5f;
            dl.AddCircleFilled(center, 5, 0xFF00FF00);
        }

        /// <summary>
        /// Handles mouse input for interactive manipulation of the core parameters.
        /// </summary>
        public bool HandleMouseInput(Vector2 mousePos, Vector2 imagePos, Vector2 imageSize,
            int imageWidth, int imageHeight, int viewIndex, bool clicked, bool dragging, bool released)
        {
            if (!_tool.ShowPreview) return false;

            var parameters = _tool.GetCoreParameters();
            var view = parameters.View;
            
            // Convert mouse position to image coordinates
            Vector2 mousePosInImage = (mousePos - imagePos) / imageSize;
            mousePosInImage.X *= imageWidth;
            mousePosInImage.Y *= imageHeight;

            bool handled = false;

            // Check which view should handle interaction
            bool isCircularView = IsCircularView(view, viewIndex);
            
            if (isCircularView)
            {
                handled = HandleCircularViewInput(mousePosInImage, imageWidth, imageHeight, clicked, dragging, released);
            }
            else
            {
                handled = HandleLateralViewInput(mousePosInImage, imageWidth, imageHeight, viewIndex, clicked, dragging, released);
            }

            if (released)
            {
                _isDraggingCenter = false;
                _isDraggingDiameter = false;
                _isDraggingLength = false;
            }

            return handled;
        }

        private bool IsCircularView(RockCoreExtractorTool.CircularView view, int viewIndex)
        {
            return (view == RockCoreExtractorTool.CircularView.XY_Circular_Z_Lateral && viewIndex == 0) ||
                   (view == RockCoreExtractorTool.CircularView.XZ_Circular_Y_Lateral && viewIndex == 1) ||
                   (view == RockCoreExtractorTool.CircularView.YZ_Circular_X_Lateral && viewIndex == 2);
        }

        private bool HandleCircularViewInput(Vector2 mousePos, int width, int height, bool clicked, bool dragging, bool released)
        {
            var parameters = _tool.GetCoreParameters();
            Vector2 center = new Vector2(parameters.Center.X * width, parameters.Center.Y * height);
            float radius = parameters.Diameter / 2f;

            if (clicked)
            {
                float distToCenter = Vector2.Distance(mousePos, center);
                
                // Check if clicking near the edge (for diameter adjustment)
                if (Math.Abs(distToCenter - radius) < 10f)
                {
                    _isDraggingDiameter = true;
                    _dragStartPos = mousePos;
                    _dragStartValue = parameters.Diameter;
                    return true;
                }
                // Check if clicking near the center (for position adjustment)
                else if (distToCenter < 20f)
                {
                    _isDraggingCenter = true;
                    _dragStartPos = mousePos;
                    _dragStartCenter = parameters.Center;
                    return true;
                }
            }

            if (dragging)
            {
                if (_isDraggingDiameter)
                {
                    float newRadius = Vector2.Distance(mousePos, center);
                    _tool.SetCoreDiameter(newRadius * 2f);
                    return true;
                }
                else if (_isDraggingCenter)
                {
                    Vector2 delta = mousePos - _dragStartPos;
                    Vector2 newCenter = new Vector2(
                        (_dragStartCenter.X * width + delta.X) / width,
                        (_dragStartCenter.Y * height + delta.Y) / height
                    );
                    _tool.SetCoreCenter(newCenter);
                    return true;
                }
            }

            return false;
        }

        private bool HandleLateralViewInput(Vector2 mousePos, int width, int height, int viewIndex, bool clicked, bool dragging, bool released)
        {
            var parameters = _tool.GetCoreParameters();
            
            if (clicked)
            {
                // Check if clicking near the length edge
                float lengthEnd = parameters.StartPosition * width + parameters.Length;
                if (Math.Abs(mousePos.X - lengthEnd) < 10f)
                {
                    _isDraggingLength = true;
                    _dragStartPos = mousePos;
                    _dragStartValue = parameters.Length;
                    return true;
                }
            }

            if (dragging && _isDraggingLength)
            {
                float delta = mousePos.X - _dragStartPos.X;
                _tool.SetCoreLength(_dragStartValue + delta);
                return true;
            }

            return false;
        }
    }
}