// GeoscientistToolkit/Analysis/Geomechanics/FluidGeothermalVisualizationRenderer.cs

using System.Numerics;
using ImGuiNET;
using SkiaSharp;

namespace GeoscientistToolkit.Analysis.Geomechanics;

public class FluidGeothermalVisualizationRenderer : IDisposable
{
    private float _apertureMin, _apertureMax;
    private float _pressureMin, _pressureMax;
    private int _selectedSliceZ;
    private bool _showFractureOverlay = true;
    private bool _showVelocityVectors = true;
    private float _tempMin, _tempMax;
    private float _vectorScale = 1.0f;
    private int _visualizationMode; // 0=Pressure, 1=Temperature, 2=Aperture, 3=Velocity

    public void Dispose()
    {
        // No resources to dispose
    }

    public void DrawVisualization(GeomechanicalResults results, GeomechanicalParameters parameters)
    {
        if (results == null) return;

        var hasPressure = results.PressureField != null;
        var hasTemperature = results.TemperatureField != null;
        var hasFractures = results.FractureAperture != null;
        var hasVelocity = results.FluidVelocityX != null;

        if (!hasPressure && !hasTemperature && !hasFractures)
        {
            ImGui.Text("No fluid/geothermal data available for visualization");
            return;
        }

        // Controls
        ImGui.BeginChild("VisualizationControls", new Vector2(250, 0), ImGuiChildFlags.Border);
        DrawControls(results, hasPressure, hasTemperature, hasFractures, hasVelocity);
        ImGui.EndChild();

        ImGui.SameLine();

        // Visualization canvas
        ImGui.BeginChild("VisualizationCanvas", new Vector2(0, 0), ImGuiChildFlags.Border);
        DrawCanvas(results, parameters);
        ImGui.EndChild();
    }

