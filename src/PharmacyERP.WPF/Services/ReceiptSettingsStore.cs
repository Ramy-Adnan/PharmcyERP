using System.IO;
using System.Text.Json;

namespace PharmacyERP.WPF.Services;

public sealed class ReceiptSettingsStore : IReceiptSettingsStore
{
    public const int MaxLogoBytes = 2 * 1024 * 1024;
    private readonly string _filePath;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ReceiptSettingsStore() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PharmacyERP", "receipt-settings.json")) { }

    public ReceiptSettingsStore(string filePath) => _filePath = filePath;

    public ReceiptSettings Load()
    {
        if (!File.Exists(_filePath)) return new ReceiptSettings();
        if (new FileInfo(_filePath).Length > MaxLogoBytes * 2)
            throw new InvalidOperationException("ملف إعدادات الوصل أكبر من الحجم المسموح.");
        var settings = JsonSerializer.Deserialize<ReceiptSettings>(File.ReadAllText(_filePath), JsonOptions)
            ?? throw new InvalidOperationException("ملف إعدادات الوصل غير صالح. افتح الإعدادات واحفظها من جديد.");
        Validate(settings);
        return settings;
    }

    public void Save(ReceiptSettings settings)
    {
        Validate(settings);
        var directory = Path.GetDirectoryName(Path.GetFullPath(_filePath))!;
        Directory.CreateDirectory(directory);
        var temporaryFile = Path.Combine(directory, $"receipt-settings-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryFile, JsonSerializer.Serialize(settings, JsonOptions));
            // Replace only after writing succeeds; a failed save leaves the previous settings intact.
            File.Move(temporaryFile, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryFile)) File.Delete(temporaryFile);
        }
    }

    private static void Validate(ReceiptSettings settings)
    {
        if (settings.PharmacyName is null || settings.PharmacyName.Length > 120)
            throw new InvalidOperationException("اسم الصيدلية يجب ألا يتجاوز 120 حرفاً.");
        if (settings.Address is null || settings.Address.Length > 250)
            throw new InvalidOperationException("العنوان يجب ألا يتجاوز 250 حرفاً.");
        if (settings.FooterMessage is null || settings.FooterMessage.Length > 400)
            throw new InvalidOperationException("العبارة الختامية يجب ألا تتجاوز 400 حرف.");
        if (settings.LogoBase64 is not null)
        {
            byte[] logo;
            try { logo = Convert.FromBase64String(settings.LogoBase64); }
            catch (FormatException) { throw new InvalidOperationException("بيانات الشعار غير صالحة. اختر الصورة من جديد."); }
            if (logo.Length == 0 || logo.Length > MaxLogoBytes)
                throw new InvalidOperationException("الشعار يجب ألا يتجاوز 2 MB.");
            var isPng = logo.Length >= 24 && logo.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            var isJpeg = logo.Length >= 4 && logo[0] == 255 && logo[1] == 216 && logo[2] == 255
                && logo[^2] == 255 && logo[^1] == 217;
            if (!isPng && !isJpeg)
                throw new InvalidOperationException("بيانات الشعار ليست صورة PNG أو JPG صالحة. اختر الصورة من جديد.");
        }
    }
}
