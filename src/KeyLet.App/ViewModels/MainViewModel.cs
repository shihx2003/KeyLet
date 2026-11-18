using System.Collections.ObjectModel;
using System.Security.Cryptography;
using KeyLet.App.Services;
using KeyLet.Core.Models;
using KeyLet.Core.Storage;
using KeyLet.Core.Totp;

namespace KeyLet.App.ViewModels;

/// <summary>
/// 主窗口视图模型：协调账号加载、搜索过滤、实时倒计时、安全复制与设置持久化。
/// </summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly PortablePathService _pathService;
    private readonly CsvVaultRepository _vaultRepository;
    private readonly SettingsRepository _settingsRepository;
    private readonly DpapiKeyStore _dpapiKeyStore;
    private readonly ClipboardService _clipboardService;
    private readonly List<OtpAccount> _allAccounts = [];

    private byte[]? _masterKey;
    private AppSettings _settings;
    private string _searchQuery = string.Empty;
    private string _statusMessage = "点击卡片复制";

    public MainViewModel(
        PortablePathService pathService,
        CsvVaultRepository vaultRepository,
        SettingsRepository settingsRepository,
        DpapiKeyStore dpapiKeyStore,
        ClipboardService clipboardService,
        byte[] masterKey,
        AppSettings settings)
    {
        _pathService = pathService;
        _vaultRepository = vaultRepository;
        _settingsRepository = settingsRepository;
        _dpapiKeyStore = dpapiKeyStore;
        _clipboardService = clipboardService;
        _masterKey = masterKey;
        _settings = settings;
    }

    public ObservableCollection<AccountCardViewModel> FilteredAccounts { get; } = [];

    public AppSettings Settings => _settings;

    public string DataDirectory => _pathService.DataDirectory;

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                ApplyFilter();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsVaultEmpty => _allAccounts.Count == 0;

    public bool HasSearchNoMatch => _allAccounts.Count > 0 && FilteredAccounts.Count == 0;

    public event Action<string>? CopySucceeded;

    /// <summary>
    /// 从 data/vault.csv 加载全部账号并刷新验证码。
    /// </summary>
    public void LoadAccounts()
    {
        EnsureUnlocked();
        _allAccounts.Clear();
        _allAccounts.AddRange(_vaultRepository.LoadAccounts(_pathService.DataDirectory, _masterKey!));
        ApplyFilter();
    }

    /// <summary>
    /// 刷新当前列表中所有卡片的动态码与倒计时。
    /// </summary>
    public void RefreshAllCodes()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (AccountCardViewModel card in FilteredAccounts)
        {
            card.RefreshCode(now);
        }
    }

    /// <summary>
    /// 按账号默认模式复制（Shift+点击时切换到另一种模式）。
    /// </summary>
    public void CopyAccount(AccountCardViewModel card, bool alternateMode)
    {
        CopyFormatMode mode = card.DefaultCopyMode;
        if (alternateMode)
        {
            mode = mode == CopyFormatMode.WithAffix ? CopyFormatMode.CodeOnly : CopyFormatMode.WithAffix;
        }

        CopyWithMode(card, mode);
    }

    /// <summary>
    /// 按明确指定的模式（纯动态码 或 带前后缀完整码）复制到剪贴板。
    /// </summary>
    public void CopyWithMode(AccountCardViewModel card, CopyFormatMode mode)
    {
        ArgumentNullException.ThrowIfNull(card);

        card.RefreshCode(DateTimeOffset.UtcNow);
        CopyFormatMode effectiveMode = mode == CopyFormatMode.WithAffix && !card.HasAffix
            ? CopyFormatMode.CodeOnly
            : mode;

        string payload = CodeFormatter.FormatForClipboard(
            card.RawCode,
            card.Account.Prefix,
            card.Account.Suffix,
            effectiveMode);

        int clearSeconds = _settings.ClipboardClearSeconds;
        _clipboardService.CopyWithAutoClear(payload, clearSeconds, () =>
        {
            StatusMessage = "剪贴板已自动安全清空";
        });

        string clearHint = clearSeconds > 0 ? $" · {clearSeconds}s 后清空" : string.Empty;
        string notice = $"已复制 {card.DisplayTitle}{clearHint}";

        StatusMessage = notice;
        CopySucceeded?.Invoke(notice);
    }

    /// <summary>
    /// 批量新增导入的账号并加密保存。
    /// </summary>
    public void AddAccounts(IEnumerable<OtpAccount> newAccounts)
    {
        EnsureUnlocked();
        _allAccounts.AddRange(newAccounts);
        PersistAccounts();
        ApplyFilter();
    }

    /// <summary>
    /// 更新已有账号信息并保存。
    /// </summary>
    public void UpdateAccount(OtpAccount updatedAccount)
    {
        EnsureUnlocked();
        int index = _allAccounts.FindIndex(a => a.Id == updatedAccount.Id);
        if (index < 0)
        {
            return;
        }

        _allAccounts[index] = updatedAccount;
        PersistAccounts();

        AccountCardViewModel? visibleCard = FilteredAccounts.FirstOrDefault(c => c.Id == updatedAccount.Id);
        visibleCard?.UpdateAccount(updatedAccount);
        ApplyFilter();
    }

    /// <summary>
    /// 删除指定账号并保存。
    /// </summary>
    public void DeleteAccount(AccountCardViewModel card)
    {
        EnsureUnlocked();
        _allAccounts.RemoveAll(a => a.Id == card.Id);
        PersistAccounts();
        ApplyFilter();
        StatusMessage = $"已删除「{card.DisplayTitle}」";
    }

    /// <summary>
    /// 拖拽调整账号顺序并持久化到 data/vault.csv。
    /// </summary>
    public void MoveAccount(AccountCardViewModel sourceCard, AccountCardViewModel targetCard, bool insertAfter)
    {
        EnsureUnlocked();
        if (sourceCard.Id == targetCard.Id)
        {
            return;
        }

        int sourceIndex = _allAccounts.FindIndex(a => a.Id == sourceCard.Id);
        int targetIndex = _allAccounts.FindIndex(a => a.Id == targetCard.Id);
        if (sourceIndex < 0 || targetIndex < 0)
        {
            return;
        }

        OtpAccount item = _allAccounts[sourceIndex];
        _allAccounts.RemoveAt(sourceIndex);

        if (sourceIndex < targetIndex)
        {
            targetIndex--;
        }

        int insertIndex = insertAfter ? targetIndex + 1 : targetIndex;
        insertIndex = Math.Clamp(insertIndex, 0, _allAccounts.Count);
        if (insertIndex == sourceIndex)
        {
            _allAccounts.Insert(sourceIndex, item);
            return;
        }

        _allAccounts.Insert(insertIndex, item);
        PersistAccounts();
        ApplyFilter();
        StatusMessage = $"已调整 {sourceCard.DisplayTitle} 顺序";
    }

    /// <summary>
    /// 更新并持久化应用设置，同步本机 DPAPI 免密缓存状态与主题。
    /// </summary>
    public void UpdateSettings(AppSettings updated)
    {
        EnsureUnlocked();
        _settings = updated;
        _settingsRepository.Save(_pathService.DataDirectory, updated);

        if (updated.IsAutoUnlockEnabled)
        {
            _dpapiKeyStore.SaveKey(_pathService.DataDirectory, _masterKey!);
        }
        else
        {
            _dpapiKeyStore.ClearSavedKey(_pathService.DataDirectory);
        }

        ThemeService.Apply(updated.Theme);
        StatusMessage = "设置已保存";
    }

    /// <summary>
    /// 修改主密码并刷新内存密钥与本机 DPAPI 缓存。
    /// </summary>
    public void ChangeMasterPassword(string currentPassword, string newPassword)
    {
        EnsureUnlocked();
        byte[] newKey = _vaultRepository.ChangeMasterPassword(_pathService.DataDirectory, currentPassword, newPassword);

        if (_masterKey is not null)
        {
            CryptographicOperations.ZeroMemory(_masterKey);
        }

        _masterKey = newKey;

        if (_settings.IsAutoUnlockEnabled)
        {
            _dpapiKeyStore.SaveKey(_pathService.DataDirectory, _masterKey);
        }

        StatusMessage = "主密码已更新并重新加密全部密钥";
    }

    public void Dispose()
    {
        if (_masterKey is not null)
        {
            CryptographicOperations.ZeroMemory(_masterKey);
            _masterKey = null;
        }

        _clipboardService.Dispose();
    }

    private void PersistAccounts()
    {
        EnsureUnlocked();
        _vaultRepository.SaveAccounts(_pathService.DataDirectory, _allAccounts, _masterKey!);
    }

    private void ApplyFilter()
    {
        string query = _searchQuery.Trim();
        IEnumerable<OtpAccount> matches = string.IsNullOrEmpty(query)
            ? _allAccounts
            : _allAccounts.Where(a =>
                a.Issuer.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.AccountName.Contains(query, StringComparison.OrdinalIgnoreCase));

        FilteredAccounts.Clear();
        foreach (OtpAccount account in matches)
        {
            FilteredAccounts.Add(new AccountCardViewModel(account));
        }

        OnPropertyChanged(nameof(IsVaultEmpty));
        OnPropertyChanged(nameof(HasSearchNoMatch));
    }

    private void EnsureUnlocked()
    {
        if (_masterKey is null)
        {
            throw new InvalidOperationException("密钥库已锁定。");
        }
    }
}
