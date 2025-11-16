// GeoscientistToolkit/Data/Image/Selection/ImageSelectionTools.cs

using System;
using System.Collections.Generic;
using System.Numerics;

namespace GeoscientistToolkit.Data.Image.Selection
{
    /// <summary>
    /// Advanced selection tools including magic wand, color range, and SAM integration
    /// </summary>
    public static class ImageSelectionTools
    {
        /// <summary>
        /// Magic wand selection - selects contiguous region based on color tolerance
        /// </summary>
        public static void MagicWand(ImageSelection selection, byte[] imageData,
            int width, int height, int startX, int startY, int tolerance = 32,
            bool antiAlias = true, bool additive = false)
        {
            if (!additive)
                selection.Clear();

            if (startX < 0 || startX >= width || startY < 0 || startY >= height)
                return;

            int startIdx = (startY * width + startX) * 4;
            byte targetR = imageData[startIdx];
            byte targetG = imageData[startIdx + 1];
            byte targetB = imageData[startIdx + 2];

            bool[,] selected = new bool[height, width];
            Queue<(int x, int y)> queue = new Queue<(int, int)>();
            queue.Enqueue((startX, startY));

            // Flood fill to find matching pixels
            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();

                if (x < 0 || x >= width || y < 0 || y >= height)
                    continue;

                if (selected[y, x])
                    continue;

                int idx = (y * width + x) * 4;

                // Check if pixel matches target color within tolerance
                int dr = Math.Abs(imageData[idx] - targetR);
                int dg = Math.Abs(imageData[idx + 1] - targetG);
                int db = Math.Abs(imageData[idx + 2] - targetB);

                if (dr > tolerance || dg > tolerance || db > tolerance)
                    continue;

                selected[y, x] = true;

                // Add neighbors
                queue.Enqueue((x + 1, y));
                queue.Enqueue((x - 1, y));
                queue.Enqueue((x, y + 1));
                queue.Enqueue((x, y - 1));
            }

