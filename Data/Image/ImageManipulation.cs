// GeoscientistToolkit/Data/Image/ImageManipulation.cs

using System;

namespace GeoscientistToolkit.Data.Image
{
    /// <summary>
    /// Image manipulation utilities for resizing, cropping, rotating, and flipping
    /// </summary>
    public static class ImageManipulation
    {
        public enum InterpolationMode
        {
            NearestNeighbor,
            Bilinear,
            Bicubic
        }

        public enum FlipMode
        {
            Horizontal,
            Vertical,
            Both
        }

        /// <summary>
        /// Resize image with specified interpolation
        /// </summary>
        public static byte[] Resize(byte[] sourceData, int srcWidth, int srcHeight,
            int dstWidth, int dstHeight, InterpolationMode mode = InterpolationMode.Bilinear)
        {
            if (srcWidth <= 0 || srcHeight <= 0 || dstWidth <= 0 || dstHeight <= 0)
                throw new ArgumentException("Invalid dimensions");

            byte[] result = new byte[dstWidth * dstHeight * 4];

            switch (mode)
            {
                case InterpolationMode.NearestNeighbor:
                    ResizeNearestNeighbor(sourceData, srcWidth, srcHeight, result, dstWidth, dstHeight);
                    break;
                case InterpolationMode.Bilinear:
                    ResizeBilinear(sourceData, srcWidth, srcHeight, result, dstWidth, dstHeight);
                    break;
                case InterpolationMode.Bicubic:
                    ResizeBicubic(sourceData, srcWidth, srcHeight, result, dstWidth, dstHeight);
                    break;
            }

            return result;
        }

        /// <summary>
        /// Crop image to specified region
        /// </summary>
        public static byte[] Crop(byte[] sourceData, int srcWidth, int srcHeight,
            int x, int y, int cropWidth, int cropHeight)
        {
            if (x < 0 || y < 0 || cropWidth <= 0 || cropHeight <= 0)
                throw new ArgumentException("Invalid crop bounds");

            // Clamp to source bounds
            int x1 = Math.Max(0, x);
            int y1 = Math.Max(0, y);
            int x2 = Math.Min(srcWidth, x + cropWidth);
            int y2 = Math.Min(srcHeight, y + cropHeight);

            int actualWidth = x2 - x1;
            int actualHeight = y2 - y1;

            byte[] result = new byte[actualWidth * actualHeight * 4];

            for (int dy = 0; dy < actualHeight; dy++)
            {
                for (int dx = 0; dx < actualWidth; dx++)
                {
                    int srcX = x1 + dx;
                    int srcY = y1 + dy;
                    int srcIdx = (srcY * srcWidth + srcX) * 4;
                    int dstIdx = (dy * actualWidth + dx) * 4;

                    result[dstIdx] = sourceData[srcIdx];
                    result[dstIdx + 1] = sourceData[srcIdx + 1];
                    result[dstIdx + 2] = sourceData[srcIdx + 2];
                    result[dstIdx + 3] = sourceData[srcIdx + 3];
                }
            }

            return result;
        }

        /// <summary>
        /// Rotate image by specified angle in degrees (clockwise)
        /// </summary>
        public static (byte[] data, int width, int height) Rotate(byte[] sourceData,
            int srcWidth, int srcHeight, double angleDegrees,
            InterpolationMode mode = InterpolationMode.Bilinear)
        {
            // Normalize angle to [0, 360)
            angleDegrees = angleDegrees % 360;
            if (angleDegrees < 0) angleDegrees += 360;

            // Handle cardinal rotations efficiently
            if (Math.Abs(angleDegrees - 0) < 0.001)
                return ((byte[])sourceData.Clone(), srcWidth, srcHeight);
            if (Math.Abs(angleDegrees - 90) < 0.001)
                return Rotate90(sourceData, srcWidth, srcHeight);
            if (Math.Abs(angleDegrees - 180) < 0.001)
                return Rotate180(sourceData, srcWidth, srcHeight);
            if (Math.Abs(angleDegrees - 270) < 0.001)
                return Rotate270(sourceData, srcWidth, srcHeight);

            // Arbitrary angle rotation
            return RotateArbitrary(sourceData, srcWidth, srcHeight, angleDegrees, mode);
        }

        /// <summary>
        /// Flip image horizontally, vertically, or both
        /// </summary>
        public static byte[] Flip(byte[] sourceData, int width, int height, FlipMode mode)
        {
            byte[] result = new byte[width * height * 4];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int srcX = mode == FlipMode.Horizontal || mode == FlipMode.Both ? width - 1 - x : x;
                    int srcY = mode == FlipMode.Vertical || mode == FlipMode.Both ? height - 1 - y : y;

                    int srcIdx = (srcY * width + srcX) * 4;
                    int dstIdx = (y * width + x) * 4;

                    result[dstIdx] = sourceData[srcIdx];
                    result[dstIdx + 1] = sourceData[srcIdx + 1];
                    result[dstIdx + 2] = sourceData[srcIdx + 2];
                    result[dstIdx + 3] = sourceData[srcIdx + 3];
                }
            }

