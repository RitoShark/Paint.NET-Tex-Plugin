////////////////////////////////////////////////////////////////////////
//
// BC7 Encoder (full, all 8 modes) - Pure C# Port
// Based on Microsoft DirectXTex BC6HBC7.cpp (MIT License)
//
////////////////////////////////////////////////////////////////////////

using System;
using System.Threading.Tasks;

namespace TexFileTypePlugin
{
    internal static class BC7Encoder
    {
        private const int NUM_PIXELS_PER_BLOCK = BC7Decoder.NUM_PIXELS_PER_BLOCK;
        private const int BC67_WEIGHT_MAX = BC7Decoder.BC67_WEIGHT_MAX;
        private const int BC67_WEIGHT_SHIFT = BC7Decoder.BC67_WEIGHT_SHIFT;
        private const int BC67_WEIGHT_ROUND = BC7Decoder.BC67_WEIGHT_ROUND;
        private const int BC7_MAX_REGIONS = 3;
        private const int BC7_MAX_SHAPES = 64;
        private const int BC7_MAX_INDICES = 16;
        private const int BC7_NUM_CHANNELS = 4;
        private const int C_NUM_MODES = 8;

        private const float fEpsilon = (0.25f / 64.0f) * (0.25f / 64.0f);
        private static readonly float[] pC3 = { 2f / 2f, 1f / 2f, 0f / 2f };
        private static readonly float[] pD3 = { 0f / 2f, 1f / 2f, 2f / 2f };
        private static readonly float[] pC4 = { 3f / 3f, 2f / 3f, 1f / 3f, 0f / 3f };
        private static readonly float[] pD4 = { 0f / 3f, 1f / 3f, 2f / 3f, 3f / 3f };

        // -------------------- Color types --------------------
        private struct LDR { public byte r, g, b, a; }
        private struct LDREP { public LDR A, B; }
        private struct HDR { public float r, g, b, a; }

        private static byte GetCh(LDR c, int ch) { if (ch == 0) return c.r; if (ch == 1) return c.g; if (ch == 2) return c.b; return c.a; }
        private static void SetCh(ref LDR c, int ch, byte v) { if (ch == 0) c.r = v; else if (ch == 1) c.g = v; else if (ch == 2) c.b = v; else c.a = v; }

        private static byte GetPrec(BC7Decoder.ModeInfo info, int ch)
        {
            return ch == 0 ? info.rPrec : ch == 1 ? info.gPrec : ch == 2 ? info.bPrec : info.aPrec;
        }
        private static byte GetPrecP(BC7Decoder.ModeInfo info, int ch)
        {
            return ch == 0 ? info.rPrecP : ch == 1 ? info.gPrecP : ch == 2 ? info.bPrecP : info.aPrecP;
        }

        // -------------------- EncodeParams --------------------
        private class EncodeParams
        {
            public int uMode;
            public LDREP[,] aEndPts; // [BC7_MAX_SHAPES][BC7_MAX_REGIONS]
            public LDR[] aLDRPixels;
            public HDR[] aHDRPixels;
            public EncodeParams()
            {
                aEndPts = new LDREP[BC7_MAX_SHAPES, BC7_MAX_REGIONS];
                aLDRPixels = new LDR[NUM_PIXELS_PER_BLOCK];
                aHDRPixels = new HDR[NUM_PIXELS_PER_BLOCK];
            }
        }

        // -------------------- Bit I/O --------------------
        private static void SetBits(byte[] block, ref int uStartBit, int uNumBits, uint uValue)
        {
            if (uNumBits == 0) return;
            int uIndex = uStartBit >> 3;
            int uBase = uStartBit - (uIndex << 3);
            if (uBase + uNumBits > 8)
            {
                int uFirstIndexBits = 8 - uBase;
                int uNextIndexBits = uNumBits - uFirstIndexBits;
                block[uIndex] &= (byte)~(((1 << uFirstIndexBits) - 1) << uBase);
                block[uIndex] |= (byte)(uValue << uBase);
                block[uIndex + 1] &= (byte)~((1 << uNextIndexBits) - 1);
                block[uIndex + 1] |= (byte)(uValue >> uFirstIndexBits);
            }
            else
            {
                block[uIndex] &= (byte)~(((1 << uNumBits) - 1) << uBase);
                block[uIndex] |= (byte)(uValue << uBase);
            }
            uStartBit += uNumBits;
        }

        // -------------------- Quantize/Unquantize/Interpolate --------------------
        private static byte Quantize(byte comp, int uPrec)
        {
            int rnd = Math.Min(255, comp + (1 << (7 - uPrec)));
            return (byte)(rnd >> (8 - uPrec));
        }
        private static LDR QuantizeLDR(LDR c, BC7Decoder.ModeInfo info)
        {
            LDR q = new LDR
            {
                r = Quantize(c.r, info.rPrec),
                g = Quantize(c.g, info.gPrec),
                b = Quantize(c.b, info.bPrec),
                a = info.aPrec != 0 ? Quantize(c.a, info.aPrec) : (byte)255,
            };
            return q;
        }
        private static byte Unquantize(byte comp, int uPrec)
        {
            comp = (byte)(comp << (8 - uPrec));
            return (byte)(comp | (comp >> uPrec));
        }
        private static LDR UnquantizeLDR(LDR c, BC7Decoder.ModeInfo info)
        {
            return new LDR
            {
                r = Unquantize(c.r, info.rPrecP),
                g = Unquantize(c.g, info.gPrecP),
                b = Unquantize(c.b, info.bPrecP),
                a = info.aPrecP > 0 ? Unquantize(c.a, info.aPrecP) : (byte)255,
            };
        }

        private static int WeightFor(int prec, int idx)
        {
            return prec == 2 ? BC7Decoder.g_aWeights2[idx] :
                   prec == 3 ? BC7Decoder.g_aWeights3[idx] :
                   BC7Decoder.g_aWeights4[idx];
        }

        private static LDR InterpolateRGBA(LDR c0, LDR c1, int wc, int wa, int wcprec, int waprec)
        {
            LDR o;
            int w = WeightFor(wcprec, wc);
            o.r = (byte)((c0.r * (BC67_WEIGHT_MAX - w) + c1.r * w + BC67_WEIGHT_ROUND) >> BC67_WEIGHT_SHIFT);
            o.g = (byte)((c0.g * (BC67_WEIGHT_MAX - w) + c1.g * w + BC67_WEIGHT_ROUND) >> BC67_WEIGHT_SHIFT);
            o.b = (byte)((c0.b * (BC67_WEIGHT_MAX - w) + c1.b * w + BC67_WEIGHT_ROUND) >> BC67_WEIGHT_SHIFT);
            int wA = WeightFor(waprec, wa);
            o.a = (byte)((c0.a * (BC67_WEIGHT_MAX - wA) + c1.a * wA + BC67_WEIGHT_ROUND) >> BC67_WEIGHT_SHIFT);
            return o;
        }

