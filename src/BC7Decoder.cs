////////////////////////////////////////////////////////////////////////
//
// BC7 Decompression - Pure C# Port
// Based on Microsoft DirectXTex BC6HBC7.cpp (MIT License)
//
////////////////////////////////////////////////////////////////////////

using System;

namespace TexFileTypePlugin
{
    internal static class BC7Decoder
    {
        internal const int NUM_PIXELS_PER_BLOCK = 16;
        internal const int BC67_WEIGHT_MAX = 64;
        internal const int BC67_WEIGHT_SHIFT = 6;
        internal const int BC67_WEIGHT_ROUND = 32;

        internal static readonly int[] g_aWeights2 = { 0, 21, 43, 64 };
        internal static readonly int[] g_aWeights3 = { 0, 9, 18, 27, 37, 46, 55, 64 };
        internal static readonly int[] g_aWeights4 = { 0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64 };

        // Partition tables: [region count - 1 (0=1region, 1=2regions, 2=3regions)] [shape 0-63] [pixel 0-15]
        internal static readonly byte[][][] g_aPartitionTable = InitPartitionTable();
        internal static readonly byte[][][] g_aFixUp = InitFixUp();

        // ModeInfo: uPartitions, uPartitionBits, uPBits, uRotationBits, uIndexModeBits, uIndexPrec, uIndexPrec2,
        //          RGBAPrec(r,g,b,a), RGBAPrecWithP(r,g,b,a)
        internal struct ModeInfo
        {
            public byte uPartitions;
            public byte uPartitionBits;
            public byte uPBits;
            public byte uRotationBits;
            public byte uIndexModeBits;
            public byte uIndexPrec;
            public byte uIndexPrec2;
            public byte rPrec, gPrec, bPrec, aPrec;
            public byte rPrecP, gPrecP, bPrecP, aPrecP;
        }

        internal static readonly ModeInfo[] ms_aInfo = new ModeInfo[]
        {
            new ModeInfo{ uPartitions=2, uPartitionBits=4, uPBits=6, uRotationBits=0, uIndexModeBits=0, uIndexPrec=3, uIndexPrec2=0, rPrec=4,gPrec=4,bPrec=4,aPrec=0, rPrecP=5,gPrecP=5,bPrecP=5,aPrecP=0 },
            new ModeInfo{ uPartitions=1, uPartitionBits=6, uPBits=2, uRotationBits=0, uIndexModeBits=0, uIndexPrec=3, uIndexPrec2=0, rPrec=6,gPrec=6,bPrec=6,aPrec=0, rPrecP=7,gPrecP=7,bPrecP=7,aPrecP=0 },
            new ModeInfo{ uPartitions=2, uPartitionBits=6, uPBits=0, uRotationBits=0, uIndexModeBits=0, uIndexPrec=2, uIndexPrec2=0, rPrec=5,gPrec=5,bPrec=5,aPrec=0, rPrecP=5,gPrecP=5,bPrecP=5,aPrecP=0 },
            new ModeInfo{ uPartitions=1, uPartitionBits=6, uPBits=4, uRotationBits=0, uIndexModeBits=0, uIndexPrec=2, uIndexPrec2=0, rPrec=7,gPrec=7,bPrec=7,aPrec=0, rPrecP=8,gPrecP=8,bPrecP=8,aPrecP=0 },
            new ModeInfo{ uPartitions=0, uPartitionBits=0, uPBits=0, uRotationBits=2, uIndexModeBits=1, uIndexPrec=2, uIndexPrec2=3, rPrec=5,gPrec=5,bPrec=5,aPrec=6, rPrecP=5,gPrecP=5,bPrecP=5,aPrecP=6 },
            new ModeInfo{ uPartitions=0, uPartitionBits=0, uPBits=0, uRotationBits=2, uIndexModeBits=0, uIndexPrec=2, uIndexPrec2=2, rPrec=7,gPrec=7,bPrec=7,aPrec=8, rPrecP=7,gPrecP=7,bPrecP=7,aPrecP=8 },
            new ModeInfo{ uPartitions=0, uPartitionBits=0, uPBits=2, uRotationBits=0, uIndexModeBits=0, uIndexPrec=4, uIndexPrec2=0, rPrec=7,gPrec=7,bPrec=7,aPrec=7, rPrecP=8,gPrecP=8,bPrecP=8,aPrecP=8 },
            new ModeInfo{ uPartitions=1, uPartitionBits=6, uPBits=4, uRotationBits=0, uIndexModeBits=0, uIndexPrec=2, uIndexPrec2=0, rPrec=5,gPrec=5,bPrec=5,aPrec=5, rPrecP=6,gPrecP=6,bPrecP=6,aPrecP=6 },
        };

