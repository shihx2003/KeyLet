using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KeyLet.App.ViewModels;
using KeyLet.Core.Models;
using KeyLet.Core.Storage;
using Wpf.Ui.Controls;

namespace KeyLet.App.Views;

/// <summary>
/// 应用设置与主密码管理窗口。
/// </summary>
public partial class SettingsWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;

    public SettingsWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();

        AppSettings current = _viewModel.Settings;
        AutoUnlockCheckBox.IsChecked = current.IsAutoUnlockEnabled;
        DataPathTextBlock.Text = _viewModel.DataDirectory;

        SelectComboByTag(ClipboardClearComboBox, current.ClipboardClearSeconds.ToString(), fallbackIndex: 2);
        SelectComboByTag(ThemeComboBox, current.Theme.ToString(), fallbackIndex: 0);
        SelectComboByTag(GlobalCopyModeComboBox, current.GlobalDefaultCopyMode.ToString(), fallbackIndex: 0);
    }

    private void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
    {
        string current = CurrentPasswordBox.Password;
        string next = NewPasswordBox.Password;
        string confirm = ConfirmNewPasswordBox.Password;

        if (string.IsNullOrEmpty(current) || string.IsNullOrEmpty(next))
        {
            ShowFeedback("请填写当前主密码与新主密码。", isError: true);
            return;
        }

        if (!string.Equals(next, confirm, StringComparison.Ordinal))
        {
            ShowFeedback("两次输入的新密码不一致。", isError: true);
            return;
        }

        try
        {
            _viewModel.ChangeMasterPassword(current, next);
            CurrentPasswordBox.Clear();
            NewPasswordBox.Clear();
            ConfirmNewPasswordBox.Clear();
            ShowFeedback("主密码已更新，vault.csv 已使用新密钥重新加密。", isError: false);
        }
        catch (VaultException ex)
        {
            ShowFeedback(ex.Message, isError: true);
        }
    }

    private void OpenDataFolderButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = _viewModel.DataDirectory,
            UseShellExecute = true
        });
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        int clearSeconds = 20;
        if (ClipboardClearComboBox.SelectedItem is ComboBoxItem clearItem &&
            int.TryParse(clearItem.Tag?.ToString(), out int parsedSeconds))
        {
            clearSeconds = parsedSeconds;
        }

        ThemePreference theme = ThemePreference.System;
        if (ThemeComboBox.SelectedItem is ComboBoxItem themeItem &&
            Enum.TryParse(themeItem.Tag?.ToString(), out ThemePreference parsedTheme))
        {
            theme = parsedTheme;
        }

        CopyFormatMode defaultMode = CopyFormatMode.CodeOnly;
        if (GlobalCopyModeComboBox.SelectedItem is ComboBoxItem modeItem &&
            Enum.TryParse(modeItem.Tag?.ToString(), out CopyFormatMode parsedMode))
        {
            defaultMode = parsedMode;
        }

        var updated = new AppSettings
        {
            IsAutoUnlockEnabled = AutoUnlockCheckBox.IsChecked == true,
            ClipboardClearSeconds = clearSeconds,
            Theme = theme,
            GlobalDefaultCopyMode = defaultMode
        };

        _viewModel.UpdateSettings(updated);
        DialogResult = true;
        Close();
    }

    private static void SelectComboByTag(ComboBox comboBox, string tag, int fallbackIndex)
    {
        for (int i = 0; i < comboBox.Items.Count; i++)
        {
            if (comboBox.Items[i] is ComboBoxItem item &&
                string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedIndex = i;
                return;
            }
        }

        comboBox.SelectedIndex = fallbackIndex;
    }

    private void ShowFeedback(string message, bool isError)
    {
        FeedbackTextBlock.Text = message;
        FeedbackTextBlock.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(239, 68, 68))
            : new SolidColorBrush(Color.FromRgb(16, 185, 129));
        FeedbackTextBlock.Visibility = Visibility.Visible;
    }
}
