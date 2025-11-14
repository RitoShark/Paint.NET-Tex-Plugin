using PaintDotNet;
using PaintDotNet.Imaging;
using PaintDotNet.Rendering;
using System;
using System.IO;

namespace TexFileTypePlugin
{
    public class TexFileTypeDialogFactory : IFileTypeFactory
    {
        public FileType[] GetFileTypeInstances()
        {
            return new FileType[] { new TexFileTypeDialog() };
        }
    }

    internal class TexFileTypeDialog : FileType
    {
        private static readonly System.Collections.Generic.Dictionary<int, byte> documentFormats = new();

        public TexFileTypeDialog()
            : base(
                "League of Legends TEX (with options)",
                new FileTypeOptions
                {
                    LoadExtensions = new string[] { ".tex" },
                    SaveExtensions = new string[] { ".tex" }
                })
        {
        }

        protected override Document OnLoad(Stream input)
        {
            byte[] data = new byte[input.Length];
            input.Read(data, 0, data.Length);

            TexFile tex = TexFile.Read(data);
            byte[] rgba = tex.DecompressToRgba();

            Document doc = new Document(tex.Width, tex.Height);
            BitmapLayer layer = new BitmapLayer(tex.Width, tex.Height);

            Surface surface = layer.Surface;
            for (int y = 0; y < tex.Height; y++)
            {
                for (int x = 0; x < tex.Width; x++)
                {
                    int idx = (y * tex.Width + x) * 4;
                    byte r = rgba[idx];
                    byte g = rgba[idx + 1];
                    byte b = rgba[idx + 2];
                    byte a = rgba[idx + 3];
                    surface[x, y] = ColorBgra.FromBgra(b, g, r, a);
                }
            }

            doc.Layers.Add(layer);
            documentFormats[doc.GetHashCode()] = tex.Format;
            return doc;
        }

        protected override SaveConfigToken OnCreateDefaultSaveConfigToken()
        {
            return new TexSaveConfigToken();
        }

        public override SaveConfigWidget? CreateSaveConfigWidget()
        {
            return new TexSaveConfigWidget();
        }

