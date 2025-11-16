// GeoscientistToolkit/Data/Image/ColorSpace.cs

using System;
using System.Numerics;

namespace GeoscientistToolkit.Data.Image
{
    /// <summary>
    /// Comprehensive color space conversion utilities
    /// Supports RGB, HSV, HSL, LAB, CMYK, and Grayscale
    /// </summary>
    public static class ColorSpace
    {
        #region RGB Conversions

        /// <summary>
        /// Convert RGB (0-255) to HSV (H: 0-360, S: 0-1, V: 0-1)
        /// </summary>
        public static Vector3 RgbToHsv(byte r, byte g, byte b)
        {
            float rf = r / 255f;
            float gf = g / 255f;
            float bf = b / 255f;

            float max = Math.Max(rf, Math.Max(gf, bf));
            float min = Math.Min(rf, Math.Min(gf, bf));
            float delta = max - min;

            float h = 0;
            float s = max == 0 ? 0 : delta / max;
            float v = max;

            if (delta != 0)
            {
                if (max == rf)
                    h = 60f * (((gf - bf) / delta) % 6);
                else if (max == gf)
                    h = 60f * (((bf - rf) / delta) + 2);
                else
                    h = 60f * (((rf - gf) / delta) + 4);
            }

            if (h < 0) h += 360f;

            return new Vector3(h, s, v);
        }

        /// <summary>
        /// Convert HSV to RGB
        /// </summary>
        public static (byte r, byte g, byte b) HsvToRgb(float h, float s, float v)
        {
            float c = v * s;
            float x = c * (1 - Math.Abs((h / 60f) % 2 - 1));
            float m = v - c;

            float r, g, b;

            if (h < 60)
            {
                r = c; g = x; b = 0;
            }
            else if (h < 120)
            {
                r = x; g = c; b = 0;
            }
            else if (h < 180)
            {
                r = 0; g = c; b = x;
            }
            else if (h < 240)
            {
                r = 0; g = x; b = c;
            }
            else if (h < 300)
            {
                r = x; g = 0; b = c;
            }
            else
            {
                r = c; g = 0; b = x;
            }

            return (
                (byte)Math.Clamp((r + m) * 255, 0, 255),
                (byte)Math.Clamp((g + m) * 255, 0, 255),
                (byte)Math.Clamp((b + m) * 255, 0, 255)
            );
        }

        /// <summary>
        /// Convert RGB to HSL (H: 0-360, S: 0-1, L: 0-1)
        /// </summary>
        public static Vector3 RgbToHsl(byte r, byte g, byte b)
        {
            float rf = r / 255f;
            float gf = g / 255f;
            float bf = b / 255f;

            float max = Math.Max(rf, Math.Max(gf, bf));
            float min = Math.Min(rf, Math.Min(gf, bf));
            float delta = max - min;

            float h = 0;
            float l = (max + min) / 2f;
            float s = 0;

            if (delta != 0)
            {
                s = l > 0.5f ? delta / (2f - max - min) : delta / (max + min);

                if (max == rf)
                    h = 60f * (((gf - bf) / delta) % 6);
                else if (max == gf)
                    h = 60f * (((bf - rf) / delta) + 2);
                else
                    h = 60f * (((rf - gf) / delta) + 4);
            }

            if (h < 0) h += 360f;

            return new Vector3(h, s, l);
        }

        /// <summary>
        /// Convert HSL to RGB
        /// </summary>
        public static (byte r, byte g, byte b) HslToRgb(float h, float s, float l)
        {
            float c = (1 - Math.Abs(2 * l - 1)) * s;
            float x = c * (1 - Math.Abs((h / 60f) % 2 - 1));
            float m = l - c / 2f;

            float r, g, b;

            if (h < 60)
            {
                r = c; g = x; b = 0;
            }
            else if (h < 120)
            {
                r = x; g = c; b = 0;
            }
            else if (h < 180)
            {
                r = 0; g = c; b = x;
            }
            else if (h < 240)
            {
                r = 0; g = x; b = c;
            }
            else if (h < 300)
            {
                r = x; g = 0; b = c;
            }
            else
            {
                r = c; g = 0; b = x;
            }

            return (
                (byte)Math.Clamp((r + m) * 255, 0, 255),
                (byte)Math.Clamp((g + m) * 255, 0, 255),
                (byte)Math.Clamp((b + m) * 255, 0, 255)
            );
        }