        private static byte[][][] InitPartitionTable()
        {
            byte[][][] t = new byte[3][][];
            // 1 region case - all zeros
            t[0] = new byte[64][];
            for (int i = 0; i < 64; i++) t[0][i] = new byte[16];

            // 2 region partitions (64 shapes)
            t[1] = new byte[][]
            {
                new byte[]{0,0,1,1,0,0,1,1,0,0,1,1,0,0,1,1}, new byte[]{0,0,0,1,0,0,0,1,0,0,0,1,0,0,0,1},
                new byte[]{0,1,1,1,0,1,1,1,0,1,1,1,0,1,1,1}, new byte[]{0,0,0,1,0,0,1,1,0,0,1,1,0,1,1,1},
                new byte[]{0,0,0,0,0,0,0,1,0,0,0,1,0,0,1,1}, new byte[]{0,0,1,1,0,1,1,1,0,1,1,1,1,1,1,1},
                new byte[]{0,0,0,1,0,0,1,1,0,1,1,1,1,1,1,1}, new byte[]{0,0,0,0,0,0,0,1,0,0,1,1,0,1,1,1},
                new byte[]{0,0,0,0,0,0,0,0,0,0,0,1,0,0,1,1}, new byte[]{0,0,1,1,0,1,1,1,1,1,1,1,1,1,1,1},
                new byte[]{0,0,0,0,0,0,0,1,0,1,1,1,1,1,1,1}, new byte[]{0,0,0,0,0,0,0,0,0,0,0,1,0,1,1,1},
                new byte[]{0,0,0,1,0,1,1,1,1,1,1,1,1,1,1,1}, new byte[]{0,0,0,0,0,0,0,0,1,1,1,1,1,1,1,1},
                new byte[]{0,0,0,0,1,1,1,1,1,1,1,1,1,1,1,1}, new byte[]{0,0,0,0,0,0,0,0,0,0,0,0,1,1,1,1},
                new byte[]{0,0,0,0,1,0,0,0,1,1,1,0,1,1,1,1}, new byte[]{0,1,1,1,0,0,0,1,0,0,0,0,0,0,0,0},
                new byte[]{0,0,0,0,0,0,0,0,1,0,0,0,1,1,1,0}, new byte[]{0,1,1,1,0,0,1,1,0,0,0,1,0,0,0,0},
                new byte[]{0,0,1,1,0,0,0,1,0,0,0,0,0,0,0,0}, new byte[]{0,0,0,0,1,0,0,0,1,1,0,0,1,1,1,0},
                new byte[]{0,0,0,0,0,0,0,0,1,0,0,0,1,1,0,0}, new byte[]{0,1,1,1,0,0,1,1,0,0,1,1,0,0,0,1},
                new byte[]{0,0,1,1,0,0,0,1,0,0,0,1,0,0,0,0}, new byte[]{0,0,0,0,1,0,0,0,1,0,0,0,1,1,0,0},
                new byte[]{0,1,1,0,0,1,1,0,0,1,1,0,0,1,1,0}, new byte[]{0,0,1,1,0,1,1,0,0,1,1,0,1,1,0,0},
                new byte[]{0,0,0,1,0,1,1,1,1,1,1,0,1,0,0,0}, new byte[]{0,0,0,0,1,1,1,1,1,1,1,1,0,0,0,0},
                new byte[]{0,1,1,1,0,0,0,1,1,0,0,0,1,1,1,0}, new byte[]{0,0,1,1,1,0,0,1,1,0,0,1,1,1,0,0},
                new byte[]{0,1,0,1,0,1,0,1,0,1,0,1,0,1,0,1}, new byte[]{0,0,0,0,1,1,1,1,0,0,0,0,1,1,1,1},
                new byte[]{0,1,0,1,1,0,1,0,0,1,0,1,1,0,1,0}, new byte[]{0,0,1,1,0,0,1,1,1,1,0,0,1,1,0,0},
                new byte[]{0,0,1,1,1,1,0,0,0,0,1,1,1,1,0,0}, new byte[]{0,1,0,1,0,1,0,1,1,0,1,0,1,0,1,0},
                new byte[]{0,1,1,0,1,0,0,1,0,1,1,0,1,0,0,1}, new byte[]{0,1,0,1,1,0,1,0,1,0,1,0,0,1,0,1},
                new byte[]{0,1,1,1,0,0,1,1,1,1,0,0,1,1,1,0}, new byte[]{0,0,0,1,0,0,1,1,1,1,0,0,1,0,0,0},
                new byte[]{0,0,1,1,0,0,1,0,0,1,0,0,1,1,0,0}, new byte[]{0,0,1,1,1,0,1,1,1,1,0,1,1,1,0,0},
                new byte[]{0,1,1,0,1,0,0,1,1,0,0,1,0,1,1,0}, new byte[]{0,0,1,1,1,1,0,0,1,1,0,0,0,0,1,1},
                new byte[]{0,1,1,0,0,1,1,0,1,0,0,1,1,0,0,1}, new byte[]{0,0,0,0,0,1,1,0,0,1,1,0,0,0,0,0},
                new byte[]{0,1,0,0,1,1,1,0,0,1,0,0,0,0,0,0}, new byte[]{0,0,1,0,0,1,1,1,0,0,1,0,0,0,0,0},
                new byte[]{0,0,0,0,0,0,1,0,0,1,1,1,0,0,1,0}, new byte[]{0,0,0,0,0,1,0,0,1,1,1,0,0,1,0,0},
                new byte[]{0,1,1,0,1,1,0,0,1,0,0,1,0,0,1,1}, new byte[]{0,0,1,1,0,1,1,0,1,1,0,0,1,0,0,1},
                new byte[]{0,1,1,0,0,0,1,1,1,0,0,1,1,1,0,0}, new byte[]{0,0,1,1,1,0,0,1,1,1,0,0,0,1,1,0},
                new byte[]{0,1,1,0,1,1,0,0,1,1,0,0,1,0,0,1}, new byte[]{0,1,1,0,0,0,1,1,0,0,1,1,1,0,0,1},
                new byte[]{0,1,1,1,1,1,1,0,1,0,0,0,0,0,0,1}, new byte[]{0,0,0,1,1,0,0,0,1,1,1,0,0,1,1,1},
                new byte[]{0,0,0,0,1,1,1,1,0,0,1,1,0,0,1,1}, new byte[]{0,0,1,1,0,0,1,1,1,1,1,1,0,0,0,0},
                new byte[]{0,0,1,0,0,0,1,0,1,1,1,0,1,1,1,0}, new byte[]{0,1,0,0,0,1,0,0,0,1,1,1,0,1,1,1},
            };

            // 3 region partitions (64 shapes)
            t[2] = new byte[][]
            {
                new byte[]{0,0,1,1,0,0,1,1,0,2,2,1,2,2,2,2}, new byte[]{0,0,0,1,0,0,1,1,2,2,1,1,2,2,2,1},
                new byte[]{0,0,0,0,2,0,0,1,2,2,1,1,2,2,1,1}, new byte[]{0,2,2,2,0,0,2,2,0,0,1,1,0,1,1,1},
                new byte[]{0,0,0,0,0,0,0,0,1,1,2,2,1,1,2,2}, new byte[]{0,0,1,1,0,0,1,1,0,0,2,2,0,0,2,2},
                new byte[]{0,0,2,2,0,0,2,2,1,1,1,1,1,1,1,1}, new byte[]{0,0,1,1,0,0,1,1,2,2,1,1,2,2,1,1},
                new byte[]{0,0,0,0,0,0,0,0,1,1,1,1,2,2,2,2}, new byte[]{0,0,0,0,1,1,1,1,1,1,1,1,2,2,2,2},
                new byte[]{0,0,0,0,1,1,1,1,2,2,2,2,2,2,2,2}, new byte[]{0,0,1,2,0,0,1,2,0,0,1,2,0,0,1,2},
                new byte[]{0,1,1,2,0,1,1,2,0,1,1,2,0,1,1,2}, new byte[]{0,1,2,2,0,1,2,2,0,1,2,2,0,1,2,2},
                new byte[]{0,0,1,1,0,1,1,2,1,1,2,2,1,2,2,2}, new byte[]{0,0,1,1,2,0,0,1,2,2,0,0,2,2,2,0},
                new byte[]{0,0,0,1,0,0,1,1,0,1,1,2,1,1,2,2}, new byte[]{0,1,1,1,0,0,1,1,2,0,0,1,2,2,0,0},
                new byte[]{0,0,0,0,1,1,2,2,1,1,2,2,1,1,2,2}, new byte[]{0,0,2,2,0,0,2,2,0,0,2,2,1,1,1,1},
                new byte[]{0,1,1,1,0,1,1,1,0,2,2,2,0,2,2,2}, new byte[]{0,0,0,1,0,0,0,1,2,2,2,1,2,2,2,1},
                new byte[]{0,0,0,0,0,0,1,1,0,1,2,2,0,1,2,2}, new byte[]{0,0,0,0,1,1,0,0,2,2,1,0,2,2,1,0},
                new byte[]{0,1,2,2,0,1,2,2,0,0,1,1,0,0,0,0}, new byte[]{0,0,1,2,0,0,1,2,1,1,2,2,2,2,2,2},
                new byte[]{0,1,1,0,1,2,2,1,1,2,2,1,0,1,1,0}, new byte[]{0,0,0,0,0,1,1,0,1,2,2,1,1,2,2,1},
                new byte[]{0,0,2,2,1,1,0,2,1,1,0,2,0,0,2,2}, new byte[]{0,1,1,0,0,1,1,0,2,0,0,2,2,2,2,2},
                new byte[]{0,0,1,1,0,1,2,2,0,1,2,2,0,0,1,1}, new byte[]{0,0,0,0,2,0,0,0,2,2,1,1,2,2,2,1},
                new byte[]{0,0,0,0,0,0,0,2,1,1,2,2,1,2,2,2}, new byte[]{0,2,2,2,0,0,2,2,0,0,1,2,0,0,1,1},
                new byte[]{0,0,1,1,0,0,1,2,0,0,2,2,0,2,2,2}, new byte[]{0,1,2,0,0,1,2,0,0,1,2,0,0,1,2,0},
                new byte[]{0,0,0,0,1,1,1,1,2,2,2,2,0,0,0,0}, new byte[]{0,1,2,0,1,2,0,1,2,0,1,2,0,1,2,0},
                new byte[]{0,1,2,0,2,0,1,2,1,2,0,1,0,1,2,0}, new byte[]{0,0,1,1,2,2,0,0,1,1,2,2,0,0,1,1},
                new byte[]{0,0,1,1,1,1,2,2,2,2,0,0,0,0,1,1}, new byte[]{0,1,0,1,0,1,0,1,2,2,2,2,2,2,2,2},
                new byte[]{0,0,0,0,0,0,0,0,2,1,2,1,2,1,2,1}, new byte[]{0,0,2,2,1,1,2,2,0,0,2,2,1,1,2,2},
                new byte[]{0,0,2,2,0,0,1,1,0,0,2,2,0,0,1,1}, new byte[]{0,2,2,0,1,2,2,1,0,2,2,0,1,2,2,1},
                new byte[]{0,1,0,1,2,2,2,2,2,2,2,2,0,1,0,1}, new byte[]{0,0,0,0,2,1,2,1,2,1,2,1,2,1,2,1},
                new byte[]{0,1,0,1,0,1,0,1,0,1,0,1,2,2,2,2}, new byte[]{0,2,2,2,0,1,1,1,0,2,2,2,0,1,1,1},
                new byte[]{0,0,0,2,1,1,1,2,0,0,0,2,1,1,1,2}, new byte[]{0,0,0,0,2,1,1,2,2,1,1,2,2,1,1,2},
                new byte[]{0,2,2,2,0,1,1,1,0,1,1,1,0,2,2,2}, new byte[]{0,0,0,2,1,1,1,2,1,1,1,2,0,0,0,2},
                new byte[]{0,1,1,0,0,1,1,0,0,1,1,0,2,2,2,2}, new byte[]{0,0,0,0,0,0,0,0,2,1,1,2,2,1,1,2},
                new byte[]{0,1,1,0,0,1,1,0,2,2,2,2,2,2,2,2}, new byte[]{0,0,2,2,0,0,1,1,0,0,1,1,0,0,2,2},
                new byte[]{0,0,2,2,1,1,2,2,1,1,2,2,0,0,2,2}, new byte[]{0,0,0,0,0,0,0,0,0,0,0,0,2,1,1,2},
                new byte[]{0,0,0,2,0,0,0,1,0,0,0,2,0,0,0,1}, new byte[]{0,2,2,2,1,2,2,2,0,2,2,2,1,2,2,2},
                new byte[]{0,1,0,1,2,2,2,2,2,2,2,2,2,2,2,2}, new byte[]{0,1,1,1,2,0,1,1,2,2,0,1,2,2,2,0},
            };
            return t;
        }

