using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using KeyLet.App.Services;

namespace KeyLet.App.Views;

/// <summary>
/// 冻结屏幕快照框选窗口：在预截取的全屏画面上框选矩形并精确裁剪出二维码区域。
/// </summary>
public partial class RegionSnipWindow : Window
{
    private readonly BitmapSource _screenSnapshot;
    private Point _startPoint;
    private bool _isDragging;

    public RegionSnipWindow(BitmapSource screenSnapshot)
    {
        _screenSnapshot = screenSnapshot ?? throw new ArgumentNullException(nameof(screenSnapshot));

        InitializeComponent();
        SnapshotImage.Source = _screenSnapshot;

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }

    public BitmapSource? CapturedImage { get; private set; }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelCapture();
        }
    }

    private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        CancelCapture();
    }

    private void CancelCapture()
    {
        if (_isDragging)
        {
            _isDragging = false;
            OverlayCanvas.ReleaseMouseCapture();
        }

        DialogResult = false;
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        _startPoint = e.GetPosition(OverlayCanvas);
        OverlayCanvas.CaptureMouse();

        Canvas.SetLeft(SelectionBorder, _startPoint.X);
        Canvas.SetTop(SelectionBorder, _startPoint.Y);
        SelectionBorder.Width = 0;
        SelectionBorder.Height = 0;
        SelectionBorder.Visibility = Visibility.Visible;
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        Point current = e.GetPosition(OverlayCanvas);
        double x = Math.Min(_startPoint.X, current.X);
        double y = Math.Min(_startPoint.Y, current.Y);
        double w = Math.Abs(current.X - _startPoint.X);
        double h = Math.Abs(current.Y - _startPoint.Y);

        Canvas.SetLeft(SelectionBorder, x);
        Canvas.SetTop(SelectionBorder, y);
        SelectionBorder.Width = w;
        SelectionBorder.Height = h;
    }

    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        OverlayCanvas.ReleaseMouseCapture();

        Point current = e.GetPosition(OverlayCanvas);
        double dipX = Math.Min(_startPoint.X, current.X);
        double dipY = Math.Min(_startPoint.Y, current.Y);
        double dipW = Math.Abs(current.X - _startPoint.X);
        double dipH = Math.Abs(current.Y - _startPoint.Y);

        if (dipW < 12 || dipH < 12 || OverlayCanvas.ActualWidth <= 0 || OverlayCanvas.ActualHeight <= 0)
        {
            SelectionBorder.Visibility = Visibility.Collapsed;
            return;
        }

        // 按画布比例映射到真实像素坐标，兼容任意高 DPI 与多显示器缩放
        double scaleX = _screenSnapshot.PixelWidth / OverlayCanvas.ActualWidth;
        double scaleY = _screenSnapshot.PixelHeight / OverlayCanvas.ActualHeight;

        int pixelX = Math.Clamp((int)Math.Floor(dipX * scaleX), 0, _screenSnapshot.PixelWidth - 1);
        int pixelY = Math.Clamp((int)Math.Floor(dipY * scaleY), 0, _screenSnapshot.PixelHeight - 1);
        int pixelW = Math.Clamp((int)Math.Ceiling(dipW * scaleX), 1, _screenSnapshot.PixelWidth - pixelX);
        int pixelH = Math.Clamp((int)Math.Ceiling(dipH * scaleY), 1, _screenSnapshot.PixelHeight - pixelY);

        try
        {
            var cropped = new CroppedBitmap(_screenSnapshot, new Int32Rect(pixelX, pixelY, pixelW, pixelH));
            cropped.Freeze();
            CapturedImage = cropped;
            AppLogger.Info($"RegionSnip cropped region: X={pixelX}, Y={pixelY}, W={pixelW}, H={pixelH}");
            DialogResult = true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("CroppedBitmap failed in RegionSnipWindow.", ex);
            DialogResult = false;
        }
    }
}
