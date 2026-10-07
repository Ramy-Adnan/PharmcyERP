using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;

namespace PharmacyERP.WPF.ViewModels;

/// <summary>The saved pharmacy identity, independent of visibility choices for printed receipts.</summary>
public sealed class PharmacyBrandingViewModel : ViewModelBase, IDisposable
{
    private readonly IReceiptSettingsStore _store;
    private readonly ISessionService _session;
    private bool _active;
    private string _pharmacyName = "الصيدلية";
    private string? _logoBase64;

    public PharmacyBrandingViewModel(IReceiptSettingsStore store, ISessionService session)
    {
        _store = store; _session = session;
    }

    public string PharmacyName { get => _pharmacyName; private set => SetProperty(ref _pharmacyName, value); }
    public string? LogoBase64 { get => _logoBase64; private set => SetProperty(ref _logoBase64, value); }

    public void Activate()
    {
        if (!_active) { _store.SettingsChanged += SettingsChanged; _active = true; }
        Refresh();
    }

    private void SettingsChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var fallback = _session.CurrentSession?.BranchName;
        ReceiptSettings? settings;
        try { settings = _store.Load(); }
        catch { settings = null; } // A damaged local file must not prevent opening the main window.
        PharmacyName = !string.IsNullOrWhiteSpace(settings?.PharmacyName) ? settings.PharmacyName.Trim()
            : !string.IsNullOrWhiteSpace(fallback) ? fallback : "الصيدلية";
        LogoBase64 = settings?.LogoBase64;
    }

    public void Dispose()
    {
        if (_active) { _store.SettingsChanged -= SettingsChanged; _active = false; }
    }
}