        // -------------------- GeneratePaletteQuantized --------------------
        private static void GeneratePaletteQuantized(EncodeParams pEP, int uIndexMode, LDREP endPts, LDR[] aPalette)
        {
            ref BC7Decoder.ModeInfo info = ref BC7Decoder.ms_aInfo[pEP.uMode];
            int uIndexPrec = uIndexMode != 0 ? info.uIndexPrec2 : info.uIndexPrec;
            int uIndexPrec2 = uIndexMode != 0 ? info.uIndexPrec : info.uIndexPrec2;
            int uNumIndices = 1 << uIndexPrec;
            int uNumIndices2 = 1 << uIndexPrec2;

            LDR a = UnquantizeLDR(endPts.A, info);
            LDR b = UnquantizeLDR(endPts.B, info);
            if (uIndexPrec2 == 0)
            {
                for (int i = 0; i < uNumIndices; i++)
                    aPalette[i] = InterpolateRGBA(a, b, i, i, uIndexPrec, uIndexPrec);
            }
            else
            {
                for (int i = 0; i < uNumIndices; i++)
                {
                    LDR o; int w = WeightFor(uIndexPrec, i);
                    o.r = (byte)((a.r * (BC67_WEIGHT_MAX - w) + b.r * w + BC67_WEIGHT_ROUND) >> BC67_WEIGHT_SHIFT);
                    o.g = (byte)((a.g * (BC67_WEIGHT_MAX - w) + b.g * w + BC67_WEIGHT_ROUND) >> BC67_WEIGHT_SHIFT);
                    o.b = (byte)((a.b * (BC67_WEIGHT_MAX - w) + b.b * w + BC67_WEIGHT_ROUND) >> BC67_WEIGHT_SHIFT);
                    o.a = 0; // filled below
                    aPalette[i] = o;
                }
                for (int i = 0; i < uNumIndices2; i++)
                {
                    int w = WeightFor(uIndexPrec2, i);
                    aPalette[i].a = (byte)((a.a * (BC67_WEIGHT_MAX - w) + b.a * w + BC67_WEIGHT_ROUND) >> BC67_WEIGHT_SHIFT);
                }
            }
        }

        // -------------------- ComputeError --------------------
        private static float ComputeError(LDR pixel, LDR[] aPalette, int uIndexPrec, int uIndexPrec2, out int bestIndex, out int bestIndex2)
        {
            int uNumIndices = 1 << uIndexPrec;
            int uNumIndices2 = 1 << uIndexPrec2;
            float fTotalErr = 0;
            float fBestErr = float.MaxValue;
            bestIndex = 0; bestIndex2 = 0;

            if (uIndexPrec2 == 0)
            {
                for (int i = 0; i < uNumIndices && fBestErr > 0; i++)
                {
                    LDR p = aPalette[i];
                    int dr = pixel.r - p.r, dg = pixel.g - p.g, db = pixel.b - p.b, da = pixel.a - p.a;
                    float fErr = dr * dr + dg * dg + db * db + da * da;
                    if (fErr > fBestErr) break;
                    if (fErr < fBestErr) { fBestErr = fErr; bestIndex = i; }
                }
                fTotalErr += fBestErr;
            }
            else
            {
                for (int i = 0; i < uNumIndices && fBestErr > 0; i++)
                {
                    LDR p = aPalette[i];
                    int dr = pixel.r - p.r, dg = pixel.g - p.g, db = pixel.b - p.b;
                    float fErr = dr * dr + dg * dg + db * db;
                    if (fErr > fBestErr) break;
                    if (fErr < fBestErr) { fBestErr = fErr; bestIndex = i; }
                }
                fTotalErr += fBestErr;
                fBestErr = float.MaxValue;
                for (int i = 0; i < uNumIndices2 && fBestErr > 0; i++)
                {
                    float ea = pixel.a - aPalette[i].a;
                    float fErr = ea * ea;
                    if (fErr > fBestErr) break;
                    if (fErr < fBestErr) { fBestErr = fErr; bestIndex2 = i; }
                }
                fTotalErr += fBestErr;
            }
            return fTotalErr;
        }

        private static float ComputeError(LDR pixel, LDR[] aPalette, int uIndexPrec, int uIndexPrec2)
        {
            return ComputeError(pixel, aPalette, uIndexPrec, uIndexPrec2, out _, out _);
        }

        // -------------------- OptimizeRGB --------------------
        private static void OptimizeRGB(HDR[] pPoints, out HDR pX, out HDR pY, int cSteps, int cPixels, int[] pIndex)
        {
            float[] pC = cSteps == 3 ? pC3 : pC4;
            float[] pD = cSteps == 3 ? pD3 : pD4;

            HDR X; X.r = float.MaxValue; X.g = float.MaxValue; X.b = float.MaxValue; X.a = 0;
            HDR Y; Y.r = -float.MaxValue; Y.g = -float.MaxValue; Y.b = -float.MaxValue; Y.a = 0;

            for (int i = 0; i < cPixels; i++)
            {
                HDR p = pPoints[pIndex[i]];
                if (p.r < X.r) X.r = p.r; if (p.g < X.g) X.g = p.g; if (p.b < X.b) X.b = p.b;
                if (p.r > Y.r) Y.r = p.r; if (p.g > Y.g) Y.g = p.g; if (p.b > Y.b) Y.b = p.b;
            }

            HDR AB; AB.r = Y.r - X.r; AB.g = Y.g - X.g; AB.b = Y.b - X.b; AB.a = 0;
            float fAB = AB.r * AB.r + AB.g * AB.g + AB.b * AB.b;

            if (fAB < float.Epsilon) { pX = X; pY = Y; return; }

            float fABInv = 1.0f / fAB;
            HDR Dir; Dir.r = AB.r * fABInv; Dir.g = AB.g * fABInv; Dir.b = AB.b * fABInv; Dir.a = 0;
            HDR Mid; Mid.r = (X.r + Y.r) * 0.5f; Mid.g = (X.g + Y.g) * 0.5f; Mid.b = (X.b + Y.b) * 0.5f; Mid.a = 0;

            float[] fDir = new float[4];
            for (int i = 0; i < cPixels; i++)
            {
                HDR p = pPoints[pIndex[i]];
                float pr = (p.r - Mid.r) * Dir.r;
                float pg = (p.g - Mid.g) * Dir.g;
                float pb = (p.b - Mid.b) * Dir.b;
                float f;
                f = pr + pg + pb; fDir[0] += f * f;
                f = pr + pg - pb; fDir[1] += f * f;
                f = pr - pg + pb; fDir[2] += f * f;
                f = pr - pg - pb; fDir[3] += f * f;
            }

            int iDirMax = 0;
            float fDirMax = fDir[0];
            for (int i = 1; i < 4; i++) if (fDir[i] > fDirMax) { fDirMax = fDir[i]; iDirMax = i; }
            if ((iDirMax & 2) != 0) { float t = X.g; X.g = Y.g; Y.g = t; }
            if ((iDirMax & 1) != 0) { float t = X.b; X.b = Y.b; Y.b = t; }

            if (fAB < 1.0f / 4096.0f) { pX = X; pY = Y; return; }

            float fSteps = cSteps - 1;

            for (int iter = 0; iter < 8; iter++)
            {
                HDR[] pSteps = new HDR[4];
                for (int s = 0; s < cSteps; s++)
                {
                    pSteps[s].r = X.r * pC[s] + Y.r * pD[s];
                    pSteps[s].g = X.g * pC[s] + Y.g * pD[s];
                    pSteps[s].b = X.b * pC[s] + Y.b * pD[s];
                }
                Dir.r = Y.r - X.r; Dir.g = Y.g - X.g; Dir.b = Y.b - X.b;
                float fLen = Dir.r * Dir.r + Dir.g * Dir.g + Dir.b * Dir.b;
                if (fLen < (1.0f / 4096.0f)) break;
                float fScale = fSteps / fLen;
                Dir.r *= fScale; Dir.g *= fScale; Dir.b *= fScale;

                float d2X = 0, d2Y = 0;
                HDR dX = default, dY = default;

                for (int i = 0; i < cPixels; i++)
                {
                    HDR p = pPoints[pIndex[i]];
                    float fDot = (p.r - X.r) * Dir.r + (p.g - X.g) * Dir.g + (p.b - X.b) * Dir.b;
                    int iStep;
                    if (fDot <= 0) iStep = 0;
                    else if (fDot >= fSteps) iStep = cSteps - 1;
                    else iStep = (int)(fDot + 0.5f);

                    HDR Diff;
                    Diff.r = pSteps[iStep].r - p.r;
                    Diff.g = pSteps[iStep].g - p.g;
                    Diff.b = pSteps[iStep].b - p.b;
                    Diff.a = 0;
                    float fC = pC[iStep] * (1.0f / 8.0f);
                    float fD = pD[iStep] * (1.0f / 8.0f);
                    d2X += fC * pC[iStep];
                    dX.r += fC * Diff.r; dX.g += fC * Diff.g; dX.b += fC * Diff.b;
                    d2Y += fD * pD[iStep];
                    dY.r += fD * Diff.r; dY.g += fD * Diff.g; dY.b += fD * Diff.b;
                }
                if (d2X > 0) { float f = -1.0f / d2X; X.r += dX.r * f; X.g += dX.g * f; X.b += dX.b * f; }
                if (d2Y > 0) { float f = -1.0f / d2Y; Y.r += dY.r * f; Y.g += dY.g * f; Y.b += dY.b * f; }
                if (dX.r * dX.r < fEpsilon && dX.g * dX.g < fEpsilon && dX.b * dX.b < fEpsilon &&
                    dY.r * dY.r < fEpsilon && dY.g * dY.g < fEpsilon && dY.b * dY.b < fEpsilon) break;
            }
            pX = X; pY = Y;
        }

