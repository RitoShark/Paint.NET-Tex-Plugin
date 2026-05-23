////////////////////////////////////////////////////////////////////////
//
// BC4/BC5 Compression - Pure C# Port
// Based on Microsoft DirectXTex BC4BC5.cpp (MIT License)
//
////////////////////////////////////////////////////////////////////////

using System;

namespace TexFileTypePlugin
{
    internal static class BC4BC5Codec
    {
        private const int NUM_PIXELS_PER_BLOCK = 16;

        // ----- DECODE -----

        public static float DecodeUnormFromIndex(byte red0, byte red1, int uIndex)
        {
            if (uIndex == 0) return red0 / 255.0f;
            if (uIndex == 1) return red1 / 255.0f;
            float fred_0 = red0 / 255.0f;
            float fred_1 = red1 / 255.0f;
            if (red0 > red1)
            {
                int i = uIndex - 1;
                return (fred_0 * (7 - i) + fred_1 * i) / 7.0f;
            }
            else
            {
                if (uIndex == 6) return 0.0f;
                if (uIndex == 7) return 1.0f;
                int i = uIndex - 1;
                return (fred_0 * (5 - i) + fred_1 * i) / 5.0f;
            }
        }

        public static float DecodeSnormFromIndex(sbyte red0, sbyte red1, int uIndex)
        {
            sbyte sred_0 = red0 == -128 ? (sbyte)-127 : red0;
            sbyte sred_1 = red1 == -128 ? (sbyte)-127 : red1;

            if (uIndex == 0) return sred_0 / 127.0f;
            if (uIndex == 1) return sred_1 / 127.0f;
            float fred_0 = sred_0 / 127.0f;
            float fred_1 = sred_1 / 127.0f;
            if (red0 > red1)
            {
                int i = uIndex - 1;
                return (fred_0 * (7 - i) + fred_1 * i) / 7.0f;
            }
            else
            {
                if (uIndex == 6) return -1.0f;
                if (uIndex == 7) return 1.0f;
                int i = uIndex - 1;
                return (fred_0 * (5 - i) + fred_1 * i) / 5.0f;
            }
        }

        private static int GetIndex(ulong data, int uOffset)
        {
            return (int)((data >> (3 * uOffset + 16)) & 0x07);
        }

        // Decode a BC5 block (16 bytes, two 8-byte BC4 blocks: R, G)
        // For SNORM, we map [-1,1] -> [0,255] so it can be displayed
        public static void DecodeBC5(byte[] block, int blockOffset, byte[] rgba, int x, int y, int width, int height, bool isSigned)
        {
            ulong dataR = BitConverter.ToUInt64(block, blockOffset);
            ulong dataG = BitConverter.ToUInt64(block, blockOffset + 8);

            byte r0 = (byte)(dataR & 0xFF);
            byte r1 = (byte)((dataR >> 8) & 0xFF);
            byte g0 = (byte)(dataG & 0xFF);
            byte g1 = (byte)((dataG >> 8) & 0xFF);

            for (int py = 0; py < 4; py++)
            {
                for (int px = 0; px < 4; px++)
                {
                    if (x + px >= width || y + py >= height) continue;
                    int idx = py * 4 + px;
                    int rIdx = GetIndex(dataR, idx);
                    int gIdx = GetIndex(dataG, idx);

                    float fr, fg;
                    if (isSigned)
                    {
                        fr = DecodeSnormFromIndex((sbyte)r0, (sbyte)r1, rIdx);
                        fg = DecodeSnormFromIndex((sbyte)g0, (sbyte)g1, gIdx);
                        // map [-1,1] -> [0,255]
                        fr = (fr + 1.0f) * 0.5f;
                        fg = (fg + 1.0f) * 0.5f;
                    }
                    else
                    {
                        fr = DecodeUnormFromIndex(r0, r1, rIdx);
                        fg = DecodeUnormFromIndex(g0, g1, gIdx);
                    }

                    // Reconstruct normal map blue (Z): sqrt(1 - X^2 - Y^2) using mapped channels
                    // We treat R,G as the normal X,Y in [-1,1] (or [0,1] for unsigned)
                    float nx = fr * 2.0f - 1.0f;
                    float ny = fg * 2.0f - 1.0f;
                    float nz2 = 1.0f - nx * nx - ny * ny;
                    float nz = nz2 > 0 ? (float)Math.Sqrt(nz2) : 0;
                    float fb = nz * 0.5f + 0.5f;

                    int pixelIdx = ((y + py) * width + (x + px)) * 4;
                    rgba[pixelIdx]     = (byte)Math.Clamp(fr * 255.0f + 0.5f, 0, 255);
                    rgba[pixelIdx + 1] = (byte)Math.Clamp(fg * 255.0f + 0.5f, 0, 255);
                    rgba[pixelIdx + 2] = (byte)Math.Clamp(fb * 255.0f + 0.5f, 0, 255);
                    rgba[pixelIdx + 3] = 255;
                }
            }
        }

