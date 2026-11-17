using KeyLet.Core.Models;

namespace KeyLet.Core.Totp;

/// <summary>
/// 动态验证码展示与剪贴板输出格式化工具。
/// </summary>
public static class CodeFormatter
{
    /// <summary>
    /// 为主界面展示格式化动态码（6 位分为 3+3，8 位分为 4+4）。
    /// </summary>
    public static string FormatForDisplay(string rawCode)
    {
        if (string.IsNullOrEmpty(rawCode))
        {
            return string.Empty;
        }

        return rawCode.Length switch
        {
            6 => $"{rawCode[..3]} {rawCode[3..]}",
            8 => $"{rawCode[..4]} {rawCode[4..]}",
            _ => rawCode
        };
    }

    /// <summary>
    /// 按指定复制模式生成最终写入剪贴板的文本。
    /// </summary>
    public static string FormatForClipboard(string rawCode, string prefix, string suffix, CopyFormatMode mode)
    {
        ArgumentNullException.ThrowIfNull(rawCode);

        return mode switch
        {
            CopyFormatMode.CodeOnly => rawCode,
            CopyFormatMode.WithAffix => $"{prefix}{rawCode}{suffix}",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "不支持的复制模式。")
        };
    }
}
