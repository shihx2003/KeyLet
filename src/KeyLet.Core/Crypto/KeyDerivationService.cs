using System.Security.Cryptography;
using System.Text;

namespace KeyLet.Core.Crypto;

/// <summary>
/// 基于 PBKDF2-SHA256 的主密钥派生服务。
/// </summary>
public static class KeyDerivationService
{
    public const string KdfAlgorithmName = "PBKDF2-SHA256";
    public const int DefaultIterations = 100_000;
    public const int SaltSizeBytes = 16;
    public const int KeySizeBytes = 32;

    /// <summary>
    /// 生成密码学随机盐值。
    /// </summary>
    public static byte[] GenerateSalt() => RandomNumberGenerator.GetBytes(SaltSizeBytes);

    /// <summary>
    /// 使用 PBKDF2-SHA256 从主密码和盐值派生 32 字节（256 位）对称密钥。
    /// </summary>
    public static byte[] DeriveKey(string masterPassword, ReadOnlySpan<byte> salt, int iterations = DefaultIterations)
    {
        if (string.IsNullOrEmpty(masterPassword))
        {
            throw new ArgumentException("主密码不能为空。", nameof(masterPassword));
        }

        if (salt.Length < 8)
        {
            throw new ArgumentException("盐值长度不能少于 8 字节。", nameof(salt));
        }

        if (iterations <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations), "迭代次数必须大于 0。");
        }

        byte[] passwordBytes = Encoding.UTF8.GetBytes(masterPassword);
        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(
                passwordBytes,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                KeySizeBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }
}