        // -------------------- OptimizeRGBA --------------------
        private static void OptimizeRGBA(HDR[] pPoints, out HDR pX, out HDR pY, int cSteps, int cPixels, int[] pIndex)
        {
            float[] pC = cSteps == 3 ? pC3 : pC4;
            float[] pD = cSteps == 3 ? pD3 : pD4;

            HDR X; X.r = 1f; X.g = 1f; X.b = 1f; X.a = 1f;
            HDR Y = default;

            for (int i = 0; i < cPixels; i++)
            {
                HDR p = pPoints[pIndex[i]];
                if (p.r < X.r) X.r = p.r; if (p.g < X.g) X.g = p.g; if (p.b < X.b) X.b = p.b; if (p.a < X.a) X.a = p.a;
                if (p.r > Y.r) Y.r = p.r; if (p.g > Y.g) Y.g = p.g; if (p.b > Y.b) Y.b = p.b; if (p.a > Y.a) Y.a = p.a;
            }

            HDR AB; AB.r = Y.r - X.r; AB.g = Y.g - X.g; AB.b = Y.b - X.b; AB.a = Y.a - X.a;
            float fAB = AB.r * AB.r + AB.g * AB.g + AB.b * AB.b + AB.a * AB.a;
            if (fAB < float.Epsilon) { pX = X; pY = Y; return; }

            float fABInv = 1.0f / fAB;
            HDR Dir; Dir.r = AB.r * fABInv; Dir.g = AB.g * fABInv; Dir.b = AB.b * fABInv; Dir.a = AB.a * fABInv;
            HDR Mid; Mid.r = (X.r + Y.r) * 0.5f; Mid.g = (X.g + Y.g) * 0.5f; Mid.b = (X.b + Y.b) * 0.5f; Mid.a = (X.a + Y.a) * 0.5f;

            float[] fDir = new float[8];
            for (int i = 0; i < cPixels; i++)
            {
                HDR p = pPoints[pIndex[i]];
                float pr = (p.r - Mid.r) * Dir.r;
                float pg = (p.g - Mid.g) * Dir.g;
                float pb = (p.b - Mid.b) * Dir.b;
                float pa = (p.a - Mid.a) * Dir.a;
                float f;
                f = pr + pg + pb + pa; fDir[0] += f * f;
                f = pr + pg + pb - pa; fDir[1] += f * f;
                f = pr + pg - pb + pa; fDir[2] += f * f;
                f = pr + pg - pb - pa; fDir[3] += f * f;
                f = pr - pg + pb + pa; fDir[4] += f * f;
                f = pr - pg + pb - pa; fDir[5] += f * f;
                f = pr - pg - pb + pa; fDir[6] += f * f;
                f = pr - pg - pb - pa; fDir[7] += f * f;
            }
            int iDirMax = 0;
            float fDirMax = fDir[0];
            for (int i = 1; i < 8; i++) if (fDir[i] > fDirMax) { fDirMax = fDir[i]; iDirMax = i; }
            if ((iDirMax & 4) != 0) { float t = X.g; X.g = Y.g; Y.g = t; }
            if ((iDirMax & 2) != 0) { float t = X.b; X.b = Y.b; Y.b = t; }
            if ((iDirMax & 1) != 0) { float t = X.a; X.a = Y.a; Y.a = t; }

            if (fAB < 1.0f / 4096.0f) { pX = X; pY = Y; return; }

            float fSteps = cSteps - 1;
            for (int iter = 0; iter < 8; iter++)
            {
                HDR[] pSteps = new HDR[BC7_MAX_INDICES];
                for (int s = 0; s < cSteps; s++)
                {
                    pSteps[s].r = X.r * pC[s] + Y.r * pD[s];
                    pSteps[s].g = X.g * pC[s] + Y.g * pD[s];
                    pSteps[s].b = X.b * pC[s] + Y.b * pD[s];
                    pSteps[s].a = X.a * pC[s] + Y.a * pD[s];
                }
                Dir.r = Y.r - X.r; Dir.g = Y.g - X.g; Dir.b = Y.b - X.b; Dir.a = Y.a - X.a;
                float fLen = Dir.r * Dir.r + Dir.g * Dir.g + Dir.b * Dir.b + Dir.a * Dir.a;
                if (fLen < (1.0f / 4096.0f)) break;
                float fScale = fSteps / fLen;
                Dir.r *= fScale; Dir.g *= fScale; Dir.b *= fScale; Dir.a *= fScale;

                float d2X = 0, d2Y = 0;
                HDR dX = default, dY = default;
                for (int i = 0; i < cPixels; i++)
                {
                    HDR p = pPoints[pIndex[i]];
                    float fDot = (p.r - X.r) * Dir.r + (p.g - X.g) * Dir.g + (p.b - X.b) * Dir.b + (p.a - X.a) * Dir.a;
                    int iStep;
                    if (fDot <= 0) iStep = 0;
                    else if (fDot >= fSteps) iStep = cSteps - 1;
                    else iStep = (int)(fDot + 0.5f);

                    HDR Diff;
                    Diff.r = pSteps[iStep].r - p.r;
                    Diff.g = pSteps[iStep].g - p.g;
                    Diff.b = pSteps[iStep].b - p.b;
                    Diff.a = pSteps[iStep].a - p.a;
                    float fC = pC[iStep] * (1.0f / 8.0f);
                    float fD = pD[iStep] * (1.0f / 8.0f);
                    d2X += fC * pC[iStep];
                    dX.r += fC * Diff.r; dX.g += fC * Diff.g; dX.b += fC * Diff.b; dX.a += fC * Diff.a;
                    d2Y += fD * pD[iStep];
                    dY.r += fD * Diff.r; dY.g += fD * Diff.g; dY.b += fD * Diff.b; dY.a += fD * Diff.a;
                }
                if (d2X > 0) { float f = -1.0f / d2X; X.r += dX.r * f; X.g += dX.g * f; X.b += dX.b * f; X.a += dX.a * f; }
                if (d2Y > 0) { float f = -1.0f / d2Y; Y.r += dY.r * f; Y.g += dY.g * f; Y.b += dY.b * f; Y.a += dY.a * f; }
                float ddX = dX.r * dX.r + dX.g * dX.g + dX.b * dX.b + dX.a * dX.a;
                float ddY = dY.r * dY.r + dY.g * dY.g + dY.b * dY.b + dY.a * dY.a;
                if (ddX < fEpsilon && ddY < fEpsilon) break;
            }
            pX = X; pY = Y;
        }