        private static byte[][][] InitFixUp()
        {
            byte[][][] t = new byte[3][][];
            // 1 region - no fixup
            t[0] = new byte[64][];
            for (int i = 0; i < 64; i++) t[0][i] = new byte[] { 0, 0, 0 };

            // 2 region fixups
            byte[] sec2 = {
                15,15,15,15, 15,15,15,15, 15,15,15,15, 15,15,15,15,
                15, 2, 8, 2,  2, 8, 8,15,  2, 8, 2, 2,  8, 8, 2, 2,
                15,15, 6, 8,  2, 8,15,15,  2, 8, 2, 2,  2,15,15, 6,
                 6, 2, 6, 8, 15,15, 2, 2, 15,15,15,15, 15, 2, 2,15
            };
            t[1] = new byte[64][];
            for (int i = 0; i < 64; i++) t[1][i] = new byte[] { 0, sec2[i], 0 };

            // 3 region fixups
            byte[,] sec3 = {
                { 3,15},{ 3, 8},{15, 8},{15, 3}, { 8,15},{ 3,15},{15, 3},{15, 8},
                { 8,15},{ 8,15},{ 6,15},{ 6,15}, { 6,15},{ 5,15},{ 3,15},{ 3, 8},
                { 3,15},{ 3, 8},{ 8,15},{15, 3}, { 3,15},{ 3, 8},{ 6,15},{10, 8},
                { 5, 3},{ 8,15},{ 8, 6},{ 6,10}, { 8,15},{ 5,15},{15,10},{15, 8},
                { 8,15},{15, 3},{ 3,15},{ 5,10}, { 6,10},{10, 8},{ 8, 9},{15,10},
                {15, 6},{ 3,15},{15, 8},{ 5,15}, {15, 3},{15, 6},{15, 6},{15, 8},
                { 3,15},{15, 3},{ 5,15},{ 5,15}, { 5,15},{ 8,15},{ 5,15},{10,15},
                { 5,15},{10,15},{ 8,15},{13,15}, {15, 3},{12,15},{ 3,15},{ 3, 8},
            };
            t[2] = new byte[64][];
            for (int i = 0; i < 64; i++) t[2][i] = new byte[] { 0, sec3[i, 0], sec3[i, 1] };
            return t;
        }