        protected override void OnSave(Document input, Stream output, SaveConfigToken? saveConfigToken, Surface scratchSurface, ProgressEventHandler? progressCallback)
        {
            input.Flatten(scratchSurface);

            int width = scratchSurface.Width;
            int height = scratchSurface.Height;

            byte format = 12; // Default to DXT5
            
            if (saveConfigToken is TexSaveConfigToken token)
            {
                format = token.CompressionFormat;
            }
            else if (documentFormats.TryGetValue(input.GetHashCode(), out byte savedFormat))
            {
                format = savedFormat;
            }

            byte[] data;
            
            if (format == 20)
            {
                byte[] bgra = new byte[width * height * 4];
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        ColorBgra pixel = scratchSurface[x, y];
                        int idx = (y * width + x) * 4;
                        bgra[idx] = pixel.B;
                        bgra[idx + 1] = pixel.G;
                        bgra[idx + 2] = pixel.R;
                        bgra[idx + 3] = pixel.A;
                    }
                }
                data = bgra;
            }
            else
            {
                if (width % 4 != 0 || height % 4 != 0)
                {
                    string errorMsg = $"Image dimensions must be divisible by 4 for DXT compression.\n\n" +
                                    $"Current size: {width}x{height}\n" +
                                    $"Width: {width} (needs to be {((width + 3) / 4) * 4})\n" +
                                    $"Height: {height} (needs to be {((height + 3) / 4) * 4})\n\n" +
                                    $"Please resize your image to dimensions divisible by 4.\n" +
                                    $"Examples: 1024x1024, 2048x2048, 512x256, etc.";
                    
                    throw new FormatException(errorMsg);
                }

                byte[] rgba = new byte[width * height * 4];
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        ColorBgra pixel = scratchSurface[x, y];
                        int idx = (y * width + x) * 4;
                        rgba[idx] = pixel.R;
                        rgba[idx + 1] = pixel.G;
                        rgba[idx + 2] = pixel.B;
                        rgba[idx + 3] = pixel.A;
                    }
                }

                if (format == 10)
                {
                    data = CompressDxt1Native(rgba, width, height);
                }
                else
                {
                    data = CompressDxt5Native(rgba, width, height);
                }
            }

            TexFile tex = new TexFile
            {
                Width = (ushort)width,
                Height = (ushort)height,
                Format = format,
                Mipmaps = false,
                Data = data
            };

            byte[] fileData = tex.Write();
            output.Write(fileData, 0, fileData.Length);
        }

        private byte[] CompressDxt1Native(byte[] rgba, int width, int height)
        {
            int blockWidth = (width + 3) / 4;
            int blockHeight = (height + 3) / 4;
            byte[] output = new byte[blockWidth * blockHeight * 8];

            for (int by = 0; by < blockHeight; by++)
            {
                for (int bx = 0; bx < blockWidth; bx++)
                {
                    int blockOffset = (by * blockWidth + bx) * 8;
                    CompressDxt1Block(rgba, width, height, bx * 4, by * 4, output, blockOffset);
                }
            }

            return output;
        }

        private void CompressDxt1Block(byte[] rgba, int width, int height, int blockX, int blockY, byte[] output, int offset)
        {
            byte[] colors = new byte[48];

            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    int px = blockX + x;
                    int py = blockY + y;
                    int idx = y * 4 + x;

                    if (px < width && py < height)
                    {
                        int pixelIdx = (py * width + px) * 4;
                        colors[idx * 3] = rgba[pixelIdx];
                        colors[idx * 3 + 1] = rgba[pixelIdx + 1];
                        colors[idx * 3 + 2] = rgba[pixelIdx + 2];
                    }
                    else
                    {
                        colors[idx * 3] = 0;
                        colors[idx * 3 + 1] = 0;
                        colors[idx * 3 + 2] = 0;
                    }
                }
            }

            CompressColorBlock(colors, output, offset);
        }

        private byte[] CompressDxt5Native(byte[] rgba, int width, int height)
        {
            int blockWidth = (width + 3) / 4;
            int blockHeight = (height + 3) / 4;
            byte[] output = new byte[blockWidth * blockHeight * 16];

            for (int by = 0; by < blockHeight; by++)
            {
                for (int bx = 0; bx < blockWidth; bx++)
                {
                    int blockOffset = (by * blockWidth + bx) * 16;
                    CompressDxt5Block(rgba, width, height, bx * 4, by * 4, output, blockOffset);
                }
            }

            return output;
        }

        private void CompressDxt5Block(byte[] rgba, int width, int height, int blockX, int blockY, byte[] output, int offset)
        {
            byte[] alphas = new byte[16];
            byte[] colors = new byte[48];

            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    int px = blockX + x;
                    int py = blockY + y;
                    int idx = y * 4 + x;

                    if (px < width && py < height)
                    {
                        int pixelIdx = (py * width + px) * 4;
                        colors[idx * 3] = rgba[pixelIdx];
                        colors[idx * 3 + 1] = rgba[pixelIdx + 1];
                        colors[idx * 3 + 2] = rgba[pixelIdx + 2];
                        alphas[idx] = rgba[pixelIdx + 3];
                    }
                    else
                    {
                        colors[idx * 3] = 0;
                        colors[idx * 3 + 1] = 0;
                        colors[idx * 3 + 2] = 0;
                        alphas[idx] = 255;
                    }
                }
            }

            CompressAlphaBlock(alphas, output, offset);
            CompressColorBlock(colors, output, offset + 8);
        }

        private void CompressAlphaBlock(byte[] alphas, byte[] output, int offset)
        {
            byte minAlpha = 255;
            byte maxAlpha = 0;
            foreach (byte a in alphas)
            {
                if (a < minAlpha) minAlpha = a;
                if (a > maxAlpha) maxAlpha = a;
            }

            output[offset] = maxAlpha;
            output[offset + 1] = minAlpha;

            byte[] alphaPalette = new byte[8];
            alphaPalette[0] = maxAlpha;
            alphaPalette[1] = minAlpha;

            if (maxAlpha > minAlpha)
            {
                for (int i = 1; i < 7; i++)
                {
                    alphaPalette[i + 1] = (byte)(((7 - i) * maxAlpha + i * minAlpha) / 7);
                }
            }
            else
            {
                for (int i = 1; i < 5; i++)
                {
                    alphaPalette[i + 1] = (byte)(((5 - i) * maxAlpha + i * minAlpha) / 5);
                }
                alphaPalette[6] = 0;
                alphaPalette[7] = 255;
            }

            ulong bits = 0;
            for (int i = 0; i < 16; i++)
            {
                int bestIndex = 0;
                int bestDiff = Math.Abs(alphas[i] - alphaPalette[0]);
                for (int j = 1; j < 8; j++)
                {
                    int diff = Math.Abs(alphas[i] - alphaPalette[j]);
                    if (diff < bestDiff)
                    {
                        bestDiff = diff;
                        bestIndex = j;
                    }
                }
                bits |= ((ulong)bestIndex << (i * 3));
            }

            for (int i = 0; i < 6; i++)
            {
                output[offset + 2 + i] = (byte)(bits >> (i * 8));
            }
        }

        private void CompressColorBlock(byte[] colors, byte[] output, int offset)
        {
            int minR = 255, minG = 255, minB = 255;
            int maxR = 0, maxG = 0, maxB = 0;

            for (int i = 0; i < 16; i++)
            {
                int r = colors[i * 3];
                int g = colors[i * 3 + 1];
                int b = colors[i * 3 + 2];

                if (r < minR) minR = r;
                if (g < minG) minG = g;
                if (b < minB) minB = b;
                if (r > maxR) maxR = r;
                if (g > maxG) maxG = g;
                if (b > maxB) maxB = b;
            }

            ushort color0 = (ushort)(((maxR >> 3) << 11) | ((maxG >> 2) << 5) | (maxB >> 3));
            ushort color1 = (ushort)(((minR >> 3) << 11) | ((minG >> 2) << 5) | (minB >> 3));

            if (color0 < color1)
            {
                ushort temp = color0;
                color0 = color1;
                color1 = temp;
            }

            output[offset] = (byte)(color0 & 0xFF);
            output[offset + 1] = (byte)(color0 >> 8);
            output[offset + 2] = (byte)(color1 & 0xFF);
            output[offset + 3] = (byte)(color1 >> 8);

            byte[] palette = new byte[12];
            palette[0] = (byte)maxR; palette[1] = (byte)maxG; palette[2] = (byte)maxB;
            palette[3] = (byte)minR; palette[4] = (byte)minG; palette[5] = (byte)minB;
            palette[6] = (byte)((maxR * 2 + minR) / 3);
            palette[7] = (byte)((maxG * 2 + minG) / 3);
            palette[8] = (byte)((maxB * 2 + minB) / 3);
            palette[9] = (byte)((maxR + minR * 2) / 3);
            palette[10] = (byte)((maxG + minG * 2) / 3);
            palette[11] = (byte)((maxB + minB * 2) / 3);

            uint bits = 0;
            for (int i = 0; i < 16; i++)
            {
                int r = colors[i * 3];
                int g = colors[i * 3 + 1];
                int b = colors[i * 3 + 2];

                int bestIndex = 0;
                int bestDiff = int.MaxValue;
                for (int j = 0; j < 4; j++)
                {
                    int dr = r - palette[j * 3];
                    int dg = g - palette[j * 3 + 1];
                    int db = b - palette[j * 3 + 2];
                    int diff = dr * dr + dg * dg + db * db;
                    if (diff < bestDiff)
                    {
                        bestDiff = diff;
                        bestIndex = j;
                    }
                }
                bits |= ((uint)bestIndex << (i * 2));
            }

            output[offset + 4] = (byte)(bits & 0xFF);
            output[offset + 5] = (byte)((bits >> 8) & 0xFF);
            output[offset + 6] = (byte)((bits >> 16) & 0xFF);
            output[offset + 7] = (byte)((bits >> 24) & 0xFF);
        }
    }
}