        private static LDR HdrToLdr(HDR h)
        {
            LDR o;
            o.r = (byte)Math.Max(0, Math.Min(255, h.r + 0.01f));
            o.g = (byte)Math.Max(0, Math.Min(255, h.g + 0.01f));
            o.b = (byte)Math.Max(0, Math.Min(255, h.b + 0.01f));
            o.a = (byte)Math.Max(0, Math.Min(255, h.a + 0.01f));
            return o;
        }

        // -------------------- RoughMSE --------------------
        private static float RoughMSE(EncodeParams pEP, int uShape, int uIndexMode)
        {
            ref BC7Decoder.ModeInfo info = ref BC7Decoder.ms_aInfo[pEP.uMode];
            int uPartitions = info.uPartitions;
            int uIndexPrec = uIndexMode != 0 ? info.uIndexPrec2 : info.uIndexPrec;
            int uIndexPrec2 = uIndexMode != 0 ? info.uIndexPrec : info.uIndexPrec2;
            int uNumIndices = 1 << uIndexPrec;
            int uNumIndices2 = 1 << uIndexPrec2;

            int[] auPixIdx = new int[NUM_PIXELS_PER_BLOCK];
            LDR[][] aPalette = new LDR[BC7_MAX_REGIONS][];
            for (int p = 0; p < BC7_MAX_REGIONS; p++) aPalette[p] = new LDR[BC7_MAX_INDICES];

            for (int p = 0; p <= uPartitions; p++)
            {
                int np = 0;
                for (int i = 0; i < NUM_PIXELS_PER_BLOCK; i++)
                {
                    if (BC7Decoder.g_aPartitionTable[uPartitions][uShape][i] == p) auPixIdx[np++] = i;
                }
                if (np == 1)
                {
                    pEP.aEndPts[uShape, p].A = pEP.aLDRPixels[auPixIdx[0]];
                    pEP.aEndPts[uShape, p].B = pEP.aLDRPixels[auPixIdx[0]];
                    continue;
                }
                else if (np == 2)
                {
                    pEP.aEndPts[uShape, p].A = pEP.aLDRPixels[auPixIdx[0]];
                    pEP.aEndPts[uShape, p].B = pEP.aLDRPixels[auPixIdx[1]];
                    continue;
                }

                if (uIndexPrec2 == 0)
                {
                    OptimizeRGBA(pEP.aHDRPixels, out HDR epA, out HDR epB, 4, np, auPixIdx);
                    epA.r = Math.Clamp(epA.r, 0, 1); epA.g = Math.Clamp(epA.g, 0, 1); epA.b = Math.Clamp(epA.b, 0, 1); epA.a = Math.Clamp(epA.a, 0, 1);
                    epB.r = Math.Clamp(epB.r, 0, 1); epB.g = Math.Clamp(epB.g, 0, 1); epB.b = Math.Clamp(epB.b, 0, 1); epB.a = Math.Clamp(epB.a, 0, 1);
                    epA.r *= 255; epA.g *= 255; epA.b *= 255; epA.a *= 255;
                    epB.r *= 255; epB.g *= 255; epB.b *= 255; epB.a *= 255;
                    pEP.aEndPts[uShape, p].A = HdrToLdr(epA);
                    pEP.aEndPts[uShape, p].B = HdrToLdr(epB);
                }
                else
                {
                    byte uMinAlpha = 255, uMaxAlpha = 0;
                    for (int i = 0; i < np; i++)
                    {
                        byte a = pEP.aLDRPixels[auPixIdx[i]].a;
                        if (a < uMinAlpha) uMinAlpha = a;
                        if (a > uMaxAlpha) uMaxAlpha = a;
                    }
                    OptimizeRGB(pEP.aHDRPixels, out HDR epA, out HDR epB, 4, np, auPixIdx);
                    epA.r = Math.Clamp(epA.r, 0, 1); epA.g = Math.Clamp(epA.g, 0, 1); epA.b = Math.Clamp(epA.b, 0, 1);
                    epB.r = Math.Clamp(epB.r, 0, 1); epB.g = Math.Clamp(epB.g, 0, 1); epB.b = Math.Clamp(epB.b, 0, 1);
                    epA.r *= 255; epA.g *= 255; epA.b *= 255;
                    epB.r *= 255; epB.g *= 255; epB.b *= 255;
                    LDR ldrA = HdrToLdr(epA); ldrA.a = uMinAlpha;
                    LDR ldrB = HdrToLdr(epB); ldrB.a = uMaxAlpha;
                    pEP.aEndPts[uShape, p].A = ldrA;
                    pEP.aEndPts[uShape, p].B = ldrB;
                }
            }

            // Build palettes and compute total error
            float fTotalErr = 0;
            if (uIndexPrec2 == 0)
            {
                for (int p = 0; p <= uPartitions; p++)
                    for (int i = 0; i < uNumIndices; i++)
                        aPalette[p][i] = InterpolateRGBA(pEP.aEndPts[uShape, p].A, pEP.aEndPts[uShape, p].B, i, i, uIndexPrec, uIndexPrec);
            }
            else
            {
                for (int p = 0; p <= uPartitions; p++)
                {
                    for (int i = 0; i < uNumIndices; i++)
                    {
                        LDR o; int w = WeightFor(uIndexPrec, i);
                        LDR A = pEP.aEndPts[uShape, p].A, B = pEP.aEndPts[uShape, p].B;
                        o.r = (byte)((A.r * (BC67_WEIGHT_MAX - w) + B.r * w + BC67_WEIGHT_ROUND) >> BC67_WEIGHT_SHIFT);
                        o.g = (byte)((A.g * (BC67_WEIGHT_MAX - w) + B.g * w + BC67_WEIGHT_ROUND) >> BC67_WEIGHT_SHIFT);
                        o.b = (byte)((A.b * (BC67_WEIGHT_MAX - w) + B.b * w + BC67_WEIGHT_ROUND) >> BC67_WEIGHT_SHIFT);
                        o.a = 0;
                        aPalette[p][i] = o;
                    }
                    for (int i = 0; i < uNumIndices2; i++)
                    {
                        int w = WeightFor(uIndexPrec2, i);
                        LDR A = pEP.aEndPts[uShape, p].A, B = pEP.aEndPts[uShape, p].B;
                        aPalette[p][i].a = (byte)((A.a * (BC67_WEIGHT_MAX - w) + B.a * w + BC67_WEIGHT_ROUND) >> BC67_WEIGHT_SHIFT);
                    }
                }
            }

            for (int i = 0; i < NUM_PIXELS_PER_BLOCK; i++)
            {
                int uRegion = BC7Decoder.g_aPartitionTable[uPartitions][uShape][i];
                fTotalErr += ComputeError(pEP.aLDRPixels[i], aPalette[uRegion], uIndexPrec, uIndexPrec2);
            }
            return fTotalErr;
        }

