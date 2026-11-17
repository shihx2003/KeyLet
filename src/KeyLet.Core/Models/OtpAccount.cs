namespace KeyLet.Core.Models;

/// <summary>
/// 表示一条本地保存的 TOTP 2FA 账号记录。
/// </summary>
public sealed record OtpAccount
{
    public const int DefaultDigits = 6;
    public const int DefaultPeriodSeconds = 30;

    public Guid Id { get; init; } = Guid.NewGuid();

    public string Issuer { get; init; } = string.Empty;

    public string AccountName { get; init; } = string.Empty;

    public string Secret { get; init; } = string.Empty;

    public OtpAlgorithm Algorithm { get; init; } = OtpAlgorithm.Sha1;

    public int Digits { get; init; } = DefaultDigits;

    public int Period { get; init; } = DefaultPeriodSeconds;

    public string Prefix { get; init; } = string.Empty;

    public string Suffix { get; init; } = string.Empty;

    public CopyFormatMode DefaultCopyMode { get; init; } = CopyFormatMode.CodeOnly;

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 是否配置了隐藏前缀或后缀。
    /// </summary>
    public bool HasAffix => !string.IsNullOrEmpty(Prefix) || !string.IsNullOrEmpty(Suffix);
}
