using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;

namespace PharmacyERP.WPF.ViewModels.SystemManagement;

public sealed class ReceiptSettingsViewModel : ViewModelBase
{
    private readonly IReceiptSettingsStore _store;
    private readonly ICurrentUserService _currentUser;
    private readonly ISessionService _session;
    private string _pharmacyName = string.Empty, _address = string.Empty, _footerMessage = string.Empty;
    private string? _logoBase64;
    private bool _showLogo = true, _showPharmacyName = true, _showAddress = true, _showFooterMessage = true;
    private string _statusMessage = string.Empty;
    private bool _hasUnsavedChanges;

    public ReceiptSettingsViewModel(IReceiptSettingsStore store, ICurrentUserService currentUser, ISessionService session)
    {
        _store = store; _currentUser = currentUser; _session = session;
        SaveCommand = new RelayCommand(Save, () => CanManage);
        RemoveLogoCommand = new RelayCommand(() => SetLogo(null), () => CanManage && HasLogo);
    }

    public bool CanManage => _currentUser.HasPermission("Sales.UsePos") || _currentUser.HasPermission("Branches.Manage");
    public string PharmacyName { get => _pharmacyName; set => Edit(ref _pharmacyName, value, nameof(PharmacyName)); }
    public string Address { get => _address; set => Edit(ref _address, value, nameof(Address)); }
    public string FooterMessage { get => _footerMessage; set => Edit(ref _footerMessage, value, nameof(FooterMessage)); }
    public bool ShowLogo { get => _showLogo; set => Edit(ref _showLogo, value, nameof(ShowLogo)); }
    public bool ShowPharmacyName { get => _showPharmacyName; set => Edit(ref _showPharmacyName, value, nameof(ShowPharmacyName)); }
    public bool ShowAddress { get => _showAddress; set => Edit(ref _showAddress, value, nameof(ShowAddress)); }
    public bool ShowFooterMessage { get => _showFooterMessage; set => Edit(ref _showFooterMessage, value, nameof(ShowFooterMessage)); }
    public bool HasLogo => _logoBase64 is not null;
    public string? LogoBase64 => _logoBase64;
    public string BranchName => _session.CurrentSession?.BranchName ?? "الصيدلية";
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public bool HasUnsavedChanges { get => _hasUnsavedChanges; private set => SetProperty(ref _hasUnsavedChanges, value); }
    public RelayCommand SaveCommand { get; }
    public RelayCommand RemoveLogoCommand { get; }

    public void Initialize()
    {
        ReceiptSettings settings;
        try { settings = _store.Load(); StatusMessage = "الإعدادات المحفوظة تُطبق على الطباعة وإعادة الطباعة على هذا الجهاز."; }
        catch (Exception ex) { settings = new(); StatusMessage = $"تعذر تحميل الإعدادات؛ اختر بياناتك واحفظ من جديد. {ex.Message}"; }
        _pharmacyName = string.IsNullOrWhiteSpace(settings.PharmacyName) ? BranchName : settings.PharmacyName;
        _address = settings.Address; _footerMessage = settings.FooterMessage; _logoBase64 = settings.LogoBase64;
        _showLogo = settings.ShowLogo; _showPharmacyName = settings.ShowPharmacyName;
        _showAddress = settings.ShowAddress; _showFooterMessage = settings.ShowFooterMessage;
        HasUnsavedChanges = false;
        OnPropertyChanged(string.Empty);
    }

    public ReceiptSettings Snapshot() => new()
    {
        PharmacyName = PharmacyName.Trim(), Address = Address.Trim(), FooterMessage = FooterMessage.Trim(),
        LogoBase64 = LogoBase64, ShowLogo = ShowLogo, ShowPharmacyName = ShowPharmacyName,
        ShowAddress = ShowAddress, ShowFooterMessage = ShowFooterMessage
    };

    public void SetLogo(string? data)
    {
        if (!CanManage || _logoBase64 == data) return;
        _logoBase64 = data;
        MarkChanged(); OnPropertyChanged(nameof(LogoBase64)); OnPropertyChanged(nameof(HasLogo));
    }

    public void ShowError(string message) => StatusMessage = message;

    public void Save()
    {
        if (!CanManage) { StatusMessage = "لا تملك صلاحية إعداد الوصل."; return; }
        try
        {
            var settings = Snapshot();
            if (settings.ShowPharmacyName && string.IsNullOrWhiteSpace(settings.PharmacyName))
                throw new InvalidOperationException("أدخل اسم الصيدلية أو ألغِ إظهاره في الوصل.");
            _store.Save(settings);
            HasUnsavedChanges = false;
            StatusMessage = "تم حفظ الإعدادات. ستظهر في الوصل القادم وفي إعادة طباعة الفواتير.";
        }
        catch (Exception ex) { StatusMessage = $"تعذر الحفظ: {ex.Message}"; }
    }

    private void Edit<T>(ref T field, T value, string name)
    {
        if (SetProperty(ref field, value, name)) MarkChanged();
    }

    private void MarkChanged()
    {
        HasUnsavedChanges = true;
        StatusMessage = "تغييرات غير محفوظة — اضغط حفظ لتطبيقها على الطباعة.";
    }
}
