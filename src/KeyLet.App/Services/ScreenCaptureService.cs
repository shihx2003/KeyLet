using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace KeyLet.App.Services;

/// <summary>
/// 基于 Win32 GDI BitBlt 的屏幕指定区域截图服务（无 System.Drawing 依赖）。
/// </summary>
public static class ScreenCaptureService
{
    private const int Srccopy = 0x00CC0020;

    /// <summary>
    /// 截取屏幕像素坐标系中的指定矩形区域并返回冻结的 BitmapSource。
    /// </summary>
    public static BitmapSource? CaptureRegionPixels(int pixelX, int pixelY, int pixelWidth, int pixelHeight)
    {
        if (pixelWidth <= 4 || pixelHeight <= 4)
        {
            return null;
        }

        nint screenDc = GetDC(0);
        if (screenDc == 0)
        {
            return null;
        }

        nint memoryDc = 0;
        nint bitmap = 0;
        nint oldBitmap = 0;

        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            bitmap = CreateCompatibleBitmap(screenDc, pixelWidth, pixelHeight);
            oldBitmap = SelectObject(memoryDc, bitmap);

            if (!BitBlt(memoryDc, 0, 0, pixelWidth, pixelHeight, screenDc, pixelX, pixelY, Srccopy))
            {
                return null;
            }

            BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap,
                0,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            if (oldBitmap != 0 && memoryDc != 0)
            {
                SelectObject(memoryDc, oldBitmap);
            }

            if (bitmap != 0)
            {
                DeleteObject(bitmap);
            }

            if (memoryDc != 0)
            {
                DeleteDC(memoryDc);
            }

            ReleaseDC(0, screenDc);
        }
    }

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hWnd, nint hDC);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleBitmap(nint hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hdc, nint hgdiobj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint hObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(
        nint hdcDest,
        int nXDest,
        int nYDest,
        int nWidth,
        int nHeight,
        nint hdcSrc,
        int nXSrc,
        int nYSrc,
        int dwRop);
}
