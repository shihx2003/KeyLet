using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KeyLet.App.Services;
using KeyLet.Core.Models;
using KeyLet.Core.Totp;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace KeyLet.App.Views;

/// <summary>
/// 添加/导入/编辑 2FA 账号窗口：支持 URI、Base32、图片、拖拽、Ctrl+V 截图与屏幕扫码。
/// </summary>
public partial class AccountEditorWindow : FluentWindow
{
    private readonly QrCodeScannerService _qrScanner;
    private readonly OtpAccount? _editingAccount;
    private readonly ObservableCollection<BatchImportCandidate> _batchCandidates = [];

    public AccountEditorWindow(
        QrCodeScannerService qrScanner,
        CopyFormatMode globalDefaultCopyMode,
        OtpAccount? editingAccount = null,
        string? prefilledQrPayload = null)
    {
        _qrScanner = qrScanner;
        _editingAccount = editingAccount;

        InitializeComponent();
        BatchAccountsListBox.ItemsSource = _batchCandidates;

        if (_editingAccount is not null)
        {
            EditorTitleBar.Title = "编辑 2FA 密钥";
            Title = "编辑 2FA 密钥";
            ImportSectionBorder.Visibility = Visibility.Collapsed;
            LoadAccountForEdit(_editingAccount);
        }
        else
        {
            AlgorithmComboBox.SelectedIndex = 0;
            DigitsComboBox.SelectedIndex = 0;
            SelectCopyMode(globalDefaultCopyMode);
            if (!string.IsNullOrWhiteSpace(prefilledQrPayload))
            {
                ImportInputTextBox.Text = prefilledQrPayload;
                TryParseAndApply(prefilledQrPayload);
            }
        }
    }

    public IReadOnlyList<OtpAccount> ResultAccounts { get; private set; } = Array.Empty<OtpAccount>();

    private void LoadAccountForEdit(OtpAccount account)
    {
        IssuerTextBox.Text = account.Issuer;
        AccountNameTextBox.Text = account.AccountName;
        SecretTextBox.Text = account.Secret;
        PeriodTextBox.Text = account.Period.ToString();

        AlgorithmComboBox.SelectedIndex = account.Algorithm switch
        {
            OtpAlgorithm.Sha256 => 1,
            OtpAlgorithm.Sha512 => 2,
            _ => 0
        };

        DigitsComboBox.SelectedIndex = account.Digits == 8 ? 1 : 0;

        PrefixPasswordBox.Password = account.Prefix;
        PrefixPlainTextBox.Text = account.Prefix;
        SuffixPasswordBox.Password = account.Suffix;
        SuffixPlainTextBox.Text = account.Suffix;
        SelectCopyMode(account.DefaultCopyMode);
    }

    private void SelectCopyMode(CopyFormatMode mode)
    {
        CopyModeComboBox.SelectedIndex = mode == CopyFormatMode.WithAffix ? 1 : 0;
    }

