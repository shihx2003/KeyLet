namespace KeyLet.Core.Models;

/// <summary>
/// 从 URI、纯 Base32 或批量迁移协议解析出的待导入账号草稿。
/// </summary>
public sealed record ParsedOtpImport
{
    public string Issuer { get; init; } = string.Empty;

    public string AccountName { get; init; } = string.Empty;

    public string Secret { get; init; } = string.Empty;

    public OtpAlgorithm Algorithm { get; init; } = OtpAlgorithm.Sha1;

    public int Digits { get; init; } = OtpAccount.DefaultDigits;

    public int Period { get; init; } = OtpAccount.DefaultPeriodSeconds;

    /// <summary>
    /// 转换为持久化账号实体。
    /// </summary>
    public OtpAccount ToAccount(string prefix = "", string suffix = "", CopyFormatMode defaultCopyMode = CopyFormatMode.CodeOnly)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new OtpAccount
        {
            Id = Guid.NewGuid(),
            Issuer = Issuer,
            AccountName = AccountName,
            Secret = Secret,
            Algorithm = Algorithm,
            Digits = Digits,
            Period = Period,
            Prefix = prefix,
            Suffix = suffix,
            DefaultCopyMode = defaultCopyMode,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }
}