        // -------------------- AssignIndices --------------------
        private static void AssignIndices(EncodeParams pEP, int uShape, int uIndexMode, LDREP[] endPts, int[] aIndices, int[] aIndices2, float[] afTotErr)
        {
            ref BC7Decoder.ModeInfo info = ref BC7Decoder.ms_aInfo[pEP.uMode];
            int uPartitions = info.uPartitions;
            int uIndexPrec = uIndexMode != 0 ? info.uIndexPrec2 : info.uIndexPrec;
            int uIndexPrec2 = uIndexMode != 0 ? info.uIndexPrec : info.uIndexPrec2;
            int uNumIndices = 1 << uIndexPrec;
            int uNumIndices2 = 1 << uIndexPrec2;
            int uHighestIndexBit = uNumIndices >> 1;
            int uHighestIndexBit2 = uNumIndices2 >> 1;

            LDR[][] aPalette = new LDR[BC7_MAX_REGIONS][];
            for (int p = 0; p <= uPartitions; p++)
            {
                aPalette[p] = new LDR[BC7_MAX_INDICES];
                GeneratePaletteQuantized(pEP, uIndexMode, endPts[p], aPalette[p]);
                afTotErr[p] = 0;
            }

            for (int i = 0; i < NUM_PIXELS_PER_BLOCK; i++)
            {
                int uRegion = BC7Decoder.g_aPartitionTable[uPartitions][uShape][i];
                afTotErr[uRegion] += ComputeError(pEP.aLDRPixels[i], aPalette[uRegion], uIndexPrec, uIndexPrec2, out int bi, out int bi2);
                aIndices[i] = bi;
                aIndices2[i] = bi2;
            }

            if (uIndexPrec2 == 0)
            {
                for (int p = 0; p <= uPartitions; p++)
                {
                    if ((aIndices[BC7Decoder.g_aFixUp[uPartitions][uShape][p]] & uHighestIndexBit) != 0)
                    {
                        LDR tmp = endPts[p].A; endPts[p].A = endPts[p].B; endPts[p].B = tmp;
                        for (int i = 0; i < NUM_PIXELS_PER_BLOCK; i++)
                            if (BC7Decoder.g_aPartitionTable[uPartitions][uShape][i] == p)
                                aIndices[i] = uNumIndices - 1 - aIndices[i];
                    }
                }
            }
            else
            {
                for (int p = 0; p <= uPartitions; p++)
                {
                    if ((aIndices[BC7Decoder.g_aFixUp[uPartitions][uShape][p]] & uHighestIndexBit) != 0)
                    {
                        byte tr = endPts[p].A.r; endPts[p].A.r = endPts[p].B.r; endPts[p].B.r = tr;
                        byte tg = endPts[p].A.g; endPts[p].A.g = endPts[p].B.g; endPts[p].B.g = tg;
                        byte tb = endPts[p].A.b; endPts[p].A.b = endPts[p].B.b; endPts[p].B.b = tb;
                        for (int i = 0; i < NUM_PIXELS_PER_BLOCK; i++)
                            if (BC7Decoder.g_aPartitionTable[uPartitions][uShape][i] == p)
                                aIndices[i] = uNumIndices - 1 - aIndices[i];
                    }
                    if ((aIndices2[0] & uHighestIndexBit2) != 0)
                    {
                        byte ta = endPts[p].A.a; endPts[p].A.a = endPts[p].B.a; endPts[p].B.a = ta;
                        for (int i = 0; i < NUM_PIXELS_PER_BLOCK; i++)
                            aIndices2[i] = uNumIndices2 - 1 - aIndices2[i];
                    }
                }
            }
        }

        // -------------------- FixEndpointPBits --------------------
        private static void FixEndpointPBits(EncodeParams pEP, LDREP[] pOrig, LDREP[] pFixed)
        {
            ref BC7Decoder.ModeInfo info = ref BC7Decoder.ms_aInfo[pEP.uMode];
            int uPartitions = info.uPartitions;
            for (int i = 0; i < BC7_MAX_REGIONS; i++) pFixed[i] = pOrig[i];
            int uPBits = info.uPBits;
            if (uPBits == 0) return;

            int uNumEP = (1 + uPartitions) << 1;
            int[] aPVote = new int[BC7_MAX_REGIONS << 1];
            int[] aCount = new int[BC7_MAX_REGIONS << 1];

            for (int ch = 0; ch < BC7_NUM_CHANNELS; ch++)
            {
                int prec = GetPrec(info, ch);
                int precP = GetPrecP(info, ch);
                int ep = 0;
                for (int i = 0; i <= uPartitions; i++)
                {
                    if (prec == precP)
                    {
                        SetCh(ref pFixed[i].A, ch, GetCh(pOrig[i].A, ch));
                        SetCh(ref pFixed[i].B, ch, GetCh(pOrig[i].B, ch));
                    }
                    else
                    {
                        SetCh(ref pFixed[i].A, ch, (byte)(GetCh(pOrig[i].A, ch) >> 1));
                        SetCh(ref pFixed[i].B, ch, (byte)(GetCh(pOrig[i].B, ch) >> 1));

                        int idx = ep++ * uPBits / uNumEP;
                        aPVote[idx] += GetCh(pOrig[i].A, ch) & 1;
                        aCount[idx]++;
                        idx = ep++ * uPBits / uNumEP;
                        aPVote[idx] += GetCh(pOrig[i].B, ch) & 1;
                        aCount[idx]++;
                    }
                }
            }

            int[] pbits = new int[BC7_MAX_REGIONS << 1];
            for (int i = 0; i < uPBits; i++) pbits[i] = aPVote[i] > (aCount[i] >> 1) ? 1 : 0;

            if (pEP.uMode == 1)
            {
                // shared pbits
                for (int ch = 0; ch < BC7_NUM_CHANNELS; ch++)
                {
                    for (int i = 0; i <= uPartitions; i++)
                    {
                        byte va = GetCh(pFixed[i].A, ch);
                        byte vb = GetCh(pFixed[i].B, ch);
                        SetCh(ref pFixed[i].A, ch, (byte)((va << 1) | pbits[i]));
                        SetCh(ref pFixed[i].B, ch, (byte)((vb << 1) | pbits[i]));
                    }
                }
            }
            else
            {
                for (int ch = 0; ch < BC7_NUM_CHANNELS; ch++)
                {
                    for (int i = 0; i <= uPartitions; i++)
                    {
                        byte va = GetCh(pFixed[i].A, ch);
                        byte vb = GetCh(pFixed[i].B, ch);
                        SetCh(ref pFixed[i].A, ch, (byte)((va << 1) | pbits[i * 2 + 0]));
                        SetCh(ref pFixed[i].B, ch, (byte)((vb << 1) | pbits[i * 2 + 1]));
                    }
                }
            }
        }

        // -------------------- MapColors / PerturbOne / Exhaustive / OptimizeOne / OptimizeEndPoints --------------------
        private static float MapColors(EncodeParams pEP, LDR[] aColors, int np, int uIndexMode, LDREP endPts, float fMinErr)
        {
            ref BC7Decoder.ModeInfo info = ref BC7Decoder.ms_aInfo[pEP.uMode];
            int uIndexPrec = uIndexMode != 0 ? info.uIndexPrec2 : info.uIndexPrec;
            int uIndexPrec2 = uIndexMode != 0 ? info.uIndexPrec : info.uIndexPrec2;
            LDR[] aPalette = new LDR[BC7_MAX_INDICES];
            float fTotalErr = 0;
            GeneratePaletteQuantized(pEP, uIndexMode, endPts, aPalette);
            for (int i = 0; i < np; i++)
            {
                fTotalErr += ComputeError(aColors[i], aPalette, uIndexPrec, uIndexPrec2);
                if (fTotalErr > fMinErr) return float.MaxValue;
            }
            return fTotalErr;
        }