        /// <summary>
        /// Convert RGB to LAB color space
        /// </summary>
        public static Vector3 RgbToLab(byte r, byte g, byte b)
        {
            // First convert to XYZ
            var xyz = RgbToXyz(r, g, b);

            // Then XYZ to LAB
            return XyzToLab(xyz.X, xyz.Y, xyz.Z);
        }

        /// <summary>
        /// Convert LAB to RGB
        /// </summary>
        public static (byte r, byte g, byte b) LabToRgb(float l, float a, float b)
        {
            // First LAB to XYZ
            var xyz = LabToXyz(l, a, b);

            // Then XYZ to RGB
            return XyzToRgb(xyz.X, xyz.Y, xyz.Z);
        }

        /// <summary>
        /// Convert RGB to XYZ (intermediate for LAB conversion)
        /// </summary>
        private static Vector3 RgbToXyz(byte r, byte g, byte b)
        {
            float rf = r / 255f;
            float gf = g / 255f;
            float bf = b / 255f;

            // Gamma correction
            rf = rf > 0.04045f ? MathF.Pow((rf + 0.055f) / 1.055f, 2.4f) : rf / 12.92f;
            gf = gf > 0.04045f ? MathF.Pow((gf + 0.055f) / 1.055f, 2.4f) : gf / 12.92f;
            bf = bf > 0.04045f ? MathF.Pow((bf + 0.055f) / 1.055f, 2.4f) : bf / 12.92f;

            rf *= 100f;
            gf *= 100f;
            bf *= 100f;

            // Observer = 2°, Illuminant = D65
            float x = rf * 0.4124f + gf * 0.3576f + bf * 0.1805f;
            float y = rf * 0.2126f + gf * 0.7152f + bf * 0.0722f;
            float z = rf * 0.0193f + gf * 0.1192f + bf * 0.9505f;

            return new Vector3(x, y, z);
        }

        /// <summary>
        /// Convert XYZ to RGB
        /// </summary>
        private static (byte r, byte g, byte b) XyzToRgb(float x, float y, float z)
        {
            x /= 100f;
            y /= 100f;
            z /= 100f;

            float r = x * 3.2406f + y * -1.5372f + z * -0.4986f;
            float g = x * -0.9689f + y * 1.8758f + z * 0.0415f;
            float b = x * 0.0557f + y * -0.2040f + z * 1.0570f;

            // Reverse gamma correction
            r = r > 0.0031308f ? 1.055f * MathF.Pow(r, 1f / 2.4f) - 0.055f : 12.92f * r;
            g = g > 0.0031308f ? 1.055f * MathF.Pow(g, 1f / 2.4f) - 0.055f : 12.92f * g;
            b = b > 0.0031308f ? 1.055f * MathF.Pow(b, 1f / 2.4f) - 0.055f : 12.92f * b;

            return (
                (byte)Math.Clamp(r * 255, 0, 255),
                (byte)Math.Clamp(g * 255, 0, 255),
                (byte)Math.Clamp(b * 255, 0, 255)
            );
        }

        /// <summary>
        /// Convert XYZ to LAB
        /// </summary>
        private static Vector3 XyzToLab(float x, float y, float z)
        {
            // D65 reference white point
            const float refX = 95.047f;
            const float refY = 100.000f;
            const float refZ = 108.883f;

            x /= refX;
            y /= refY;
            z /= refZ;

            x = x > 0.008856f ? MathF.Pow(x, 1f / 3f) : (7.787f * x) + (16f / 116f);
            y = y > 0.008856f ? MathF.Pow(y, 1f / 3f) : (7.787f * y) + (16f / 116f);
            z = z > 0.008856f ? MathF.Pow(z, 1f / 3f) : (7.787f * z) + (16f / 116f);

            float l = (116f * y) - 16f;
            float a = 500f * (x - y);
            float b = 200f * (y - z);

            return new Vector3(l, a, b);
        }

