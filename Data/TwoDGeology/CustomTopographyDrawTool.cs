// GeoscientistToolkit/UI/GIS/CustomTopographyDrawTool.cs

using System.Numerics;
using GeoscientistToolkit.Business.GIS;
using GeoscientistToolkit.Data.TwoDGeology;
using GeoscientistToolkit.Util;
using ImGuiNET;
using static GeoscientistToolkit.Business.GIS.GeologicalMapping.ProfileGenerator;

namespace GeoscientistToolkit.UI.GIS;

/// <summary>
/// Tool for drawing custom topography profiles interactively
/// </summary>
public class CustomTopographyDrawTool
{
    private enum DrawMode
    {
        None,
        FreeDraw,
        StraightLines,
        SmoothCurve
    }
    
    private DrawMode _currentMode = DrawMode.None;
    private List<Vector2> _drawnPoints = new();
    private bool _isDrawing = false;
    private Vector2 _lastDrawPoint;
    private float _smoothingFactor = 0.5f;
    private bool _snapToGrid = false;
    private float _gridSnap = 50f;
    
    public void Draw(TwoDGeologyDataset dataset)
    {
        if (dataset?.ProfileData?.Profile == null)
            return;
        
        var profile = dataset.ProfileData.Profile;
        
        ImGui.Text("Custom Topography Drawing");
        ImGui.Separator();
        
        // Drawing mode selection
        ImGui.Text("Drawing Mode:");
        if (ImGui.RadioButton("Free Draw", _currentMode == DrawMode.FreeDraw))
        {
            _currentMode = DrawMode.FreeDraw;
            ClearDrawing();
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Straight Lines", _currentMode == DrawMode.StraightLines))
        {
            _currentMode = DrawMode.StraightLines;
            ClearDrawing();
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Smooth Curve", _currentMode == DrawMode.SmoothCurve))
        {
            _currentMode = DrawMode.SmoothCurve;
            ClearDrawing();
        }
        
        ImGui.Separator();
        
        // Drawing options
        ImGui.Checkbox("Snap to Grid", ref _snapToGrid);
        if (_snapToGrid)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            ImGui.DragFloat("Grid Size##snap", ref _gridSnap, 10f, 10f, 500f, "%.0f m");
        }
        
        if (_currentMode == DrawMode.SmoothCurve)
        {
            ImGui.SetNextItemWidth(200);
            ImGui.SliderFloat("Smoothing", ref _smoothingFactor, 0f, 1f);
            ImGui.SameLine();
            if (ImGui.Button("?"))
                ImGui.SetTooltip("Higher values create smoother curves");
        }
        
        ImGui.Separator();
        
        // Instructions
        ImGui.TextColored(new Vector4(0.7f, 0.9f, 1f, 1f), "Instructions:");
        ImGui.Text(_currentMode switch
        {
            DrawMode.FreeDraw => "Click and drag in the viewport to draw freehand",
            DrawMode.StraightLines => "Click points to create straight line segments",
            DrawMode.SmoothCurve => "Click points to create a smooth interpolated curve",
            _ => "Select a drawing mode to begin"
        });
        
        if (_drawnPoints.Count > 0)
        {
            ImGui.Text($"Points drawn: {_drawnPoints.Count}");
        }
        
        ImGui.Separator();
        
        // Action buttons
        if (_drawnPoints.Count > 1)
        {
            if (ImGui.Button("Apply to Profile", new Vector2(150, 30)))
            {
                ApplyDrawnProfile(profile);
                dataset.MarkAsModified();
                Logger.Log("Applied drawn topography profile");
            }
            
            ImGui.SameLine();
        }
        
        if (_drawnPoints.Count > 0)
        {
            if (ImGui.Button("Clear", new Vector2(100, 30)))
            {
                ClearDrawing();
            }
        }
        
        if (_currentMode != DrawMode.None)
        {
            if (ImGui.Button("Cancel Drawing Mode", new Vector2(150, 30)))
            {
                _currentMode = DrawMode.None;
                ClearDrawing();
            }
        }
        
        ImGui.Separator();
        
        // Preset operations
        ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.5f, 1f), "Quick Operations:");
        
