using System.Globalization;
using System.Windows.Data;
using PharmacyERP.WPF.Services;

namespace PharmacyERP.WPF.Converters;

public sealed class Base64LogoConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string data || string.IsNullOrWhiteSpace(data)) return null;
        try { return ReceiptLogo.Load(data); }
        catch { return null; }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
