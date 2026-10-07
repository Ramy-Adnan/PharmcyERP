using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PharmacyERP.WPF.Converters;

/// <summary>Collapses an element when the bound string is null/empty — used to hide validation/error messages until they have content.</summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