        private static float PerturbOne(EncodeParams pEP, LDR[] aColors, int np, int uIndexMode, int ch, LDREP oldEndPts, out LDREP newEndPts, float fOldErr, int do_b)
        {
            ref BC7Decoder.ModeInfo info = ref BC7Decoder.ms_aInfo[pEP.uMode];
            int prec = GetPrecP(info, ch);
            newEndPts = oldEndPts;
            LDREP tmpEndPts = oldEndPts;
            float fMinErr = fOldErr;

            for (int step = 1 << (prec - 1); step != 0; step >>= 1)
            {
                bool bImproved = false;
                int beststep = 0;
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    int curVal = do_b != 0 ? GetCh(newEndPts.B, ch) : GetCh(newEndPts.A, ch);
                    int tmp = curVal + sign * step;
                    if (tmp < 0 || tmp >= (1 << prec)) continue;
                    if (do_b != 0) SetCh(ref tmpEndPts.B, ch, (byte)tmp);
                    else SetCh(ref tmpEndPts.A, ch, (byte)tmp);

                    float fTotalErr = MapColors(pEP, aColors, np, uIndexMode, tmpEndPts, fMinErr);
                    if (fTotalErr < fMinErr)
                    {
                        bImproved = true;
                        fMinErr = fTotalErr;
                        beststep = sign * step;
                    }
                }
                if (bImproved)
                {
                    int curVal = do_b != 0 ? GetCh(newEndPts.B, ch) : GetCh(newEndPts.A, ch);
                    if (do_b != 0) SetCh(ref newEndPts.B, ch, (byte)(curVal + beststep));
                    else SetCh(ref newEndPts.A, ch, (byte)(curVal + beststep));
                }
            }
            return fMinErr;
        }

        private static void Exhaustive(EncodeParams pEP, LDR[] aColors, int np, int uIndexMode, int ch, ref float fOrgErr, ref LDREP optEndPt)
        {
            ref BC7Decoder.ModeInfo info = ref BC7Decoder.ms_aInfo[pEP.uMode];
            int uPrec = GetPrecP(info, ch);
            if (fOrgErr == 0) return;
            const int delta = 5;
            LDREP tmpEndPt = optEndPt;

            byte aOpt = GetCh(optEndPt.A, ch);
            byte bOpt = GetCh(optEndPt.B, ch);
            int alow = Math.Max(0, aOpt - delta);
            int ahigh = Math.Min((1 << uPrec) - 1, aOpt + delta);
            int blow = Math.Max(0, bOpt - delta);
            int bhigh = Math.Min((1 << uPrec) - 1, bOpt + delta);
            int amin = 0, bmin = 0;
            float fBestErr = fOrgErr;

            if (aOpt <= bOpt)
            {
                for (int a = alow; a <= ahigh; a++)
                {
                    for (int b = Math.Max(a, blow); b < bhigh; b++)
                    {
                        SetCh(ref tmpEndPt.A, ch, (byte)a);
                        SetCh(ref tmpEndPt.B, ch, (byte)b);
                        float fErr = MapColors(pEP, aColors, np, uIndexMode, tmpEndPt, fBestErr);
                        if (fErr < fBestErr) { amin = a; bmin = b; fBestErr = fErr; }
                    }
                }
            }
            else
            {
                for (int b = blow; b < bhigh; b++)
                {
                    for (int a = Math.Max(b, alow); a <= ahigh; a++)
                    {
                        SetCh(ref tmpEndPt.A, ch, (byte)a);
                        SetCh(ref tmpEndPt.B, ch, (byte)b);
                        float fErr = MapColors(pEP, aColors, np, uIndexMode, tmpEndPt, fBestErr);
                        if (fErr < fBestErr) { amin = a; bmin = b; fBestErr = fErr; }
                    }
                }
            }

            if (fBestErr < fOrgErr)
            {
                SetCh(ref optEndPt.A, ch, (byte)amin);
                SetCh(ref optEndPt.B, ch, (byte)bmin);
                fOrgErr = fBestErr;
            }
        }

        private static void OptimizeOne(EncodeParams pEP, LDR[] aColors, int np, int uIndexMode, float fOrgErr, LDREP org, out LDREP opt)
        {
            ref BC7Decoder.ModeInfo info = ref BC7Decoder.ms_aInfo[pEP.uMode];
            float fOptErr = fOrgErr;
            opt = org;

            for (int ch = 0; ch < BC7_NUM_CHANNELS; ch++)
            {
                if (GetPrecP(info, ch) == 0) continue;
                float fErr0 = PerturbOne(pEP, aColors, np, uIndexMode, ch, opt, out LDREP new_a, fOptErr, 0);
                float fErr1 = PerturbOne(pEP, aColors, np, uIndexMode, ch, opt, out LDREP new_b, fOptErr, 1);

                int do_b;
                if (fErr0 < fErr1)
                {
                    if (fErr0 >= fOptErr) continue;
                    SetCh(ref opt.A, ch, GetCh(new_a.A, ch));
                    fOptErr = fErr0;
                    do_b = 1;
                }
                else
                {
                    if (fErr1 >= fOptErr) continue;
                    SetCh(ref opt.B, ch, GetCh(new_b.B, ch));
                    fOptErr = fErr1;
                    do_b = 0;
                }

                for (; ; )
                {
                    float fErr = PerturbOne(pEP, aColors, np, uIndexMode, ch, opt, out LDREP newEndPts, fOptErr, do_b);
                    if (fErr >= fOptErr) break;
                    if (do_b == 0) SetCh(ref opt.A, ch, GetCh(newEndPts.A, ch));
                    else SetCh(ref opt.B, ch, GetCh(newEndPts.B, ch));
                    fOptErr = fErr;
                    do_b = 1 - do_b;
                }
            }

            for (int ch = 0; ch < BC7_NUM_CHANNELS; ch++)
                Exhaustive(pEP, aColors, np, uIndexMode, ch, ref fOptErr, ref opt);
        }

        private static void OptimizeEndPoints(EncodeParams pEP, int uShape, int uIndexMode, float[] afOrgErr, LDREP[] aOrgEndPts, LDREP[] aOptEndPts)
        {
            ref BC7Decoder.ModeInfo info = ref BC7Decoder.ms_aInfo[pEP.uMode];
            int uPartitions = info.uPartitions;
            LDR[] aPixels = new LDR[NUM_PIXELS_PER_BLOCK];

            for (int p = 0; p <= uPartitions; p++)
            {
                int np = 0;
                for (int i = 0; i < NUM_PIXELS_PER_BLOCK; i++)
                    if (BC7Decoder.g_aPartitionTable[uPartitions][uShape][i] == p) aPixels[np++] = pEP.aLDRPixels[i];

                OptimizeOne(pEP, aPixels, np, uIndexMode, afOrgErr[p], aOrgEndPts[p], out aOptEndPts[p]);
            }
        }

