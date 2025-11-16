// GeoscientistToolkit/Data/Image/ImageLayer.cs

using System;
using System.Numerics;

namespace GeoscientistToolkit.Data.Image
{
    /// <summary>
    /// Represents a single layer in a layered image
    /// </summary>
    public class ImageLayer : IDisposable
    {
        public string Name { get; set; }
        public byte[] Data { get; set; } // RGBA format
        public int Width { get; private set; }
        public int Height { get; private set; }
        public float Opacity { get; set; } = 1.0f;
        public BlendMode BlendMode { get; set; } = BlendMode.Normal;
        public bool Visible { get; set; } = true;
        public bool Locked { get; set; } = false;

        // Layer transformations
        public int OffsetX { get; set; } = 0;
        public int OffsetY { get; set; } = 0;

        public ImageLayer(string name, int width, int height)
        {
            Name = name;
            Width = width;
            Height = height;
            Data = new byte[width * height * 4];
        }

        public ImageLayer(string name, byte[] data, int width, int height)
        {
            Name = name;
            Width = width;
            Height = height;
            Data = (byte[])data.Clone();
        }

        public void Clear()
        {
            Array.Clear(Data, 0, Data.Length);
        }

        public void Fill(byte r, byte g, byte b, byte a = 255)
        {
            for (int i = 0; i < Width * Height; i++)
            {
                int idx = i * 4;
                Data[idx] = r;
                Data[idx + 1] = g;
                Data[idx + 2] = b;
                Data[idx + 3] = a;
            }
        }

        public ImageLayer Clone()
        {
            var clone = new ImageLayer(Name + " Copy", Width, Height)
            {
                Opacity = Opacity,
                BlendMode = BlendMode,
                Visible = Visible,
                Locked = Locked,
                OffsetX = OffsetX,
                OffsetY = OffsetY
            };

            Array.Copy(Data, clone.Data, Data.Length);

            return clone;
        }

        public void Dispose()
        {
            Data = null;
        }
    }

    /// <summary>
    /// Layer blending modes
    /// </summary>
    public enum BlendMode
    {
        Normal,
        Multiply,
        Screen,
        Overlay,
        HardLight,
        SoftLight,
        Darken,
        Lighten,
        ColorDodge,
        ColorBurn,
        LinearDodge,
        LinearBurn,
        Difference,
        Exclusion,
        Hue,
        Saturation,
        Color,
        Luminosity
    }

    /// <summary>
    /// Layer blending operations
    /// </summary>
    public static class LayerBlending
    {
        /// <summary>
        /// Composite two layers using blend mode and opacity
        /// </summary>
        public static byte[] Blend(ImageLayer bottom, ImageLayer top)
        {
            if (!top.Visible || top.Opacity <= 0)
                return (byte[])bottom.Data.Clone();

            int width = bottom.Width;
            int height = bottom.Height;
            byte[] result = new byte[width * height * 4];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int topX = x - top.OffsetX;
                    int topY = y - top.OffsetY;

                    int bottomIdx = (y * width + x) * 4;

                    // Check if top layer pixel is in bounds
                    if (topX >= 0 && topX < top.Width && topY >= 0 && topY < top.Height)
                    {
                        int topIdx = (topY * top.Width + topX) * 4;

                        byte topR = top.Data[topIdx];
                        byte topG = top.Data[topIdx + 1];
                        byte topB = top.Data[topIdx + 2];
                        byte topA = top.Data[topIdx + 3];

                        byte bottomR = bottom.Data[bottomIdx];
                        byte bottomG = bottom.Data[bottomIdx + 1];
                        byte bottomB = bottom.Data[bottomIdx + 2];
                        byte bottomA = bottom.Data[bottomIdx + 3];

                        // Apply blend mode
                        var (blendR, blendG, blendB) = ApplyBlendMode(
                            bottomR, bottomG, bottomB,
                            topR, topG, topB,
                            top.BlendMode);

                        // Apply opacity and alpha blending
                        float topAlpha = (topA / 255f) * top.Opacity;
                        float bottomAlpha = bottomA / 255f;

                        float outAlpha = topAlpha + bottomAlpha * (1f - topAlpha);

                        if (outAlpha > 0)
                        {
                            result[bottomIdx] = (byte)Math.Clamp(
                                (blendR * topAlpha + bottomR * bottomAlpha * (1f - topAlpha)) / outAlpha, 0, 255);
                            result[bottomIdx + 1] = (byte)Math.Clamp(
                                (blendG * topAlpha + bottomG * bottomAlpha * (1f - topAlpha)) / outAlpha, 0, 255);
                            result[bottomIdx + 2] = (byte)Math.Clamp(
                                (blendB * topAlpha + bottomB * bottomAlpha * (1f - topAlpha)) / outAlpha, 0, 255);
                            result[bottomIdx + 3] = (byte)Math.Clamp(outAlpha * 255, 0, 255);
                        }
                    }
                    else
                    {
                        // Use bottom pixel
                        result[bottomIdx] = bottom.Data[bottomIdx];
                        result[bottomIdx + 1] = bottom.Data[bottomIdx + 1];
                        result[bottomIdx + 2] = bottom.Data[bottomIdx + 2];
                        result[bottomIdx + 3] = bottom.Data[bottomIdx + 3];
                    }
                }
            }

