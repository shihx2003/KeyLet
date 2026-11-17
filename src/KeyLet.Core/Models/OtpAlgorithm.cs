namespace KeyLet.Core.Models;

/// <summary>
/// 支持的 TOTP 摘要哈希算法。
/// </summary>
public enum OtpAlgorithm
{
    Sha1 = 0,
    Sha256 = 1,
    Sha512 = 2
}