        internal static bool IsFixUpOffset(int uPartitions, int uShape, int uOffset)
        {
            for (int p = 0; p <= uPartitions; p++)
                if (uOffset == g_aFixUp[uPartitions][uShape][p]) return true;
            return false;
        }

        // CBits<16> GetBit/GetBits
        private static byte GetBit(byte[] block, int blockOffset, ref int uStartBit)
        {
            int uIndex = uStartBit >> 3;
            byte ret = (byte)((block[blockOffset + uIndex] >> (uStartBit - (uIndex << 3))) & 0x01);
            uStartBit++;
            return ret;
        }

        private static byte GetBits(byte[] block, int blockOffset, ref int uStartBit, int uNumBits)
        {
            if (uNumBits == 0) return 0;
            byte ret;
            int uIndex = uStartBit >> 3;
            int uBase = uStartBit - (uIndex << 3);
            if (uBase + uNumBits > 8)
            {
                int firstIndexBits = 8 - uBase;
                int nextIndexBits = uNumBits - firstIndexBits;
                ret = (byte)(((uint)(block[blockOffset + uIndex] >> uBase)) | (uint)((block[blockOffset + uIndex + 1] & ((1u << nextIndexBits) - 1)) << firstIndexBits));
            }
            else
            {
                ret = (byte)((block[blockOffset + uIndex] >> uBase) & ((1 << uNumBits) - 1));
            }
            uStartBit += uNumBits;
            return ret;
        }

