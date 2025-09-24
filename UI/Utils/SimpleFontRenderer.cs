// GeoscientistToolkit/UI/Utils/SimpleFontRenderer.cs
using System;
using System.Collections.Generic;
using System.Numerics;

namespace GeoscientistToolkit.UI.Utils
{
    /// <summary>
    /// A minimal, dependency-free bitmap font renderer for creating composite images.
    /// Renders simple text onto a byte array representing an RGBA image.
    /// </summary>
    public static class SimpleFontRenderer
    {
        // Basic 5x7 pixel font representation.
        private static readonly Dictionary<char, byte[]> FontMap = new Dictionary<char, byte[]>()
        {
            {'A', new byte[] {0x7C, 0x12, 0x11, 0x12, 0x7C}}, {'B', new byte[] {0x7F, 0x49, 0x49, 0x49, 0x36}},
            {'C', new byte[] {0x3E, 0x41, 0x41, 0x41, 0x22}}, {'D', new byte[] {0x7F, 0x41, 0x41, 0x22, 0x1C}},
            {'E', new byte[] {0x7F, 0x49, 0x49, 0x49, 0x41}}, {'F', new byte[] {0x7F, 0x09, 0x09, 0x09, 0x01}},
            {'G', new byte[] {0x3E, 0x41, 0x49, 0x49, 0x7A}}, {'H', new byte[] {0x7F, 0x08, 0x08, 0x08, 0x7F}},
            {'I', new byte[] {0x00, 0x41, 0x7F, 0x41, 0x00}}, {'J', new byte[] {0x20, 0x40, 0x41, 0x3F, 0x01}},
            {'K', new byte[] {0x7F, 0x08, 0x14, 0x22, 0x41}}, {'L', new byte[] {0x7F, 0x40, 0x40, 0x40, 0x40}},
            {'M', new byte[] {0x7F, 0x02, 0x0C, 0x02, 0x7F}}, {'N', new byte[] {0x7F, 0x04, 0x08, 0x10, 0x7F}},
            {'O', new byte[] {0x3E, 0x41, 0x41, 0x41, 0x3E}}, {'P', new byte[] {0x7F, 0x09, 0x09, 0x09, 0x06}},
            {'Q', new byte[] {0x3E, 0x41, 0x51, 0x21, 0x5E}}, {'R', new byte[] {0x7F, 0x09, 0x19, 0x29, 0x46}},
            {'S', new byte[] {0x46, 0x49, 0x49, 0x49, 0x31}}, {'T', new byte[] {0x01, 0x01, 0x7F, 0x01, 0x01}},
            {'U', new byte[] {0x3F, 0x40, 0x40, 0x40, 0x3F}}, {'V', new byte[] {0x1F, 0x20, 0x40, 0x20, 0x1F}},
            {'W', new byte[] {0x3F, 0x40, 0x38, 0x40, 0x3F}}, {'X', new byte[] {0x63, 0x14, 0x08, 0x14, 0x63}},
            {'Y', new byte[] {0x07, 0x08, 0x70, 0x08, 0x07}}, {'Z', new byte[] {0x61, 0x51, 0x49, 0x45, 0x43}},
            {'0', new byte[] {0x3E, 0x51, 0x49, 0x45, 0x3E}}, {'1', new byte[] {0x00, 0x42, 0x7F, 0x40, 0x00}},
            {'2', new byte[] {0x42, 0x61, 0x51, 0x49, 0x46}}, {'3', new byte[] {0x21, 0x41, 0x45, 0x4B, 0x31}},
            {'4', new byte[] {0x18, 0x14, 0x12, 0x7F, 0x10}}, {'5', new byte[] {0x27, 0x45, 0x45, 0x45, 0x39}},
            {'6', new byte[] {0x3C, 0x4A, 0x49, 0x49, 0x30}}, {'7', new byte[] {0x01, 0x71, 0x09, 0x05, 0x03}},
            {'8', new byte[] {0x36, 0x49, 0x49, 0x49, 0x36}}, {'9', new byte[] {0x06, 0x49, 0x49, 0x29, 0x1E}},
            {' ', new byte[] {0x00, 0x00, 0x00, 0x00, 0x00}}, {'.', new byte[] {0x00, 0x60, 0x60, 0x00, 0x00}},
            {':', new byte[] {0x00, 0x36, 0x36, 0x00, 0x00}}, {'-', new byte[] {0x08, 0x08, 0x08, 0x08, 0x08}},
            {'|', new byte[] {0x00, 0x00, 0x7F, 0x00, 0x00}}, {'(', new byte[] {0x00, 0x1C, 0x22, 0x41, 0x00}},
            {')', new byte[] {0x00, 0x41, 0x22, 0x1C, 0x00}}, {'_', new byte[] {0x40, 0x40, 0x40, 0x40, 0x40}},
        };

        private const int CharWidth = 5;
        private const int CharHeight = 7;
        private const int CharSpacing = 1;

        /// <summary>
        /// Draws text onto an RGBA image buffer.
        /// </summary>
        /// <param name="buffer">The target RGBA image buffer (4 bytes per pixel).</param>
        /// <param name="bufferWidth">The width of the target image.</param>
        /// <param name="x">The starting X coordinate.</param>
        /// <param name="y">The starting Y coordinate.</param>
        /// <param name="text">The string to draw.</param>
        /// <param name="color">The color in 0xAABBGGRR format.</param>
        public static void DrawText(byte[] buffer, int bufferWidth, int x, int y, string text, uint color)
        {
            byte r = (byte)(color & 0xFF);
            byte g = (byte)((color >> 8) & 0xFF);
            byte b = (byte)((color >> 16) & 0xFF);
            byte a = (byte)((color >> 24) & 0xFF);

            int currentX = x;
            foreach (char c in text.ToUpper())
            {
                if (FontMap.TryGetValue(c, out byte[] charData))
                {
                    for (int col = 0; col < CharWidth; col++)
                    {
                        for (int row = 0; row < CharHeight; row++)
                        {
                            if (((charData[col] >> row) & 1) == 1)
                            {
                                int px = currentX + col;
                                int py = y + row;
                                int index = (py * bufferWidth + px) * 4;

                                if (index >= 0 && index < buffer.Length - 3)
                                {
                                    buffer[index] = r;
                                    buffer[index + 1] = g;
                                    buffer[index + 2] = b;
                                    buffer[index + 3] = a;
                                }
                            }
                        }
                    }
                }
                currentX += CharWidth + CharSpacing;
            }
        }
    }
}