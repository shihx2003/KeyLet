using System.Security.Cryptography;
using KeyLet.Core.Models;
using OtpNet;

namespace KeyLet.Core.Totp;

/// <summary>
/// 基于 RFC 6238 的 TOTP 动态码计算服务。
/// </summary>
public static class TotpGenerator
{
    /// <summary>
    /// 计算指定账号在目标 UTC 时间下的 TOTP 动态码与剩余秒数。
    /// </summary>
    public static TotpCodeSnapshot ComputeCode(OtpAccount account, DateTimeOffset? utcNow = null)
    {
        ArgumentNullException.ThrowIfNull(account);

        DateTimeOffset effectiveTime = utcNow ?? DateTimeOffset.UtcNow;
        int period = account.Period > 0 ? account.Period : OtpAccount.DefaultPeriodSeconds;
        int digits = account.Digits is 6 or 8 ? account.Digits : OtpAccount.DefaultDigits;

        string normalizedSecret = OtpAuthUriParser.NormalizeBase32Secret(account.Secret);
        byte[] secretBytes = Base32Encoding.ToBytes(normalizedSecret);

        try
        {
            OtpHashMode hashMode = MapHashMode(account.Algorithm);
            var totp = new OtpNet.Totp(secretBytes, step: period, mode: hashMode, totpSize: digits);

            DateTime utcDateTime = effectiveTime.UtcDateTime;
            string rawCode = totp.ComputeTotp(utcDateTime);
            long unixSeconds = effectiveTime.ToUnixTimeSeconds();
            int elapsed = (int)(((unixSeconds % period) + period) % period);
            int remaining = period - elapsed;

            return new TotpCodeSnapshot(
                rawCode,
                CodeFormatter.FormatForDisplay(rawCode),
                remaining,
                period);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretBytes);
        }
    }

    private static OtpHashMode MapHashMode(OtpAlgorithm algorithm) => algorithm switch
    {
        OtpAlgorithm.Sha1 => OtpHashMode.Sha1,
        OtpAlgorithm.Sha256 => OtpHashMode.Sha256,
        OtpAlgorithm.Sha512 => OtpHashMode.Sha512,
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "不支持的 TOTP 算法。")
    };
}
