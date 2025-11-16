// GeoscientistToolkit/Data/Image/Selection/ImageSelection.cs

using System;
using System.Collections.Generic;
using System.Numerics;

namespace GeoscientistToolkit.Data.Image.Selection
{
    /// <summary>
    /// Represents a selection region in an image
    /// </summary>
    public class ImageSelection
    {
        public byte[] Mask { get; private set; } // 0-255 for partial selections
        public int Width { get; private set; }
        public int Height { get; private set; }
        public bool HasSelection { get; private set; }

        // Selection bounds for optimization
        public int MinX { get; private set; }
        public int MinY { get; private set; }
        public int MaxX { get; private set; }
        public int MaxY { get; private set; }

        public ImageSelection(int width, int height)
        {
            Width = width;
            Height = height;
            Mask = new byte[width * height];
            HasSelection = false;
        }

        public void Clear()
        {
            Array.Clear(Mask, 0, Mask.Length);
            HasSelection = false;
        }

        public void SelectAll()
        {
            for (int i = 0; i < Mask.Length; i++)
                Mask[i] = 255;

            HasSelection = true;
            MinX = 0;
            MinY = 0;
            MaxX = Width - 1;
            MaxY = Height - 1;
        }

        public void SetRectangle(int x, int y, int width, int height, bool additive = false)
        {
            if (!additive)
                Clear();

            for (int py = y; py < y + height; py++)
            {
                for (int px = x; px < x + width; px++)
                {
                    if (px >= 0 && px < Width && py >= 0 && py < Height)
                    {
                        Mask[py * Width + px] = 255;
                    }
                }
            }

            UpdateBounds();
            HasSelection = true;
        }

        public void SetEllipse(int cx, int cy, int rx, int ry, bool additive = false)
        {
            if (!additive)
                Clear();

            for (int y = cy - ry; y <= cy + ry; y++)
            {
                for (int x = cx - rx; x <= cx + rx; x++)
                {
                    if (x >= 0 && x < Width && y >= 0 && y < Height)
                    {
                        float dx = (x - cx) / (float)rx;
                        float dy = (y - cy) / (float)ry;

                        if (dx * dx + dy * dy <= 1.0f)
                        {
                            Mask[y * Width + x] = 255;
                        }
                    }
                }
            }

            UpdateBounds();
            HasSelection = true;
        }

        public void SetPolygon(List<Vector2> points, bool additive = false)
        {
            if (!additive)
                Clear();

            if (points.Count < 3) return;

            // Find bounding box
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            foreach (var pt in points)
            {
                minX = Math.Min(minX, pt.X);
                minY = Math.Min(minY, pt.Y);
                maxX = Math.Max(maxX, pt.X);
                maxY = Math.Max(maxY, pt.Y);
            }

            // Scan pixels in bounding box
            for (int y = (int)minY; y <= (int)maxY; y++)
            {
                for (int x = (int)minX; x <= (int)maxX; x++)
                {
                    if (x >= 0 && x < Width && y >= 0 && y < Height)
                    {
                        if (IsPointInPolygon(new Vector2(x, y), points))
                        {
                            Mask[y * Width + x] = 255;
                        }
                    }
                }
            }

            UpdateBounds();
            HasSelection = true;
        }

        private bool IsPointInPolygon(Vector2 point, List<Vector2> polygon)
        {
            bool inside = false;
            int j = polygon.Count - 1;

            for (int i = 0; i < polygon.Count; i++)
            {
                if ((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y) &&
                    point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) /
                    (polygon[j].Y - polygon[i].Y) + polygon[i].X)
                {
                    inside = !inside;
                }

                j = i;
            }

            return inside;
        }

        public void Invert()
        {
            for (int i = 0; i < Mask.Length; i++)
                Mask[i] = (byte)(255 - Mask[i]);

            UpdateBounds();
            HasSelection = true;
        }

        public void Feather(int radius)
        {
            if (radius <= 0) return;

            byte[] feathered = new byte[Width * Height];

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int sum = 0;
                    int count = 0;

                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int nx = x + dx;
                            int ny = y + dy;

                            if (nx >= 0 && nx < Width && ny >= 0 && ny < Height)
                            {
                                sum += Mask[ny * Width + nx];
                                count++;
                            }
                        }
                    }

                    feathered[y * Width + x] = (byte)(sum / count);
                }
            }

            Mask = feathered;
        }

        public void Expand(int pixels)
        {
            if (pixels <= 0) return;

            byte[] expanded = new byte[Width * Height];
            Array.Copy(Mask, expanded, Mask.Length);

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (Mask[y * Width + x] > 0)
                    {
                        // Expand around selected pixels
                        for (int dy = -pixels; dy <= pixels; dy++)
                        {
                            for (int dx = -pixels; dx <= pixels; dx++)
                            {
                                int nx = x + dx;
                                int ny = y + dy;

                                if (nx >= 0 && nx < Width && ny >= 0 && ny < Height)
                                {
                                    expanded[ny * Width + nx] = Math.Max(expanded[ny * Width + nx], Mask[y * Width + x]);
                                }
                            }
                        }
                    }
                }
            }

            Mask = expanded;
            UpdateBounds();
        }

        public void Contract(int pixels)
        {
            if (pixels <= 0) return;

            byte[] contracted = new byte[Width * Height];

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (Mask[y * Width + x] > 0)
                    {
                        bool isEdge = false;

                        // Check if any neighbor is unselected
                        for (int dy = -pixels; dy <= pixels && !isEdge; dy++)
                        {
                            for (int dx = -pixels; dx <= pixels && !isEdge; dx++)
                            {
                                int nx = x + dx;
                                int ny = y + dy;

                                if (nx >= 0 && nx < Width && ny >= 0 && ny < Height)
                                {
                                    if (Mask[ny * Width + nx] == 0)
                                        isEdge = true;
                                }
                                else
                                {
                                    isEdge = true;
                                }
                            }
                        }

                        if (!isEdge)
                            contracted[y * Width + x] = Mask[y * Width + x];
                    }
                }
            }

            Mask = contracted;
            UpdateBounds();
        }

        public void UpdateBounds()
        {
            MinX = Width;
            MinY = Height;
            MaxX = -1;
            MaxY = -1;

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (Mask[y * Width + x] > 0)
                    {
                        MinX = Math.Min(MinX, x);
                        MinY = Math.Min(MinY, y);
                        MaxX = Math.Max(MaxX, x);
                        MaxY = Math.Max(MaxY, y);
                    }
                }
            }

            if (MaxX < 0)
            {
                MinX = MinY = MaxX = MaxY = 0;
                HasSelection = false;
            }
        }

        public ImageSelection Clone()
        {
            var clone = new ImageSelection(Width, Height);
            Array.Copy(Mask, clone.Mask, Mask.Length);
            clone.HasSelection = HasSelection;
            clone.MinX = MinX;
            clone.MinY = MinY;
            clone.MaxX = MaxX;
            clone.MaxY = MaxY;
            return clone;
        }
    }
}
