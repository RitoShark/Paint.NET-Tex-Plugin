using PaintDotNet;
using System;

namespace TexFileTypePlugin
{
    [Serializable]
    public enum CompressionType
    {
        DXT1_BC1 = 10,
        DXT5_BC3 = 12,
        RGBA8_Uncompressed = 20
    }

    [Serializable]
    public class TexSaveConfigToken : SaveConfigToken
    {
        public CompressionType Compression { get; set; } = CompressionType.DXT5_BC3;

        public TexSaveConfigToken()
        {
        }

        protected TexSaveConfigToken(TexSaveConfigToken copyMe)
            : base(copyMe)
        {
            Compression = copyMe.Compression;
        }

        public override object Clone()
        {
            return new TexSaveConfigToken(this);
        }

        public byte CompressionFormat => (byte)Compression;
    }
}
