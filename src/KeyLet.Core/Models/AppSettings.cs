namespace KeyLet.Core.Models;

/// <summary>
/// 应用便携运行配置（保存于 data/settings.json）。
/// </summary>
public sealed record AppSettings
{
    public const int DefaultClipboardClearSeconds = 20;

    public int ClipboardClearSeconds { get; init; } = DefaultClipboardClearSeconds;

    public bool IsAutoUnlockEnabled { get; init; } = true;

    public ThemePreference Theme { get; init; } = ThemePreference.System;

    public CopyFormatMode GlobalDefaultCopyMode { get; init; } = CopyFormatMode.CodeOnly;
}
