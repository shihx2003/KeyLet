using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace KeyLet.App.Services;

/// <summary>
/// 基于 ZXing.Net 的本地图片、剪贴板（PNG / CF_DIB / 文件）与屏幕截图二维码识别服务。
/// </summary>
public sealed class QrCodeScannerService
{
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".gif"
    };

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
    /// 判断系统剪贴板当前是否包含图片数据或图片文件。
    /// </summary>
    public bool HasClipboardImageOrFile()
    {
        try
        {
            if (Clipboard.ContainsImage())
            {
                return true;
            }

            IDataObject? data = Clipboard.GetDataObject();
            if (data is null)
            {
                return false;
            }

            if (data.GetDataPresent("PNG") || data.GetDataPresent(DataFormats.Dib))
            {
                return true;
            }

            if (Clipboard.ContainsFileDropList())
            {
                StringCollection files = Clipboard.GetFileDropList();
                foreach (string? file in files)
                {
                    if (!string.IsNullOrWhiteSpace(file) &&
                        SupportedImageExtensions.Contains(Path.GetExtension(file)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch (ExternalException ex)
        {
            AppLogger.Warn($"HasClipboardImageOrFile clipboard access failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 从本地图片文件解析二维码文本。
    /// </summary>
    public string? DecodeFromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            using FileStream stream = File.OpenRead(filePath);
            BitmapDecoder decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            if (decoder.Frames.Count == 0)
            {
                return null;
            }

            BitmapFrame frame = decoder.Frames[0];
            frame.Freeze();
            string? decoded = DecodeFromBitmapSource(frame);
            AppLogger.Info($"DecodeFromFile: size={frame.PixelWidth}x{frame.PixelHeight}, decoded={decoded is not null}");
            return decoded;
        }
        catch (Exception ex)
        {
            AppLogger.Error("DecodeFromFile failed.", ex);
            return null;
        }
    }

    /// <summary>
    /// 从系统剪贴板中的图片（PNG 流、图片文件、CF_DIB 或 BitmapSource）解析二维码文本。
    /// </summary>
    public string? DecodeFromClipboard()
    {
        try
        {
            IDataObject? data = Clipboard.GetDataObject();
            if (data is not null)
            {
                AppLogger.Info($"Clipboard formats: {string.Join(", ", data.GetFormats())}");

                // 优先读取无损 PNG 流（Win11 截图工具、微信、浏览器复制图片均会提供）
                if (data.GetDataPresent("PNG") && data.GetData("PNG") is Stream pngStream)
                {
                    BitmapSource? pngBitmap = DecodeStreamToBitmapSource(pngStream);
                    if (pngBitmap is not null)
                    {
                        string? decoded = DecodeFromBitmapSource(pngBitmap);
                        if (!string.IsNullOrWhiteSpace(decoded))
                        {
                            AppLogger.Info("DecodeFromClipboard succeeded via PNG stream.");
                            return decoded;
                        }
                    }
                }

                // 支持在资源管理器中复制图片文件后直接 Ctrl+V
                if (Clipboard.ContainsFileDropList())
                {
                    StringCollection files = Clipboard.GetFileDropList();
                    foreach (string? file in files)
                    {
                        if (!string.IsNullOrWhiteSpace(file) &&
                            SupportedImageExtensions.Contains(Path.GetExtension(file)))
                        {
                            string? decoded = DecodeFromFile(file);
                            if (!string.IsNullOrWhiteSpace(decoded))
                            {
                                AppLogger.Info("DecodeFromClipboard succeeded via FileDrop.");
                                return decoded;
                            }
                        }
                    }
                }

                // 手动补全 BMP 文件头解析 CF_DIB，规避 WPF Clipboard.GetImage() 的 stride/alpha 兼容问题
                if (data.GetDataPresent(DataFormats.Dib) && data.GetData(DataFormats.Dib) is MemoryStream dibStream)
                {
                    BitmapSource? dibBitmap = DecodeDibStreamToBitmapSource(dibStream);
                    if (dibBitmap is not null)
                    {
                        string? decoded = DecodeFromBitmapSource(dibBitmap);
                        if (!string.IsNullOrWhiteSpace(decoded))
                        {
                            AppLogger.Info("DecodeFromClipboard succeeded via CF_DIB.");
                            return decoded;
                        }
                    }
                }
            }

            if (Clipboard.ContainsImage())
            {
                BitmapSource? image = Clipboard.GetImage();
                if (image is not null)
                {
                    string? decoded = DecodeFromBitmapSource(image);
                    AppLogger.Info($"DecodeFromClipboard via Clipboard.GetImage: size={image.PixelWidth}x{image.PixelHeight}, decoded={decoded is not null}");
                    return decoded;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            AppLogger.Error("DecodeFromClipboard failed.", ex);
            return null;
        }
    }

    /// <summary>
    /// 从 WPF BitmapSource 解析二维码文本（自动铺白底去透明通道，并支持小图 2x 放大重试）。
    /// </summary>
    public string? DecodeFromBitmapSource(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        try
        {
            BitmapSource flattened = FlattenToWhiteBgr24(source);
            string? decoded = DecodeBgr24(flattened);
            if (!string.IsNullOrWhiteSpace(decoded))
            {
                return decoded;
            }

            // 对小尺寸二维码（如网页缩小图）进行 2 倍最近邻放大后重试
            if (flattened.PixelWidth < 600 || flattened.PixelHeight < 600)
            {
                var scaled = new TransformedBitmap(flattened, new ScaleTransform(2.0, 2.0));
                scaled.Freeze();
                decoded = DecodeBgr24(FlattenToWhiteBgr24(scaled));
                if (!string.IsNullOrWhiteSpace(decoded))
                {
                    return decoded;
                }
            }

            // 回退尝试 GlobalHistogramBinarizer，适配低对比度或阴影截图
            decoded = DecodeBgr24WithGlobalHistogram(flattened);
            return decoded;
        }
        catch (Exception ex)
        {
            AppLogger.Error("DecodeFromBitmapSource failed.", ex);
            return null;
        }
    }

    private string? DecodeBgr24(BitmapSource bgr24Source)
    {
        byte[] pixels = CopyBgr24Pixels(bgr24Source, out int width, out int height);
        if (pixels.Length == 0)
        {
            return null;
        }

        Result? result = _reader.Decode(pixels, width, height, RGBLuminanceSource.BitmapFormat.BGR24);
        return result?.Text;
    }

    private static string? DecodeBgr24WithGlobalHistogram(BitmapSource bgr24Source)
    {
        byte[] pixels = CopyBgr24Pixels(bgr24Source, out int width, out int height);
        if (pixels.Length == 0)
        {
            return null;
        }

        var luminance = new RGBLuminanceSource(pixels, width, height, RGBLuminanceSource.BitmapFormat.BGR24);
        var binaryBitmap = new BinaryBitmap(new GlobalHistogramBinarizer(luminance));
        var qrReader = new QRCodeReader();
        var hints = new Dictionary<DecodeHintType, object>
        {
            [DecodeHintType.TRY_HARDER] = true
        };
        Result? result = qrReader.decode(binaryBitmap, hints);
        return result?.Text;
    }

    private static byte[] CopyBgr24Pixels(BitmapSource bgr24Source, out int width, out int height)
    {
        width = bgr24Source.PixelWidth;
        height = bgr24Source.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            return Array.Empty<byte>();
        }

        int stride = width * 3;
        byte[] pixels = new byte[stride * height];
        bgr24Source.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    /// <summary>
    /// 将任意像素格式与透明背景的图片统一合成到纯白底，并转换为 BGR24，避免透明 QR 变全黑。
    /// </summary>
    private static BitmapSource FlattenToWhiteBgr24(BitmapSource source)
    {
        int width = source.PixelWidth;
        int height = source.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            return source;
        }

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            dc.DrawImage(source, new Rect(0, 0, width, height));
        }

        var renderTarget = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        renderTarget.Render(visual);
        renderTarget.Freeze();

        var bgr24 = new FormatConvertedBitmap(renderTarget, PixelFormats.Bgr24, null, 0);
        bgr24.Freeze();
        return bgr24;
    }

    private static BitmapSource? DecodeStreamToBitmapSource(Stream stream)
    {
        try
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            BitmapDecoder decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
            {
                return null;
            }

            BitmapFrame frame = decoder.Frames[0];
            frame.Freeze();
            return frame;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"DecodeStreamToBitmapSource failed: {ex.Message}");
            return null;
        }
    }

    private static BitmapSource? DecodeDibStreamToBitmapSource(MemoryStream dibStream)
    {
        try
        {
            byte[] dibBytes = dibStream.ToArray();
            if (dibBytes.Length < 40)
            {
                return null;
            }

            int headerSize = BitConverter.ToInt32(dibBytes, 0);
            int bitCount = BitConverter.ToInt16(dibBytes, 14);
            int colorsUsed = BitConverter.ToInt32(dibBytes, 32);
            int paletteSize = bitCount <= 8
                ? (colorsUsed > 0 ? colorsUsed : 1 << bitCount) * 4
                : 0;
            int pixelDataOffset = 14 + headerSize + paletteSize;
            int totalSize = 14 + dibBytes.Length;

            using var bmpStream = new MemoryStream(totalSize);
            using var writer = new BinaryWriter(bmpStream);
            writer.Write((byte)'B');
            writer.Write((byte)'M');
            writer.Write(totalSize);
            writer.Write(0);
            writer.Write(pixelDataOffset);
            writer.Write(dibBytes);
            writer.Flush();

            bmpStream.Position = 0;
            return DecodeStreamToBitmapSource(bmpStream);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"DecodeDibStreamToBitmapSource failed: {ex.Message}");
            return null;
        }
    }
}
