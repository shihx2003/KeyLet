using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;

namespace KeyLet.App.Services;

/// <summary>
/// 基于 ZXing.Net 的本地图片、剪贴板与屏幕截图二维码识别服务。
/// </summary>
public sealed class QrCodeScannerService
{
    private readonly BarcodeReaderGeneric _reader = new()
    {
        AutoRotate = true,
        Options = new DecodingOptions
        {
            TryHarder = true,
            TryInverted = true,
            PossibleFormats = [BarcodeFormat.QR_CODE]
        }
    };

    /// <summary>
    /// 从本地图片文件解析二维码文本。
    /// </summary>
    public string? DecodeFromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        using FileStream stream = File.OpenRead(filePath);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();

        return DecodeFromBitmapSource(bitmap);
    }

    /// <summary>
    /// 从系统剪贴板中的图片解析二维码文本。
    /// </summary>
    public string? DecodeFromClipboard()
    {
        if (!Clipboard.ContainsImage())
        {
            return null;
        }

        BitmapSource? image = Clipboard.GetImage();
        return image is null ? null : DecodeFromBitmapSource(image);
    }

    /// <summary>
    /// 从 WPF BitmapSource 解析二维码文本。
    /// </summary>
    public string? DecodeFromBitmapSource(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        BitmapSource bgraSource = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        int width = bgraSource.PixelWidth;
        int height = bgraSource.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        bgraSource.CopyPixels(pixels, stride, 0);

        Result? result = _reader.Decode(pixels, width, height, RGBLuminanceSource.BitmapFormat.BGRA32);
        return result?.Text;
    }
}
