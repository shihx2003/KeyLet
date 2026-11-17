using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CsvHelper;
using KeyLet.Core.Crypto;
using KeyLet.Core.Models;

namespace KeyLet.Core.Storage;

/// <summary>
/// 基于标准 CSV 与 AES-256-GCM 的便携密钥库仓储。
/// </summary>
public sealed class CsvVaultRepository
{
    public const string MetaFileName = "vault.meta.json";
    public const string VaultFileName = "vault.csv";
    private const string KeyCheckPlaintext = "KEYLET-VAULT-V1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 检查指定数据目录下是否已初始化密钥库。
    /// </summary>
    public bool VaultExists(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        string metaPath = Path.Combine(dataDirectory, MetaFileName);
        return File.Exists(metaPath);
    }

    /// <summary>
    /// 使用初始主密码创建新的密钥库，并返回派生后的 32 字节主密钥。
    /// </summary>
    public byte[] InitializeVault(string dataDirectory, string masterPassword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        if (string.IsNullOrEmpty(masterPassword))
        {
            throw new ArgumentException("主密码不能为空。", nameof(masterPassword));
        }

        Directory.CreateDirectory(dataDirectory);

        byte[] salt = KeyDerivationService.GenerateSalt();
        byte[] masterKey = KeyDerivationService.DeriveKey(masterPassword, salt, KeyDerivationService.DefaultIterations);

        try
        {
            var meta = new VaultMeta
            {
                SchemaVersion = VaultMeta.CurrentSchemaVersion,
                KdfAlgorithm = KeyDerivationService.KdfAlgorithmName,
                Iterations = KeyDerivationService.DefaultIterations,
                SaltBase64 = Convert.ToBase64String(salt),
                KeyCheckToken = AesGcmCryptoService.Encrypt(KeyCheckPlaintext, masterKey)
            };

            WriteJsonAtomic(Path.Combine(dataDirectory, MetaFileName), meta);
            SaveAccounts(dataDirectory, Array.Empty<OtpAccount>(), masterKey);
            return masterKey;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(masterKey);
            throw;
        }
    }

    /// <summary>
    /// 使用主密码解锁密钥库，校验通过后返回 32 字节主密钥。
    /// </summary>
    public byte[] UnlockWithPassword(string dataDirectory, string masterPassword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        if (string.IsNullOrEmpty(masterPassword))
        {
            throw new VaultException("请输入主密码。");
        }

        VaultMeta meta = LoadMeta(dataDirectory);
        byte[] salt = Convert.FromBase64String(meta.SaltBase64);
        byte[] candidateKey = KeyDerivationService.DeriveKey(masterPassword, salt, meta.Iterations);

        if (!VerifyKeyInternal(meta, candidateKey))
        {
            CryptographicOperations.ZeroMemory(candidateKey);
            throw new VaultException("主密码错误或密钥库元数据已损坏。");
        }

        return candidateKey;
    }