        /// <summary>
        /// Convert LAB to XYZ
        /// </summary>
        private static Vector3 LabToXyz(float l, float a, float b)
        {
            float y = (l + 16f) / 116f;
            float x = a / 500f + y;
            float z = y - b / 200f;

            float y3 = y * y * y;
            float x3 = x * x * x;
            float z3 = z * z * z;

            y = y3 > 0.008856f ? y3 : (y - 16f / 116f) / 7.787f;
            x = x3 > 0.008856f ? x3 : (x - 16f / 116f) / 7.787f;
            z = z3 > 0.008856f ? z3 : (z - 16f / 116f) / 7.787f;

            // D65 reference white point
            const float refX = 95.047f;
            const float refY = 100.000f;
            const float refZ = 108.883f;

            x *= refX;
            y *= refY;
            z *= refZ;

            return new Vector3(x, y, z);
        }

        /// <summary>
        /// Convert RGB to CMYK (C, M, Y, K: 0-1)
        /// </summary>
        public static Vector4 RgbToCmyk(byte r, byte g, byte b)
        {
            float rf = r / 255f;
            float gf = g / 255f;
            float bf = b / 255f;

            float k = 1f - Math.Max(rf, Math.Max(gf, bf));

            if (k == 1f)
                return new Vector4(0, 0, 0, 1);

            float c = (1f - rf - k) / (1f - k);
            float m = (1f - gf - k) / (1f - k);
            float y = (1f - bf - k) / (1f - k);

            return new Vector4(c, m, y, k);
        }

        /// <summary>
        /// Convert CMYK to RGB
        /// </summary>
        public static (byte r, byte g, byte b) CmykToRgb(float c, float m, float y, float k)
        {
            float r = (1f - c) * (1f - k);
            float g = (1f - m) * (1f - k);
            float b = (1f - y) * (1f - k);

            return (
                (byte)Math.Clamp(r * 255, 0, 255),
                (byte)Math.Clamp(g * 255, 0, 255),
                (byte)Math.Clamp(b * 255, 0, 255)
            );
        }

        /// <summary>
        /// Convert RGB to Grayscale using luminance formula
        /// </summary>
        public static byte RgbToGrayscale(byte r, byte g, byte b)
        {
            return (byte)(0.299f * r + 0.587f * g + 0.114f * b);
        }

        #endregion

        #region Bulk Conversion Operations

        /// <summary>
        /// Convert entire image data to HSV color space
        /// </summary>
        public static float[] ConvertToHsv(byte[] rgbaData, int width, int height)
        {
            float[] hsvData = new float[width * height * 3];

            for (int i = 0; i < width * height; i++)
            {
                int rgbaIdx = i * 4;
                int hsvIdx = i * 3;

                var hsv = RgbToHsv(rgbaData[rgbaIdx], rgbaData[rgbaIdx + 1], rgbaData[rgbaIdx + 2]);
                hsvData[hsvIdx] = hsv.X;
                hsvData[hsvIdx + 1] = hsv.Y;
                hsvData[hsvIdx + 2] = hsv.Z;
            }

            return hsvData;
        }

        /// <summary>
        /// Convert HSV data back to RGBA
        /// </summary>
        public static byte[] ConvertFromHsv(float[] hsvData, int width, int height, byte[] originalAlpha = null)
        {
            byte[] rgbaData = new byte[width * height * 4];

            for (int i = 0; i < width * height; i++)
            {
                int rgbaIdx = i * 4;
                int hsvIdx = i * 3;

                var (r, g, b) = HsvToRgb(hsvData[hsvIdx], hsvData[hsvIdx + 1], hsvData[hsvIdx + 2]);
                rgbaData[rgbaIdx] = r;
                rgbaData[rgbaIdx + 1] = g;
                rgbaData[rgbaIdx + 2] = b;
                rgbaData[rgbaIdx + 3] = originalAlpha != null ? originalAlpha[i] : (byte)255;
            }

            return rgbaData;
        }

        /// <summary>
        /// Convert entire image data to grayscale
        /// </summary>
        public static byte[] ConvertToGrayscale(byte[] rgbaData, int width, int height)
        {
            byte[] grayData = new byte[width * height * 4];

            for (int i = 0; i < width * height; i++)
            {
                int idx = i * 4;
                byte gray = RgbToGrayscale(rgbaData[idx], rgbaData[idx + 1], rgbaData[idx + 2]);

                grayData[idx] = gray;
                grayData[idx + 1] = gray;
                grayData[idx + 2] = gray;
                grayData[idx + 3] = rgbaData[idx + 3]; // Preserve alpha
            }

            return grayData;
        }

        #endregion
    }
}