        private static byte Unquantize(byte comp, int uPrec)
        {
            comp = (byte)(comp << (8 - uPrec));
            return (byte)(comp | (comp >> uPrec));
        }

        private static void InterpolateRGB(byte c0r, byte c0g, byte c0b, byte c1r, byte c1g, byte c1b, int wc, int wcprec, out byte or, out byte og, out byte ob)
        {
            int[] aWeights = wcprec == 2 ? g_aWeights2 : (wcprec == 3 ? g_aWeights3 : g_aWeights4);
            int w = aWeights[wc];
            or = (byte)((c0r * (BC67_WEIGHT_MAX - w) + c1r * w + BC67_WEIGHT_ROUND) >> BC67_WEIGHT_SHIFT);
            og = (byte)((c0g * (BC67_WEIGHT_MAX - w) + c1g * w + BC67_WEIGHT_ROUND) >> BC67_WEIGHT_SHIFT);
            ob = (byte)((c0b * (BC67_WEIGHT_MAX - w) + c1b * w + BC67_WEIGHT_ROUND) >> BC67_WEIGHT_SHIFT);
        }

        private static byte InterpolateA(byte c0a, byte c1a, int wa, int waprec)
        {
            int[] aWeights = waprec == 2 ? g_aWeights2 : (waprec == 3 ? g_aWeights3 : g_aWeights4);
            int w = aWeights[wa];
            return (byte)((c0a * (BC67_WEIGHT_MAX - w) + c1a * w + BC67_WEIGHT_ROUND) >> BC67_WEIGHT_SHIFT);
        }

