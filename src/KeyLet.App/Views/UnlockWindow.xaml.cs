using System.Windows;
using KeyLet.App.Services;
using KeyLet.Core.Models;
using KeyLet.Core.Storage;
using Wpf.Ui.Controls;

namespace KeyLet.App.Views;

/// <summary>
/// 密钥库密码解锁窗口（仅在未开启本机免密或跨设备迁移时显示）。
/// </summary>
public partial class UnlockWindow : FluentWindow
{
    private readonly string _dataDirectory;
    private readonly CsvVaultRepository _vaultRepository;
    private readonly SettingsRepository _settingsRepository;
    private readonly DpapiKeyStore _dpapiKeyStore;
    private readonly AppSettings _initialSettings;

    public UnlockWindow(
        string dataDirectory,
        CsvVaultRepository vaultRepository,
        SettingsRepository settingsRepository,
        DpapiKeyStore dpapiKeyStore,
        AppSettings initialSettings)
    {
        _dataDirectory = dataDirectory;
        _vaultRepository = vaultRepository;
        _settingsRepository = settingsRepository;
        _dpapiKeyStore = dpapiKeyStore;
        _initialSettings = initialSettings;

        InitializeComponent();
        UpdatedSettings = initialSettings;
        RememberUnlockCheckBox.IsChecked = initialSettings.IsAutoUnlockEnabled;
        Loaded += (_, _) => MasterPasswordBox.Focus();
    }

    public byte[]? DerivedKey { get; private set; }

    public AppSettings UpdatedSettings { get; private set; }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        string password = MasterPasswordBox.Password;
        if (string.IsNullOrEmpty(password))
        {
            ShowError("请输入主密码。");
            return;
        }

        try
        {
            byte[] key = _vaultRepository.UnlockWithPassword(_dataDirectory, password);
            bool remember = RememberUnlockCheckBox.IsChecked == true;

            if (remember)
            {
                _dpapiKeyStore.SaveKey(_dataDirectory, key);
            }
            else
            {
                _dpapiKeyStore.ClearSavedKey(_dataDirectory);
            }

            if (remember != _initialSettings.IsAutoUnlockEnabled)
            {
                UpdatedSettings = _initialSettings with { IsAutoUnlockEnabled = remember };
                _settingsRepository.Save(_dataDirectory, UpdatedSettings);
            }

            DerivedKey = key;
            DialogResult = true;
            Close();
        }
        catch (VaultException ex)
        {
            ShowError(ex.Message);
        }
    }

    private void ShowError(string message)
    {
        ErrorTextBlock.Text = message;
        ErrorTextBlock.Visibility = Visibility.Visible;
    }
}
