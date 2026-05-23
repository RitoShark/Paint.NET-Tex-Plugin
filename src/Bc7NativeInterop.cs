using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace TexFileTypePlugin
{
    /// <summary>
    /// P/Invoke wrapper for the bundled Bc7Native.dll (DirectXTex CPU BC7 encoder).
    /// Gracefully falls back to the managed encoder if the DLL is unavailable.
    /// </summary>
    internal static class Bc7NativeInterop
    {
        private const string DllName = "Bc7Native.dll";
        private static readonly bool s_available = ProbeAvailable();

        public static bool Available => s_available;

        // DirectXTex flag bits we pass through (declared in DirectXTex.h)
        public const uint TEX_COMPRESS_BC7_USE_3SUBSETS = 0x80000;
        public const uint TEX_COMPRESS_BC7_QUICK        = 0x100000;

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int EncodeBC7Image(
            [Out] byte[] outBlocks,
            [In]  byte[] inRgba,
            uint width,
            uint height,
            uint flags);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int EncodeBC7ImageGPU(
            [Out] byte[] outBlocks,
            [In]  byte[] inRgba,
            uint width,
            uint height,
            uint flags);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int GpuAvailable();

        private static readonly bool s_gpu = s_available && SafeGpuProbe();
        private static bool SafeGpuProbe() { try { return GpuAvailable() != 0; } catch { return false; } }

        public static bool GpuPathAvailable => s_gpu;

        public static byte[]? CompressBC7(byte[] rgba, int width, int height, uint flags = 0)
        {
            if (!s_available) return null;
            int bw = (width + 3) / 4, bh = (height + 3) / 4;
            byte[] output = new byte[bw * bh * 16];

            if (s_gpu && EncodeBC7ImageGPU(output, rgba, (uint)width, (uint)height, flags) != 0)
                return output;

            // GPU not available or failed — fall back to native CPU encoder.
            if (EncodeBC7Image(output, rgba, (uint)width, (uint)height, flags) != 0)
                return output;

            return null;
        }

        private static bool ProbeAvailable()
        {
            try
            {
                // Load the DLL from next to our managed assembly so the loader can resolve it.
                string? asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (asmDir == null) return false;
                string dllPath = Path.Combine(asmDir, DllName);
                if (!File.Exists(dllPath)) return false;
                IntPtr h = NativeLibrary.Load(dllPath);
                return h != IntPtr.Zero;
            }
            catch { return false; }
        }
    }
}