        // Decode one 16-byte BC7 block. Outputs 16 RGBA pixels into rgba at (x,y) for image dims width/height.
        public static void DecodeBlock(byte[] block, int blockOffset, byte[] rgba, int x, int y, int width, int height)
        {
            int uStartBit = 0;

            // find mode (count leading zeros until first 1-bit, then mode = position)
            int uFirst = 0;
            while (uFirst < 128 && GetBit(block, blockOffset, ref uStartBit) == 0) uFirst++;
            int uMode = uFirst;

            if (uMode >= 8)
            {
                // reserved mode: transparent black
                for (int py = 0; py < 4; py++)
                    for (int px = 0; px < 4; px++)
                    {
                        if (x + px >= width || y + py >= height) continue;
                        int pIdx = ((y + py) * width + (x + px)) * 4;
                        rgba[pIdx] = 0; rgba[pIdx + 1] = 0; rgba[pIdx + 2] = 0; rgba[pIdx + 3] = 0;
                    }
                return;
            }

            ref ModeInfo info = ref ms_aInfo[uMode];
            int uPartitions = info.uPartitions;
            int uNumEndPts = (uPartitions + 1) << 1;
            int uIndexPrec = info.uIndexPrec;
            int uIndexPrec2 = info.uIndexPrec2;

            byte[] cr = new byte[6], cg = new byte[6], cb = new byte[6], ca = new byte[6];

            int uShape = GetBits(block, blockOffset, ref uStartBit, info.uPartitionBits);
            int uRotation = GetBits(block, blockOffset, ref uStartBit, info.uRotationBits);
            int uIndexMode = GetBits(block, blockOffset, ref uStartBit, info.uIndexModeBits);

            for (int i = 0; i < uNumEndPts; i++) cr[i] = GetBits(block, blockOffset, ref uStartBit, info.rPrec);
            for (int i = 0; i < uNumEndPts; i++) cg[i] = GetBits(block, blockOffset, ref uStartBit, info.gPrec);
            for (int i = 0; i < uNumEndPts; i++) cb[i] = GetBits(block, blockOffset, ref uStartBit, info.bPrec);
            for (int i = 0; i < uNumEndPts; i++) ca[i] = info.aPrec != 0 ? GetBits(block, blockOffset, ref uStartBit, info.aPrec) : (byte)255;

            byte[] P = new byte[6];
            for (int i = 0; i < info.uPBits; i++) P[i] = GetBit(block, blockOffset, ref uStartBit);

            if (info.uPBits != 0)
            {
                for (int i = 0; i < uNumEndPts; i++)
                {
                    int pi = i * info.uPBits / uNumEndPts;
                    if (info.rPrec != info.rPrecP) cr[i] = (byte)((cr[i] << 1) | P[pi]);
                    if (info.gPrec != info.gPrecP) cg[i] = (byte)((cg[i] << 1) | P[pi]);
                    if (info.bPrec != info.bPrecP) cb[i] = (byte)((cb[i] << 1) | P[pi]);
                    if (info.aPrec != info.aPrecP) ca[i] = (byte)((ca[i] << 1) | P[pi]);
                }
            }

            for (int i = 0; i < uNumEndPts; i++)
            {
                cr[i] = Unquantize(cr[i], info.rPrecP);
                cg[i] = Unquantize(cg[i], info.gPrecP);
                cb[i] = Unquantize(cb[i], info.bPrecP);
                ca[i] = info.aPrecP > 0 ? Unquantize(ca[i], info.aPrecP) : (byte)255;
            }

            byte[] w1 = new byte[NUM_PIXELS_PER_BLOCK];
            byte[] w2 = new byte[NUM_PIXELS_PER_BLOCK];

            for (int i = 0; i < NUM_PIXELS_PER_BLOCK; i++)
            {
                int uNumBits = IsFixUpOffset(uPartitions, uShape, i) ? (uIndexPrec - 1) : uIndexPrec;
                w1[i] = GetBits(block, blockOffset, ref uStartBit, uNumBits);
            }

            if (uIndexPrec2 != 0)
            {
                for (int i = 0; i < NUM_PIXELS_PER_BLOCK; i++)
                {
                    int uNumBits = (i != 0) ? uIndexPrec2 : (uIndexPrec2 - 1);
                    w2[i] = GetBits(block, blockOffset, ref uStartBit, uNumBits);
                }
            }

            for (int i = 0; i < NUM_PIXELS_PER_BLOCK; i++)
            {
                int uRegion = g_aPartitionTable[uPartitions][uShape][i];
                int ep0 = uRegion << 1;
                int ep1 = (uRegion << 1) + 1;

                byte outR, outG, outB, outA;
                if (uIndexPrec2 == 0)
                {
                    InterpolateRGB(cr[ep0], cg[ep0], cb[ep0], cr[ep1], cg[ep1], cb[ep1], w1[i], uIndexPrec, out outR, out outG, out outB);
                    outA = InterpolateA(ca[ep0], ca[ep1], w1[i], uIndexPrec);
                }
                else
                {
                    int wc = uIndexMode == 0 ? w1[i] : w2[i];
                    int wa = uIndexMode == 0 ? w2[i] : w1[i];
                    int pc = uIndexMode == 0 ? uIndexPrec : uIndexPrec2;
                    int pa = uIndexMode == 0 ? uIndexPrec2 : uIndexPrec;
                    InterpolateRGB(cr[ep0], cg[ep0], cb[ep0], cr[ep1], cg[ep1], cb[ep1], wc, pc, out outR, out outG, out outB);
                    outA = InterpolateA(ca[ep0], ca[ep1], wa, pa);
                }

                switch (uRotation)
                {
                    case 1: { byte t = outR; outR = outA; outA = t; break; }
                    case 2: { byte t = outG; outG = outA; outA = t; break; }
                    case 3: { byte t = outB; outB = outA; outA = t; break; }
                }

                int py = i >> 2, px = i & 3;
                if (x + px >= width || y + py >= height) continue;
                int pIdx = ((y + py) * width + (x + px)) * 4;
                rgba[pIdx] = outR;
                rgba[pIdx + 1] = outG;
                rgba[pIdx + 2] = outB;
                rgba[pIdx + 3] = outA;
            }
        }

        public static byte[] DecompressBC7(byte[] data, int width, int height)
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
                        DecodeBlock(data, off, rgba, bx * 4, by * 4, width, height);
                }
            }
            return rgba;
        }
    }
}
