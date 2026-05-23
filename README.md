# Paint.NET TEX Plugin

Load and save League of Legends .tex texture files in Paint.NET

## Features
- Load and save DXT1, DXT5, BC5, BC7, and BGRA8 texture formats.
- Auto detects the compression, mipmaps, alpha and saves accordingly.
- Second saving option with a menu to manually pick the compression, error diffusion dithering and mipmap generation.
- Fast DXT compression/decompression using native C#.
- Fast GPU-accelerated BC7 saving.
- Great error diffusion dithering quality.
- Full alpha channel support.

## Requirements

- Latest Paint.NET version (.NET 9)
- .NET 9.0 SDK (for building)
- Windows 10 or later

## Installation

### Installer
1. Close Paint.NET
2. Run **TexFileTypeSetup.exe** from the releases
3. Go through the installation. The installer finds your Paint.NET FileTypes folder automatically. You can change the destination if needed.
4. Start Paint.NET

### Manual
1. Close Paint.NET
2. Open `C:\Program Files\Paint.NET\FileTypes`
3. Paste **both** `TexFileType.dll` and `Bc7Native.dll` from the releases zip into this folder
4. Start Paint.NET
- Portable version note! In order to install the plugin on the portable version of Paint.NET please paste both dll files into `[Paint.NET folder]\FileTypes`
- Keep both dll files together. `Bc7Native.dll` is what makes BC7 saving fast. Without it BC7 still works but saves very slow.

## Usage

### Opening TEX Files

- File → Open → Select your .tex file
- Drag and drop .tex files into Paint.NET

### Saving TEX Files

1. File → Save As
2. Choose "League of Legends TEX" from the file type dropdown

### Saving TEX Files with customizable settings
1. File → Save As
2. Choose League of Legends TEX (with options) from the file type dropdown

## Compression formats

Not sure which one to pick? Here's a quick rundown:

- **DXT1 / BC1** — smallest file size. No alpha channel. Use for opaque textures where you want the file to be as small as possible.
- **DXT5 / BC3** — twice the size of DXT1. Has full alpha. The classic choice for anything with transparency.
- **BC7** — same size as DXT5 but way higher quality, works on champions. This is what Riot uses for most newer UI atlases.
- **BC5** — two channels only (red and green). Used for normal maps. Same size as DXT5.
- **BGRA8/Uncompressed** — no compression at all, perfect quality. Takes 4× more space than DXT5. Use only when you need lossless quality and don't care about file size.

### Other options

- **Error Diffusion Dithering** (DXT1/DXT5 only) — spreads out color quantization errors across nearby pixels using Floyd-Steinberg. Helps a lot on smooth gradients where DXT compression normally shows visible banding. Turn it on unless you have a reason not to.
- **Error Metric** (DXT1/DXT5 only):
  - **Perceptual** — weights the green channel higher since your eyes are more sensitive to green. Better for normal photo-like textures. Default.
  - **Uniform** — treats R, G and B equally. Better for non-color data like normal maps or masks where you don't want green to be favored.
- **Generate MipMaps** — saves smaller pre-resized copies of the texture alongside the main one making it 33% bigger. League engine scales .tex files on it's own if mipmaps are missing, enable only if you have a reason to.

## Troubleshooting

**Paint.NET crashes or the file doesn't load**
- Make sure you're running the latest version of Paint.NET

**Plugin doesn't appear in Paint.NET:**
- Make sure you're running the latest version of Paint.NET
- Make sure Paint.NET is completely closed
- Check the FileTypes folder exists: `C:\Program Files\Paint.NET`
- Verify both **TexFileType.dll** and **Bc7Native.dll** are in the `FileTypes` folder

**BC7 saving is slow:**
- Make sure `Bc7Native.dll` is in the FileTypes folder next to `TexFileType.dll`

**"There was an error while saving the file." error when saving:**
- Make sure that the dimensions of the file are divisible by 4. This is a requirement for it to work in League.

## Credits

### Uses LtMAO's tex reading logic. Thanks to Tarngaina.
- GitHub: https://github.com/tarngaina/LtMAO

### Compression and Error Diffusion Dithering Logic
- BC1/BC3/BC5/BC7 compression and decompression is based on [Microsoft DirectXTex](https://github.com/microsoft/DirectXTex) (MIT License).
