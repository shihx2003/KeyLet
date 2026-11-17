using System.Windows;
using KeyLet.App.Services;
using KeyLet.Core.Models;
using KeyLet.Core.Storage;
using Wpf.Ui.Controls;

namespace KeyLet.App.Views;

/// <summary>
/// 首次启动初始化向导窗口：设置主密码并可选启用本机 DPAPI 免密打开。
/// </summary>
public partial class SetupVaultWindow : FluentWindow
{
    private readonly string _dataDirectory;
    private readonly CsvVaultRepository _vaultRepository;
    private readonly SettingsRepository _settingsRepository;
    private readonly DpapiKeyStore _dpapiKeyStore;
    private readonly AppSettings _initialSettings;

    public SetupVaultWindow(
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
        Loaded += (_, _) => MasterPasswordBox.Focus();
    }

    public byte[]? DerivedKey { get; private set; }

    public AppSettings UpdatedSettings { get; private set; }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        string password = MasterPasswordBox.Password;
        string confirm = ConfirmPasswordBox.Password;

        if (string.IsNullOrEmpty(password))
        {
            ShowError("请输入主密码。");
            return;
        }

        if (!string.Equals(password, confirm, StringComparison.Ordinal))
        {
            ShowError("两次输入的密码不一致。");
            return;
        }

        bool enableAutoUnlock = AutoUnlockCheckBox.IsChecked == true;
        byte[] key = _vaultRepository.InitializeVault(_dataDirectory, password);

        if (enableAutoUnlock)
        {
            _dpapiKeyStore.SaveKey(_dataDirectory, key);
        }
        else
        {
            _dpapiKeyStore.ClearSavedKey(_dataDirectory);
        }

        UpdatedSettings = _initialSettings with { IsAutoUnlockEnabled = enableAutoUnlock };
        _settingsRepository.Save(_dataDirectory, UpdatedSettings);

        DerivedKey = key;
        DialogResult = true;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorTextBlock.Text = message;
        ErrorTextBlock.Visibility = Visibility.Visible;
    }
}
