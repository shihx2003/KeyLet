using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace KeyLet.App.Converters;

/// <summary>
/// 布尔值与 Visibility 的通用转换器，支持 ConverterParameter="Invert" 反转。
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = value is bool b && b;
        if (parameter is string param && string.Equals(param, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool visible = value is Visibility v && v == Visibility.Visible;
        if (parameter is string param && string.Equals(param, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            visible = !visible;
        }

        return visible;
    }
}