        public static byte[] DecompressBC5(byte[] data, int width, int height, bool isSigned)
        {
            byte[] rgba = new byte[width * height * 4];
            int blockWidth = (width + 3) / 4;
            int blockHeight = (height + 3) / 4;
            for (int by = 0; by < blockHeight; by++)
            {
                for (int bx = 0; bx < blockWidth; bx++)
                {
                    int off = (by * blockWidth + bx) * 16;
                    if (off + 16 <= data.Length)
                        DecodeBC5(data, off, rgba, bx * 4, by * 4, width, height, isSigned);
                }
            }
            return rgba;
        }

        // ----- ENCODE -----

        private static void FloatToSNorm(float fVal, out sbyte piSNorm)
        {
            if (float.IsNaN(fVal)) fVal = 0;
            else if (fVal > 1) fVal = 1;
            else if (fVal < -1) fVal = -1;

            fVal *= 127;
            if (fVal >= 0) fVal += 0.5f;
            else fVal -= 0.5f;
            piSNorm = (sbyte)fVal;
        }

        // Newton's-method endpoint optimizer (port of OptimizeAlpha<bRange>)
        private static void OptimizeAlpha(out float pX, out float pY, float[] pPoints, int cSteps, bool bRange)
        {
            float[] pC6 = { 5f / 5f, 4f / 5f, 3f / 5f, 2f / 5f, 1f / 5f, 0f / 5f };
            float[] pD6 = { 0f / 5f, 1f / 5f, 2f / 5f, 3f / 5f, 4f / 5f, 5f / 5f };
            float[] pC8 = { 7f / 7f, 6f / 7f, 5f / 7f, 4f / 7f, 3f / 7f, 2f / 7f, 1f / 7f, 0f / 7f };
            float[] pD8 = { 0f / 7f, 1f / 7f, 2f / 7f, 3f / 7f, 4f / 7f, 5f / 7f, 6f / 7f, 7f / 7f };

            float[] pC = cSteps == 6 ? pC6 : pC8;
            float[] pD = cSteps == 6 ? pD6 : pD8;

            const float MAX_VALUE = 1.0f;
            float MIN_VALUE = bRange ? -1.0f : 0.0f;

            float fX = MAX_VALUE;
            float fY = MIN_VALUE;

            if (cSteps == 8)
            {
                for (int i = 0; i < NUM_PIXELS_PER_BLOCK; i++)
                {
                    if (pPoints[i] < fX) fX = pPoints[i];
                    if (pPoints[i] > fY) fY = pPoints[i];
                }
            }
            else
            {
                for (int i = 0; i < NUM_PIXELS_PER_BLOCK; i++)
                {
                    if (pPoints[i] < fX && pPoints[i] > MIN_VALUE) fX = pPoints[i];
                    if (pPoints[i] > fY && pPoints[i] < MAX_VALUE) fY = pPoints[i];
                }
                if (fX == fY) fY = MAX_VALUE;
            }

            float fSteps = cSteps - 1;

            for (int iter = 0; iter < 8; iter++)
            {
                if ((fY - fX) < (1.0f / 256.0f)) break;
                float fScale = fSteps / (fY - fX);

                float[] pSteps = new float[8];
                for (int s = 0; s < cSteps; s++)
                    pSteps[s] = pC[s] * fX + pD[s] * fY;
                if (cSteps == 6) { pSteps[6] = MIN_VALUE; pSteps[7] = MAX_VALUE; }

                float dX = 0, dY = 0, d2X = 0, d2Y = 0;

                for (int i = 0; i < NUM_PIXELS_PER_BLOCK; i++)
                {
                    float fDot = (pPoints[i] - fX) * fScale;
                    int iStep;
                    if (fDot <= 0.0f)
                        iStep = (cSteps == 6 && pPoints[i] <= (fX + MIN_VALUE) * 0.5f) ? 6 : 0;
                    else if (fDot >= fSteps)
                        iStep = (cSteps == 6 && pPoints[i] >= (fY + MAX_VALUE) * 0.5f) ? 7 : (cSteps - 1);
                    else
                        iStep = (int)(fDot + 0.5f);

                    if (iStep < cSteps)
                    {
                        float fDiff = pSteps[iStep] - pPoints[i];
                        dX += pC[iStep] * fDiff;
                        d2X += pC[iStep] * pC[iStep];
                        dY += pD[iStep] * fDiff;
                        d2Y += pD[iStep] * pD[iStep];
                    }
                }

                if (d2X > 0) fX -= dX / d2X;
                if (d2Y > 0) fY -= dY / d2Y;
                if (fX > fY) { float t = fX; fX = fY; fY = t; }
                if (dX * dX < (1.0f / 64.0f) && dY * dY < (1.0f / 64.0f)) break;
            }

            pX = fX < MIN_VALUE ? MIN_VALUE : (fX > MAX_VALUE ? MAX_VALUE : fX);
            pY = fY < MIN_VALUE ? MIN_VALUE : (fY > MAX_VALUE ? MAX_VALUE : fY);
        }

