namespace KeyLet.Core.Models;

/// <summary>
/// 动态码复制到剪贴板时的格式模式。
/// </summary>
public enum CopyFormatMode
{
    /// <summary>
    /// 仅复制纯动态码（如 123456）。
    /// </summary>
    CodeOnly = 0,

    /// <summary>
    /// 复制带前后缀的完整字符串（如 qwer123456tyu）。
    /// </summary>
    WithAffix = 1
}
