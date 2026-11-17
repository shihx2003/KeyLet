using System.Security.Cryptography;
using KeyLet.Core.Crypto;

namespace KeyLet.Tests;

public sealed class CryptoServiceTests
{
    [Fact]
    public void DeriveKey_WithSamePasswordAndSalt_ReturnsIdentical32ByteKey()
    {
        byte[] salt = Convert.FromBase64String("AQIDBAUGBwgJCgsMDQ4PEA==");

        byte[] key1 = KeyDerivationService.DeriveKey("admin1234", salt, 100_000);
        byte[] key2 = KeyDerivationService.DeriveKey("admin1234", salt, 100_000);

        Assert.Equal(32, key1.Length);
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void DeriveKey_WithDifferentSalt_ReturnsDifferentKeys()
    {
        byte[] salt1 = KeyDerivationService.GenerateSalt();
        byte[] salt2 = KeyDerivationService.GenerateSalt();

        byte[] key1 = KeyDerivationService.DeriveKey("admin1234", salt1, 10_000);
        byte[] key2 = KeyDerivationService.DeriveKey("admin1234", salt2, 10_000);

        Assert.NotEqual(key1, key2);
    }

    [Theory]
    [InlineData("JBSWY3DPEHPK3PXP")]
    [InlineData("")]
    [InlineData("qwer!@#$中文前缀_123456")]
    public void EncryptDecrypt_RoundTrip_ReturnsOriginalPlaintext(string plaintext)
    {
        byte[] salt = KeyDerivationService.GenerateSalt();
        byte[] key = KeyDerivationService.DeriveKey("admin1234", salt, 10_000);

        string encrypted1 = AesGcmCryptoService.Encrypt(plaintext, key);
        string encrypted2 = AesGcmCryptoService.Encrypt(plaintext, key);

        Assert.StartsWith("v1:", encrypted1);
        Assert.NotEqual(encrypted1, encrypted2);
        Assert.Equal(plaintext, AesGcmCryptoService.Decrypt(encrypted1, key));
        Assert.Equal(plaintext, AesGcmCryptoService.Decrypt(encrypted2, key));
    }

    [Fact]
    public void Decrypt_WithWrongKey_ThrowsCryptographicException()
    {
        byte[] salt = KeyDerivationService.GenerateSalt();
        byte[] rightKey = KeyDerivationService.DeriveKey("admin1234", salt, 10_000);
        byte[] wrongKey = KeyDerivationService.DeriveKey("wrong-pass", salt, 10_000);

        string encrypted = AesGcmCryptoService.Encrypt("TOPSECRETKEY", rightKey);

        Assert.ThrowsAny<CryptographicException>(() => AesGcmCryptoService.Decrypt(encrypted, wrongKey));
    }

    [Fact]
    public void Decrypt_WithTamperedCiphertext_ThrowsCryptographicException()
    {
        byte[] key = KeyDerivationService.DeriveKey("admin1234", KeyDerivationService.GenerateSalt(), 10_000);
        string encrypted = AesGcmCryptoService.Encrypt("TOPSECRETKEY", key);
        string[] parts = encrypted.Split(':');

        byte[] cipher = Convert.FromBase64String(parts[2]);
        cipher[0] ^= 0xFF;
        string tampered = $"{parts[0]}:{parts[1]}:{Convert.ToBase64String(cipher)}:{parts[3]}";

        Assert.ThrowsAny<CryptographicException>(() => AesGcmCryptoService.Decrypt(tampered, key));
    }
}
