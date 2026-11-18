using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KeyLet.App.Services;
using KeyLet.App.ViewModels;
using KeyLet.Core.Models;
using Wpf.Ui.Controls;

namespace KeyLet.App.Views;

/// <summary>
/// KeyLet 主界面窗口：实时展示 TOTP 动态码、处理多模式复制、拖拽排序与快捷键导入交互。
/// </summary>
public partial class MainWindow : FluentWindow
{
    private const string AccountDragFormat = "KeyLetAccountCard";

    private readonly MainViewModel _viewModel;
    private readonly QrCodeScannerService _qrScanner;
    private readonly DispatcherTimer _timer;

    private Point? _dragStartPoint;
    private AccountCardViewModel? _draggedCard;
    private AccountCardViewModel? _hoveredDropCard;
    private bool _isDraggingCard;

    public MainWindow(MainViewModel viewModel, QrCodeScannerService qrScanner)
    {
        _viewModel = viewModel;
        _qrScanner = qrScanner;

        InitializeComponent();
        DataContext = _viewModel;

        _timer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _timer.Tick += (_, _) => _viewModel.RefreshAllCodes();
        _timer.Start();

        Closed += (_, _) => _timer.Stop();
    }

    private void AddAccountButton_Click(object sender, RoutedEventArgs e)
    {
        OpenEditorForAdd(prefilledQrPayload: null);
    }

    private async void QuickScreenScanButton_Click(object sender, RoutedEventArgs e)
    {
        string? qrText = null;
        try
        {
            Hide();
            await Task.Delay(220);

            System.Windows.Media.Imaging.BitmapSource? fullSnapshot = ScreenCaptureService.CaptureVirtualScreen();
            if (fullSnapshot is null)
            {
                _viewModel.StatusMessage = "屏幕截图失败，详情请查看 data/logs";
                return;
            }

            var snipWindow = new RegionSnipWindow(fullSnapshot);
            if (snipWindow.ShowDialog() == true && snipWindow.CapturedImage is not null)
            {
                qrText = _qrScanner.DecodeFromBitmapSource(snipWindow.CapturedImage);
                AppLogger.Info($"MainWindow screen scan decoded={qrText is not null}");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("QuickScreenScanButton_Click failed.", ex);
            _viewModel.StatusMessage = $"屏幕扫码失败：{ex.Message}";
            return;
        }
        finally
        {
            Show();
            Activate();
        }

        if (string.IsNullOrWhiteSpace(qrText))
        {
            _viewModel.StatusMessage = "未在框选区域中识别到有效二维码";
            return;
        }

        OpenEditorForAdd(qrText);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_viewModel)
        {
            Owner = this
        };
        settingsWindow.ShowDialog();
    }

