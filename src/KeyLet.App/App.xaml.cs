using System.Security.Cryptography;
using System.Windows;
using KeyLet.App.Services;
using KeyLet.App.ViewModels;
using KeyLet.App.Views;
using KeyLet.Core.Models;
using KeyLet.Core.Storage;

namespace KeyLet.App;

/// <summary>
/// KeyLet 桌面端入口：负责便携数据目录初始化、免密/密码解锁流程以及零后台安全退出。
/// </summary>
public partial class App : Application
{
    private MainViewModel? _mainViewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var pathService = new PortablePathService();
        pathService.EnsureDataDirectoryExists();

        var vaultRepository = new CsvVaultRepository();
        var settingsRepository = new SettingsRepository();
        var dpapiKeyStore = new DpapiKeyStore();
        var clipboardService = new ClipboardService();
        var qrScanner = new QrCodeScannerService();

        AppSettings settings = settingsRepository.Load(pathService.DataDirectory);
        ThemeService.Apply(settings.Theme);

        byte[]? masterKey = ResolveMasterKey(pathService.DataDirectory, vaultRepository, settingsRepository, dpapiKeyStore, ref settings);
        if (masterKey is null)
        {
            Shutdown(0);
            return;
        }

        _mainViewModel = new MainViewModel(
            pathService,
            vaultRepository,
            settingsRepository,
            dpapiKeyStore,
            clipboardService,
            masterKey,
            settings);

        _mainViewModel.LoadAccounts();

        var mainWindow = new MainWindow(_mainViewModel, qrScanner);
        MainWindow = mainWindow;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mainViewModel?.Dispose();
        _mainViewModel = null;
        base.OnExit(e);
    }

    private static byte[]? ResolveMasterKey(
        string dataDirectory,
        CsvVaultRepository vaultRepository,
        SettingsRepository settingsRepository,
        DpapiKeyStore dpapiKeyStore,
        ref AppSettings settings)
    {
        if (!vaultRepository.VaultExists(dataDirectory))
        {
            var setupWindow = new SetupVaultWindow(dataDirectory, vaultRepository, settingsRepository, dpapiKeyStore, settings);
            bool? created = setupWindow.ShowDialog();
            if (created != true || setupWindow.DerivedKey is null)
            {
                return null;
            }

            settings = setupWindow.UpdatedSettings;
            return setupWindow.DerivedKey;
        }

        if (settings.IsAutoUnlockEnabled &&
            dpapiKeyStore.TryLoadKey(dataDirectory, out byte[]? cachedKey) &&
            cachedKey is not null)
        {
            if (vaultRepository.VerifyKey(dataDirectory, cachedKey))
            {
                return cachedKey;
            }

            CryptographicOperations.ZeroMemory(cachedKey);
        }

        var unlockWindow = new UnlockWindow(dataDirectory, vaultRepository, settingsRepository, dpapiKeyStore, settings);
        bool? unlocked = unlockWindow.ShowDialog();
        if (unlocked != true || unlockWindow.DerivedKey is null)
        {
            return null;
        }

        settings = unlockWindow.UpdatedSettings;
        return unlockWindow.DerivedKey;
    }
}
