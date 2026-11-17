using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace KeyLet.App.Services;

/// <summary>
/// 基于 Win32 GDI BitBlt 的全虚拟屏幕与指定区域截图服务（无 System.Drawing 依赖）。
/// </summary>
public static class ScreenCaptureService
{
    private const int Srccopy = 0x00CC0020;
    private const int Captureblt = 0x40000000;

    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    /// <summary>
    /// 截取整个虚拟桌面（支持多显示器与高 DPI 物理像素）并返回冻结的 BitmapSource。
    /// </summary>
    public static BitmapSource? CaptureVirtualScreen()
    {
        int x = GetSystemMetrics(SmXVirtualScreen);
        int y = GetSystemMetrics(SmYVirtualScreen);
        int width = GetSystemMetrics(SmCxVirtualScreen);
        int height = GetSystemMetrics(SmCyVirtualScreen);

        AppLogger.Info($"CaptureVirtualScreen bounds: X={x}, Y={y}, W={width}, H={height}");
        return CaptureRegionPixels(x, y, width, height);
    }

    /// <summary>
    /// 截取屏幕物理像素坐标系中的指定矩形区域并返回冻结的 BitmapSource。
    /// </summary>
    public static BitmapSource? CaptureRegionPixels(int pixelX, int pixelY, int pixelWidth, int pixelHeight)
    {
        if (pixelWidth <= 4 || pixelHeight <= 4)
        {
            AppLogger.Warn($"CaptureRegionPixels skipped invalid size: {pixelWidth}x{pixelHeight}");
            return null;
        }

        nint screenDc = GetDC(0);
        if (screenDc == 0)
        {
            AppLogger.Error("GetDC(0) returned null handle.");
            return null;
        }

        nint memoryDc = 0;
        nint bitmap = 0;
        nint oldBitmap = 0;

        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            if (memoryDc == 0)
            {
                AppLogger.Error($"CreateCompatibleDC failed, Win32Error={Marshal.GetLastWin32Error()}");
                return null;
            }

            bitmap = CreateCompatibleBitmap(screenDc, pixelWidth, pixelHeight);
            if (bitmap == 0)
            {
                AppLogger.Error($"CreateCompatibleBitmap failed, Win32Error={Marshal.GetLastWin32Error()}");
                return null;
            }

            oldBitmap = SelectObject(memoryDc, bitmap);

            if (!BitBlt(memoryDc, 0, 0, pixelWidth, pixelHeight, screenDc, pixelX, pixelY, Srccopy | Captureblt))
            {
                AppLogger.Error($"BitBlt failed, Win32Error={Marshal.GetLastWin32Error()}");
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
        catch (Exception ex)
        {
            AppLogger.Error("CaptureRegionPixels threw an exception.", ex);
            return null;
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
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hWnd, nint hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateCompatibleBitmap(nint hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hdc, nint hgdiobj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
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
