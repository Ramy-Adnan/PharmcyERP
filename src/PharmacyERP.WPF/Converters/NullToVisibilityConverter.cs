using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PharmacyERP.WPF.Converters;

/// <summary>Visible when the bound value is non-null; collapsed when null. ConverterParameter="Invert" flips this (used for "empty state" placeholders).</summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasValue = value is not null;
        if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase))
            hasValue = !hasValue;

        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
