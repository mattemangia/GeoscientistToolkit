// GeoscientistToolkit/UI/GIS/GISViewer.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.GIS
{
    public class GISViewer : IDatasetViewer
    {
        private readonly GISDataset _dataset;
        private EditMode _editMode = EditMode.None;
        private FeatureType _drawingType = FeatureType.Point;
        private GISLayer _activeLayer;
        private GISFeature _selectedFeature;
        private List<Vector2> _currentDrawing = new List<Vector2>();
        private bool _showGrid = true;
        private bool _showCoordinates = true;
        private bool _showScale = true;
        private float _gridSpacing = 1.0f;
        
        // View state
        private Matrix3x2 _viewTransform = Matrix3x2.Identity;
        private Vector2 _lastMousePos;
        private bool _isPanning;
        
        public GISViewer(GISDataset dataset)
        {
            _dataset = dataset;
            _activeLayer = _dataset.Layers.FirstOrDefault(l => l.IsEditable);
        }
        
        public void DrawToolbarControls()
        {
            // Edit mode buttons
            if (ImGui.Button(_editMode == EditMode.None ? "Select" : "Select", new Vector2(60, 0)))
                _editMode = EditMode.None;
            
            ImGui.SameLine();
            if (ImGui.Button("Point", new Vector2(50, 0)))
            {
                _editMode = EditMode.Draw;
                _drawingType = FeatureType.Point;
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Line", new Vector2(50, 0)))
            {
                _editMode = EditMode.Draw;
                _drawingType = FeatureType.Line;
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Polygon", new Vector2(60, 0)))
            {
                _editMode = EditMode.Draw;
                _drawingType = FeatureType.Polygon;
            }
            
            ImGui.SameLine();
            ImGui.Separator();
            ImGui.SameLine();
            
            // Layer selector
            if (_dataset.Layers.Count > 0)
            {
                ImGui.SetNextItemWidth(150);
                if (ImGui.BeginCombo("##Layer", _activeLayer?.Name ?? "Select Layer"))
                {
                    foreach (var layer in _dataset.Layers.Where(l => l.IsEditable))
                    {
                        if (ImGui.Selectable(layer.Name, layer == _activeLayer))
                        {
                            _activeLayer = layer;
                        }
                    }
                    ImGui.EndCombo();
                }
            }
            
            ImGui.SameLine();
            ImGui.Separator();
            ImGui.SameLine();
            
            // View options
            ImGui.Checkbox("Grid", ref _showGrid);
            ImGui.SameLine();
            ImGui.Checkbox("Coords", ref _showCoordinates);
            ImGui.SameLine();
            ImGui.Checkbox("Scale", ref _showScale);
            
            // Status
            if (_editMode == EditMode.Draw)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1, 1, 0, 1), $"Drawing: {_drawingType}");
                
                if (_currentDrawing.Count > 0)
                {
                    ImGui.SameLine();
                    if (ImGui.Button("Finish"))
                    {
                        FinishDrawing();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Cancel"))
                    {
                        _currentDrawing.Clear();
                    }
                }
            }
        }
        
        public void DrawContent(ref float zoom, ref Vector2 pan)
        {
            var drawList = ImGui.GetWindowDrawList();
            var canvas_pos = ImGui.GetCursorScreenPos();
            var canvas_size = ImGui.GetContentRegionAvail();
            
            if (canvas_size.X < 50.0f) canvas_size.X = 50.0f;
            if (canvas_size.Y < 50.0f) canvas_size.Y = 50.0f;
            
            // Create invisible button for interaction
            ImGui.InvisibleButton("GISCanvas", canvas_size);
            var io = ImGui.GetIO();
            var mouse_pos = io.MousePos;
            var is_hovered = ImGui.IsItemHovered();
            var is_active = ImGui.IsItemActive();
            
            // Background
            drawList.AddRectFilled(canvas_pos, canvas_pos + canvas_size, 
                ImGui.GetColorU32(new Vector4(0.05f, 0.05f, 0.05f, 1.0f)));
            
            // Set up clipping
            drawList.PushClipRect(canvas_pos, canvas_pos + canvas_size);
            
            // Handle input
            if (is_hovered)
            {
                // Zoom with mouse wheel
                if (io.MouseWheel != 0)
                {
                    float zoomDelta = io.MouseWheel * 0.1f;
                    zoom = Math.Max(0.1f, Math.Min(10.0f, zoom + zoomDelta));
                }
                
                // Pan with middle mouse or right mouse
                if (ImGui.IsMouseDragging(ImGuiMouseButton.Middle) || 
                    (ImGui.IsMouseDragging(ImGuiMouseButton.Right) && _editMode != EditMode.Draw))
                {
                    var delta = mouse_pos - _lastMousePos;
                    pan += delta;
                }
                
                // Drawing/Selection with left mouse
                if (_editMode == EditMode.Draw && is_active && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    var worldPos = ScreenToWorld(mouse_pos - canvas_pos, canvas_pos, canvas_size, zoom, pan);
                    HandleDrawClick(worldPos);
                }
            }
            
            _lastMousePos = mouse_pos;
            
            // Calculate transform
            var center = canvas_pos + canvas_size * 0.5f;
            _viewTransform = Matrix3x2.CreateTranslation(-_dataset.Center) *
                            Matrix3x2.CreateScale(zoom) *
                            Matrix3x2.CreateTranslation(center + pan);
            
            // Draw grid
            if (_showGrid)
            {
                DrawGrid(drawList, canvas_pos, canvas_size, zoom, pan);
            }
            
            // Draw basemap if available
            if (_dataset.BasemapType != BasemapType.None && !string.IsNullOrEmpty(_dataset.BasemapPath))
            {
                DrawBasemap(drawList, canvas_pos, canvas_size, zoom, pan);
            }
            
            // Draw layers
            foreach (var layer in _dataset.Layers.Where(l => l.IsVisible))
            {
                DrawLayer(drawList, layer, canvas_pos, canvas_size, zoom, pan);
            }
            
            // Draw current drawing
            if (_currentDrawing.Count > 0)
            {
                DrawCurrentDrawing(drawList, canvas_pos, canvas_size, zoom, pan);
            }
            
            // Draw coordinates
            if (_showCoordinates && is_hovered)
            {
                var worldPos = ScreenToWorld(mouse_pos - canvas_pos, canvas_pos, canvas_size, zoom, pan);
                var coordText = $"Lon: {worldPos.X:F6}, Lat: {worldPos.Y:F6}";
                drawList.AddText(canvas_pos + new Vector2(5, canvas_size.Y - 20), 
                    ImGui.GetColorU32(ImGuiCol.Text), coordText);
            }
            
            // Draw scale
            if (_showScale)
            {
                DrawScale(drawList, canvas_pos, canvas_size, zoom);
            }
            
            drawList.PopClipRect();
        }
        
        private void DrawGrid(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan)
        {
            var gridColor = ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 0.2f, 0.5f));
            float spacing = _gridSpacing * zoom;
            
            if (spacing < 10) return; // Don't draw if too dense
            
            var center = canvasPos + canvasSize * 0.5f + pan;
            
            // Vertical lines
            for (float x = center.X % spacing; x < canvasSize.X; x += spacing)
            {
                drawList.AddLine(canvasPos + new Vector2(x, 0), 
                                 canvasPos + new Vector2(x, canvasSize.Y), gridColor);
            }
            
            // Horizontal lines
            for (float y = center.Y % spacing; y < canvasSize.Y; y += spacing)
            {
                drawList.AddLine(canvasPos + new Vector2(0, y), 
                                 canvasPos + new Vector2(canvasSize.X, y), gridColor);
            }
        }
        
        private void DrawBasemap(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan)
        {
            // Placeholder for basemap rendering
            // In production, you'd render the actual GeoTIFF or tile data here
            var textPos = canvasPos + canvasSize * 0.5f - new Vector2(50, 10);
            drawList.AddText(textPos, ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 1.0f)), 
                "[Basemap]");
        }
        
        private void DrawLayer(ImDrawListPtr drawList, GISLayer layer, Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan)
        {
            var color = ImGui.GetColorU32(layer.Color);
            
            foreach (var feature in layer.Features)
            {
                DrawFeature(drawList, feature, canvasPos, canvasSize, zoom, pan, color, layer);
            }
        }
        
        private void DrawFeature(ImDrawListPtr drawList, GISFeature feature, Vector2 canvasPos, 
            Vector2 canvasSize, float zoom, Vector2 pan, uint color, GISLayer layer)
        {
            if (feature.Coordinates.Count == 0) return;
            
            var screenCoords = feature.Coordinates
                .Select(c => WorldToScreen(c, canvasPos, canvasSize, zoom, pan))
                .ToList();
            
            if (feature.IsSelected)
            {
                color = ImGui.GetColorU32(new Vector4(1, 1, 0, 1));
            }
            
            switch (feature.Type)
            {
                case FeatureType.Point:
                    foreach (var coord in screenCoords)
                    {
                        drawList.AddCircleFilled(coord, layer.PointSize, color);
                        if (feature.IsSelected)
                        {
                            drawList.AddCircle(coord, layer.PointSize + 2, 
                                ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), 0, 2);
                        }
                    }
                    break;
                    
                case FeatureType.Line:
                    if (screenCoords.Count >= 2)
                    {
                        for (int i = 0; i < screenCoords.Count - 1; i++)
                        {
                            drawList.AddLine(screenCoords[i], screenCoords[i + 1], color, layer.LineWidth);
                        }
                    }
                    break;
                    
                case FeatureType.Polygon:
                    if (screenCoords.Count >= 3)
                    {
                        // Draw filled polygon
                        var fillColor = ImGui.GetColorU32(new Vector4(layer.Color.X, layer.Color.Y, 
                            layer.Color.Z, layer.Color.W * 0.3f));
                        drawList.AddConvexPolyFilled(ref screenCoords.ToArray()[0], screenCoords.Count, fillColor);
                        
                        // Draw outline
                        drawList.AddPolyline(ref screenCoords.ToArray()[0], screenCoords.Count, color, 
                            ImDrawFlags.Closed, layer.LineWidth);
                    }
                    break;
            }
        }
        
        private void DrawCurrentDrawing(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize, 
            float zoom, Vector2 pan)
        {
            if (_currentDrawing.Count == 0) return;
            
            var color = ImGui.GetColorU32(new Vector4(1, 0.5f, 0, 1));
            var screenCoords = _currentDrawing
                .Select(c => WorldToScreen(c, canvasPos, canvasSize, zoom, pan))
                .ToList();
            
            // Draw points
            foreach (var coord in screenCoords)
            {
                drawList.AddCircleFilled(coord, 3, color);
            }
            
            // Draw lines between points
            if (_drawingType != FeatureType.Point && screenCoords.Count >= 2)
            {
                for (int i = 0; i < screenCoords.Count - 1; i++)
                {
                    drawList.AddLine(screenCoords[i], screenCoords[i + 1], color, 2);
                }
                
                // Draw closing line for polygon
                if (_drawingType == FeatureType.Polygon && screenCoords.Count >= 3)
                {
                    var dashColor = ImGui.GetColorU32(new Vector4(1, 0.5f, 0, 0.5f));
                    DrawDashedLine(drawList, screenCoords[screenCoords.Count - 1], 
                        screenCoords[0], dashColor, 2);
                }
            }
        }
        
        private void DrawDashedLine(ImDrawListPtr drawList, Vector2 start, Vector2 end, uint color, float thickness)
        {
            var dir = end - start;
            var length = dir.Length();
            dir = Vector2.Normalize(dir);
            
            float dashLength = 5.0f;
            float gapLength = 5.0f;
            float currentLength = 0;
            bool drawing = true;
            
            while (currentLength < length)
            {
                float segmentLength = drawing ? dashLength : gapLength;
                if (currentLength + segmentLength > length)
                    segmentLength = length - currentLength;
                
                if (drawing)
                {
                    var segStart = start + dir * currentLength;
                    var segEnd = start + dir * (currentLength + segmentLength);
                    drawList.AddLine(segStart, segEnd, color, thickness);
                }
                
                currentLength += segmentLength;
                drawing = !drawing;
            }
        }
        
        private void DrawScale(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize, float zoom)
        {
            // Draw scale bar
            var scalePos = canvasPos + new Vector2(canvasSize.X - 150, canvasSize.Y - 40);
            var scaleWidth = 100.0f;
            
            drawList.AddRectFilled(scalePos, scalePos + new Vector2(scaleWidth, 20), 
                ImGui.GetColorU32(new Vector4(0, 0, 0, 0.7f)));
            
            drawList.AddLine(scalePos + new Vector2(5, 15), 
                            scalePos + new Vector2(scaleWidth - 5, 15), 
                            ImGui.GetColorU32(ImGuiCol.Text), 2);
            
            // Calculate scale
            float worldDistance = scaleWidth / zoom;
            string scaleText = FormatDistance(worldDistance);
            
            var textSize = ImGui.CalcTextSize(scaleText);
            drawList.AddText(scalePos + new Vector2((scaleWidth - textSize.X) * 0.5f, 0), 
                ImGui.GetColorU32(ImGuiCol.Text), scaleText);
        }
        
        private string FormatDistance(float distance)
        {
            // Assuming distance is in degrees, convert to km (rough approximation)
            float km = distance * 111.0f;
            
            if (km < 1)
                return $"{(km * 1000):F0} m";
            else if (km < 10)
                return $"{km:F1} km";
            else
                return $"{km:F0} km";
        }
        
        private Vector2 WorldToScreen(Vector2 worldPos, Vector2 canvasPos, Vector2 canvasSize, 
            float zoom, Vector2 pan)
        {
            var center = canvasPos + canvasSize * 0.5f + pan;
            var offset = (worldPos - _dataset.Center) * zoom;
            return center + offset;
        }
        
        private Vector2 ScreenToWorld(Vector2 screenPos, Vector2 canvasPos, Vector2 canvasSize, 
            float zoom, Vector2 pan)
        {
            var center = canvasSize * 0.5f + pan;
            var offset = screenPos - center;
            return _dataset.Center + offset / zoom;
        }
        
        private void HandleDrawClick(Vector2 worldPos)
        {
            if (_activeLayer == null || !_activeLayer.IsEditable)
            {
                Logger.LogWarning("No editable layer selected");
                return;
            }
            
            switch (_drawingType)
            {
                case FeatureType.Point:
                    // Create point immediately
                    var pointFeature = new GISFeature
                    {
                        Type = FeatureType.Point,
                        Coordinates = new List<Vector2> { worldPos }
                    };
                    _dataset.AddFeature(_activeLayer, pointFeature);
                    break;
                    
                case FeatureType.Line:
                    _currentDrawing.Add(worldPos);
                    if (_currentDrawing.Count >= 2 && ImGui.IsKeyPressed(ImGuiKey.Enter))
                    {
                        FinishDrawing();
                    }
                    break;
                    
                case FeatureType.Polygon:
                    _currentDrawing.Add(worldPos);
                    if (_currentDrawing.Count >= 3 && ImGui.IsKeyPressed(ImGuiKey.Enter))
                    {
                        FinishDrawing();
                    }
                    break;
            }
        }
        
        private void FinishDrawing()
        {
            if (_currentDrawing.Count == 0 || _activeLayer == null) return;
            
            var feature = new GISFeature
            {
                Type = _drawingType,
                Coordinates = new List<Vector2>(_currentDrawing)
            };
            
            _dataset.AddFeature(_activeLayer, feature);
            _currentDrawing.Clear();
        }
        
        public void Dispose()
        {
            // Cleanup if needed
        }
        
        private enum EditMode
        {
            None,
            Draw,
            Edit,
            Delete
        }
    }
}
