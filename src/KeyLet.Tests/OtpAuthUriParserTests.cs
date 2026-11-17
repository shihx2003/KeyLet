using System.Text;
using KeyLet.Core.Models;
using KeyLet.Core.Totp;
using OtpNet;

namespace KeyLet.Tests;

public sealed class OtpAuthUriParserTests
{
    [Fact]
    public void ParseInput_StandardOtpAuthUri_ExtractsAllFields()
    {
        const string uri = "otpauth://totp/GitHub:alice%40example.com?secret=jbsw%20y3dpehpk3pxp&issuer=GitHub&algorithm=SHA256&digits=8&period=60";

        IReadOnlyList<ParsedOtpImport> results = OtpAuthUriParser.ParseInput(uri);

        ParsedOtpImport item = Assert.Single(results);
        Assert.Equal("GitHub", item.Issuer);
        Assert.Equal("alice@example.com", item.AccountName);
        Assert.Equal("JBSWY3DPEHPK3PXP", item.Secret);
        Assert.Equal(OtpAlgorithm.Sha256, item.Algorithm);
        Assert.Equal(8, item.Digits);
        Assert.Equal(60, item.Period);
    }

    [Fact]
    public void ParseInput_RawBase32WithSpacesAndHyphens_NormalizesSecret()
    {
        IReadOnlyList<ParsedOtpImport> results = OtpAuthUriParser.ParseInput("jbsw y3dp-ehpk 3pxp");

        ParsedOtpImport item = Assert.Single(results);
        Assert.Equal("JBSWY3DPEHPK3PXP", item.Secret);
        Assert.Equal(OtpAlgorithm.Sha1, item.Algorithm);
        Assert.Equal(6, item.Digits);
        Assert.Equal(30, item.Period);
    }

    [Fact]
    public void ParseInput_InvalidBase32_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => OtpAuthUriParser.ParseInput("not-a-valid-secret-189!"));
    }

    [Fact]
    public void ParseInput_HotpUri_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() =>
            OtpAuthUriParser.ParseInput("otpauth://hotp/Test:user?secret=JBSWY3DPEHPK3PXP&counter=1"));
    }

    [Fact]
    public void ParseInput_GoogleMigrationUri_ExtractsTotpAccountsAndSkipsHotp()
    {
        byte[] secret1 = Base32Encoding.ToBytes("JBSWY3DPEHPK3PXP");
        byte[] secret2 = Base32Encoding.ToBytes("GEZDGNBVGY3TQOJQ");

        byte[] entry1 = BuildOtpParameters(secret1, "GitHub:alice@test.com", "GitHub", algo: 1, digits: 1, otpType: 2);
        byte[] entry2 = BuildOtpParameters(secret2, "VPN:bob", "VPN", algo: 2, digits: 2, otpType: 2);
        byte[] hotpEntry = BuildOtpParameters(secret1, "Legacy:hotp", "Legacy", algo: 1, digits: 1, otpType: 1);

        var payload = new List<byte>();
        AppendBytesField(payload, 1, entry1);
        AppendBytesField(payload, 1, hotpEntry);
        AppendBytesField(payload, 1, entry2);

        string urlSafeBase64 = Convert.ToBase64String(payload.ToArray()).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        string migrationUri = $"otpauth-migration://offline?data={urlSafeBase64}";

        IReadOnlyList<ParsedOtpImport> results = OtpAuthUriParser.ParseInput(migrationUri);

        Assert.Equal(2, results.Count);
        Assert.Equal("GitHub", results[0].Issuer);
        Assert.Equal("alice@test.com", results[0].AccountName);
        Assert.Equal("JBSWY3DPEHPK3PXP", results[0].Secret);
        Assert.Equal(OtpAlgorithm.Sha1, results[0].Algorithm);
        Assert.Equal(6, results[0].Digits);

        Assert.Equal("VPN", results[1].Issuer);
        Assert.Equal("bob", results[1].AccountName);
        Assert.Equal("GEZDGNBVGY3TQOJQ", results[1].Secret);
        Assert.Equal(OtpAlgorithm.Sha256, results[1].Algorithm);
        Assert.Equal(8, results[1].Digits);
    }

    private static byte[] BuildOtpParameters(byte[] secret, string name, string issuer, int algo, int digits, int otpType)
    {
        var buffer = new List<byte>();
        AppendBytesField(buffer, 1, secret);
        AppendBytesField(buffer, 2, Encoding.UTF8.GetBytes(name));
        AppendBytesField(buffer, 3, Encoding.UTF8.GetBytes(issuer));
        AppendVarintField(buffer, 4, algo);
        AppendVarintField(buffer, 5, digits);
        AppendVarintField(buffer, 6, otpType);
        return buffer.ToArray();
    }

    private static void AppendBytesField(List<byte> buffer, int fieldNumber, byte[] value)
    {
        WriteVarint(buffer, (ulong)((fieldNumber << 3) | 2));
        WriteVarint(buffer, (ulong)value.Length);
        buffer.AddRange(value);
    }

    private static void AppendVarintField(List<byte> buffer, int fieldNumber, int value)
    {
        WriteVarint(buffer, (ulong)((fieldNumber << 3) | 0));
        WriteVarint(buffer, (ulong)value);
    }

    private static void WriteVarint(List<byte> buffer, ulong value)
    {
        while (value >= 0x80)
        {
            buffer.Add((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        buffer.Add((byte)value);
    }
}
