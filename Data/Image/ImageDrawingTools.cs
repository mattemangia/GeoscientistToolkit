// GeoscientistToolkit/Data/Image/ImageDrawingTools.cs

using System;
using System.Collections.Generic;
using System.Numerics;
using GeoscientistToolkit.Data.Image.Selection;

namespace GeoscientistToolkit.Data.Image
{
    /// <summary>
    /// Drawing tools for image manipulation: fill, gradient, pencil, eraser, brush
    /// </summary>
    public static class ImageDrawingTools
    {
        public enum GradientType
        {
            Linear,
            Radial,
            Angular,
            Diamond
        }

        public enum BrushShape
        {
            Circle,
            Square,
            Soft
        }

        #region Fill Tools

        /// <summary>
        /// Flood fill with tolerance
        /// </summary>
        public static void FloodFill(byte[] imageData, int width, int height,
            int startX, int startY, byte r, byte g, byte b, byte a, int tolerance = 0,
            ImageSelection selection = null)
        {
            if (startX < 0 || startX >= width || startY < 0 || startY >= height)
                return;

            int startIdx = (startY * width + startX) * 4;
            byte targetR = imageData[startIdx];
            byte targetG = imageData[startIdx + 1];
            byte targetB = imageData[startIdx + 2];
            byte targetA = imageData[startIdx + 3];

            // Don't fill if color is the same
            if (targetR == r && targetG == g && targetB == b && targetA == a)
                return;

            bool[,] filled = new bool[height, width];
            Queue<(int x, int y)> queue = new Queue<(int, int)>();
            queue.Enqueue((startX, startY));

            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();

                if (x < 0 || x >= width || y < 0 || y >= height)
                    continue;

                if (filled[y, x])
                    continue;

                // Check selection
                if (selection != null && selection.HasSelection)
                {
                    byte maskValue = selection.Mask[y * width + x];
                    if (maskValue == 0)
                        continue;
                }

                int idx = (y * width + x) * 4;

                // Check if pixel matches target color within tolerance
                int dr = Math.Abs(imageData[idx] - targetR);
                int dg = Math.Abs(imageData[idx + 1] - targetG);
                int db = Math.Abs(imageData[idx + 2] - targetB);
                int da = Math.Abs(imageData[idx + 3] - targetA);

                if (dr > tolerance || dg > tolerance || db > tolerance || da > tolerance)
                    continue;

                filled[y, x] = true;

                // Fill pixel
                imageData[idx] = r;
                imageData[idx + 1] = g;
                imageData[idx + 2] = b;
                imageData[idx + 3] = a;

                // Add neighbors
                queue.Enqueue((x + 1, y));
                queue.Enqueue((x - 1, y));
                queue.Enqueue((x, y + 1));
                queue.Enqueue((x, y - 1));
            }
        }

        /// <summary>
        /// Fill entire selection or image with solid color
        /// </summary>
        public static void Fill(byte[] imageData, int width, int height,
            byte r, byte g, byte b, byte a, ImageSelection selection = null)
        {
            if (selection == null || !selection.HasSelection)
            {
                // Fill entire image
                for (int i = 0; i < imageData.Length; i += 4)
                {
                    imageData[i] = r;
                    imageData[i + 1] = g;
                    imageData[i + 2] = b;
                    imageData[i + 3] = a;
                }
            }
            else
            {
                // Fill selection
                for (int y = selection.MinY; y <= selection.MaxY; y++)
                {
                    for (int x = selection.MinX; x <= selection.MaxX; x++)
                    {
                        if (x >= 0 && x < width && y >= 0 && y < height)
                        {
                            byte maskValue = selection.Mask[y * width + x];
                            if (maskValue > 0)
                            {
                                int idx = (y * width + x) * 4;

                                if (maskValue == 255)
                                {
                                    imageData[idx] = r;
                                    imageData[idx + 1] = g;
                                    imageData[idx + 2] = b;
                                    imageData[idx + 3] = a;
                                }
                                else
                                {
                                    // Partial selection - blend
                                    float alpha = (maskValue / 255.0f) * (a / 255.0f);
                                    float invAlpha = 1 - alpha;

                                    imageData[idx] = (byte)(r * alpha + imageData[idx] * invAlpha);
                                    imageData[idx + 1] = (byte)(g * alpha + imageData[idx + 1] * invAlpha);
                                    imageData[idx + 2] = (byte)(b * alpha + imageData[idx + 2] * invAlpha);
                                    imageData[idx + 3] = (byte)Math.Max(imageData[idx + 3], a);
                                }
                            }
                        }
                    }
                }
            }
        }

