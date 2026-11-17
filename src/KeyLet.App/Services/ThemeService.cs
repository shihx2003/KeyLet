using KeyLet.Core.Models;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace KeyLet.App.Services;

/// <summary>
/// 应用浅色/深色/跟随系统主题管理服务。
/// </summary>
public static class ThemeService
{
    public static void Apply(ThemePreference preference)
    {
        ApplicationTheme targetTheme = preference switch
        {
            ThemePreference.Light => ApplicationTheme.Light,
            ThemePreference.Dark => ApplicationTheme.Dark,
            ThemePreference.System => ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark
                ? ApplicationTheme.Dark
                : ApplicationTheme.Light,
            _ => ApplicationTheme.Light
        };

        ApplicationThemeManager.Apply(targetTheme, WindowBackdropType.Mica, updateAccent: true);
    }
}
