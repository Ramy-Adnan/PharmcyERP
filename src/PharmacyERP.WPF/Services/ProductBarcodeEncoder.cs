using ZXing;
using ZXing.Common;

namespace PharmacyERP.WPF.Services;

/// <summary>
/// A one-module-high barcode, including quiet zones. The renderer repeats its
/// single row vertically and scales each module uniformly; it must not crop it.
/// </summary>
public sealed record ProductBarcodePattern(string Text, BarcodeFormat Format, BitMatrix Modules)
{
    public string FormatName => Format == BarcodeFormat.EAN_13 ? "EAN-13" : "Code 128";
    public int Width => Modules.Width;
}

/// <summary>Encodes the stored barcode exactly, without replacing its check digit or leading zeros.</summary>
public static class ProductBarcodeEncoder
{
    public const int MaximumLength = 32;
    public const int QuietZoneModules = 10;

    public static ProductBarcodePattern Encode(string? barcode)
    {
        if (string.IsNullOrEmpty(barcode))
            throw new ArgumentException("الباركود مطلوب لطباعة الملصق.", nameof(barcode));
        if (barcode.Length > MaximumLength)
            throw new ArgumentException($"الباركود طويل جداً للملصق؛ الحد الأقصى {MaximumLength} حرفاً.", nameof(barcode));
        // The POS HID input accepts visible ASCII without spaces. Reject rather
        // than print a label whose scan would be discarded or change the value.
        if (barcode.Any(c => c < '!' || c > '~'))
            throw new ArgumentException("استخدم باركوداً بأرقام أو أحرف إنجليزية ورموز، دون مسافات أو أحرف عربية.", nameof(barcode));

        var format = IsValidEan13(barcode) ? BarcodeFormat.EAN_13 : BarcodeFormat.CODE_128;
        var modules = new MultiFormatWriter().encode(barcode, format, 0, 1,
            new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = QuietZoneModules * 2 });
        return new ProductBarcodePattern(barcode, format, modules);
    }

    private static bool IsValidEan13(string value)
    {
        if (value.Length != 13 || value.Any(c => c < '0' || c > '9')) return false;
        var weightedSum = 0;
        for (var i = 0; i < value.Length; i++)
            weightedSum += (value[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return weightedSum % 10 == 0;
    }
}