            return result;
        }

        private static (byte r, byte g, byte b) ApplyBlendMode(
            byte br, byte bg, byte bb,
            byte tr, byte tg, byte tb,
            BlendMode mode)
        {
            return mode switch
            {
                BlendMode.Normal => (tr, tg, tb),
                BlendMode.Multiply => BlendMultiply(br, bg, bb, tr, tg, tb),
                BlendMode.Screen => BlendScreen(br, bg, bb, tr, tg, tb),
                BlendMode.Overlay => BlendOverlay(br, bg, bb, tr, tg, tb),
                BlendMode.HardLight => BlendHardLight(br, bg, bb, tr, tg, tb),
                BlendMode.SoftLight => BlendSoftLight(br, bg, bb, tr, tg, tb),
                BlendMode.Darken => BlendDarken(br, bg, bb, tr, tg, tb),
                BlendMode.Lighten => BlendLighten(br, bg, bb, tr, tg, tb),
                BlendMode.ColorDodge => BlendColorDodge(br, bg, bb, tr, tg, tb),
                BlendMode.ColorBurn => BlendColorBurn(br, bg, bb, tr, tg, tb),
                BlendMode.LinearDodge => BlendLinearDodge(br, bg, bb, tr, tg, tb),
                BlendMode.LinearBurn => BlendLinearBurn(br, bg, bb, tr, tg, tb),
                BlendMode.Difference => BlendDifference(br, bg, bb, tr, tg, tb),
                BlendMode.Exclusion => BlendExclusion(br, bg, bb, tr, tg, tb),
                BlendMode.Hue => BlendHue(br, bg, bb, tr, tg, tb),
                BlendMode.Saturation => BlendSaturation(br, bg, bb, tr, tg, tb),
                BlendMode.Color => BlendColor(br, bg, bb, tr, tg, tb),
                BlendMode.Luminosity => BlendLuminosity(br, bg, bb, tr, tg, tb),
                _ => (tr, tg, tb)
            };
        }

        #region Blend Mode Implementations

        private static (byte r, byte g, byte b) BlendMultiply(byte br, byte bg, byte bb, byte tr, byte tg, byte tb)
        {
            return (
                (byte)((br * tr) / 255),
                (byte)((bg * tg) / 255),
                (byte)((bb * tb) / 255)
            );
        }

        private static (byte r, byte g, byte b) BlendScreen(byte br, byte bg, byte bb, byte tr, byte tg, byte tb)
        {
            return (
                (byte)(255 - ((255 - br) * (255 - tr)) / 255),
                (byte)(255 - ((255 - bg) * (255 - tg)) / 255),
                (byte)(255 - ((255 - bb) * (255 - tb)) / 255)
            );
        }

        private static (byte r, byte g, byte b) BlendOverlay(byte br, byte bg, byte bb, byte tr, byte tg, byte tb)
        {
            return (
                BlendOverlayChannel(br, tr),
                BlendOverlayChannel(bg, tg),
                BlendOverlayChannel(bb, tb)
            );
        }

        private static byte BlendOverlayChannel(byte b, byte t)
        {
            return b < 128
                ? (byte)((2 * b * t) / 255)
                : (byte)(255 - (2 * (255 - b) * (255 - t)) / 255);
        }

        private static (byte r, byte g, byte b) BlendHardLight(byte br, byte bg, byte bb, byte tr, byte tg, byte tb)
        {
            return (
                BlendHardLightChannel(br, tr),
                BlendHardLightChannel(bg, tg),
                BlendHardLightChannel(bb, tb)
            );
        }

        private static byte BlendHardLightChannel(byte b, byte t)
        {
            return t < 128
                ? (byte)((2 * b * t) / 255)
                : (byte)(255 - (2 * (255 - b) * (255 - t)) / 255);
        }

        private static (byte r, byte g, byte b) BlendSoftLight(byte br, byte bg, byte bb, byte tr, byte tg, byte tb)
        {
            return (
                BlendSoftLightChannel(br, tr),
                BlendSoftLightChannel(bg, tg),
                BlendSoftLightChannel(bb, tb)
            );
        }

        private static byte BlendSoftLightChannel(byte b, byte t)
        {
            float bf = b / 255f;
            float tf = t / 255f;

            float result = tf < 0.5f
                ? 2 * bf * tf + bf * bf * (1 - 2 * tf)
                : 2 * bf * (1 - tf) + MathF.Sqrt(bf) * (2 * tf - 1);

            return (byte)Math.Clamp(result * 255, 0, 255);
        }

        private static (byte r, byte g, byte b) BlendDarken(byte br, byte bg, byte bb, byte tr, byte tg, byte tb)
        {
            return (Math.Min(br, tr), Math.Min(bg, tg), Math.Min(bb, tb));
        }

        private static (byte r, byte g, byte b) BlendLighten(byte br, byte bg, byte bb, byte tr, byte tg, byte tb)
        {
            return (Math.Max(br, tr), Math.Max(bg, tg), Math.Max(bb, tb));
        }

        private static (byte r, byte g, byte b) BlendColorDodge(byte br, byte bg, byte bb, byte tr, byte tg, byte tb)
        {
            return (
                BlendColorDodgeChannel(br, tr),
                BlendColorDodgeChannel(bg, tg),
                BlendColorDodgeChannel(bb, tb)
            );
        }

        private static byte BlendColorDodgeChannel(byte b, byte t)
        {
            return t == 255 ? (byte)255 : (byte)Math.Min(255, (b * 255) / (255 - t));
        }

        private static (byte r, byte g, byte b) BlendColorBurn(byte br, byte bg, byte bb, byte tr, byte tg, byte tb)
        {
            return (
                BlendColorBurnChannel(br, tr),
                BlendColorBurnChannel(bg, tg),
                BlendColorBurnChannel(bb, tb)
            );
        }

        private static byte BlendColorBurnChannel(byte b, byte t)
        {
            return t == 0 ? (byte)0 : (byte)Math.Max(0, 255 - ((255 - b) * 255) / t);
        }

        private static (byte r, byte g, byte b) BlendLinearDodge(byte br, byte bg, byte bb, byte tr, byte tg, byte tb)
        {
            return (
                (byte)Math.Min(255, br + tr),
                (byte)Math.Min(255, bg + tg),
                (byte)Math.Min(255, bb + tb)
            );
        }

        private static (byte r, byte g, byte b) BlendLinearBurn(byte br, byte bg, byte bb, byte tr, byte tg, byte tb)
        {
            return (
                (byte)Math.Max(0, br + tr - 255),
                (byte)Math.Max(0, bg + tg - 255),
                (byte)Math.Max(0, bb + tb - 255)
            );
        }

        private static (byte r, byte g, byte b) BlendDifference(byte br, byte bg, byte bb, byte tr, byte tg, byte tb)
        {
            return (
                (byte)Math.Abs(br - tr),
                (byte)Math.Abs(bg - tg),
                (byte)Math.Abs(bb - tb)
            );
        }

        private static (byte r, byte g, byte b) BlendExclusion(byte br, byte bg, byte bb, byte tr, byte tg, byte tb)
        {
            return (
                (byte)(br + tr - 2 * br * tr / 255),
                (byte)(bg + tg - 2 * bg * tg / 255),
                (byte)(bb + tb - 2 * bb * tb / 255)
            );
        }

        private static (byte r, byte g, byte b) BlendHue(byte br, byte bg, byte bb, byte tr, byte tg, byte tb)
        {
            var topHsl = ColorSpace.RgbToHsl(tr, tg, tb);
            var bottomHsl = ColorSpace.RgbToHsl(br, bg, bb);

            return ColorSpace.HslToRgb(topHsl.X, bottomHsl.Y, bottomHsl.Z);
        }

        private static (byte r, byte g, byte b) BlendSaturation(byte br, byte bg, byte bb, byte tr, byte tg, byte tb)
        {
            var topHsl = ColorSpace.RgbToHsl(tr, tg, tb);
            var bottomHsl = ColorSpace.RgbToHsl(br, bg, bb);

            return ColorSpace.HslToRgb(bottomHsl.X, topHsl.Y, bottomHsl.Z);
        }

        private static (byte r, byte g, byte b) BlendColor(byte br, byte bg, byte bb, byte tr, byte tg, byte tb)
        {
            var topHsl = ColorSpace.RgbToHsl(tr, tg, tb);
            var bottomHsl = ColorSpace.RgbToHsl(br, bg, bb);

            return ColorSpace.HslToRgb(topHsl.X, topHsl.Y, bottomHsl.Z);
        }

        private static (byte r, byte g, byte b) BlendLuminosity(byte br, byte bg, byte bb, byte tr, byte tg, byte tb)
        {
            var topHsl = ColorSpace.RgbToHsl(tr, tg, tb);
            var bottomHsl = ColorSpace.RgbToHsl(br, bg, bb);

            return ColorSpace.HslToRgb(bottomHsl.X, bottomHsl.Y, topHsl.Z);
        }

        #endregion
    }
}
