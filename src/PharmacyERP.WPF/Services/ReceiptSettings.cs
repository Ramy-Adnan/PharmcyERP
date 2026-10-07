namespace PharmacyERP.WPF.Services;

/// <summary>Receipt branding for the current Windows profile, independent of the checkout folder.</summary>
public sealed class ReceiptSettings
{
    public string PharmacyName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string FooterMessage { get; set; } = "شكراً لزيارتكم، نتمنى لكم دوام الصحة والعافية";
    // Store the image itself so moving or deleting the original file does not break printing.
    public string? LogoBase64 { get; set; }
    public bool ShowLogo { get; set; } = true;
    public bool ShowPharmacyName { get; set; } = true;
    public bool ShowAddress { get; set; } = true;
    public bool ShowFooterMessage { get; set; } = true;
}

public interface IReceiptSettingsStore
{
    event EventHandler? SettingsChanged;
    ReceiptSettings Load();
    void Save(ReceiptSettings settings);
}
