using System;
using System.Collections.Generic;
using System.IO;

namespace TexFileTypePlugin
{
    internal class TexFile
    {
        public ushort Width { get; set; }
        public ushort Height { get; set; }
        public byte Format { get; set; }
        public bool Mipmaps { get; set; }
        public byte[] Data { get; set; }

        public const byte DXT1 = 10;
        public const byte DXT5 = 12;
        public const byte BGRA8 = 20;

        public static TexFile Read(byte[] data)
        {
            using (MemoryStream ms = new MemoryStream(data))
            using (BinaryReader br = new BinaryReader(ms))
            {
                uint signature = br.ReadUInt32();
                if (signature != 0x00584554)
                {
                    throw new FormatException($"Invalid TEX signature: 0x{signature:X8}");
                }

                TexFile tex = new TexFile
                {
                    Width = br.ReadUInt16(),
                    Height = br.ReadUInt16()
                };

                byte unknown1 = br.ReadByte();
                tex.Format = br.ReadByte();
                byte unknown2 = br.ReadByte();
                tex.Mipmaps = br.ReadBoolean();

                if (tex.Mipmaps && (tex.Format == DXT1 || tex.Format == DXT5 || tex.Format == BGRA8))
                {
                    int maxDim = Math.Max(tex.Width, tex.Height);
                    int mipmapCount = 0;
                    while (maxDim > 0)
                    {
                        mipmapCount++;
                        maxDim >>= 1;
                    }
                    
                    List<byte[]> mipmaps = new List<byte[]>();
                    for (int i = mipmapCount - 1; i >= 0; i--)
                    {
                        int mipWidth = Math.Max(tex.Width >> i, 1);
                        int mipHeight = Math.Max(tex.Height >> i, 1);
                        
                        int mipSize;
                        if (tex.Format == DXT1)
                        {
                            int blockWidth = (mipWidth + 3) / 4;
                            int blockHeight = (mipHeight + 3) / 4;
                            mipSize = blockWidth * blockHeight * 8;
                        }
                        else if (tex.Format == DXT5)
                        {
                            int blockWidth = (mipWidth + 3) / 4;
                            int blockHeight = (mipHeight + 3) / 4;
                            mipSize = blockWidth * blockHeight * 16;
                        }
                        else
                        {
                            mipSize = mipWidth * mipHeight * 4;
                        }
                        
                        byte[] mipData = br.ReadBytes(mipSize);
                        mipmaps.Add(mipData);
                    }
                    
                    tex.Data = mipmaps[mipmaps.Count - 1];
                }
                else
                {
                    int mainTextureSize;
                    if (tex.Format == DXT1)
                    {
                        int blockWidth = (tex.Width + 3) / 4;
                        int blockHeight = (tex.Height + 3) / 4;
                        mainTextureSize = blockWidth * blockHeight * 8;
                    }
                    else if (tex.Format == DXT5)
                    {
                        int blockWidth = (tex.Width + 3) / 4;
                        int blockHeight = (tex.Height + 3) / 4;
                        mainTextureSize = blockWidth * blockHeight * 16;
                    }
                    else if (tex.Format == BGRA8)
                    {
                        mainTextureSize = tex.Width * tex.Height * 4;
                    }
                    else
                    {
                        mainTextureSize = (int)(ms.Length - ms.Position);
                    }
                    
                    tex.Data = br.ReadBytes(mainTextureSize);
                }

                return tex;
            }
        }

        public byte[] Write()
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                bw.Write((uint)0x00584554);
                bw.Write(Width);
                bw.Write(Height);
                bw.Write((byte)1);
                bw.Write(Format);
                bw.Write((byte)0);
                bw.Write(Mipmaps);
                bw.Write(Data);

