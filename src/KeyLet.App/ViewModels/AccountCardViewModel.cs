using KeyLet.Core.Models;
using KeyLet.Core.Totp;

namespace KeyLet.App.ViewModels;

/// <summary>
/// 主界面单张 2FA 账号卡片的视图模型（严格隐藏前后缀明文）。
/// </summary>
public sealed class AccountCardViewModel : ObservableObject
{
    private string _rawCode = string.Empty;
    private string _displayCode = "------";
    private int _remainingSeconds = OtpAccount.DefaultPeriodSeconds;
    private int _periodSeconds = OtpAccount.DefaultPeriodSeconds;
    private bool _isDropAbove;
    private bool _isDropBelow;

    public AccountCardViewModel(OtpAccount account)
    {
        Account = account ?? throw new ArgumentNullException(nameof(account));
        RefreshCode(DateTimeOffset.UtcNow);
    }

    public OtpAccount Account { get; private set; }

    public Guid Id => Account.Id;

    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(Account.Issuer)
            ? Account.Issuer
            : !string.IsNullOrWhiteSpace(Account.AccountName)
                ? Account.AccountName
                : "未命名密钥";

    public string DisplaySubtitle =>
        !string.IsNullOrWhiteSpace(Account.Issuer) && !string.IsNullOrWhiteSpace(Account.AccountName)
            ? Account.AccountName
            : string.Empty;

    public bool HasSubtitle => !string.IsNullOrWhiteSpace(DisplaySubtitle);

    public bool HasAffix => Account.HasAffix;

    public CopyFormatMode DefaultCopyMode => Account.DefaultCopyMode;

    public bool IsDefaultWithAffix => Account.HasAffix && Account.DefaultCopyMode == CopyFormatMode.WithAffix;

    public string DefaultActionTooltip => IsDefaultWithAffix
        ? "点击复制完整码（Shift+点击纯码）"
        : Account.HasAffix
            ? "点击复制纯码（Shift+点击完整码）"
            : "点击复制动态码";

    public string RawCode
    {
        get => _rawCode;
        private set => SetProperty(ref _rawCode, value);
    }

    public string DisplayCode
    {
        get => _displayCode;
        private set => SetProperty(ref _displayCode, value);
    }

    public int RemainingSeconds
    {
        get => _remainingSeconds;
        private set
        {
            if (SetProperty(ref _remainingSeconds, value))
            {
                OnPropertyChanged(nameof(ProgressPercentage));
                OnPropertyChanged(nameof(IsExpiringSoon));
                OnPropertyChanged(nameof(CountdownLabel));
            }
        }
    }

    public int PeriodSeconds
    {
        get => _periodSeconds;
        private set
        {
            if (SetProperty(ref _periodSeconds, value))
            {
                OnPropertyChanged(nameof(ProgressPercentage));
            }
        }
    }

    public double ProgressPercentage => PeriodSeconds > 0 ? RemainingSeconds * 100.0 / PeriodSeconds : 0;

    public bool IsExpiringSoon => RemainingSeconds <= 5;

    public string CountdownLabel => $"{RemainingSeconds}s";

    public bool IsDropAbove
    {
        get => _isDropAbove;
        set => SetProperty(ref _isDropAbove, value);
    }

    public bool IsDropBelow
    {
        get => _isDropBelow;
        set => SetProperty(ref _isDropBelow, value);
    }

    /// <summary>
    /// 用最新的账号实体替换并立即刷新展示与验证码。
    /// </summary>
    public void UpdateAccount(OtpAccount updated)
    {
        Account = updated ?? throw new ArgumentNullException(nameof(updated));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(DisplaySubtitle));
        OnPropertyChanged(nameof(HasSubtitle));
        OnPropertyChanged(nameof(HasAffix));
        OnPropertyChanged(nameof(DefaultCopyMode));
        OnPropertyChanged(nameof(IsDefaultWithAffix));
        OnPropertyChanged(nameof(DefaultActionTooltip));
        RefreshCode(DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// 刷新当前时刻的 TOTP 验证码与倒计时。
    /// </summary>
    public void RefreshCode(DateTimeOffset utcNow)
    {
        TotpCodeSnapshot snapshot = TotpGenerator.ComputeCode(Account, utcNow);
        RawCode = snapshot.RawCode;
        DisplayCode = snapshot.FormattedDisplayCode;
        PeriodSeconds = snapshot.PeriodSeconds;
        RemainingSeconds = snapshot.RemainingSeconds;
    }
}