        // -------------------- EmitBlock --------------------
        private static void EmitBlock(EncodeParams pEP, byte[] block, int blockOff, int uShape, int uRotation, int uIndexMode, LDREP[] aEndPts, int[] aIndex, int[] aIndex2)
        {
            ref BC7Decoder.ModeInfo info = ref BC7Decoder.ms_aInfo[pEP.uMode];
            int uPartitions = info.uPartitions;
            int uPBits = info.uPBits;
            int uIndexPrec = info.uIndexPrec;
            int uIndexPrec2 = info.uIndexPrec2;

            int uStartBit = 0;
            // mode marker: uMode leading zeros + 1 bit set
            SetBits(block, ref uStartBit, pEP.uMode, 0);
            SetBits(block, ref uStartBit, 1, 1);
            SetBits(block, ref uStartBit, info.uRotationBits, (uint)uRotation);
            SetBits(block, ref uStartBit, info.uIndexModeBits, (uint)uIndexMode);
            SetBits(block, ref uStartBit, info.uPartitionBits, (uint)uShape);

            if (uPBits != 0)
            {
                int uNumEP = ((uPartitions + 1) << 1);
                int[] aPVote = new int[BC7_MAX_REGIONS << 1];
                int[] aCount = new int[BC7_MAX_REGIONS << 1];
                int blockOff2 = blockOff;
                // adjust block ref so SetBits operates on (block, blockOff + ...) actually we're using a separate local block
                // here block is the full output array starting at blockOff. We'll write via wrapper below.
                for (int ch = 0; ch < BC7_NUM_CHANNELS; ch++)
                {
                    int prec = GetPrec(info, ch);
                    int precP = GetPrecP(info, ch);
                    int ep = 0;
                    for (int i = 0; i <= uPartitions; i++)
                    {
                        if (prec == precP)
                        {
                            SetBitsAt(block, blockOff, ref uStartBit, prec, GetCh(aEndPts[i].A, ch));
                            SetBitsAt(block, blockOff, ref uStartBit, prec, GetCh(aEndPts[i].B, ch));
                        }
                        else
                        {
                            SetBitsAt(block, blockOff, ref uStartBit, prec, (byte)(GetCh(aEndPts[i].A, ch) >> 1));
                            SetBitsAt(block, blockOff, ref uStartBit, prec, (byte)(GetCh(aEndPts[i].B, ch) >> 1));
                            int idx = ep++ * uPBits / uNumEP;
                            aPVote[idx] += GetCh(aEndPts[i].A, ch) & 1;
                            aCount[idx]++;
                            idx = ep++ * uPBits / uNumEP;
                            aPVote[idx] += GetCh(aEndPts[i].B, ch) & 1;
                            aCount[idx]++;
                        }
                    }
                }
                for (int i = 0; i < uPBits; i++)
                    SetBitsAt(block, blockOff, ref uStartBit, 1, (uint)(aPVote[i] > (aCount[i] >> 1) ? 1 : 0));
            }
            else
            {
                for (int ch = 0; ch < BC7_NUM_CHANNELS; ch++)
                {
                    int prec = GetPrec(info, ch);
                    for (int i = 0; i <= uPartitions; i++)
                    {
                        SetBitsAt(block, blockOff, ref uStartBit, prec, GetCh(aEndPts[i].A, ch));
                        SetBitsAt(block, blockOff, ref uStartBit, prec, GetCh(aEndPts[i].B, ch));
                    }
                }
            }

            int[] aI1 = uIndexMode != 0 ? aIndex2 : aIndex;
            int[] aI2 = uIndexMode != 0 ? aIndex : aIndex2;

            for (int i = 0; i < NUM_PIXELS_PER_BLOCK; i++)
            {
                if (BC7Decoder.IsFixUpOffset(uPartitions, uShape, i))
                    SetBitsAt(block, blockOff, ref uStartBit, uIndexPrec - 1, (uint)aI1[i]);
                else
                    SetBitsAt(block, blockOff, ref uStartBit, uIndexPrec, (uint)aI1[i]);
            }
            if (uIndexPrec2 != 0)
                for (int i = 0; i < NUM_PIXELS_PER_BLOCK; i++)
                    SetBitsAt(block, blockOff, ref uStartBit, i != 0 ? uIndexPrec2 : uIndexPrec2 - 1, (uint)aI2[i]);
        }

        private static void SetBitsAt(byte[] block, int blockOff, ref int uStartBit, int uNumBits, uint uValue)
        {
            if (uNumBits == 0) return;
            int uIndex = blockOff + (uStartBit >> 3);
            int uBase = uStartBit - ((uStartBit >> 3) << 3);
            if (uBase + uNumBits > 8)
            {
                int uFirstIndexBits = 8 - uBase;
                int uNextIndexBits = uNumBits - uFirstIndexBits;
                block[uIndex] &= (byte)~(((1 << uFirstIndexBits) - 1) << uBase);
                block[uIndex] |= (byte)(uValue << uBase);
                block[uIndex + 1] &= (byte)~((1 << uNextIndexBits) - 1);
                block[uIndex + 1] |= (byte)(uValue >> uFirstIndexBits);
            }
            else
            {
                block[uIndex] &= (byte)~(((1 << uNumBits) - 1) << uBase);
                block[uIndex] |= (byte)(uValue << uBase);
            }
            uStartBit += uNumBits;
        }

        // -------------------- Refine --------------------
        private static float Refine(EncodeParams pEP, byte[] outBuf, int outOff, int uShape, int uRotation, int uIndexMode)
        {
            ref BC7Decoder.ModeInfo info = ref BC7Decoder.ms_aInfo[pEP.uMode];
            int uPartitions = info.uPartitions;
            LDREP[] aOrgEndPts = new LDREP[BC7_MAX_REGIONS];
            LDREP[] aOptEndPts = new LDREP[BC7_MAX_REGIONS];
            int[] aOrgIdx = new int[NUM_PIXELS_PER_BLOCK];
            int[] aOrgIdx2 = new int[NUM_PIXELS_PER_BLOCK];
            int[] aOptIdx = new int[NUM_PIXELS_PER_BLOCK];
            int[] aOptIdx2 = new int[NUM_PIXELS_PER_BLOCK];
            float[] aOrgErr = new float[BC7_MAX_REGIONS];
            float[] aOptErr = new float[BC7_MAX_REGIONS];

            for (int p = 0; p <= uPartitions; p++)
            {
                aOrgEndPts[p].A = QuantizeLDR(pEP.aEndPts[uShape, p].A, info);
                aOrgEndPts[p].B = QuantizeLDR(pEP.aEndPts[uShape, p].B, info);
            }

            LDREP[] newEndPts1 = new LDREP[BC7_MAX_REGIONS];
            FixEndpointPBits(pEP, aOrgEndPts, newEndPts1);
            AssignIndices(pEP, uShape, uIndexMode, newEndPts1, aOrgIdx, aOrgIdx2, aOrgErr);
            OptimizeEndPoints(pEP, uShape, uIndexMode, aOrgErr, newEndPts1, aOptEndPts);

            LDREP[] newEndPts2 = new LDREP[BC7_MAX_REGIONS];
            FixEndpointPBits(pEP, aOptEndPts, newEndPts2);
            AssignIndices(pEP, uShape, uIndexMode, newEndPts2, aOptIdx, aOptIdx2, aOptErr);

            float fOrgTotErr = 0, fOptTotErr = 0;
            for (int p = 0; p <= uPartitions; p++) { fOrgTotErr += aOrgErr[p]; fOptTotErr += aOptErr[p]; }

            if (fOptTotErr < fOrgTotErr)
            {
                EmitBlock(pEP, outBuf, outOff, uShape, uRotation, uIndexMode, newEndPts2, aOptIdx, aOptIdx2);
                return fOptTotErr;
            }
            else
            {
                EmitBlock(pEP, outBuf, outOff, uShape, uRotation, uIndexMode, newEndPts1, aOrgIdx, aOrgIdx2);
                return fOrgTotErr;
            }
        }