            return result;
        }

        #region Resize Implementations

        private static void ResizeNearestNeighbor(byte[] src, int srcW, int srcH,
            byte[] dst, int dstW, int dstH)
        {
            float xRatio = (float)srcW / dstW;
            float yRatio = (float)srcH / dstH;

            for (int y = 0; y < dstH; y++)
            {
                for (int x = 0; x < dstW; x++)
                {
                    int srcX = (int)(x * xRatio);
                    int srcY = (int)(y * yRatio);

                    int srcIdx = (srcY * srcW + srcX) * 4;
                    int dstIdx = (y * dstW + x) * 4;

                    dst[dstIdx] = src[srcIdx];
                    dst[dstIdx + 1] = src[srcIdx + 1];
                    dst[dstIdx + 2] = src[srcIdx + 2];
                    dst[dstIdx + 3] = src[srcIdx + 3];
                }
            }
        }

        private static void ResizeBilinear(byte[] src, int srcW, int srcH,
            byte[] dst, int dstW, int dstH)
        {
            float xRatio = (float)(srcW - 1) / dstW;
            float yRatio = (float)(srcH - 1) / dstH;

            for (int y = 0; y < dstH; y++)
            {
                for (int x = 0; x < dstW; x++)
                {
                    float srcX = x * xRatio;
                    float srcY = y * yRatio;

                    int x1 = (int)srcX;
                    int y1 = (int)srcY;
                    int x2 = Math.Min(x1 + 1, srcW - 1);
                    int y2 = Math.Min(y1 + 1, srcH - 1);

                    float fx = srcX - x1;
                    float fy = srcY - y1;

                    int dstIdx = (y * dstW + x) * 4;

                    for (int c = 0; c < 4; c++)
                    {
                        float p1 = src[(y1 * srcW + x1) * 4 + c];
                        float p2 = src[(y1 * srcW + x2) * 4 + c];
                        float p3 = src[(y2 * srcW + x1) * 4 + c];
                        float p4 = src[(y2 * srcW + x2) * 4 + c];

                        float val = p1 * (1 - fx) * (1 - fy) +
                                   p2 * fx * (1 - fy) +
                                   p3 * (1 - fx) * fy +
                                   p4 * fx * fy;

                        dst[dstIdx + c] = (byte)Math.Clamp(val, 0, 255);
                    }
                }
            }
        }

        private static void ResizeBicubic(byte[] src, int srcW, int srcH,
            byte[] dst, int dstW, int dstH)
        {
            float xRatio = (float)(srcW - 1) / dstW;
            float yRatio = (float)(srcH - 1) / dstH;

            for (int y = 0; y < dstH; y++)
            {
                for (int x = 0; x < dstW; x++)
                {
                    float srcX = x * xRatio;
                    float srcY = y * yRatio;

                    int x0 = (int)srcX;
                    int y0 = (int)srcY;
                    float fx = srcX - x0;
                    float fy = srcY - y0;

                    int dstIdx = (y * dstW + x) * 4;

                    for (int c = 0; c < 4; c++)
                    {
                        float sum = 0;
                        float weightSum = 0;

                        for (int dy = -1; dy <= 2; dy++)
                        {
                            for (int dx = -1; dx <= 2; dx++)
                            {
                                int px = Math.Clamp(x0 + dx, 0, srcW - 1);
                                int py = Math.Clamp(y0 + dy, 0, srcH - 1);

                                float wx = CubicWeight(fx - dx);
                                float wy = CubicWeight(fy - dy);
                                float weight = wx * wy;

                                sum += src[(py * srcW + px) * 4 + c] * weight;
                                weightSum += weight;
                            }
                        }

                        dst[dstIdx + c] = (byte)Math.Clamp(sum / weightSum, 0, 255);
                    }
                }
            }
        }

        private static float CubicWeight(float x)
        {
            x = Math.Abs(x);
            if (x <= 1)
                return 1.5f * x * x * x - 2.5f * x * x + 1;
            else if (x < 2)
                return -0.5f * x * x * x + 2.5f * x * x - 4 * x + 2;
            else
                return 0;
        }

        #endregion

        #region Rotation Implementations

        private static (byte[], int, int) Rotate90(byte[] src, int width, int height)
        {
            int newWidth = height;
            int newHeight = width;
            byte[] result = new byte[newWidth * newHeight * 4];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int srcIdx = (y * width + x) * 4;
                    int dstX = newWidth - 1 - y;
                    int dstY = x;
                    int dstIdx = (dstY * newWidth + dstX) * 4;

                    result[dstIdx] = src[srcIdx];
                    result[dstIdx + 1] = src[srcIdx + 1];
                    result[dstIdx + 2] = src[srcIdx + 2];
                    result[dstIdx + 3] = src[srcIdx + 3];
                }
            }

            return (result, newWidth, newHeight);
        }

        private static (byte[], int, int) Rotate180(byte[] src, int width, int height)
        {
            byte[] result = new byte[width * height * 4];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int srcIdx = (y * width + x) * 4;
                    int dstX = width - 1 - x;
                    int dstY = height - 1 - y;
                    int dstIdx = (dstY * width + dstX) * 4;

                    result[dstIdx] = src[srcIdx];
                    result[dstIdx + 1] = src[srcIdx + 1];
                    result[dstIdx + 2] = src[srcIdx + 2];
                    result[dstIdx + 3] = src[srcIdx + 3];
                }
            }

            return (result, width, height);
        }

        private static (byte[], int, int) Rotate270(byte[] src, int width, int height)
        {
            int newWidth = height;
            int newHeight = width;
            byte[] result = new byte[newWidth * newHeight * 4];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int srcIdx = (y * width + x) * 4;
                    int dstX = y;
                    int dstY = newHeight - 1 - x;
                    int dstIdx = (dstY * newWidth + dstX) * 4;

                    result[dstIdx] = src[srcIdx];
                    result[dstIdx + 1] = src[srcIdx + 1];
                    result[dstIdx + 2] = src[srcIdx + 2];
                    result[dstIdx + 3] = src[srcIdx + 3];
                }
            }

            return (result, newWidth, newHeight);
        }

        private static (byte[], int, int) RotateArbitrary(byte[] src, int srcW, int srcH,
            double angleDegrees, InterpolationMode mode)
        {
            double angleRad = angleDegrees * Math.PI / 180.0;
            double cos = Math.Cos(angleRad);
            double sin = Math.Sin(angleRad);

            // Calculate bounds of rotated image
            double[] cornersX = new double[] {
                0, srcW, srcW, 0
            };
            double[] cornersY = new double[] {
                0, 0, srcH, srcH
            };

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;

            for (int i = 0; i < 4; i++)
            {
                double rx = cornersX[i] * cos - cornersY[i] * sin;
                double ry = cornersX[i] * sin + cornersY[i] * cos;
                minX = Math.Min(minX, rx);
                maxX = Math.Max(maxX, rx);
                minY = Math.Min(minY, ry);
                maxY = Math.Max(maxY, ry);
            }

            int dstW = (int)Math.Ceiling(maxX - minX);
            int dstH = (int)Math.Ceiling(maxY - minY);
            double offsetX = -minX;
            double offsetY = -minY;

            byte[] result = new byte[dstW * dstH * 4];

            // Reverse rotation to sample from source
            double invCos = Math.Cos(-angleRad);
            double invSin = Math.Sin(-angleRad);

            double centerX = srcW / 2.0;
            double centerY = srcH / 2.0;
            double dstCenterX = dstW / 2.0;
            double dstCenterY = dstH / 2.0;

            for (int y = 0; y < dstH; y++)
            {
                for (int x = 0; x < dstW; x++)
                {
                    // Transform back to source coordinates
                    double dx = x - dstCenterX;
                    double dy = y - dstCenterY;

                    double srcX = dx * invCos - dy * invSin + centerX;
                    double srcY = dx * invSin + dy * invCos + centerY;

                    int dstIdx = (y * dstW + x) * 4;

                    if (srcX >= 0 && srcX < srcW - 1 && srcY >= 0 && srcY < srcH - 1)
                    {
                        if (mode == InterpolationMode.Bilinear || mode == InterpolationMode.Bicubic)
                        {
                            SampleBilinear(src, srcW, srcH, srcX, srcY, result, dstIdx);
                        }
                        else
                        {
                            int sx = (int)srcX;
                            int sy = (int)srcY;
                            int srcIdx = (sy * srcW + sx) * 4;

                            result[dstIdx] = src[srcIdx];
                            result[dstIdx + 1] = src[srcIdx + 1];
                            result[dstIdx + 2] = src[srcIdx + 2];
                            result[dstIdx + 3] = src[srcIdx + 3];
                        }
                    }
                }
            }

            return (result, dstW, dstH);
        }

        private static void SampleBilinear(byte[] src, int srcW, int srcH,
            double srcX, double srcY, byte[] dst, int dstIdx)
        {
            int x1 = (int)srcX;
            int y1 = (int)srcY;
            int x2 = Math.Min(x1 + 1, srcW - 1);
            int y2 = Math.Min(y1 + 1, srcH - 1);

            float fx = (float)(srcX - x1);
            float fy = (float)(srcY - y1);

            for (int c = 0; c < 4; c++)
            {
                float p1 = src[(y1 * srcW + x1) * 4 + c];
                float p2 = src[(y1 * srcW + x2) * 4 + c];
                float p3 = src[(y2 * srcW + x1) * 4 + c];
                float p4 = src[(y2 * srcW + x2) * 4 + c];

                float val = p1 * (1 - fx) * (1 - fy) +
                           p2 * fx * (1 - fy) +
                           p3 * (1 - fx) * fy +
                           p4 * fx * fy;

                dst[dstIdx + c] = (byte)Math.Clamp(val, 0, 255);
            }
        }

        #endregion
    }
}
