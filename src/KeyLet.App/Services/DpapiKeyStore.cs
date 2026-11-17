using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace KeyLet.App.Services;

/// <summary>
/// 基于 Windows DPAPI（CurrentUser）的本机免密解锁凭据存储服务。
/// </summary>
public sealed class DpapiKeyStore
{
    private static readonly byte[] EntropyBytes = Encoding.UTF8.GetBytes("KeyLet.LocalUnlock.v1");

    public bool HasSavedKey(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        return File.Exists(GetUnlockPath(dataDirectory));
    }

    /// <summary>
    /// 使用当前 Windows 用户的 DPAPI 加密并缓存 32 字节主密钥。
    /// </summary>
    public void SaveKey(string dataDirectory, ReadOnlySpan<byte> masterKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        Directory.CreateDirectory(dataDirectory);

        byte[] protectedBytes = ProtectedData.Protect(
            masterKey.ToArray(),
            EntropyBytes,
            DataProtectionScope.CurrentUser);

        string targetPath = GetUnlockPath(dataDirectory);
        string tempPath = Path.Combine(dataDirectory, $"{PortablePathService.UnlockFileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllBytes(tempPath, protectedBytes);
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

    /// <summary>
    /// 尝试从本机 DPAPI 缓存解密主密钥。若跨设备/跨用户或文件缺失则返回 false。
    /// </summary>
    public bool TryLoadKey(string dataDirectory, out byte[]? masterKey)
    {
        masterKey = null;
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            return false;
        }

        string path = GetUnlockPath(dataDirectory);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            byte[] protectedBytes = File.ReadAllBytes(path);
            byte[] unprotected = ProtectedData.Unprotect(
                protectedBytes,
                EntropyBytes,
                DataProtectionScope.CurrentUser);

            if (unprotected.Length != 32)
            {
                CryptographicOperations.ZeroMemory(unprotected);
                return false;
            }

            masterKey = unprotected;
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// 删除本机免密解锁缓存。
    /// </summary>
    public void ClearSavedKey(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            return;
        }

        string path = GetUnlockPath(dataDirectory);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string GetUnlockPath(string dataDirectory) =>
        Path.Combine(dataDirectory, PortablePathService.UnlockFileName);
}
