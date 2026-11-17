using System.Security.Cryptography;
using System.Text;

namespace KeyLet.Core.Crypto;

/// <summary>
/// 基于 AES-256-GCM 的认证加解密服务。
/// 密文格式为：v1:&lt;Base64Nonce&gt;:&lt;Base64Ciphertext&gt;:&lt;Base64Tag&gt;。
/// </summary>
public static class AesGcmCryptoService
{
    public const string VersionPrefix = "v1";
    public const int NonceSizeBytes = 12;
    public const int TagSizeBytes = 16;
    public const int KeySizeBytes = 32;

    /// <summary>
    /// 使用 AES-256-GCM 加密明文字符串。
    /// </summary>
    public static string Encrypt(string plaintext, ReadOnlySpan<byte> key)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ValidateKeyLength(key);

        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        byte[] ciphertext = new byte[plaintextBytes.Length];
        byte[] tag = new byte[TagSizeBytes];

        try
        {
            using var aesGcm = new AesGcm(key, TagSizeBytes);
            aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);
            return $"{VersionPrefix}:{Convert.ToBase64String(nonce)}:{Convert.ToBase64String(ciphertext)}:{Convert.ToBase64String(tag)}";
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    /// <summary>
    /// 使用 AES-256-GCM 解密密文载荷并验证完整性标签。
    /// </summary>
    public static string Decrypt(string encryptedPayload, ReadOnlySpan<byte> key)
    {
        if (string.IsNullOrWhiteSpace(encryptedPayload))
        {
            throw new ArgumentException("密文载荷不能为空。", nameof(encryptedPayload));
        }

        ValidateKeyLength(key);

        string[] parts = encryptedPayload.Split(':');
        if (parts.Length != 4 || !string.Equals(parts[0], VersionPrefix, StringComparison.Ordinal))
        {
            throw new CryptographicException("密文格式无效或版本不受支持。");
        }

        byte[] nonce = Convert.FromBase64String(parts[1]);
        byte[] ciphertext = Convert.FromBase64String(parts[2]);
        byte[] tag = Convert.FromBase64String(parts[3]);

        if (nonce.Length != NonceSizeBytes || tag.Length != TagSizeBytes)
        {
            throw new CryptographicException("Nonce 或认证标签长度无效。");
        }

        byte[] plaintextBytes = new byte[ciphertext.Length];
        try
        {
            using var aesGcm = new AesGcm(key, TagSizeBytes);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintextBytes);
            return Encoding.UTF8.GetString(plaintextBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    private static void ValidateKeyLength(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeySizeBytes)
        {
            throw new ArgumentException($"密钥长度必须为 {KeySizeBytes} 字节。", nameof(key));
        }
    }
}
