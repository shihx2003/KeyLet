using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using KeyLet.App.Services;
using KeyLet.App.ViewModels;
using KeyLet.Core.Models;
using Wpf.Ui.Controls;

namespace KeyLet.App.Views;

/// <summary>
/// KeyLet 主界面窗口：实时展示 TOTP 动态码、处理多模式复制与快捷键导入交互。
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;
    private readonly QrCodeScannerService _qrScanner;
    private readonly DispatcherTimer _timer;

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

    private void QuickScreenScanButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        Thread.Sleep(120);

        string? qrText = null;
        try
        {
            var snipWindow = new RegionSnipWindow();
            if (snipWindow.ShowDialog() == true && snipWindow.CapturedImage is not null)
            {
                qrText = _qrScanner.DecodeFromBitmapSource(snipWindow.CapturedImage);
            }
        }
        finally
        {
            Show();
            Activate();
        }

        if (string.IsNullOrWhiteSpace(qrText))
        {
            _viewModel.StatusMessage = "未在框选区域中识别到二维码";
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

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is AccountCardViewModel card)
        {
            bool alternate = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            _viewModel.CopyAccount(card, alternate);
        }
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
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && Clipboard.ContainsImage())
        {
            string? qrText = _qrScanner.DecodeFromClipboard();
            if (string.IsNullOrWhiteSpace(qrText))
            {
                _viewModel.StatusMessage = "剪贴板图片中未识别到有效二维码";
                return;
            }

            e.Handled = true;
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