        private static void FindEndPointsBC4U(float[] texels, out byte ep0, out byte ep1)
        {
            float min = texels[0], max = texels[0];
            for (int i = 0; i < NUM_PIXELS_PER_BLOCK; i++) { if (texels[i] < min) min = texels[i]; else if (texels[i] > max) max = texels[i]; }
            bool b4 = min == 0f || max == 1f;
            float fStart, fEnd;
            if (!b4)
            {
                OptimizeAlpha(out fStart, out fEnd, texels, 8, false);
                ep0 = (byte)(fEnd * 255f);
                ep1 = (byte)(fStart * 255f);
            }
            else
            {
                OptimizeAlpha(out fStart, out fEnd, texels, 6, false);
                ep1 = (byte)(fEnd * 255f);
                ep0 = (byte)(fStart * 255f);
            }
        }

        private static void FindClosestUNorm(byte red0, byte red1, float[] texels, out ulong indices)
        {
            float[] grad = new float[8];
            for (int i = 0; i < 8; i++) grad[i] = DecodeUnormFromIndex(red0, red1, i);
            ulong data = 0;
            for (int i = 0; i < NUM_PIXELS_PER_BLOCK; i++)
            {
                int best = 0;
                float bestDelta = 1e10f;
                for (int u = 0; u < 8; u++)
                {
                    float d = Math.Abs(grad[u] - texels[i]);
                    if (d < bestDelta) { best = u; bestDelta = d; }
                }
                data |= ((ulong)best & 0x07) << (3 * i + 16);
            }
            indices = data;
        }

        // Encode a single BC4-unorm block (8 bytes)
        public static void EncodeBC4UBlock(float[] texels, byte[] output, int offset)
        {
            FindEndPointsBC4U(texels, out byte r0, out byte r1);
            FindClosestUNorm(r0, r1, texels, out ulong idx);
            ulong data = r0 | ((ulong)r1 << 8) | idx;
            for (int i = 0; i < 8; i++) output[offset + i] = (byte)(data >> (i * 8));
        }

        // BC5 UNORM encode from RGBA source (uses R and G channels)
        public static byte[] CompressBC5(byte[] rgba, int width, int height)
        {
            int blockWidth = (width + 3) / 4;
            int blockHeight = (height + 3) / 4;
            byte[] output = new byte[blockWidth * blockHeight * 16];

            float[] texR = new float[NUM_PIXELS_PER_BLOCK];
            float[] texG = new float[NUM_PIXELS_PER_BLOCK];

            for (int by = 0; by < blockHeight; by++)
            {
                for (int bx = 0; bx < blockWidth; bx++)
                {
                    for (int py = 0; py < 4; py++)
                    {
                        for (int px = 0; px < 4; px++)
                        {
                            int sx = Math.Min(bx * 4 + px, width - 1);
                            int sy = Math.Min(by * 4 + py, height - 1);
                            int sIdx = (sy * width + sx) * 4;
                            int tIdx = py * 4 + px;
                            texR[tIdx] = rgba[sIdx] / 255.0f;
                            texG[tIdx] = rgba[sIdx + 1] / 255.0f;
                        }
                    }
                    int blockOff = (by * blockWidth + bx) * 16;
                    EncodeBC4UBlock(texR, output, blockOff);
                    EncodeBC4UBlock(texG, output, blockOff + 8);
                }
            }
            return output;
        }
    }
}
