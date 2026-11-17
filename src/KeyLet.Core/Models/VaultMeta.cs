namespace KeyLet.Core.Models;

/// <summary>
/// 密钥库元信息（明文保存于 data/vault.meta.json，供本应用与外部脚本派生密钥）。
/// </summary>
public sealed record VaultMeta
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string KdfAlgorithm { get; init; } = "PBKDF2-SHA256";

    public int Iterations { get; init; } = 100_000;

    public string SaltBase64 { get; init; } = string.Empty;

    public string KeyCheckToken { get; init; } = string.Empty;
}