                return ms.ToArray();
            }
        }

        public byte[] DecompressToRgba()
        {
            if (Format == BGRA8)
            {
                return DecompressBgra8();
            }
            else if (Format == DXT1)
            {
                return DecompressDxt1();
            }
            else if (Format == DXT5)
            {
                return DecompressDxt5();
            }
            else
            {
                throw new FormatException($"Unsupported format: {Format}");
            }
        }

        private byte[] DecompressBgra8()
        {
            byte[] rgba = new byte[Width * Height * 4];
            for (int i = 0; i < Data.Length && i < rgba.Length; i += 4)
            {
                rgba[i] = Data[i + 2];
                rgba[i + 1] = Data[i + 1];
                rgba[i + 2] = Data[i];
                rgba[i + 3] = Data[i + 3];
            }
            return rgba;
        }

        private byte[] DecompressDxt1()
        {
            byte[] rgba = new byte[Width * Height * 4];
            int blockWidth = (Width + 3) / 4;
            int blockHeight = (Height + 3) / 4;

            for (int by = 0; by < blockHeight; by++)
            {
                for (int bx = 0; bx < blockWidth; bx++)
                {
                    int blockIdx = (by * blockWidth + bx) * 8;
                    if (blockIdx + 8 <= Data.Length)
                    {
                        DecompressDxt1Block(Data, blockIdx, bx * 4, by * 4, rgba);
                    }
                }
            }

            return rgba;
        }

        private byte[] DecompressDxt5()
        {
            byte[] rgba = new byte[Width * Height * 4];
            int blockWidth = (Width + 3) / 4;
            int blockHeight = (Height + 3) / 4;

            for (int by = 0; by < blockHeight; by++)
            {
                for (int bx = 0; bx < blockWidth; bx++)
                {
                    int blockIdx = (by * blockWidth + bx) * 16;
                    if (blockIdx + 16 <= Data.Length)
                    {
                        DecompressDxt5Block(Data, blockIdx, bx * 4, by * 4, rgba);
                    }
                }
            }

            return rgba;
        }

        private void DecompressDxt1Block(byte[] data, int offset, int x, int y, byte[] pixels)
        {
            ushort color0 = (ushort)(data[offset] | (data[offset + 1] << 8));
            ushort color1 = (ushort)(data[offset + 2] | (data[offset + 3] << 8));
            uint colorBits = (uint)(data[offset + 4] | (data[offset + 5] << 8) | (data[offset + 6] << 16) | (data[offset + 7] << 24));

            byte r0 = (byte)(((color0 >> 11) & 0x1F) << 3);
            byte g0 = (byte)(((color0 >> 5) & 0x3F) << 2);
            byte b0 = (byte)((color0 & 0x1F) << 3);
            byte r1 = (byte)(((color1 >> 11) & 0x1F) << 3);
            byte g1 = (byte)(((color1 >> 5) & 0x3F) << 2);
            byte b1 = (byte)((color1 & 0x1F) << 3);

            byte[][] colors = new byte[4][];
            colors[0] = new byte[] { r0, g0, b0, 255 };
            colors[1] = new byte[] { r1, g1, b1, 255 };

            if (color0 > color1)
            {
                colors[2] = new byte[] { (byte)((r0 * 2 + r1) / 3), (byte)((g0 * 2 + g1) / 3), (byte)((b0 * 2 + b1) / 3), 255 };
                colors[3] = new byte[] { (byte)((r0 + r1 * 2) / 3), (byte)((g0 + g1 * 2) / 3), (byte)((b0 + b1 * 2) / 3), 255 };
            }
            else
            {
                colors[2] = new byte[] { (byte)((r0 + r1) / 2), (byte)((g0 + g1) / 2), (byte)((b0 + b1) / 2), 255 };
                colors[3] = new byte[] { 0, 0, 0, 0 };
            }

            for (int py = 0; py < 4; py++)
            {
                for (int px = 0; px < 4; px++)
                {
                    if (x + px < Width && y + py < Height)
                    {
                        int idx = py * 4 + px;
                        int colorIdx = (int)((colorBits >> (idx * 2)) & 3);
                        int pixelIdx = ((y + py) * Width + (x + px)) * 4;
                        pixels[pixelIdx] = colors[colorIdx][0];
                        pixels[pixelIdx + 1] = colors[colorIdx][1];
                        pixels[pixelIdx + 2] = colors[colorIdx][2];
                        pixels[pixelIdx + 3] = colors[colorIdx][3];
                    }
                }
            }
        }

        private void DecompressDxt5Block(byte[] data, int offset, int x, int y, byte[] pixels)
        {
            byte alpha0 = data[offset];
            byte alpha1 = data[offset + 1];
            ulong alphaBits = 0;
            for (int i = 0; i < 6; i++)
            {
                alphaBits |= ((ulong)data[offset + 2 + i] << (i * 8));
            }

            byte[] alphas = new byte[8];
            alphas[0] = alpha0;
            alphas[1] = alpha1;
            if (alpha0 > alpha1)
            {
                for (int i = 1; i < 7; i++)
                {
                    alphas[i + 1] = (byte)(((7 - i) * alpha0 + i * alpha1) / 7);
                }
            }
            else
            {
                for (int i = 1; i < 5; i++)
                {
                    alphas[i + 1] = (byte)(((5 - i) * alpha0 + i * alpha1) / 5);
                }
                alphas[6] = 0;
                alphas[7] = 255;
            }

            ushort color0 = (ushort)(data[offset + 8] | (data[offset + 9] << 8));
            ushort color1 = (ushort)(data[offset + 10] | (data[offset + 11] << 8));
            uint colorBits = (uint)(data[offset + 12] | (data[offset + 13] << 8) | (data[offset + 14] << 16) | (data[offset + 15] << 24));

            byte r0 = (byte)(((color0 >> 11) & 0x1F) << 3);
            byte g0 = (byte)(((color0 >> 5) & 0x3F) << 2);
            byte b0 = (byte)((color0 & 0x1F) << 3);
            byte r1 = (byte)(((color1 >> 11) & 0x1F) << 3);
            byte g1 = (byte)(((color1 >> 5) & 0x3F) << 2);
            byte b1 = (byte)((color1 & 0x1F) << 3);

            byte[][] colors = new byte[4][];
            colors[0] = new byte[] { r0, g0, b0 };
            colors[1] = new byte[] { r1, g1, b1 };
            colors[2] = new byte[] { (byte)((r0 * 2 + r1) / 3), (byte)((g0 * 2 + g1) / 3), (byte)((b0 * 2 + b1) / 3) };
            colors[3] = new byte[] { (byte)((r0 + r1 * 2) / 3), (byte)((g0 + g1 * 2) / 3), (byte)((b0 + b1 * 2) / 3) };

            for (int py = 0; py < 4; py++)
            {
                for (int px = 0; px < 4; px++)
                {
                    if (x + px < Width && y + py < Height)
                    {
                        int idx = py * 4 + px;
                        int alphaIdx = (int)((alphaBits >> (idx * 3)) & 7);
                        int colorIdx = (int)((colorBits >> (idx * 2)) & 3);
                        int pixelIdx = ((y + py) * Width + (x + px)) * 4;
                        pixels[pixelIdx] = colors[colorIdx][0];
                        pixels[pixelIdx + 1] = colors[colorIdx][1];
                        pixels[pixelIdx + 2] = colors[colorIdx][2];
                        pixels[pixelIdx + 3] = alphas[alphaIdx];
                    }
                }
            }
        }
    }
}
