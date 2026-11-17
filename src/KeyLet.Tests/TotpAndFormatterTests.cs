using System.Text;
using KeyLet.Core.Models;
using KeyLet.Core.Totp;
using OtpNet;

namespace KeyLet.Tests;

public sealed class TotpAndFormatterTests
{
    [Theory]
    [InlineData(59L, "94287082")]
    [InlineData(1111111109L, "07081804")]
    [InlineData(1111111111L, "14050471")]
    [InlineData(1234567890L, "89005924")]
    [InlineData(2000000000L, "69279037")]
    public void ComputeCode_Rfc6238Sha1Vectors_MatchExpected8DigitCodes(long unixSeconds, string expectedCode)
    {
        string base32Secret = Base32Encoding.ToString(Encoding.ASCII.GetBytes("12345678901234567890"));
        var account = new OtpAccount
        {
            Issuer = "RFC",
            AccountName = "test",
            Secret = base32Secret,
            Algorithm = OtpAlgorithm.Sha1,
            Digits = 8,
            Period = 30
        };

        TotpCodeSnapshot snapshot = TotpGenerator.ComputeCode(account, DateTimeOffset.FromUnixTimeSeconds(unixSeconds));

        Assert.Equal(expectedCode, snapshot.RawCode);
        Assert.Equal($"{expectedCode[..4]} {expectedCode[4..]}", snapshot.FormattedDisplayCode);
    }

    [Theory]
    [InlineData(59L, "46119246")]
    [InlineData(1111111109L, "68084774")]
    public void ComputeCode_Rfc6238Sha256Vectors_MatchExpected8DigitCodes(long unixSeconds, string expectedCode)
    {
        string base32Secret = Base32Encoding.ToString(Encoding.ASCII.GetBytes("12345678901234567890123456789012"));
        var account = new OtpAccount
        {
            Issuer = "RFC",
            AccountName = "sha256",
            Secret = base32Secret,
            Algorithm = OtpAlgorithm.Sha256,
            Digits = 8,
            Period = 30
        };

        TotpCodeSnapshot snapshot = TotpGenerator.ComputeCode(account, DateTimeOffset.FromUnixTimeSeconds(unixSeconds));
        Assert.Equal(expectedCode, snapshot.RawCode);
    }

    [Theory]
    [InlineData(0L, 30)]
    [InlineData(1L, 29)]
    [InlineData(29L, 1)]
    [InlineData(30L, 30)]
    public void ComputeCode_RemainingSeconds_ComputesExactWindowCountdown(long unixSeconds, int expectedRemaining)
    {
        var account = new OtpAccount
        {
            Secret = "JBSWY3DPEHPK3PXP",
            Digits = 6,
            Period = 30
        };

        TotpCodeSnapshot snapshot = TotpGenerator.ComputeCode(account, DateTimeOffset.FromUnixTimeSeconds(unixSeconds));

        Assert.Equal(expectedRemaining, snapshot.RemainingSeconds);
        Assert.Equal(6, snapshot.RawCode.Length);
        Assert.Contains(" ", snapshot.FormattedDisplayCode);
    }

    [Fact]
    public void FormatForClipboard_HandlesCodeOnlyAndWithAffix()
    {
        Assert.Equal("123456", CodeFormatter.FormatForClipboard("123456", "qwer", "tyu", CopyFormatMode.CodeOnly));
        Assert.Equal("qwer123456tyu", CodeFormatter.FormatForClipboard("123456", "qwer", "tyu", CopyFormatMode.WithAffix));
        Assert.Equal("123456", CodeFormatter.FormatForClipboard("123456", "", "", CopyFormatMode.WithAffix));
    }
}
