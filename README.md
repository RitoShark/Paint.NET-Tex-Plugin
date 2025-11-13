# Paint.NET TEX Plugin

Load and save League of Legends .tex texture files in Paint.NET 5.1.11 (latest)

## Features
- Seamless installation wizard, includes [Tex Previews](https://github.com/RitoShark/TexThumbnailProvider) in the installer.
- Easy uninstall through the installation wizard.
- Load DXT1, DXT5, and BGRA8 texture formats.
- Fast DXT compression/decompression using native C#.
- Full alpha channel support.

## Requirements

- Paint.NET 5.1.11 (latest)
- .NET 9.0 SDK (for building)
- Windows 10 or later

## Installation

### Wizard Installation (RECOMMENDED)

1. Close Paint.NET
2. Run **Tex Plugin Installer.msi** and follow the installation wizard
3. Finish the installation with the wizard and start Paint.NET
- Note! Windows Explorer will restart to apply the changes if you select the Tex File Preview during the installation.

### Manual Installation

1. Close Paint.NET
2. Open `C:\Program Files\Paint.NET\FileTypes`
3. Paste **TexFileType.dll** from the source code into this folder
4. Start Paint.NET
- Note! This method doesn't include the Tex File Previews, if you want them please go to [TexThumbnailProvider](https://github.com/RitoShark/TexThumbnailProvider) and follow the instructions.

## Usage

### Opening TEX Files

- File → Open → Select your .tex file
- Drag and drop .tex files into Paint.NET

### Saving TEX Files

1. File → Save As
2. Choose "League of Legends TEX" from the file type dropdown

## Troubleshooting

**Plugin doesn't appear in Paint.NET:**
- Make sure Paint.NET is completely closed
- Check the FileTypes folder exists: `C:\Program Files\Paint.NET`
- Verify **TexFileType.dll** is in the `FileTypes` folder

**"There was an error while saving the file." error when saving:**
- Make sure that the dimensions of the file are divisible by 4. This is  a requirement for it to work in League.

## Credits

Made using LtMAO's tex logic. Thanks to Tarngaina.
- GitHub: https://github.com/tarngaina/LtMAO