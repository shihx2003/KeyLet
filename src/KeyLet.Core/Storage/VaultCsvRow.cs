using CsvHelper;

namespace KeyLet.Core.Storage;

/// <summary>
/// 对应 data/vault.csv 的单行存储结构。
/// </summary>
[CsvRecord]
internal sealed partial class VaultCsvRow
{
    public string Id { get; set; } = string.Empty;

    public string Issuer { get; set; } = string.Empty;

    public string AccountName { get; set; } = string.Empty;

    public string Algorithm { get; set; } = "Sha1";

    public int Digits { get; set; } = 6;

    public int Period { get; set; } = 30;

    public string DefaultCopyMode { get; set; } = "CodeOnly";

    public bool HasAffix { get; set; }

    public string EncryptedSecret { get; set; } = string.Empty;

    public string EncryptedPrefix { get; set; } = string.Empty;

    public string EncryptedSuffix { get; set; } = string.Empty;

    public string CreatedAtUtc { get; set; } = string.Empty;

    public string UpdatedAtUtc { get; set; } = string.Empty;
}
