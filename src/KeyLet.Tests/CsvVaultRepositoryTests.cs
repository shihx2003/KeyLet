using System.Security.Cryptography;
using KeyLet.Core.Models;
using KeyLet.Core.Storage;

namespace KeyLet.Tests;

public sealed class CsvVaultRepositoryTests : IDisposable
{
    private readonly string _tempDataDir;
    private readonly CsvVaultRepository _repository = new();
    private readonly SettingsRepository _settingsRepository = new();

    public CsvVaultRepositoryTests()
    {
        _tempDataDir = Path.Combine(Path.GetTempPath(), $"KeyLetTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDataDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDataDir))
        {
            Directory.Delete(_tempDataDir, recursive: true);
        }
    }

    [Fact]
    public void InitializeUnlockAndRoundTripCsv_EncryptsSensitiveFieldsAndKeepsMetadataReadable()
    {
        byte[] key = _repository.InitializeVault(_tempDataDir, "admin1234");
        try
        {
            Assert.True(_repository.VaultExists(_tempDataDir));
            Assert.True(_repository.VerifyKey(_tempDataDir, key));

            var account = new OtpAccount
            {
                Issuer = "CorpVPN",
                AccountName = "alice,ops@corp.com",
                Secret = "JBSWY3DPEHPK3PXP",
                Algorithm = OtpAlgorithm.Sha256,
                Digits = 6,
                Period = 30,
                Prefix = "qwer",
                Suffix = "tyu",
                DefaultCopyMode = CopyFormatMode.WithAffix
            };

            _repository.SaveAccounts(_tempDataDir, [account], key);

            string rawCsv = File.ReadAllText(Path.Combine(_tempDataDir, CsvVaultRepository.VaultFileName));
            Assert.Contains("CorpVPN", rawCsv);
            Assert.Contains("\"alice,ops@corp.com\"", rawCsv);
            Assert.DoesNotContain("JBSWY3DPEHPK3PXP", rawCsv);
            Assert.DoesNotContain("qwer", rawCsv);
            Assert.DoesNotContain("tyu", rawCsv);
            Assert.Contains("v1:", rawCsv);

            byte[] unlockedKey = _repository.UnlockWithPassword(_tempDataDir, "admin1234");
            try
            {
                IReadOnlyList<OtpAccount> loaded = _repository.LoadAccounts(_tempDataDir, unlockedKey);
                OtpAccount loadedItem = Assert.Single(loaded);

                Assert.Equal(account.Id, loadedItem.Id);
                Assert.Equal("CorpVPN", loadedItem.Issuer);
                Assert.Equal("alice,ops@corp.com", loadedItem.AccountName);
                Assert.Equal("JBSWY3DPEHPK3PXP", loadedItem.Secret);
                Assert.Equal(OtpAlgorithm.Sha256, loadedItem.Algorithm);
                Assert.Equal("qwer", loadedItem.Prefix);
                Assert.Equal("tyu", loadedItem.Suffix);
                Assert.True(loadedItem.HasAffix);
                Assert.Equal(CopyFormatMode.WithAffix, loadedItem.DefaultCopyMode);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(unlockedKey);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    [Fact]
    public void UnlockWithPassword_WrongPassword_ThrowsVaultException()
    {
        byte[] key = _repository.InitializeVault(_tempDataDir, "admin1234");
        CryptographicOperations.ZeroMemory(key);

        Assert.Throws<VaultException>(() => _repository.UnlockWithPassword(_tempDataDir, "wrong123"));
    }

    [Fact]
    public void ChangeMasterPassword_ReEncryptsAccountsAndInvalidatesOldPassword()
    {
        byte[] oldKey = _repository.InitializeVault(_tempDataDir, "admin1234");
        var account = new OtpAccount
        {
            Issuer = "GitHub",
            AccountName = "shihx",
            Secret = "JBSWY3DPEHPK3PXP",
            Prefix = "pin-",
            Suffix = "!"
        };
        _repository.SaveAccounts(_tempDataDir, [account], oldKey);
        CryptographicOperations.ZeroMemory(oldKey);

        byte[] newKey = _repository.ChangeMasterPassword(_tempDataDir, "admin1234", "newpass5678");
        try
        {
            Assert.Throws<VaultException>(() => _repository.UnlockWithPassword(_tempDataDir, "admin1234"));

            IReadOnlyList<OtpAccount> loaded = _repository.LoadAccounts(_tempDataDir, newKey);
            OtpAccount item = Assert.Single(loaded);
            Assert.Equal("JBSWY3DPEHPK3PXP", item.Secret);
            Assert.Equal("pin-", item.Prefix);
            Assert.Equal("!", item.Suffix);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(newKey);
        }
    }

    [Fact]
    public void SettingsRepository_SaveAndLoad_PreservesValues()
    {
        var settings = new AppSettings
        {
            ClipboardClearSeconds = 15,
            IsAutoUnlockEnabled = false,
            Theme = ThemePreference.Dark,
            GlobalDefaultCopyMode = CopyFormatMode.WithAffix
        };

        _settingsRepository.Save(_tempDataDir, settings);
        AppSettings loaded = _settingsRepository.Load(_tempDataDir);

        Assert.Equal(settings, loaded);
    }
}