            // Apply to selection mask
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (selected[y, x])
                    {
                        selection.Mask[y * width + x] = 255;
                    }
                }
            }

            // Anti-alias edges
            if (antiAlias)
            {
                AntiAliasSelection(selection, imageData, width, height, targetR, targetG, targetB, tolerance);
            }

            selection.UpdateBounds();
            selection.HasSelection = true;
        }

        /// <summary>
        /// Select by color range across entire image
        /// </summary>
        public static void SelectByColorRange(ImageSelection selection, byte[] imageData,
            int width, int height, byte r, byte g, byte b, int tolerance = 32,
            bool additive = false)
        {
            if (!additive)
                selection.Clear();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = (y * width + x) * 4;

                    int dr = Math.Abs(imageData[idx] - r);
                    int dg = Math.Abs(imageData[idx + 1] - g);
                    int db = Math.Abs(imageData[idx + 2] - b);

                    if (dr <= tolerance && dg <= tolerance && db <= tolerance)
                    {
                        selection.Mask[y * width + x] = 255;
                    }
                }
            }

            selection.UpdateBounds();
            selection.HasSelection = true;
        }

        /// <summary>
        /// Select by luminance range
        /// </summary>
        public static void SelectByLuminance(ImageSelection selection, byte[] imageData,
            int width, int height, float minLuminance, float maxLuminance,
            bool additive = false)
        {
            if (!additive)
                selection.Clear();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = (y * width + x) * 4;

                    float r = imageData[idx] / 255.0f;
                    float g = imageData[idx + 1] / 255.0f;
                    float b = imageData[idx + 2] / 255.0f;

                    // Calculate luminance (Rec. 709)
                    float luminance = 0.2126f * r + 0.7152f * g + 0.0722f * b;

                    if (luminance >= minLuminance && luminance <= maxLuminance)
                    {
                        selection.Mask[y * width + x] = 255;
                    }
                }
            }

            selection.UpdateBounds();
            selection.HasSelection = true;
        }

        /// <summary>
        /// Select by alpha channel
        /// </summary>
        public static void SelectByAlpha(ImageSelection selection, byte[] imageData,
            int width, int height, byte minAlpha, byte maxAlpha,
            bool additive = false)
        {
            if (!additive)
                selection.Clear();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = (y * width + x) * 4;
                    byte alpha = imageData[idx + 3];

                    if (alpha >= minAlpha && alpha <= maxAlpha)
                    {
                        selection.Mask[y * width + x] = 255;
                    }
                }
            }

            selection.UpdateBounds();
            selection.HasSelection = true;
        }

        /// <summary>
        /// Grow selection by specified pixels
        /// </summary>
        public static void Grow(ImageSelection selection, int pixels)
        {
            if (pixels <= 0 || !selection.HasSelection) return;

            byte[] newMask = new byte[selection.Mask.Length];
            Array.Copy(selection.Mask, newMask, selection.Mask.Length);

            for (int y = 0; y < selection.Height; y++)
            {
                for (int x = 0; x < selection.Width; x++)
                {
                    int idx = y * selection.Width + x;

                    if (selection.Mask[idx] > 0)
                    {
                        // Grow around selected pixels
                        for (int dy = -pixels; dy <= pixels; dy++)
                        {
                            for (int dx = -pixels; dx <= pixels; dx++)
                            {
                                int nx = x + dx;
                                int ny = y + dy;

                                if (nx >= 0 && nx < selection.Width && ny >= 0 && ny < selection.Height)
                                {
                                    int nIdx = ny * selection.Width + nx;
                                    float dist = MathF.Sqrt(dx * dx + dy * dy);

                                    if (dist <= pixels)
                                    {
                                        byte value = (byte)(255 * (1 - dist / pixels));
                                        newMask[nIdx] = Math.Max(newMask[nIdx], value);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            selection.Mask = newMask;
            selection.UpdateBounds();
        }

        /// <summary>
        /// Shrink selection by specified pixels
        /// </summary>
        public static void Shrink(ImageSelection selection, int pixels)
        {
            if (pixels <= 0 || !selection.HasSelection) return;

            byte[] newMask = new byte[selection.Mask.Length];

            for (int y = 0; y < selection.Height; y++)
            {
                for (int x = 0; x < selection.Width; x++)
                {
                    int idx = y * selection.Width + x;

                    if (selection.Mask[idx] > 0)
                    {
                        bool isEdge = false;

                        // Check if any neighbor within distance is unselected
                        for (int dy = -pixels; dy <= pixels && !isEdge; dy++)
                        {
                            for (int dx = -pixels; dx <= pixels && !isEdge; dx++)
                            {
                                int nx = x + dx;
                                int ny = y + dy;

                                if (nx < 0 || nx >= selection.Width || ny < 0 || ny >= selection.Height)
                                {
                                    isEdge = true;
                                }
                                else
                                {
                                    int nIdx = ny * selection.Width + nx;
                                    if (selection.Mask[nIdx] == 0)
                                    {
                                        float dist = MathF.Sqrt(dx * dx + dy * dy);
                                        if (dist <= pixels)
                                            isEdge = true;
                                    }
                                }
                            }
                        }

                        if (!isEdge)
                            newMask[idx] = selection.Mask[idx];
                    }
                }
            }

            selection.Mask = newMask;
            selection.UpdateBounds();
        }

        /// <summary>
        /// Border selection - creates border of specified width
        /// </summary>
        public static void Border(ImageSelection selection, int width)
        {
            if (width <= 0 || !selection.HasSelection) return;

            var original = selection.Clone();
            Grow(selection, width);

            var shrunk = selection.Clone();
            Shrink(shrunk, width);

            // Border = grown - shrunk
            for (int i = 0; i < selection.Mask.Length; i++)
            {
                if (shrunk.Mask[i] > 0)
                    selection.Mask[i] = 0;
            }

            selection.UpdateBounds();
        }

        /// <summary>
        /// Smooth selection edges
        /// </summary>
        public static void Smooth(ImageSelection selection, int radius = 2)
        {
            if (radius <= 0 || !selection.HasSelection) return;

            byte[] smoothed = new byte[selection.Mask.Length];

            for (int y = 0; y < selection.Height; y++)
            {
                for (int x = 0; x < selection.Width; x++)
                {
                    int sum = 0;
                    int count = 0;

                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int nx = x + dx;
                            int ny = y + dy;

                            if (nx >= 0 && nx < selection.Width && ny >= 0 && ny < selection.Height)
                            {
                                sum += selection.Mask[ny * selection.Width + nx];
                                count++;
                            }
                        }
                    }

                    smoothed[y * selection.Width + x] = (byte)(sum / count);
                }
            }

            selection.Mask = smoothed;
        }

        /// <summary>
        /// Create selection from SAM mask
        /// </summary>
        public static void FromSAMMask(ImageSelection selection, byte[,] samMask, bool additive = false)
        {
            if (!additive)
                selection.Clear();

            int height = samMask.GetLength(0);
            int width = samMask.GetLength(1);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    byte value = samMask[y, x];

                    if (additive)
                        selection.Mask[idx] = Math.Max(selection.Mask[idx], value);
                    else
                        selection.Mask[idx] = value;
                }
            }

            selection.UpdateBounds();
            selection.HasSelection = true;
        }

        /// <summary>
        /// Convert selection to SAM mask format
        /// </summary>
        public static byte[,] ToSAMMask(ImageSelection selection)
        {
            byte[,] mask = new byte[selection.Height, selection.Width];

            for (int y = 0; y < selection.Height; y++)
            {
                for (int x = 0; x < selection.Width; x++)
                {
                    mask[y, x] = selection.Mask[y * selection.Width + x];
                }
            }

            return mask;
        }

        /// <summary>
        /// Combine two selections
        /// </summary>
        public static void Combine(ImageSelection target, ImageSelection source, CombineMode mode)
        {
            for (int i = 0; i < target.Mask.Length; i++)
            {
                switch (mode)
                {
                    case CombineMode.Add:
                        target.Mask[i] = Math.Max(target.Mask[i], source.Mask[i]);
                        break;

                    case CombineMode.Subtract:
                        target.Mask[i] = (byte)Math.Max(0, target.Mask[i] - source.Mask[i]);
                        break;

                    case CombineMode.Intersect:
                        target.Mask[i] = Math.Min(target.Mask[i], source.Mask[i]);
                        break;

                    case CombineMode.Xor:
                        target.Mask[i] = (byte)(target.Mask[i] > 0 && source.Mask[i] == 0 ||
                                                target.Mask[i] == 0 && source.Mask[i] > 0 ? 255 : 0);
                        break;
                }
            }

            target.UpdateBounds();
            target.HasSelection = target.Mask.Any(m => m > 0);
        }

        public enum CombineMode
        {
            Add,
            Subtract,
            Intersect,
            Xor
        }

        #region Helper Methods

        private static void AntiAliasSelection(ImageSelection selection, byte[] imageData,
            int width, int height, byte targetR, byte targetG, byte targetB, int tolerance)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;

                    if (selection.Mask[idx] > 0 && selection.Mask[idx] < 255)
                        continue; // Already anti-aliased

                    // Check if this is an edge pixel
                    bool hasSelected = false;
                    bool hasUnselected = false;

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx;
                            int ny = y + dy;

                            if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                            {
                                int nIdx = ny * width + nx;
                                if (selection.Mask[nIdx] > 0)
                                    hasSelected = true;
                                else
                                    hasUnselected = true;
                            }
                        }
                    }

                    // If this is an edge, calculate partial selection
                    if (hasSelected && hasUnselected)
                    {
                        int pixIdx = (y * width + x) * 4;
                        int dr = Math.Abs(imageData[pixIdx] - targetR);
                        int dg = Math.Abs(imageData[pixIdx + 1] - targetG);
                        int db = Math.Abs(imageData[pixIdx + 2] - targetB);

                        float colorDist = MathF.Sqrt(dr * dr + dg * dg + db * db);
                        float maxDist = MathF.Sqrt(3 * tolerance * tolerance);

                        if (colorDist < maxDist)
                        {
                            float alpha = 1 - (colorDist / maxDist);
                            selection.Mask[idx] = (byte)(alpha * 255);
                        }
                    }
                }
            }
        }

        #endregion
    }
}