        // -------------------- Encode (block-level entry) --------------------
        private static void EncodeBlock(EncodeParams pEP, byte[] outBuf, int outOff)
        {
            byte[] finalBlock = new byte[16];
            byte[] tmpBlock = new byte[16];
            float fMSEBest = float.MaxValue;
            int alphaMask = 0xFF;

            for (int i = 0; i < NUM_PIXELS_PER_BLOCK; i++) alphaMask &= pEP.aLDRPixels[i].a;
            bool bHasAlpha = (alphaMask != 0xFF);

            float[] afRoughMSE = new float[BC7_MAX_SHAPES];
            int[] auShape = new int[BC7_MAX_SHAPES];

            for (pEP.uMode = 0; pEP.uMode < C_NUM_MODES && fMSEBest > 0; pEP.uMode++)
            {
                // Skip 3-subset modes (matches DirectXTex's default behavior without BC_FLAGS_USE_3SUBSETS).
                // They are rarely chosen and cost ~5x more time than the rest combined.
                if (pEP.uMode == 0 || pEP.uMode == 2) continue;
                if (!bHasAlpha && pEP.uMode == 7) continue;

                ref BC7Decoder.ModeInfo info = ref BC7Decoder.ms_aInfo[pEP.uMode];
                int uShapes = 1 << info.uPartitionBits;
                int uNumRots = 1 << info.uRotationBits;
                int uNumIdxMode = 1 << info.uIndexModeBits;
                int uItems = Math.Max(1, uShapes >> 2);

                for (int r = 0; r < uNumRots && fMSEBest > 0; r++)
                {
                    if (r == 1) { for (int i = 0; i < NUM_PIXELS_PER_BLOCK; i++) { byte t = pEP.aLDRPixels[i].r; pEP.aLDRPixels[i].r = pEP.aLDRPixels[i].a; pEP.aLDRPixels[i].a = t; float tf = pEP.aHDRPixels[i].r; pEP.aHDRPixels[i].r = pEP.aHDRPixels[i].a; pEP.aHDRPixels[i].a = tf; } }
                    else if (r == 2) { for (int i = 0; i < NUM_PIXELS_PER_BLOCK; i++) { byte t = pEP.aLDRPixels[i].g; pEP.aLDRPixels[i].g = pEP.aLDRPixels[i].a; pEP.aLDRPixels[i].a = t; float tf = pEP.aHDRPixels[i].g; pEP.aHDRPixels[i].g = pEP.aHDRPixels[i].a; pEP.aHDRPixels[i].a = tf; } }
                    else if (r == 3) { for (int i = 0; i < NUM_PIXELS_PER_BLOCK; i++) { byte t = pEP.aLDRPixels[i].b; pEP.aLDRPixels[i].b = pEP.aLDRPixels[i].a; pEP.aLDRPixels[i].a = t; float tf = pEP.aHDRPixels[i].b; pEP.aHDRPixels[i].b = pEP.aHDRPixels[i].a; pEP.aHDRPixels[i].a = tf; } }

                    for (int im = 0; im < uNumIdxMode && fMSEBest > 0; im++)
                    {
                        for (int s = 0; s < uShapes; s++)
                        {
                            afRoughMSE[s] = RoughMSE(pEP, s, im);
                            auShape[s] = s;
                        }
                        for (int i = 0; i < uItems; i++)
                        {
                            for (int j = i + 1; j < uShapes; j++)
                            {
                                if (afRoughMSE[i] > afRoughMSE[j])
                                {
                                    float tmsf = afRoughMSE[i]; afRoughMSE[i] = afRoughMSE[j]; afRoughMSE[j] = tmsf;
                                    int tmsi = auShape[i]; auShape[i] = auShape[j]; auShape[j] = tmsi;
                                }
                            }
                        }
                        for (int i = 0; i < uItems && fMSEBest > 0; i++)
                        {
                            float fMSE = Refine(pEP, tmpBlock, 0, auShape[i], r, im);
                            if (fMSE < fMSEBest)
                            {
                                Array.Copy(tmpBlock, finalBlock, 16);
                                fMSEBest = fMSE;
                            }
                        }
                    }

                    if (r == 1) { for (int i = 0; i < NUM_PIXELS_PER_BLOCK; i++) { byte t = pEP.aLDRPixels[i].r; pEP.aLDRPixels[i].r = pEP.aLDRPixels[i].a; pEP.aLDRPixels[i].a = t; float tf = pEP.aHDRPixels[i].r; pEP.aHDRPixels[i].r = pEP.aHDRPixels[i].a; pEP.aHDRPixels[i].a = tf; } }
                    else if (r == 2) { for (int i = 0; i < NUM_PIXELS_PER_BLOCK; i++) { byte t = pEP.aLDRPixels[i].g; pEP.aLDRPixels[i].g = pEP.aLDRPixels[i].a; pEP.aLDRPixels[i].a = t; float tf = pEP.aHDRPixels[i].g; pEP.aHDRPixels[i].g = pEP.aHDRPixels[i].a; pEP.aHDRPixels[i].a = tf; } }
                    else if (r == 3) { for (int i = 0; i < NUM_PIXELS_PER_BLOCK; i++) { byte t = pEP.aLDRPixels[i].b; pEP.aLDRPixels[i].b = pEP.aLDRPixels[i].a; pEP.aLDRPixels[i].a = t; float tf = pEP.aHDRPixels[i].b; pEP.aHDRPixels[i].b = pEP.aHDRPixels[i].a; pEP.aHDRPixels[i].a = tf; } }
                }
            }

            Array.Copy(finalBlock, 0, outBuf, outOff, 16);
        }

        // -------------------- Public entry --------------------
        public static byte[] CompressBC7(byte[] rgba, int width, int height)
        {
            // Fast path: native DirectXTex encoder (CPU, OpenMP-parallelised).
            byte[]? native = Bc7NativeInterop.CompressBC7(rgba, width, height);
            if (native != null) return native;

            // Managed fallback (still parallel across blocks).
            int bw = (width + 3) / 4;
            int bh = (height + 3) / 4;
            byte[] output = new byte[bw * bh * 16];
            int totalBlocks = bw * bh;

            Parallel.For(0, totalBlocks,
                () => new EncodeParams(),
                (blockIdx, _, pEP) =>
                {
                    int by = blockIdx / bw;
                    int bx = blockIdx % bw;
                    for (int py = 0; py < 4; py++)
                    {
                        for (int px = 0; px < 4; px++)
                        {
                            int sx = Math.Min(bx * 4 + px, width - 1);
                            int sy = Math.Min(by * 4 + py, height - 1);
                            int s = (sy * width + sx) * 4;
                            int t = py * 4 + px;
                            byte r = rgba[s], g = rgba[s + 1], b = rgba[s + 2], a = rgba[s + 3];
                            pEP.aLDRPixels[t] = new LDR { r = r, g = g, b = b, a = a };
                            pEP.aHDRPixels[t] = new HDR { r = r / 255f, g = g / 255f, b = b / 255f, a = a / 255f };
                        }
                    }
                    EncodeBlock(pEP, output, blockIdx * 16);
                    return pEP;
                },
                _ => { });

            return output;
        }
    }
}
