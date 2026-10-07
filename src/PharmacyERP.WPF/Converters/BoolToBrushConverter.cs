using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PharmacyERP.WPF.Converters;

/// <summary>Converts bool to a brush — true renders in a success color, false in a warning/error color. ConverterParameter="Invert" flips which brush maps to true.</summary>
public class BoolToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(0x1F, 0x6F, 0x50));
    private static readonly SolidColorBrush WarningBrush = new(Color.FromRgb(0xDC, 0x26, 0x26));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var boolValue = value is bool b && b;
        if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase))
            boolValue = !boolValue;

        return boolValue ? SuccessBrush : WarningBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