    private void DrawControls(GeomechanicalResults results, bool hasPressure, bool hasTemperature,
        bool hasFractures, bool hasVelocity)
    {
        ImGui.Text("Field Visualization");
        ImGui.Separator();
        ImGui.Spacing();

        // Mode selection
        var modes = new List<string>();
        if (hasPressure) modes.Add("Fluid Pressure");
        if (hasTemperature) modes.Add("Temperature");
        if (hasFractures) modes.Add("Fracture Aperture");
        if (hasVelocity) modes.Add("Velocity Magnitude");

        if (modes.Count > 0)
        {
            ImGui.Text("Display Mode:");
            for (var i = 0; i < modes.Count; i++)
                if (ImGui.RadioButton(modes[i], _visualizationMode == i))
                    _visualizationMode = i;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Slice selection
        var d = results.MaterialLabels.GetLength(2);
        ImGui.Text($"Z-Slice: {_selectedSliceZ} / {d - 1}");
        ImGui.SliderInt("##SliceZ", ref _selectedSliceZ, 0, d - 1);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Overlay options
        ImGui.Checkbox("Show Fracture Overlay", ref _showFractureOverlay);

        if (hasVelocity)
        {
            ImGui.Checkbox("Show Velocity Vectors", ref _showVelocityVectors);
            if (_showVelocityVectors)
            {
                ImGui.Indent();
                ImGui.DragFloat("Vector Scale", ref _vectorScale, 0.1f, 0.1f, 10f);
                ImGui.Unindent();
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Statistics for current slice
        ImGui.Text("Slice Statistics:");
        ImGui.Indent();

        if (_visualizationMode == 0 && hasPressure)
        {
            CalculateSliceRange(results.PressureField, _selectedSliceZ, out var min, out var max);
            ImGui.Text($"Pressure: {min / 1e6f:F2} - {max / 1e6f:F2} MPa");
        }
        else if (_visualizationMode == 1 && hasTemperature)
        {
            CalculateSliceRange(results.TemperatureField, _selectedSliceZ, out var min, out var max);
            ImGui.Text($"Temperature: {min:F1} - {max:F1} °C");
        }
        else if (_visualizationMode == 2 && hasFractures)
        {
            CalculateSliceRange(results.FractureAperture, _selectedSliceZ, out var min, out var max);
            ImGui.Text($"Aperture: {min * 1e6f:F2} - {max * 1e6f:F2} µm");
        }
        else if (_visualizationMode == 3 && hasVelocity)
        {
            var (min, max) = CalculateVelocityMagnitudeRange(results, _selectedSliceZ);
            ImGui.Text($"Velocity: {min * 1e3f:F4} - {max * 1e3f:F4} mm/s");
        }

        ImGui.Unindent();
    }

    private void DrawCanvas(GeomechanicalResults results, GeomechanicalParameters parameters)
    {
        var canvasPos = ImGui.GetCursorScreenPos();
        var canvasSize = ImGui.GetContentRegionAvail();

        if (canvasSize.X < 50 || canvasSize.Y < 50) return;

        var drawList = ImGui.GetWindowDrawList();

        // Get dimensions
        var w = results.MaterialLabels.GetLength(0);
        var h = results.MaterialLabels.GetLength(1);
        var z = _selectedSliceZ;

        // Calculate aspect-preserving dimensions
        var aspect = (float)w / h;
        int imageW, imageH;
        if (canvasSize.X / canvasSize.Y > aspect)
        {
            imageH = (int)canvasSize.Y;
            imageW = (int)(imageH * aspect);
        }
        else
        {
            imageW = (int)canvasSize.X;
            imageH = (int)(imageW / aspect);
        }

        // Create visualization bitmap
        using var surface = SKSurface.Create(new SKImageInfo(imageW, imageH));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        // Draw field
        if (_visualizationMode == 0 && results.PressureField != null)
            DrawPressureField(canvas, results.PressureField, results.MaterialLabels, z, imageW, imageH);
        else if (_visualizationMode == 1 && results.TemperatureField != null)
            DrawTemperatureField(canvas, results.TemperatureField, results.MaterialLabels, z, imageW, imageH);
        else if (_visualizationMode == 2 && results.FractureAperture != null)
            DrawApertureField(canvas, results.FractureAperture, results.MaterialLabels, z, imageW, imageH);
        else if (_visualizationMode == 3 && results.FluidVelocityX != null)
            DrawVelocityField(canvas, results, z, imageW, imageH);

        // Overlay fractures
        if (_showFractureOverlay && results.FractureField != null)
            DrawFractureOverlay(canvas, results.FractureField, z, imageW, imageH, w, h);

        // Overlay velocity vectors
        if (_showVelocityVectors && results.FluidVelocityX != null)
            DrawVelocityVectors(canvas, results, z, imageW, imageH, w, h);

        // Render the SkiaSharp bitmap directly to ImGui using AddImage with pixel data
        // Since ImGui doesn't directly support SkiaSharp textures, we render pixel-by-pixel
        // to the ImGui draw list for immediate mode rendering

        var displayPos = new Vector2(canvasPos.X + (canvasSize.X - imageW) / 2,
            canvasPos.Y + (canvasSize.Y - imageH) / 2);

        // Read pixels from surface and draw directly
        using var pixmap = surface.PeekPixels();
        if (pixmap != null)
        {
            var pixels = pixmap.GetPixelSpan();
            int stride = pixmap.RowBytes;

            // Draw the visualization using colored rectangles for each data cell
            // This is more efficient than pixel-by-pixel for field data
            var cellW = (float)imageW / w;
            var cellH = (float)imageH / h;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // Sample the center of each cell from the rendered image
                    int px = (int)(x * cellW + cellW / 2);
                    int py = (int)(y * cellH + cellH / 2);
                    px = Math.Clamp(px, 0, imageW - 1);
                    py = Math.Clamp(py, 0, imageH - 1);

                    int pixelOffset = py * stride + px * 4;
                    if (pixelOffset + 3 < pixels.Length)
                    {
                        byte b = pixels[pixelOffset];
                        byte g = pixels[pixelOffset + 1];
                        byte r = pixels[pixelOffset + 2];
                        byte a = pixels[pixelOffset + 3];

                        if (a > 10) // Skip mostly transparent pixels
                        {
                            uint color = (uint)((a << 24) | (b << 16) | (g << 8) | r);
                            var cellPos = new Vector2(displayPos.X + x * cellW, displayPos.Y + y * cellH);
                            drawList.AddRectFilled(cellPos, cellPos + new Vector2(cellW + 1, cellH + 1), color);
                        }
                    }
                }
            }
        }

        // Draw border around the visualization
        drawList.AddRect(displayPos, displayPos + new Vector2(imageW, imageH),
            ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 1.0f)));

