// Scan a Riot .tex BC7 file and tally which BC7 modes are used.
// Usage: dotnet script Bc7ModeStats.csx <path-to-tex>

using System;
using System.IO;

string path = Args[0];
byte[] data = File.ReadAllBytes(path);

// TEX header: 4 bytes sig, 2 width, 2 height, 1 unk, 1 format, 1 unk, 1 mipFlag
if (data.Length < 12 || BitConverter.ToUInt32(data, 0) != 0x00584554)
{
    Console.WriteLine("Not a TEX file");
    return;
}
int width = BitConverter.ToUInt16(data, 4);
int height = BitConverter.ToUInt16(data, 6);
byte format = data[9];
bool hasMips = data[11] != 0;
Console.WriteLine($"{width}x{height} format={format} mips={hasMips}");

if (format != 13) { Console.WriteLine("Not BC7"); return; }

// Skip header (12 bytes). Compute offset of top-mip = end of file - top-mip size (if mips).
int bw = (width + 3) / 4, bh = (height + 3) / 4;
int topMipBytes = bw * bh * 16;
int topMipStart = data.Length - topMipBytes;

int[] modeCounts = new int[9]; // 0..7 plus 8 = reserved

for (int i = 0; i < bw * bh; i++)
{
    int off = topMipStart + i * 16;
    int mode = -1;
    for (int b = 0; b < 8; b++)
    {
        if (((data[off] >> b) & 1) != 0) { mode = b; break; }
    }
    if (mode < 0) mode = 8;
    modeCounts[mode]++;
}

int total = bw * bh;
Console.WriteLine($"Total blocks: {total}");
for (int m = 0; m < 9; m++)
{
    if (modeCounts[m] == 0) continue;
    string label = m == 8 ? "RESERVED" : $"Mode {m}";
    Console.WriteLine($"  {label,-9}: {modeCounts[m],6}  ({100.0 * modeCounts[m] / total:F1}%)");
}
