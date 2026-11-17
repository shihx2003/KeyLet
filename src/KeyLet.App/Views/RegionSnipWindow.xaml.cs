using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KeyLet.App.Services;

namespace KeyLet.App.Views;

/// <summary>
/// 全屏透明框选遮罩窗口：允许用户框选屏幕任意区域截图并识别二维码。
/// </summary>
public partial class RegionSnipWindow : Window
{
    private Point _startPoint;
    private bool _isDragging;

    public RegionSnipWindow()
    {
        InitializeComponent();
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
            DialogResult = false;
            Close();
        }
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
        double dipX = Math.Min(_startPoint.X, current.X) + Left;
        double dipY = Math.Min(_startPoint.Y, current.Y) + Top;
        double dipW = Math.Abs(current.X - _startPoint.X);
        double dipH = Math.Abs(current.Y - _startPoint.Y);

        if (dipW < 12 || dipH < 12)
        {
            SelectionBorder.Visibility = Visibility.Collapsed;
            return;
        }

        Matrix transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        int pixelX = (int)Math.Round(dipX * transform.M11);
        int pixelY = (int)Math.Round(dipY * transform.M22);
        int pixelW = (int)Math.Round(dipW * transform.M11);
        int pixelH = (int)Math.Round(dipH * transform.M22);

        Hide();
        Thread.Sleep(60);

        CapturedImage = ScreenCaptureService.CaptureRegionPixels(pixelX, pixelY, pixelW, pixelH);
        DialogResult = CapturedImage is not null;
        Close();
    }
}
