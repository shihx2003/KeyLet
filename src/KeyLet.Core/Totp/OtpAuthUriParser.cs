using System.Buffers.Binary;
using System.Text;
using KeyLet.Core.Models;
using OtpNet;

namespace KeyLet.Core.Totp;

/// <summary>
/// 解析 otpauth://、otpauth-migration:// 以及纯 Base32 密钥文本的导入解析器。
/// </summary>
public static class OtpAuthUriParser
{
    /// <summary>
    /// 解析任意支持的输入（标准 URI、Google 批量迁移 URI 或 Base32 文本）。
    /// 支持多行批量粘贴。
    /// </summary>
    public static IReadOnlyList<ParsedOtpImport> ParseInput(string rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            throw new FormatException("导入内容不能为空。");
        }

        string trimmed = rawInput.Trim();
        if (trimmed.StartsWith("otpauth-migration://", StringComparison.OrdinalIgnoreCase))
        {
            return ParseMigrationUri(trimmed);
        }

        if (trimmed.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
        {
            return [ParseOtpAuthUri(trimmed)];
        }

        string[] lines = trimmed.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length > 1)
        {
            var results = new List<ParsedOtpImport>();
            foreach (string line in lines)
            {
                results.AddRange(ParseInput(line));
            }
            return results;
        }

