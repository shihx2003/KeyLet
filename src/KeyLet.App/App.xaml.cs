using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Threading;
using KeyLet.App.Services;
using KeyLet.App.ViewModels;
using KeyLet.App.Views;
using KeyLet.Core.Models;
using KeyLet.Core.Storage;

namespace KeyLet.App;

/// <summary>
/// KeyLet 桌面端入口：负责便携数据目录与日志初始化、全局异常兜底、免密/密码解锁以及零后台安全退出。
/// </summary>
public partial class App : Application
{
    private MainViewModel? _mainViewModel;
    private string _dataDirectory = string.Empty;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var pathService = new PortablePathService();
        pathService.EnsureDataDirectoryExists();
        _dataDirectory = pathService.DataDirectory;

        AppLogger.Initialize(_dataDirectory);
        RegisterExceptionHandlers();
        AppLogger.Info($"KeyLet starting. ExeDir={pathService.ExeDirectory}");

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
            AppLogger.Info("Startup cancelled during setup or unlock.");
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
        AppLogger.Info($"Vault unlocked. AccountCount={_mainViewModel.FilteredAccounts.Count}");

        var mainWindow = new MainWindow(_mainViewModel, qrScanner);
        MainWindow = mainWindow;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Info("KeyLet exiting cleanly.");
        _mainViewModel?.Dispose();
        _mainViewModel = null;
        base.OnExit(e);
    }

    private void RegisterExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLogger.Error("Unhandled UI dispatcher exception.", e.Exception);
        e.Handled = true;
        System.Windows.MessageBox.Show(
            $"操作出现异常：{e.Exception.Message}\n\n详细日志已保存至：{Path.Combine(_dataDirectory, "logs")}",
            "KeyLet 提示",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            AppLogger.Error($"Unhandled AppDomain exception (IsTerminating={e.IsTerminating}).", ex);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLogger.Error("Unobserved background task exception.", e.Exception);
        e.SetObserved();
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
            AppLogger.Info("Vault not found; launching SetupVaultWindow.");
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
                AppLogger.Info("Auto-unlocked via Windows DPAPI.");
                return cachedKey;
            }

            AppLogger.Warn("DPAPI cached key did not match vault metadata; falling back to password unlock.");
            CryptographicOperations.ZeroMemory(cachedKey);
        }

        AppLogger.Info("Launching UnlockWindow for master password.");
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