    private void RevealAffixCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        bool reveal = RevealAffixCheckBox.IsChecked == true;
        if (reveal)
        {
            PrefixPlainTextBox.Text = PrefixPasswordBox.Password;
            SuffixPlainTextBox.Text = SuffixPasswordBox.Password;
            PrefixPasswordBox.Visibility = Visibility.Collapsed;
            SuffixPasswordBox.Visibility = Visibility.Collapsed;
            PrefixPlainTextBox.Visibility = Visibility.Visible;
            SuffixPlainTextBox.Visibility = Visibility.Visible;
        }
        else
        {
            PrefixPasswordBox.Password = PrefixPlainTextBox.Text;
            SuffixPasswordBox.Password = SuffixPlainTextBox.Text;
            PrefixPlainTextBox.Visibility = Visibility.Collapsed;
            SuffixPlainTextBox.Visibility = Visibility.Collapsed;
            PrefixPasswordBox.Visibility = Visibility.Visible;
            SuffixPasswordBox.Visibility = Visibility.Visible;
        }
    }

    private (string Prefix, string Suffix) GetAffixValues()
    {
        bool reveal = RevealAffixCheckBox.IsChecked == true;
        return reveal
            ? (PrefixPlainTextBox.Text, SuffixPlainTextBox.Text)
            : (PrefixPasswordBox.Password, SuffixPasswordBox.Password);
    }

    private void ParseInputButton_Click(object sender, RoutedEventArgs e)
    {
        TryParseAndApply(ImportInputTextBox.Text);
    }

    private void BrowseQrButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择包含 2FA 二维码的图片",
            Filter = "图片文件 (*.png;*.jpg;*.jpeg;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.webp|所有文件 (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        string? text = _qrScanner.DecodeFromFile(dialog.FileName);
        if (string.IsNullOrWhiteSpace(text))
        {
            ShowError("未能从所选图片中识别出有效的二维码。");
            return;
        }

        ImportInputTextBox.Text = text;
        TryParseAndApply(text);
    }

    private void PasteQrButton_Click(object sender, RoutedEventArgs e)
    {
        TryImportFromClipboard();
    }

    private void ScreenSnipButton_Click(object sender, RoutedEventArgs e)
    {
        Window? owner = Owner;
        WindowState previousOwnerState = owner?.WindowState ?? WindowState.Normal;
        Hide();
        if (owner is not null)
        {
            owner.WindowState = WindowState.Minimized;
        }
        Thread.Sleep(150);

        try
        {
            var snipWindow = new RegionSnipWindow();
            if (snipWindow.ShowDialog() == true && snipWindow.CapturedImage is not null)
            {
                string? qrText = _qrScanner.DecodeFromBitmapSource(snipWindow.CapturedImage);
                if (string.IsNullOrWhiteSpace(qrText))
                {
                    ShowError("框选区域内未识别到有效的 2FA 二维码。");
                }
                else
                {
                    ImportInputTextBox.Text = qrText;
                    TryParseAndApply(qrText);
                }
            }
        }
        finally
        {
            if (owner is not null)
            {
                owner.WindowState = previousOwnerState == WindowState.Minimized ? WindowState.Normal : previousOwnerState;
            }
            Show();
            Activate();
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (_editingAccount is not null || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
        {
            return;
        }

        string? text = _qrScanner.DecodeFromFile(files[0]);
        if (string.IsNullOrWhiteSpace(text))
        {
            ShowError("未能从拖入的图片中识别出二维码。");
            return;
        }

        ImportInputTextBox.Text = text;
        TryParseAndApply(text);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_editingAccount is null && e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (Clipboard.ContainsImage())
            {
                TryImportFromClipboard();
                e.Handled = true;
            }
        }
    }

    private void TryImportFromClipboard()
    {
        if (Clipboard.ContainsImage())
        {
            string? qrText = _qrScanner.DecodeFromClipboard();
            if (string.IsNullOrWhiteSpace(qrText))
            {
                ShowError("剪贴板图片中未识别到有效的二维码。");
                return;
            }

            ImportInputTextBox.Text = qrText;
            TryParseAndApply(qrText);
            return;
        }

        if (Clipboard.ContainsText())
        {
            string text = Clipboard.GetText();
            ImportInputTextBox.Text = text;
            TryParseAndApply(text);
            return;
        }

        ShowError("剪贴板中既没有图片也没有文本。");
    }

    private void TryParseAndApply(string rawInput)
    {
        ClearError();
        try
        {
            IReadOnlyList<ParsedOtpImport> parsed = OtpAuthUriParser.ParseInput(rawInput);
            if (parsed.Count == 1)
            {
                _batchCandidates.Clear();
                BatchImportBorder.Visibility = Visibility.Collapsed;
                SingleAccountPanel.Visibility = Visibility.Visible;

                ParsedOtpImport single = parsed[0];
                if (!string.IsNullOrWhiteSpace(single.Issuer))
                {
                    IssuerTextBox.Text = single.Issuer;
                }
                if (!string.IsNullOrWhiteSpace(single.AccountName))
                {
                    AccountNameTextBox.Text = single.AccountName;
                }
                SecretTextBox.Text = single.Secret;
                PeriodTextBox.Text = single.Period.ToString();
                AlgorithmComboBox.SelectedIndex = single.Algorithm switch
                {
                    OtpAlgorithm.Sha256 => 1,
                    OtpAlgorithm.Sha512 => 2,
                    _ => 0
                };
                DigitsComboBox.SelectedIndex = single.Digits == 8 ? 1 : 0;
            }
            else
            {
                _batchCandidates.Clear();
                foreach (ParsedOtpImport item in parsed)
                {
                    _batchCandidates.Add(new BatchImportCandidate(item));
                }

                BatchSummaryTextBlock.Text = $"已识别 {parsed.Count} 个迁移账号，请选择要导入的条目：";
                BatchImportBorder.Visibility = Visibility.Visible;
                SingleAccountPanel.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException)
        {
            ShowError(ex.Message);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ClearError();
        (string prefix, string suffix) = GetAffixValues();
        CopyFormatMode copyMode = CopyModeComboBox.SelectedIndex == 1
            ? CopyFormatMode.WithAffix
            : CopyFormatMode.CodeOnly;

        if (_batchCandidates.Count > 0)
        {
            var selected = _batchCandidates
                .Where(c => c.IsSelected)
                .Select(c => c.Import.ToAccount(prefix, suffix, copyMode))
                .ToList();

            if (selected.Count == 0)
            {
                ShowError("请至少勾选一个要导入的账号。");
                return;
            }

            ResultAccounts = selected;
            DialogResult = true;
            Close();
            return;
        }

        string normalizedSecret;
        try
        {
            normalizedSecret = OtpAuthUriParser.NormalizeBase32Secret(SecretTextBox.Text);
        }
        catch (FormatException ex)
        {
            ShowError(ex.Message);
            return;
        }

        if (!int.TryParse(PeriodTextBox.Text.Trim(), out int period) || period <= 0)
        {
            ShowError("周期必须为正整数秒（通常为 30）。");
            return;
        }

        string issuer = IssuerTextBox.Text.Trim();
        string accountName = AccountNameTextBox.Text.Trim();
        if (string.IsNullOrEmpty(issuer) && string.IsNullOrEmpty(accountName))
        {
            ShowError("请至少填写发行方或账号名称以便区分。");
            return;
        }

        OtpAlgorithm algorithm = AlgorithmComboBox.SelectedIndex switch
        {
            1 => OtpAlgorithm.Sha256,
            2 => OtpAlgorithm.Sha512,
            _ => OtpAlgorithm.Sha1
        };

        int digits = DigitsComboBox.SelectedIndex == 1 ? 8 : 6;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        OtpAccount result = _editingAccount is not null
            ? _editingAccount with
            {
                Issuer = issuer,
                AccountName = accountName,
                Secret = normalizedSecret,
                Algorithm = algorithm,
                Digits = digits,
                Period = period,
                Prefix = prefix,
                Suffix = suffix,
                DefaultCopyMode = copyMode,
                UpdatedAtUtc = now
            }
            : new OtpAccount
            {
                Id = Guid.NewGuid(),
                Issuer = issuer,
                AccountName = accountName,
                Secret = normalizedSecret,
                Algorithm = algorithm,
                Digits = digits,
                Period = period,
                Prefix = prefix,
                Suffix = suffix,
                DefaultCopyMode = copyMode,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

        ResultAccounts = [result];
        DialogResult = true;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorTextBlock.Text = message;
        ErrorTextBlock.Visibility = Visibility.Visible;
    }

    private void ClearError()
    {
        ErrorTextBlock.Text = string.Empty;
        ErrorTextBlock.Visibility = Visibility.Collapsed;
    }

    public sealed class BatchImportCandidate
    {
        public BatchImportCandidate(ParsedOtpImport import)
        {
            Import = import;
        }

        public ParsedOtpImport Import { get; }

        public bool IsSelected { get; set; } = true;

        public string Title => string.IsNullOrWhiteSpace(Import.Issuer) ? Import.AccountName : Import.Issuer;

        public string Subtitle => string.IsNullOrWhiteSpace(Import.Issuer) ? string.Empty : Import.AccountName;
    }
}