        if (ImGui.Button("Smooth Existing"))
        {
            SmoothExistingProfile(profile, 0.3f);
            dataset.MarkAsModified();
        }
        ImGui.SameLine();
        if (ImGui.Button("Add Noise"))
        {
            AddNoiseToProfile(profile, 20f);
            dataset.MarkAsModified();
        }
        ImGui.SameLine();
        if (ImGui.Button("Flatten"))
        {
            FlattenProfile(profile);
            dataset.MarkAsModified();
        }
    }
    
    /// <summary>
    /// Handle mouse input for drawing in the viewport
    /// </summary>
    public void HandleDrawingInput(Vector2 mouseWorldPos, bool isMouseDown, bool isMouseClicked)
    {
        if (_currentMode == DrawMode.None)
            return;
        
        var snappedPos = _snapToGrid ? SnapToGrid(mouseWorldPos, _gridSnap) : mouseWorldPos;
        
        switch (_currentMode)
        {
            case DrawMode.FreeDraw:
                HandleFreeDrawInput(snappedPos, isMouseDown);
                break;
                
            case DrawMode.StraightLines:
            case DrawMode.SmoothCurve:
                HandleClickDrawInput(snappedPos, isMouseClicked);
                break;
        }
    }
    
    private void HandleFreeDrawInput(Vector2 worldPos, bool isMouseDown)
    {
        if (isMouseDown)
        {
            if (!_isDrawing || Vector2.Distance(worldPos, _lastDrawPoint) > 50f) // Min distance between points
            {
                _drawnPoints.Add(worldPos);
                _lastDrawPoint = worldPos;
                _isDrawing = true;
            }
        }
        else
        {
            _isDrawing = false;
        }
    }
    
    private void HandleClickDrawInput(Vector2 worldPos, bool isMouseClicked)
    {
        if (isMouseClicked)
        {
            _drawnPoints.Add(worldPos);
        }
    }
    
    /// <summary>
    /// Render the drawn points as a preview
    /// </summary>
    public void RenderDrawPreview(ImDrawListPtr drawList, Func<Vector2, Vector2> worldToScreen)
    {
        if (_drawnPoints.Count < 2)
            return;
        
        var color = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.5f, 0f, 1f)); // Orange
        var highlightColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 0f, 1f)); // Yellow
        
        // Draw lines between points
        for (int i = 0; i < _drawnPoints.Count - 1; i++)
        {
            var p1 = worldToScreen(_drawnPoints[i]);
            var p2 = worldToScreen(_drawnPoints[i + 1]);
            drawList.AddLine(p1, p2, color, 2f);
        }
        
        // Draw points
        foreach (var point in _drawnPoints)
        {
            var screenPos = worldToScreen(point);
            drawList.AddCircleFilled(screenPos, 4f, highlightColor);
        }
        
        // Draw smooth curve preview for smooth mode
        if (_currentMode == DrawMode.SmoothCurve && _drawnPoints.Count > 2)
        {
            var smoothPoints = InterpolateSmooth(_drawnPoints, 50);
            var smoothColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 1f, 0.5f, 0.5f));
            
            for (int i = 0; i < smoothPoints.Count - 1; i++)
            {
                var p1 = worldToScreen(smoothPoints[i]);
                var p2 = worldToScreen(smoothPoints[i + 1]);
                drawList.AddLine(p1, p2, smoothColor, 1.5f);
            }
        }
    }
    
    private void ApplyDrawnProfile(TopographicProfile profile)
    {
        if (_drawnPoints.Count < 2)
            return;
        
        // Sort points by X coordinate
        var sortedPoints = _drawnPoints.OrderBy(p => p.X).ToList();
        
        // Apply smoothing if in smooth mode
        if (_currentMode == DrawMode.SmoothCurve)
        {
            sortedPoints = InterpolateSmooth(sortedPoints, 100);
        }
        
        // Interpolate to profile resolution
        var interpolatedPoints = InterpolateToProfileResolution(sortedPoints, profile.TotalDistance, 100);
        
        // Replace profile points
        profile.Points.Clear();
        foreach (var point in interpolatedPoints)
        {
            profile.Points.Add(new ProfilePoint
            {
                Position = point,
                Distance = point.X,
                Elevation = point.Y,
                Features = new List<GeologicalMapping.GeologicalFeature>()
            });
        }
        
        // Update elevation range
        profile.MinElevation = profile.Points.Min(p => p.Elevation);
        profile.MaxElevation = profile.Points.Max(p => p.Elevation);
        
        ClearDrawing();
    }
    
    private List<Vector2> InterpolateSmooth(List<Vector2> points, int resolution)
    {
        if (points.Count < 3)
            return points;
        
        var smoothed = new List<Vector2>();
        var sortedPoints = points.OrderBy(p => p.X).ToList();
        
        // Catmull-Rom spline interpolation
        for (int i = 0; i < resolution; i++)
        {
            float t = i / (float)(resolution - 1);
            float distance = t * (sortedPoints[^1].X - sortedPoints[0].X) + sortedPoints[0].X;
            
            // Find surrounding points
            int idx = 0;
            for (int j = 0; j < sortedPoints.Count - 1; j++)
            {
                if (distance >= sortedPoints[j].X && distance <= sortedPoints[j + 1].X)
                {
                    idx = j;
                    break;
                }
            }
            
            // Catmull-Rom interpolation
            var p0 = idx > 0 ? sortedPoints[idx - 1] : sortedPoints[idx];
            var p1 = sortedPoints[idx];
            var p2 = sortedPoints[Math.Min(idx + 1, sortedPoints.Count - 1)];
            var p3 = sortedPoints[Math.Min(idx + 2, sortedPoints.Count - 1)];
            
            float localT = (distance - p1.X) / Math.Max(p2.X - p1.X, 0.001f);
            localT = Math.Clamp(localT, 0f, 1f);
            
            var elevation = CatmullRom(p0.Y, p1.Y, p2.Y, p3.Y, localT);
            smoothed.Add(new Vector2(distance, elevation));
        }
        
        return smoothed;
    }
    
    private float CatmullRom(float p0, float p1, float p2, float p3, float t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        
        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }
    
    private List<Vector2> InterpolateToProfileResolution(List<Vector2> points, float totalDistance, int resolution)
    {
        var result = new List<Vector2>();
        var sortedPoints = points.OrderBy(p => p.X).ToList();
        
        for (int i = 0; i < resolution; i++)
        {
            float distance = i / (float)(resolution - 1) * totalDistance;
            float elevation = 0f;
            
            // Linear interpolation between points
            if (distance <= sortedPoints[0].X)
            {
                elevation = sortedPoints[0].Y;
            }
            else if (distance >= sortedPoints[^1].X)
            {
                elevation = sortedPoints[^1].Y;
            }
            else
            {
                for (int j = 0; j < sortedPoints.Count - 1; j++)
                {
                    if (distance >= sortedPoints[j].X && distance <= sortedPoints[j + 1].X)
                    {
                        float t = (distance - sortedPoints[j].X) / (sortedPoints[j + 1].X - sortedPoints[j].X);
                        elevation = sortedPoints[j].Y + t * (sortedPoints[j + 1].Y - sortedPoints[j].Y);
                        break;
                    }
                }
            }
            
            result.Add(new Vector2(distance, elevation));
        }
        
        return result;
    }
    
    private Vector2 SnapToGrid(Vector2 point, float gridSize)
    {
        return new Vector2(
            MathF.Round(point.X / gridSize) * gridSize,
            MathF.Round(point.Y / gridSize) * gridSize
        );
    }
    
    private void ClearDrawing()
    {
        _drawnPoints.Clear();
        _isDrawing = false;
    }
    
    private void SmoothExistingProfile(TopographicProfile profile, float strength)
    {
        if (profile.Points.Count < 3)
            return;
        
        var smoothedElevations = new List<float>();
        
        for (int i = 0; i < profile.Points.Count; i++)
        {
            if (i == 0 || i == profile.Points.Count - 1)
            {
                smoothedElevations.Add(profile.Points[i].Elevation);
            }
            else
            {
                var prev = profile.Points[i - 1].Elevation;
                var curr = profile.Points[i].Elevation;
                var next = profile.Points[i + 1].Elevation;
                
                var smoothed = (prev + curr + next) / 3f;
                var blended = curr + (smoothed - curr) * strength;
                smoothedElevations.Add(blended);
            }
        }
        
        for (int i = 0; i < profile.Points.Count; i++)
        {
            profile.Points[i] = new ProfilePoint
            {
                Position = new Vector2(profile.Points[i].Distance, smoothedElevations[i]),
                Distance = profile.Points[i].Distance,
                Elevation = smoothedElevations[i],
                Features = profile.Points[i].Features
            };
        }
        
        profile.MinElevation = smoothedElevations.Min();
        profile.MaxElevation = smoothedElevations.Max();
        
        Logger.Log("Smoothed topography profile");
    }
    
    private void AddNoiseToProfile(TopographicProfile profile, float amplitude)
    {
        var random = new Random();
        
        foreach (var point in profile.Points)
        {
            var noise = (float)(random.NextDouble() * 2.0 - 1.0) * amplitude;
            point.Elevation += noise;
            point.Position = new Vector2(point.Distance, point.Elevation);
        }
        
        profile.MinElevation = profile.Points.Min(p => p.Elevation);
        profile.MaxElevation = profile.Points.Max(p => p.Elevation);
        
        Logger.Log("Added noise to topography profile");
    }
    
    private void FlattenProfile(TopographicProfile profile)
    {
        var avgElevation = profile.Points.Average(p => p.Elevation);
        
        foreach (var point in profile.Points)
        {
            point.Elevation = avgElevation;
            point.Position = new Vector2(point.Distance, avgElevation);
        }
        
        profile.MinElevation = avgElevation;
        profile.MaxElevation = avgElevation;
        
        Logger.Log("Flattened topography profile");
    }
    
    public bool IsDrawing => _currentMode != DrawMode.None;
}