    /// <summary>
    /// 验证已缓存的主密钥是否与当前密钥库匹配。
    /// </summary>
    public bool VerifyKey(string dataDirectory, ReadOnlySpan<byte> masterKey)
    {
        if (!VaultExists(dataDirectory) || masterKey.Length != KeyDerivationService.KeySizeBytes)
        {
            return false;
        }

        try
        {
            VaultMeta meta = LoadMeta(dataDirectory);
            return VerifyKeyInternal(meta, masterKey);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 从 data/vault.csv 读取并解密全部 2FA 账号。
    /// </summary>
    public IReadOnlyList<OtpAccount> LoadAccounts(string dataDirectory, ReadOnlySpan<byte> masterKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        VaultMeta meta = LoadMeta(dataDirectory);
        if (!VerifyKeyInternal(meta, masterKey))
        {
            throw new VaultException("密钥校验失败，无法解密账号列表。");
        }

        string csvPath = Path.Combine(dataDirectory, VaultFileName);
        if (!File.Exists(csvPath))
        {
            return Array.Empty<OtpAccount>();
        }

        using var reader = new StreamReader(csvPath, Encoding.UTF8);
        using var csv = CsvReader.Create(reader, CsvReaderOptions.Default);

        var accounts = new List<OtpAccount>();
        foreach (VaultCsvRow row in csv.GetRecords<VaultCsvRow>())
        {
            accounts.Add(MapRowToAccount(row, masterKey));
        }

        return accounts;
    }

    /// <summary>
    /// 将全部账号加密后原子写入 data/vault.csv。
    /// </summary>
    public void SaveAccounts(string dataDirectory, IEnumerable<OtpAccount> accounts, ReadOnlySpan<byte> masterKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(accounts);

        VaultMeta meta = LoadMeta(dataDirectory);
        if (!VerifyKeyInternal(meta, masterKey))
        {
            throw new VaultException("密钥校验失败，拒绝保存账号数据。");
        }

        Directory.CreateDirectory(dataDirectory);
        string csvPath = Path.Combine(dataDirectory, VaultFileName);
        string tempPath = Path.Combine(dataDirectory, $"{VaultFileName}.{Guid.NewGuid():N}.tmp");

        var rows = new List<VaultCsvRow>();
        foreach (OtpAccount account in accounts)
        {
            rows.Add(MapAccountToRow(account, masterKey));
        }

        try
        {
            using (var writer = new StreamWriter(tempPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            using (var csv = CsvWriter.Create(writer, CsvWriterOptions.Default))
            {
                if (rows.Count == 0)
                {
                    csv.WriteHeader<VaultCsvRow>();
                }
                else
                {
                    csv.WriteRecords(rows);
                }
            }

            File.Move(tempPath, csvPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    /// <summary>
    /// 修改主密码并用新派生密钥重新加密保存全部数据。
    /// </summary>
    public byte[] ChangeMasterPassword(string dataDirectory, string currentPassword, string newPassword)
    {
        if (string.IsNullOrEmpty(newPassword))
        {
            throw new ArgumentException("新主密码不能为空。", nameof(newPassword));
        }

        byte[] currentKey = UnlockWithPassword(dataDirectory, currentPassword);
        byte[]? newKey = null;

        try
        {
            IReadOnlyList<OtpAccount> accounts = LoadAccounts(dataDirectory, currentKey);
            byte[] newSalt = KeyDerivationService.GenerateSalt();
            newKey = KeyDerivationService.DeriveKey(newPassword, newSalt, KeyDerivationService.DefaultIterations);

            var newMeta = new VaultMeta
            {
                SchemaVersion = VaultMeta.CurrentSchemaVersion,
                KdfAlgorithm = KeyDerivationService.KdfAlgorithmName,
                Iterations = KeyDerivationService.DefaultIterations,
                SaltBase64 = Convert.ToBase64String(newSalt),
                KeyCheckToken = AesGcmCryptoService.Encrypt(KeyCheckPlaintext, newKey)
            };

            WriteJsonAtomic(Path.Combine(dataDirectory, MetaFileName), newMeta);
            SaveAccounts(dataDirectory, accounts, newKey);

            byte[] result = newKey;
            newKey = null;
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(currentKey);
            if (newKey is not null)
            {
                CryptographicOperations.ZeroMemory(newKey);
            }
        }
    }

    /// <summary>
    /// 读取密钥库元数据。
    /// </summary>
    public VaultMeta LoadMeta(string dataDirectory)
    {
        string metaPath = Path.Combine(dataDirectory, MetaFileName);
        if (!File.Exists(metaPath))
        {
            throw new VaultException("未找到密钥库元数据文件。");
        }

        string json = File.ReadAllText(metaPath, Encoding.UTF8);
        VaultMeta? meta = JsonSerializer.Deserialize<VaultMeta>(json, JsonOptions);
        return meta ?? throw new VaultException("密钥库元数据格式无效。");
    }

    private static bool VerifyKeyInternal(VaultMeta meta, ReadOnlySpan<byte> masterKey)
    {
        try
        {
            string decrypted = AesGcmCryptoService.Decrypt(meta.KeyCheckToken, masterKey);
            return string.Equals(decrypted, KeyCheckPlaintext, StringComparison.Ordinal);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static VaultCsvRow MapAccountToRow(OtpAccount account, ReadOnlySpan<byte> masterKey) => new()
    {
        Id = account.Id.ToString("D"),
        Issuer = account.Issuer,
        AccountName = account.AccountName,
        Algorithm = account.Algorithm.ToString(),
        Digits = account.Digits,
        Period = account.Period,
        DefaultCopyMode = account.DefaultCopyMode.ToString(),
        HasAffix = account.HasAffix,
        EncryptedSecret = AesGcmCryptoService.Encrypt(account.Secret, masterKey),
        EncryptedPrefix = AesGcmCryptoService.Encrypt(account.Prefix, masterKey),
        EncryptedSuffix = AesGcmCryptoService.Encrypt(account.Suffix, masterKey),
        CreatedAtUtc = account.CreatedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        UpdatedAtUtc = account.UpdatedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)
    };

    private static OtpAccount MapRowToAccount(VaultCsvRow row, ReadOnlySpan<byte> masterKey)
    {
        Guid id = Guid.TryParse(row.Id, out Guid parsedId) ? parsedId : Guid.NewGuid();
        OtpAlgorithm algorithm = Enum.TryParse(row.Algorithm, ignoreCase: true, out OtpAlgorithm parsedAlgo)
            ? parsedAlgo
            : OtpAlgorithm.Sha1;
        CopyFormatMode copyMode = Enum.TryParse(row.DefaultCopyMode, ignoreCase: true, out CopyFormatMode parsedMode)
            ? parsedMode
            : CopyFormatMode.CodeOnly;

        DateTimeOffset createdAt = DateTimeOffset.TryParse(row.CreatedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsedCreated)
            ? parsedCreated
            : DateTimeOffset.UtcNow;
        DateTimeOffset updatedAt = DateTimeOffset.TryParse(row.UpdatedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsedUpdated)
            ? parsedUpdated
            : createdAt;

        string secret = AesGcmCryptoService.Decrypt(row.EncryptedSecret, masterKey);
        string prefix = string.IsNullOrWhiteSpace(row.EncryptedPrefix)
            ? string.Empty
            : AesGcmCryptoService.Decrypt(row.EncryptedPrefix, masterKey);
        string suffix = string.IsNullOrWhiteSpace(row.EncryptedSuffix)
            ? string.Empty
            : AesGcmCryptoService.Decrypt(row.EncryptedSuffix, masterKey);

        return new OtpAccount
        {
            Id = id,
            Issuer = row.Issuer,
            AccountName = row.AccountName,
            Secret = secret,
            Algorithm = algorithm,
            Digits = row.Digits is 6 or 8 ? row.Digits : OtpAccount.DefaultDigits,
            Period = row.Period > 0 ? row.Period : OtpAccount.DefaultPeriodSeconds,
            Prefix = prefix,
            Suffix = suffix,
            DefaultCopyMode = copyMode,
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = updatedAt
        };
    }

    private static void WriteJsonAtomic<T>(string targetPath, T value)
    {
        string directory = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(directory);
        string tempPath = Path.Combine(directory, $"{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            string json = JsonSerializer.Serialize(value, JsonOptions);
            File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tempPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