    private void Card_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindAncestor<ButtonBase>(source) is not null)
        {
            _dragStartPoint = null;
            _draggedCard = null;
            return;
        }

        if (sender is FrameworkElement element && element.DataContext is AccountCardViewModel card)
        {
            _dragStartPoint = e.GetPosition(null);
            _draggedCard = card;
            _isDraggingCard = false;
        }
    }

    private void Card_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            _draggedCard is null ||
            !_dragStartPoint.HasValue ||
            _isDraggingCard ||
            sender is not Border border)
        {
            return;
        }

        Point current = e.GetPosition(null);
        double dx = Math.Abs(current.X - _dragStartPoint.Value.X);
        double dy = Math.Abs(current.Y - _dragStartPoint.Value.Y);
        double thresholdX = Math.Max(6, SystemParameters.MinimumHorizontalDragDistance);
        double thresholdY = Math.Max(6, SystemParameters.MinimumVerticalDragDistance);

        if (dx < thresholdX && dy < thresholdY)
        {
            return;
        }

        _isDraggingCard = true;
        AccountCardViewModel dragged = _draggedCard;
        double originalOpacity = border.Opacity;
        border.Opacity = 0.55;

        try
        {
            DragDrop.DoDragDrop(border, new DataObject(AccountDragFormat, dragged), DragDropEffects.Move);
        }
        finally
        {
            border.Opacity = originalOpacity;
            ClearDropIndicator();
            _dragStartPoint = null;
            _draggedCard = null;
            _isDraggingCard = false;
        }
    }

    private void Card_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(AccountDragFormat))
        {
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        AutoScrollDuringDrag(e.GetPosition(AccountsScrollViewer));

        if (sender is FrameworkElement border &&
            border.DataContext is AccountCardViewModel targetCard &&
            e.Data.GetData(AccountDragFormat) is AccountCardViewModel sourceCard)
        {
            if (sourceCard.Id == targetCard.Id)
            {
                ClearDropIndicator();
                return;
            }

            bool insertAfter = e.GetPosition(border).Y > border.ActualHeight / 2.0;
            SetDropIndicator(targetCard, insertAfter);
        }
    }

    private void Card_DragLeave(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(AccountDragFormat))
        {
            return;
        }

        if (sender is FrameworkElement border &&
            border.DataContext is AccountCardViewModel targetCard &&
            _hoveredDropCard?.Id == targetCard.Id)
        {
            ClearDropIndicator();
        }
    }

    private void Card_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(AccountDragFormat))
        {
            return;
        }

        e.Handled = true;
        ClearDropIndicator();

        if (sender is FrameworkElement border &&
            border.DataContext is AccountCardViewModel targetCard &&
            e.Data.GetData(AccountDragFormat) is AccountCardViewModel sourceCard &&
            sourceCard.Id != targetCard.Id)
        {
            bool insertAfter = e.GetPosition(border).Y > border.ActualHeight / 2.0;
            _viewModel.MoveAccount(sourceCard, targetCard, insertAfter);
        }
    }

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingCard)
        {
            return;
        }

        _dragStartPoint = null;
        _draggedCard = null;

        if (sender is FrameworkElement element && element.DataContext is AccountCardViewModel card)
        {
            bool alternate = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            _viewModel.CopyAccount(card, alternate);
        }
    }

    private void SetDropIndicator(AccountCardViewModel targetCard, bool insertAfter)
    {
        if (_hoveredDropCard is not null && _hoveredDropCard.Id != targetCard.Id)
        {
            _hoveredDropCard.IsDropAbove = false;
            _hoveredDropCard.IsDropBelow = false;
        }

        _hoveredDropCard = targetCard;
        targetCard.IsDropAbove = !insertAfter;
        targetCard.IsDropBelow = insertAfter;
    }

    private void ClearDropIndicator()
    {
        if (_hoveredDropCard is not null)
        {
            _hoveredDropCard.IsDropAbove = false;
            _hoveredDropCard.IsDropBelow = false;
            _hoveredDropCard = null;
        }
    }

    private void AutoScrollDuringDrag(Point positionInViewer)
    {
        const double edgeThreshold = 36;
        const double scrollStep = 14;

        if (positionInViewer.Y < edgeThreshold)
        {
            AccountsScrollViewer.ScrollToVerticalOffset(Math.Max(0, AccountsScrollViewer.VerticalOffset - scrollStep));
        }
        else if (positionInViewer.Y > AccountsScrollViewer.ActualHeight - edgeThreshold)
        {
            AccountsScrollViewer.ScrollToVerticalOffset(
                Math.Min(AccountsScrollViewer.ScrollableHeight, AccountsScrollViewer.VerticalOffset + scrollStep));
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void CopyCodeOnlyButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (ExtractCard(sender) is AccountCardViewModel card)
        {
            _viewModel.CopyWithMode(card, CopyFormatMode.CodeOnly);
        }
    }

    private void CopyWithAffixButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (ExtractCard(sender) is AccountCardViewModel card)
        {
            _viewModel.CopyWithMode(card, CopyFormatMode.WithAffix);
        }
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (ExtractCard(sender) is AccountCardViewModel card)
        {
            OpenEditorForEdit(card);
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (ExtractCard(sender) is AccountCardViewModel card)
        {
            ConfirmAndDelete(card);
        }
    }

    private void MenuCopyCodeOnly_Click(object sender, RoutedEventArgs e)
    {
        if (ExtractCard(sender) is AccountCardViewModel card)
        {
            _viewModel.CopyWithMode(card, CopyFormatMode.CodeOnly);
        }
    }

    private void MenuCopyWithAffix_Click(object sender, RoutedEventArgs e)
    {
        if (ExtractCard(sender) is AccountCardViewModel card)
        {
            _viewModel.CopyWithMode(card, CopyFormatMode.WithAffix);
        }
    }

    private void MenuEdit_Click(object sender, RoutedEventArgs e)
    {
        if (ExtractCard(sender) is AccountCardViewModel card)
        {
            OpenEditorForEdit(card);
        }
    }

    private void MenuDelete_Click(object sender, RoutedEventArgs e)
    {
        if (ExtractCard(sender) is AccountCardViewModel card)
        {
            ConfirmAndDelete(card);
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
        {
            return;
        }

        string? qrText = _qrScanner.DecodeFromFile(files[0]);
        if (string.IsNullOrWhiteSpace(qrText))
        {
            _viewModel.StatusMessage = "未在拖入的图片中识别到有效二维码";
            return;
        }

        OpenEditorForAdd(qrText);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && _qrScanner.HasClipboardImageOrFile())
        {
            e.Handled = true;
            string? qrText = _qrScanner.DecodeFromClipboard();
            if (string.IsNullOrWhiteSpace(qrText))
            {
                _viewModel.StatusMessage = "剪贴板图片中未识别到有效二维码（可查看 data/logs）";
                return;
            }

            OpenEditorForAdd(qrText);
        }
    }

    private void OpenEditorForAdd(string? prefilledQrPayload)
    {
        var editor = new AccountEditorWindow(
            _qrScanner,
            _viewModel.Settings.GlobalDefaultCopyMode,
            editingAccount: null,
            prefilledQrPayload: prefilledQrPayload)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true && editor.ResultAccounts.Count > 0)
        {
            _viewModel.AddAccounts(editor.ResultAccounts);
            OtpAccount first = editor.ResultAccounts[0];
            string label = string.IsNullOrWhiteSpace(first.Issuer) ? first.AccountName : first.Issuer;
            _viewModel.StatusMessage = editor.ResultAccounts.Count == 1
                ? $"已添加「{label}」"
                : $"已批量导入 {editor.ResultAccounts.Count} 个账号";
        }
    }

    private void OpenEditorForEdit(AccountCardViewModel card)
    {
        var editor = new AccountEditorWindow(
            _qrScanner,
            _viewModel.Settings.GlobalDefaultCopyMode,
            editingAccount: card.Account)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true && editor.ResultAccounts.Count > 0)
        {
            OtpAccount updated = editor.ResultAccounts[0];
            string label = string.IsNullOrWhiteSpace(updated.Issuer) ? updated.AccountName : updated.Issuer;
            _viewModel.UpdateAccount(updated);
            _viewModel.StatusMessage = $"已更新「{label}」";
        }
    }

    private void ConfirmAndDelete(AccountCardViewModel card)
    {
        System.Windows.MessageBoxResult result = System.Windows.MessageBox.Show(
            this,
            $"确定要删除「{card.DisplayTitle}」吗？删除后将从 data/vault.csv 移除。",
            "确认删除",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            _viewModel.DeleteAccount(card);
        }
    }

    private static AccountCardViewModel? ExtractCard(object sender)
    {
        return sender switch
        {
            System.Windows.Controls.MenuItem { DataContext: AccountCardViewModel vm } => vm,
            System.Windows.Controls.MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement { DataContext: AccountCardViewModel vm } } } => vm,
            FrameworkElement { DataContext: AccountCardViewModel vm } => vm,
            _ => null
        };
    }
}