        string normalized = NormalizeBase32Secret(trimmed);
        return
        [
            new ParsedOtpImport
            {
                Secret = normalized,
                Algorithm = OtpAlgorithm.Sha1,
                Digits = OtpAccount.DefaultDigits,
                Period = OtpAccount.DefaultPeriodSeconds
            }
        ];
    }

    /// <summary>
    /// 校验并标准化 Base32 密钥字符串。
    /// </summary>
    public static string NormalizeBase32Secret(string rawSecret)
    {
        if (string.IsNullOrWhiteSpace(rawSecret))
        {
            throw new FormatException("密钥不能为空。");
        }

        var builder = new StringBuilder(rawSecret.Length);
        foreach (char ch in rawSecret)
        {
            if (char.IsWhiteSpace(ch) || ch == '-')
            {
                continue;
            }

            builder.Append(char.ToUpperInvariant(ch));
        }

        string cleaned = builder.ToString();
        if (cleaned.Length == 0)
        {
            throw new FormatException("密钥不能为空。");
        }

        foreach (char ch in cleaned)
        {
            bool isBase32Char = (ch >= 'A' && ch <= 'Z') || (ch >= '2' && ch <= '7') || ch == '=';
            if (!isBase32Char)
            {
                throw new FormatException("密钥包含非 Base32 字符。");
            }
        }

        byte[] decoded = Base32Encoding.ToBytes(cleaned);
        if (decoded.Length == 0)
        {
            throw new FormatException("无法将输入解码为有效的 Base32 密钥。");
        }

        return cleaned;
    }

    /// <summary>
    /// 解析标准 otpauth://totp/... URI。
    /// </summary>
    public static ParsedOtpImport ParseOtpAuthUri(string uriText)
    {
        if (!Uri.TryCreate(uriText.Trim(), UriKind.Absolute, out Uri? uri) ||
            !string.Equals(uri.Scheme, "otpauth", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("不是有效的 otpauth:// 链接。");
        }

        if (!string.Equals(uri.Host, "totp", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"暂仅支持 TOTP 类型，当前类型为: {uri.Host}");
        }

        string rawLabel = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        (string labelIssuer, string accountName) = SplitLabel(rawLabel);

        Dictionary<string, string> query = ParseQueryString(uri.Query);

        if (!query.TryGetValue("secret", out string? rawSecret) || string.IsNullOrWhiteSpace(rawSecret))
        {
            throw new FormatException("otpauth:// 链接缺少 secret 参数。");
        }

        string secret = NormalizeBase32Secret(rawSecret);
        string issuer = query.TryGetValue("issuer", out string? queryIssuer) && !string.IsNullOrWhiteSpace(queryIssuer)
            ? queryIssuer.Trim()
            : labelIssuer;

        OtpAlgorithm algorithm = query.TryGetValue("algorithm", out string? rawAlgo)
            ? ParseAlgorithm(rawAlgo)
            : OtpAlgorithm.Sha1;

        int digits = query.TryGetValue("digits", out string? rawDigits) && int.TryParse(rawDigits, out int parsedDigits)
            ? ValidateDigits(parsedDigits)
            : OtpAccount.DefaultDigits;

        int period = query.TryGetValue("period", out string? rawPeriod) && int.TryParse(rawPeriod, out int parsedPeriod) && parsedPeriod > 0
            ? parsedPeriod
            : OtpAccount.DefaultPeriodSeconds;

        return new ParsedOtpImport
        {
            Issuer = issuer,
            AccountName = accountName,
            Secret = secret,
            Algorithm = algorithm,
            Digits = digits,
            Period = period
        };
    }

    /// <summary>
    /// 解析 Google Authenticator 批量导出链接 otpauth-migration://offline?data=...。
    /// </summary>
    public static IReadOnlyList<ParsedOtpImport> ParseMigrationUri(string migrationUriText)
    {
        if (!Uri.TryCreate(migrationUriText.Trim(), UriKind.Absolute, out Uri? uri) ||
            !string.Equals(uri.Scheme, "otpauth-migration", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("不是有效的 otpauth-migration:// 链接。");
        }

        Dictionary<string, string> query = ParseQueryString(uri.Query);
        if (!query.TryGetValue("data", out string? encodedData) || string.IsNullOrWhiteSpace(encodedData))
        {
            throw new FormatException("迁移链接缺少 data 参数。");
        }

        string base64 = NormalizeBase64(encodedData);
        byte[] payloadBytes = Convert.FromBase64String(base64);

        var accounts = DecodeMigrationPayload(payloadBytes);
        if (accounts.Count == 0)
        {
            throw new FormatException("迁移数据中未找到可导入的 TOTP 账号。");
        }

        return accounts;
    }

    private static (string Issuer, string AccountName) SplitLabel(string label)
    {
        int colonIndex = label.IndexOf(':');
        if (colonIndex < 0)
        {
            return (string.Empty, label.Trim());
        }

        string issuer = label[..colonIndex].Trim();
        string account = label[(colonIndex + 1)..].Trim();
        return (issuer, account);
    }

    private static Dictionary<string, string> ParseQueryString(string query)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
        {
            return dict;
        }

        string trimmed = query.TrimStart('?');
        foreach (string pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eqIndex = pair.IndexOf('=');
            if (eqIndex <= 0)
            {
                continue;
            }

            string key = Uri.UnescapeDataString(pair[..eqIndex]);
            string value = Uri.UnescapeDataString(pair[(eqIndex + 1)..]);
            dict[key] = value;
        }

        return dict;
    }

    private static OtpAlgorithm ParseAlgorithm(string rawAlgorithm) => rawAlgorithm.Trim().ToUpperInvariant() switch
    {
        "SHA1" => OtpAlgorithm.Sha1,
        "SHA256" => OtpAlgorithm.Sha256,
        "SHA512" => OtpAlgorithm.Sha512,
        _ => throw new NotSupportedException($"不支持的 TOTP 算法: {rawAlgorithm}")
    };

    private static int ValidateDigits(int digits)
    {
        if (digits is not (6 or 8))
        {
            throw new NotSupportedException($"暂仅支持 6 位或 8 位动态码，当前为: {digits}");
        }

        return digits;
    }

    private static string NormalizeBase64(string input)
    {
        string normalized = input.Trim().Replace(' ', '+').Replace('-', '+').Replace('_', '/');
        int mod = normalized.Length % 4;
        if (mod > 0)
        {
            normalized = normalized.PadRight(normalized.Length + (4 - mod), '=');
        }
        return normalized;
    }

    private static List<ParsedOtpImport> DecodeMigrationPayload(ReadOnlySpan<byte> data)
    {
        var list = new List<ParsedOtpImport>();
        int offset = 0;

        while (offset < data.Length)
        {
            ulong tag = ReadVarint(data, ref offset);
            int fieldNumber = (int)(tag >> 3);
            int wireType = (int)(tag & 0x07);

            if (fieldNumber == 1 && wireType == 2)
            {
                int length = (int)ReadVarint(data, ref offset);
                ReadOnlySpan<byte> entrySlice = data.Slice(offset, length);
                offset += length;

                ParsedOtpImport? parsed = DecodeOtpParameters(entrySlice);
                if (parsed is not null)
                {
                    list.Add(parsed);
                }
            }
            else
            {
                SkipProtobufField(data, ref offset, wireType);
            }
        }

        return list;
    }

    private static ParsedOtpImport? DecodeOtpParameters(ReadOnlySpan<byte> data)
    {
        byte[]? secretBytes = null;
        string name = string.Empty;
        string issuer = string.Empty;
        int algoCode = 1;
        int digitsCode = 1;
        int otpTypeCode = 2;

        int offset = 0;
        while (offset < data.Length)
        {
            ulong tag = ReadVarint(data, ref offset);
            int fieldNumber = (int)(tag >> 3);
            int wireType = (int)(tag & 0x07);

            switch (fieldNumber)
            {
                case 1 when wireType == 2:
                    int secLen = (int)ReadVarint(data, ref offset);
                    secretBytes = data.Slice(offset, secLen).ToArray();
                    offset += secLen;
                    break;
                case 2 when wireType == 2:
                    int nameLen = (int)ReadVarint(data, ref offset);
                    name = Encoding.UTF8.GetString(data.Slice(offset, nameLen));
                    offset += nameLen;
                    break;
                case 3 when wireType == 2:
                    int issuerLen = (int)ReadVarint(data, ref offset);
                    issuer = Encoding.UTF8.GetString(data.Slice(offset, issuerLen));
                    offset += issuerLen;
                    break;
                case 4 when wireType == 0:
                    algoCode = (int)ReadVarint(data, ref offset);
                    break;
                case 5 when wireType == 0:
                    digitsCode = (int)ReadVarint(data, ref offset);
                    break;
                case 6 when wireType == 0:
                    otpTypeCode = (int)ReadVarint(data, ref offset);
                    break;
                default:
                    SkipProtobufField(data, ref offset, wireType);
                    break;
            }
        }

        // Google Authenticator Migration: 1 = HOTP, 2 = TOTP, 0 = Unspecified (treat 1 as non-TOTP skip)
        if (otpTypeCode == 1 || secretBytes is null || secretBytes.Length == 0)
        {
            return null;
        }

        (string labelIssuer, string accountName) = SplitLabel(name);
        string resolvedIssuer = !string.IsNullOrWhiteSpace(issuer) ? issuer.Trim() : labelIssuer;

        OtpAlgorithm algorithm = algoCode switch
        {
            0 or 1 => OtpAlgorithm.Sha1,
            2 => OtpAlgorithm.Sha256,
            3 => OtpAlgorithm.Sha512,
            _ => OtpAlgorithm.Sha1
        };

        int digits = digitsCode switch
        {
            2 => 8,
            _ => 6
        };

        return new ParsedOtpImport
        {
            Issuer = resolvedIssuer,
            AccountName = accountName,
            Secret = Base32Encoding.ToString(secretBytes),
            Algorithm = algorithm,
            Digits = digits,
            Period = OtpAccount.DefaultPeriodSeconds
        };
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> data, ref int offset)
    {
        ulong result = 0;
        int shift = 0;

        while (offset < data.Length)
        {
            byte b = data[offset++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return result;
            }
            shift += 7;
            if (shift >= 64)
            {
                throw new FormatException("Protobuf varint 溢出。");
            }
        }

        throw new FormatException("Protobuf 数据意外截断。");
    }

    private static void SkipProtobufField(ReadOnlySpan<byte> data, ref int offset, int wireType)
    {
        switch (wireType)
        {
            case 0:
                _ = ReadVarint(data, ref offset);
                break;
            case 1:
                offset += 8;
                break;
            case 2:
                int length = (int)ReadVarint(data, ref offset);
                offset += length;
                break;
            case 5:
                offset += 4;
                break;
            default:
                throw new FormatException($"不支持的 Protobuf wire type: {wireType}");
        }

        if (offset > data.Length)
        {
            throw new FormatException("Protobuf 数据长度越界。");
        }
    }
}
