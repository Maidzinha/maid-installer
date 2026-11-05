using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MaidInstaller.App.Services;

/// <summary>
/// Obtém o ícone real de um item do Shell (inclusive apps da Microsoft Store)
/// via IShellItemImageFactory. Deve ser chamado numa thread STA; o resultado é congelado
/// (Freeze) e pode ser usado na thread de UI.
/// </summary>
public static partial class ShellIconLoader
{
    public static BitmapSource? Load(string shellPath, int size = 64)
    {
        IntPtr hBitmap = IntPtr.Zero;
        try
        {
            SHCreateItemFromParsingName(shellPath, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var factory);
            try
            {
                const int SIIGBF_BIGGERSIZEOK = 0x1, SIIGBF_ICONONLY = 0x4;
                if (factory.GetImage(new NativeSize { cx = size, cy = size }, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out hBitmap) != 0)
                    return null;
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
            return ToBitmapSource(hBitmap);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or FileNotFoundException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
        }
    }

    /// <summary>Copia os pixels (32 bpp, de cima para baixo) preservando a transparência.</summary>
    private static BitmapSource? ToBitmapSource(IntPtr hBitmap)
    {
        if (GetObject(hBitmap, Marshal.SizeOf<NativeBitmap>(), out var bm) == 0 || bm.bmWidth <= 0 || bm.bmHeight == 0)
            return null;
        int width = bm.bmWidth, height = Math.Abs(bm.bmHeight);
        var header = new BitmapInfoHeader
        {
            biSize = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            biWidth = width,
            biHeight = -height, // negativo = top-down
            biPlanes = 1,
            biBitCount = 32,
        };
        var pixels = new byte[width * height * 4];
        var hdc = GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(hdc, hBitmap, 0, (uint)height, pixels, ref header, 0) == 0) return null;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }

        // Ícones antigos sem canal alfa: torna-os opacos em vez de invisíveis.
        var hasAlpha = false;
        for (var i = 3; i < pixels.Length; i += 4) if (pixels[i] != 0) { hasAlpha = true; break; }
        if (!hasAlpha) for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
        source.Freeze();
        return source;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int cx; public int cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string pszPath, IntPtr pbc, [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr hgdiobj, int cbBuffer, out NativeBitmap lpvObject);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines,
        [Out] byte[] lpvBits, ref BitmapInfoHeader lpbi, uint uUsage);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
}
