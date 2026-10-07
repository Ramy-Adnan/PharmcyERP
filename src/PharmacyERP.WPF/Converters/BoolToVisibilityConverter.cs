using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PharmacyERP.WPF.Converters;

/// <summary>Converts bool to Visibility. Supports inversion via ConverterParameter="Invert" (e.g. hide an admin-only button for non-admins).</summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var boolValue = value is bool b && b;
        if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase))
            boolValue = !boolValue;

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