        // Draw info text
        var infoText = $"Slice Z={_selectedSliceZ} | {w}x{h} cells";
        drawList.AddText(new Vector2(displayPos.X, displayPos.Y + imageH + 5),
            ImGui.GetColorU32(new Vector4(0.8f, 0.8f, 0.8f, 1.0f)), infoText);
    }

    private void DrawPressureField(SKCanvas canvas, float[,,] pressure, byte[,,] labels, int z,
        int width, int height)
    {
        var w = pressure.GetLength(0);
        var h = pressure.GetLength(1);

        CalculateSliceRange(pressure, z, out var minP, out var maxP);
        if (maxP - minP < 1e-6f) return;

        using var bitmap = new SKBitmap(w, h);

        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            if (labels[x, y, z] == 0)
            {
                bitmap.SetPixel(x, y, SKColors.Black);
                continue;
            }

            var P = pressure[x, y, z];
            var normalized = (P - minP) / (maxP - minP);
            var color = GetColorFromValue(normalized, ColorMapType.Viridis);
            bitmap.SetPixel(x, y, color);
        }

        canvas.DrawBitmap(bitmap, new SKRect(0, 0, width, height),
            new SKPaint { FilterQuality = SKFilterQuality.High });
    }

    private void DrawTemperatureField(SKCanvas canvas, float[,,] temperature, byte[,,] labels, int z,
        int width, int height)
    {
        var w = temperature.GetLength(0);
        var h = temperature.GetLength(1);

        CalculateSliceRange(temperature, z, out var minT, out var maxT);
        if (maxT - minT < 1e-6f) return;

        using var bitmap = new SKBitmap(w, h);

        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            if (labels[x, y, z] == 0)
            {
                bitmap.SetPixel(x, y, SKColors.Black);
                continue;
            }

            var T = temperature[x, y, z];
            var normalized = (T - minT) / (maxT - minT);
            var color = GetColorFromValue(normalized, ColorMapType.Hot);
            bitmap.SetPixel(x, y, color);
        }

        canvas.DrawBitmap(bitmap, new SKRect(0, 0, width, height),
            new SKPaint { FilterQuality = SKFilterQuality.High });
    }

    private void DrawApertureField(SKCanvas canvas, float[,,] aperture, byte[,,] labels, int z,
        int width, int height)
    {
        var w = aperture.GetLength(0);
        var h = aperture.GetLength(1);

        CalculateSliceRange(aperture, z, out var minA, out var maxA);
        if (maxA < 1e-7f) maxA = 1e-5f;

        using var bitmap = new SKBitmap(w, h);

        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            if (labels[x, y, z] == 0)
            {
                bitmap.SetPixel(x, y, SKColors.Black);
                continue;
            }

            var A = aperture[x, y, z];
            if (A < 1e-7f)
            {
                bitmap.SetPixel(x, y, new SKColor(20, 20, 20));
            }
            else
            {
                var normalized = (float)Math.Log10(A / minA) / (float)Math.Log10(maxA / minA);
                normalized = Math.Clamp(normalized, 0f, 1f);
                var color = GetColorFromValue(normalized, ColorMapType.Plasma);
                bitmap.SetPixel(x, y, color);
            }
        }

        canvas.DrawBitmap(bitmap, new SKRect(0, 0, width, height),
            new SKPaint { FilterQuality = SKFilterQuality.High });
    }

    private void DrawVelocityField(SKCanvas canvas, GeomechanicalResults results, int z,
        int width, int height)
    {
        var w = results.FluidVelocityX.GetLength(0);
        var h = results.FluidVelocityX.GetLength(1);

        var (minV, maxV) = CalculateVelocityMagnitudeRange(results, z);
        if (maxV < 1e-9f) maxV = 1e-6f;

        using var bitmap = new SKBitmap(w, h);

        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            if (results.MaterialLabels[x, y, z] == 0)
            {
                bitmap.SetPixel(x, y, SKColors.Black);
                continue;
            }

            var vx = results.FluidVelocityX[x, y, z];
            var vy = results.FluidVelocityY[x, y, z];
            var vz = results.FluidVelocityZ[x, y, z];
            var vmag = MathF.Sqrt(vx * vx + vy * vy + vz * vz);

            var normalized = vmag / maxV;
            normalized = Math.Clamp(normalized, 0f, 1f);
            var color = GetColorFromValue(normalized, ColorMapType.Cool);
            bitmap.SetPixel(x, y, color);
        }

        canvas.DrawBitmap(bitmap, new SKRect(0, 0, width, height),
            new SKPaint { FilterQuality = SKFilterQuality.High });
    }

    private void DrawFractureOverlay(SKCanvas canvas, bool[,,] fractures, int z,
        int canvasW, int canvasH, int dataW, int dataH)
    {
        var scaleX = (float)canvasW / dataW;
        var scaleY = (float)canvasH / dataH;

        var paint = new SKPaint
        {
            Color = new SKColor(255, 0, 0, 180),
            StrokeWidth = 1.5f,
            IsAntialias = true
        };

        for (var y = 0; y < dataH; y++)
        for (var x = 0; x < dataW; x++)
        {
            if (!fractures[x, y, z]) continue;

            var cx = x * scaleX;
            var cy = y * scaleY;
            canvas.DrawCircle(cx, cy, 1.5f, paint);
        }
    }

    private void DrawVelocityVectors(SKCanvas canvas, GeomechanicalResults results, int z,
        int canvasW, int canvasH, int dataW, int dataH)
    {
        var scaleX = (float)canvasW / dataW;
        var scaleY = (float)canvasH / dataH;
        var stride = Math.Max(1, dataW / 40); // Draw every Nth vector

        var paint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 200),
            StrokeWidth = 1.0f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };

        var (_, maxV) = CalculateVelocityMagnitudeRange(results, z);
        if (maxV < 1e-9f) return;

        for (var y = 0; y < dataH; y += stride)
        for (var x = 0; x < dataW; x += stride)
        {
            if (results.MaterialLabels[x, y, z] == 0) continue;

            var vx = results.FluidVelocityX[x, y, z];
            var vy = results.FluidVelocityY[x, y, z];
            var vmag = MathF.Sqrt(vx * vx + vy * vy);

            if (vmag < 1e-9f) continue;

            var cx = x * scaleX;
            var cy = y * scaleY;

            var length = vmag / maxV * stride * scaleX * _vectorScale;
            var angle = MathF.Atan2(vy, vx);

            var ex = cx + length * MathF.Cos(angle);
            var ey = cy + length * MathF.Sin(angle);

            canvas.DrawLine(cx, cy, ex, ey, paint);

            // Arrow head
            var headSize = 2f;
            var headAngle1 = angle + 2.8f;
            var headAngle2 = angle - 2.8f;
            canvas.DrawLine(ex, ey, ex - headSize * MathF.Cos(headAngle1),
                ey - headSize * MathF.Sin(headAngle1), paint);
            canvas.DrawLine(ex, ey, ex - headSize * MathF.Cos(headAngle2),
                ey - headSize * MathF.Sin(headAngle2), paint);
        }
    }

    private void CalculateSliceRange(float[,,] field, int z, out float min, out float max)
    {
        var w = field.GetLength(0);
        var h = field.GetLength(1);

        min = float.MaxValue;
        max = float.MinValue;

        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var val = field[x, y, z];
            if (val < min) min = val;
            if (val > max) max = val;
        }
    }

    private (float min, float max) CalculateVelocityMagnitudeRange(GeomechanicalResults results, int z)
    {
        var w = results.FluidVelocityX.GetLength(0);
        var h = results.FluidVelocityX.GetLength(1);

        var min = float.MaxValue;
        var max = float.MinValue;

        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            if (results.MaterialLabels[x, y, z] == 0) continue;

            var vx = results.FluidVelocityX[x, y, z];
            var vy = results.FluidVelocityY[x, y, z];
            var vz = results.FluidVelocityZ[x, y, z];
            var vmag = MathF.Sqrt(vx * vx + vy * vy + vz * vz);

            if (vmag < min) min = vmag;
            if (vmag > max) max = vmag;
        }

        return (min, max);
    }

    private SKColor GetColorFromValue(float value, ColorMapType colorMap)
    {
        value = Math.Clamp(value, 0f, 1f);

        return colorMap switch
        {
            ColorMapType.Viridis => InterpolateViridis(value),
            ColorMapType.Hot => InterpolateHot(value),
            ColorMapType.Cool => InterpolateCool(value),
            ColorMapType.Plasma => InterpolatePlasma(value),
            _ => new SKColor((byte)(value * 255), (byte)(value * 255), (byte)(value * 255))
        };
    }

    private SKColor InterpolateViridis(float t)
    {
        // Simplified viridis colormap
        var r = (byte)(255 * Math.Clamp(-0.1f + 1.5f * t - 0.5f * t * t, 0f, 1f));
        var g = (byte)(255 * Math.Clamp(0.1f + 1.2f * t - 0.3f * t * t, 0f, 1f));
        var b = (byte)(255 * Math.Clamp(0.6f + 0.5f * t - 1.1f * t * t, 0f, 1f));
        return new SKColor(r, g, b);
    }

    private SKColor InterpolateHot(float t)
    {
        var r = (byte)(255 * Math.Clamp(3f * t, 0f, 1f));
        var g = (byte)(255 * Math.Clamp(3f * t - 1f, 0f, 1f));
        var b = (byte)(255 * Math.Clamp(3f * t - 2f, 0f, 1f));
        return new SKColor(r, g, b);
    }

    private SKColor InterpolateCool(float t)
    {
        var r = (byte)(255 * t);
        var g = (byte)(255 * (1f - t));
        var b = (byte)255;
        return new SKColor(r, g, b);
    }

    private SKColor InterpolatePlasma(float t)
    {
        var r = (byte)(255 * Math.Clamp(0.1f + 1.2f * t, 0f, 1f));
        var g = (byte)(255 * Math.Clamp(0.2f * t + 0.5f * t * t, 0f, 1f));
        var b = (byte)(255 * Math.Clamp(0.9f - 0.8f * t, 0f, 1f));
        return new SKColor(r, g, b);
    }

    private enum ColorMapType
    {
        Viridis,
        Hot,
        Cool,
        Plasma
    }
}