        #endregion

        #region Gradient Fill

        /// <summary>
        /// Fill with gradient
        /// </summary>
        public static void GradientFill(byte[] imageData, int width, int height,
            Vector2 start, Vector2 end,
            byte r1, byte g1, byte b1, byte a1,
            byte r2, byte g2, byte b2, byte a2,
            GradientType type = GradientType.Linear,
            ImageSelection selection = null)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Check selection
                    if (selection != null && selection.HasSelection)
                    {
                        byte maskValue = selection.Mask[y * width + x];
                        if (maskValue == 0)
                            continue;
                    }

                    float t = CalculateGradientPosition(x, y, start, end, type);
                    t = Math.Clamp(t, 0, 1);

                    int idx = (y * width + x) * 4;

                    byte r = (byte)(r1 + (r2 - r1) * t);
                    byte g = (byte)(g1 + (g2 - g1) * t);
                    byte b = (byte)(b1 + (b2 - b1) * t);
                    byte a = (byte)(a1 + (a2 - a1) * t);

                    // Apply selection mask
                    if (selection != null && selection.HasSelection)
                    {
                        byte maskValue = selection.Mask[y * width + x];
                        float maskAlpha = maskValue / 255.0f;
                        float alpha = (a / 255.0f) * maskAlpha;
                        float invAlpha = 1 - alpha;

                        imageData[idx] = (byte)(r * alpha + imageData[idx] * invAlpha);
                        imageData[idx + 1] = (byte)(g * alpha + imageData[idx + 1] * invAlpha);
                        imageData[idx + 2] = (byte)(b * alpha + imageData[idx + 2] * invAlpha);
                        imageData[idx + 3] = (byte)Math.Max(imageData[idx + 3], (byte)(a * maskAlpha));
                    }
                    else
                    {
                        imageData[idx] = r;
                        imageData[idx + 1] = g;
                        imageData[idx + 2] = b;
                        imageData[idx + 3] = a;
                    }
                }
            }
        }

        private static float CalculateGradientPosition(int x, int y, Vector2 start, Vector2 end, GradientType type)
        {
            Vector2 pos = new Vector2(x, y);

            switch (type)
            {
                case GradientType.Linear:
                    {
                        Vector2 dir = end - start;
                        float length = dir.Length();
                        if (length == 0) return 0;
                        dir /= length;
                        Vector2 toPoint = pos - start;
                        return Vector2.Dot(toPoint, dir) / length;
                    }

                case GradientType.Radial:
                    {
                        float maxDist = Vector2.Distance(start, end);
                        if (maxDist == 0) return 0;
                        float dist = Vector2.Distance(start, pos);
                        return dist / maxDist;
                    }

                case GradientType.Angular:
                    {
                        Vector2 toPoint = pos - start;
                        Vector2 toEnd = end - start;
                        float angle1 = MathF.Atan2(toPoint.Y, toPoint.X);
                        float angle2 = MathF.Atan2(toEnd.Y, toEnd.X);
                        float diff = angle1 - angle2;
                        while (diff < 0) diff += MathF.PI * 2;
                        return (diff / (MathF.PI * 2));
                    }

                case GradientType.Diamond:
                    {
                        Vector2 toPoint = pos - start;
                        float maxDist = Vector2.Distance(start, end);
                        if (maxDist == 0) return 0;
                        float dist = Math.Abs(toPoint.X) + Math.Abs(toPoint.Y);
                        return dist / (maxDist * 2);
                    }

                default:
                    return 0;
            }
        }

        #endregion

        #region Brush Tools

        /// <summary>
        /// Draw with brush at specified position
        /// </summary>
        public static void Brush(byte[] imageData, int width, int height,
            int x, int y, int size, byte r, byte g, byte b, byte a,
            BrushShape shape = BrushShape.Circle, float hardness = 1.0f,
            ImageSelection selection = null)
        {
            int radius = size / 2;

            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int px = x + dx;
                    int py = y + dy;

                    if (px < 0 || px >= width || py < 0 || py >= height)
                        continue;

                    float brushAlpha = CalculateBrushAlpha(dx, dy, radius, shape, hardness);
                    if (brushAlpha == 0)
                        continue;

                    // Check selection
                    byte selectionMask = 255;
                    if (selection != null && selection.HasSelection)
                    {
                        selectionMask = selection.Mask[py * width + px];
                        if (selectionMask == 0)
                            continue;
                    }

                    int idx = (py * width + px) * 4;

                    float finalAlpha = (a / 255.0f) * brushAlpha * (selectionMask / 255.0f);
                    float invAlpha = 1 - finalAlpha;

                    imageData[idx] = (byte)(r * finalAlpha + imageData[idx] * invAlpha);
                    imageData[idx + 1] = (byte)(g * finalAlpha + imageData[idx + 1] * invAlpha);
                    imageData[idx + 2] = (byte)(b * finalAlpha + imageData[idx + 2] * invAlpha);
                    imageData[idx + 3] = (byte)Math.Max(imageData[idx + 3], (byte)(a * brushAlpha * (selectionMask / 255.0f)));
                }
            }
        }

        /// <summary>
        /// Draw line with brush between two points
        /// </summary>
        public static void BrushLine(byte[] imageData, int width, int height,
            int x1, int y1, int x2, int y2, int size, byte r, byte g, byte b, byte a,
            BrushShape shape = BrushShape.Circle, float hardness = 1.0f,
            ImageSelection selection = null)
        {
            // Bresenham's line algorithm with brush application
            int dx = Math.Abs(x2 - x1);
            int dy = Math.Abs(y2 - y1);
            int sx = x1 < x2 ? 1 : -1;
            int sy = y1 < y2 ? 1 : -1;
            int err = dx - dy;

            int x = x1;
            int y = y1;

            while (true)
            {
                Brush(imageData, width, height, x, y, size, r, g, b, a, shape, hardness, selection);

                if (x == x2 && y == y2)
                    break;

                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    y += sy;
                }
            }
        }

        private static float CalculateBrushAlpha(int dx, int dy, int radius, BrushShape shape, float hardness)
        {
            switch (shape)
            {
                case BrushShape.Circle:
                    {
                        float dist = MathF.Sqrt(dx * dx + dy * dy);
                        if (dist > radius) return 0;
                        float t = dist / radius;
                        return 1 - MathF.Pow(t, 1 / hardness);
                    }

                case BrushShape.Square:
                    return 1.0f;

                case BrushShape.Soft:
                    {
                        float dist = MathF.Sqrt(dx * dx + dy * dy);
                        if (dist > radius) return 0;
                        float t = dist / radius;
                        return (1 - t) * hardness;
                    }

                default:
                    return 1.0f;
            }
        }

        #endregion

        #region Pencil Tool

        /// <summary>
        /// Draw hard-edged pixel at position
        /// </summary>
        public static void Pencil(byte[] imageData, int width, int height,
            int x, int y, byte r, byte g, byte b, byte a,
            ImageSelection selection = null)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
                return;

            // Check selection
            if (selection != null && selection.HasSelection)
            {
                byte maskValue = selection.Mask[y * width + x];
                if (maskValue == 0)
                    return;
            }

            int idx = (y * width + x) * 4;

            imageData[idx] = r;
            imageData[idx + 1] = g;
            imageData[idx + 2] = b;
            imageData[idx + 3] = a;
        }

        /// <summary>
        /// Draw hard-edged line between two points
        /// </summary>
        public static void PencilLine(byte[] imageData, int width, int height,
            int x1, int y1, int x2, int y2, byte r, byte g, byte b, byte a,
            ImageSelection selection = null)
        {
            // Bresenham's line algorithm
            int dx = Math.Abs(x2 - x1);
            int dy = Math.Abs(y2 - y1);
            int sx = x1 < x2 ? 1 : -1;
            int sy = y1 < y2 ? 1 : -1;
            int err = dx - dy;

            int x = x1;
            int y = y1;

            while (true)
            {
                Pencil(imageData, width, height, x, y, r, g, b, a, selection);

                if (x == x2 && y == y2)
                    break;

                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    y += sy;
                }
            }
        }

        #endregion

        #region Eraser Tool

        /// <summary>
        /// Erase pixels at position (sets alpha to 0)
        /// </summary>
        public static void Eraser(byte[] imageData, int width, int height,
            int x, int y, int size, BrushShape shape = BrushShape.Circle,
            float hardness = 1.0f, ImageSelection selection = null)
        {
            int radius = size / 2;

            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int px = x + dx;
                    int py = y + dy;

                    if (px < 0 || px >= width || py < 0 || py >= height)
                        continue;

                    float brushAlpha = CalculateBrushAlpha(dx, dy, radius, shape, hardness);
                    if (brushAlpha == 0)
                        continue;

                    // Check selection
                    byte selectionMask = 255;
                    if (selection != null && selection.HasSelection)
                    {
                        selectionMask = selection.Mask[py * width + px];
                        if (selectionMask == 0)
                            continue;
                    }

                    int idx = (py * width + px) * 4;

                    float eraseAmount = brushAlpha * (selectionMask / 255.0f);
                    imageData[idx + 3] = (byte)(imageData[idx + 3] * (1 - eraseAmount));
                }
            }
        }

        /// <summary>
        /// Erase line between two points
        /// </summary>
        public static void EraserLine(byte[] imageData, int width, int height,
            int x1, int y1, int x2, int y2, int size,
            BrushShape shape = BrushShape.Circle, float hardness = 1.0f,
            ImageSelection selection = null)
        {
            // Bresenham's line algorithm with eraser application
            int dx = Math.Abs(x2 - x1);
            int dy = Math.Abs(y2 - y1);
            int sx = x1 < x2 ? 1 : -1;
            int sy = y1 < y2 ? 1 : -1;
            int err = dx - dy;

            int x = x1;
            int y = y1;

            while (true)
            {
                Eraser(imageData, width, height, x, y, size, shape, hardness, selection);

                if (x == x2 && y == y2)
                    break;

                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    y += sy;
                }
            }
        }

        #endregion

        #region Rectangle and Ellipse Tools

        /// <summary>
        /// Draw filled rectangle
        /// </summary>
        public static void DrawFilledRectangle(byte[] imageData, int width, int height,
            int x, int y, int w, int h, byte r, byte g, byte b, byte a,
            ImageSelection selection = null)
        {
            for (int py = y; py < y + h; py++)
            {
                for (int px = x; px < x + w; px++)
                {
                    if (px >= 0 && px < width && py >= 0 && py < height)
                    {
                        // Check selection
                        if (selection != null && selection.HasSelection)
                        {
                            byte maskValue = selection.Mask[py * width + px];
                            if (maskValue == 0)
                                continue;
                        }

                        int idx = (py * width + px) * 4;
                        imageData[idx] = r;
                        imageData[idx + 1] = g;
                        imageData[idx + 2] = b;
                        imageData[idx + 3] = a;
                    }
                }
            }
        }

        /// <summary>
        /// Draw filled ellipse
        /// </summary>
        public static void DrawFilledEllipse(byte[] imageData, int width, int height,
            int cx, int cy, int rx, int ry, byte r, byte g, byte b, byte a,
            ImageSelection selection = null)
        {
            for (int y = cy - ry; y <= cy + ry; y++)
            {
                for (int x = cx - rx; x <= cx + rx; x++)
                {
                    if (x >= 0 && x < width && y >= 0 && y < height)
                    {
                        float dx = (x - cx) / (float)rx;
                        float dy = (y - cy) / (float)ry;

                        if (dx * dx + dy * dy <= 1.0f)
                        {
                            // Check selection
                            if (selection != null && selection.HasSelection)
                            {
                                byte maskValue = selection.Mask[y * width + x];
                                if (maskValue == 0)
                                    continue;
                            }

                            int idx = (y * width + x) * 4;
                            imageData[idx] = r;
                            imageData[idx + 1] = g;
                            imageData[idx + 2] = b;
                            imageData[idx + 3] = a;
                        }
                    }
                }
            }
        }

        #endregion
    }
}
