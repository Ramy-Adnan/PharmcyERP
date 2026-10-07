using System.IO;
using Microsoft.Win32;
using PharmacyERP.Application.Features.Licensing;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;

namespace PharmacyERP.WPF.ViewModels.SystemManagement;

/// <summary>
/// ViewModel for the License screen: shows the current activation status,
/// the hardware fingerprint the administrator needs to send the vendor when
/// requesting a license, and lets them import the signed .lic file the
/// vendor sends back. No network call is made — activation is entirely
/// file-based, so the pharmacy stays operable with no internet connection.
/// </summary>
public class LicenseViewModel : ViewModelBase
{
    private readonly ILicenseService _licenseService;
    private readonly IDialogService _dialogService;

    private LicenseStatusDto? _status;
    private string _hardwareFingerprint = string.Empty;
    private bool _isBusy;

    public LicenseViewModel(ILicenseService licenseService, IDialogService dialogService)
    {
        _licenseService = licenseService;
        _dialogService = dialogService;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        ImportLicenseCommand = new AsyncRelayCommand(ImportLicenseAsync, () => !IsBusy);
        CopyFingerprintCommand = new RelayCommand(CopyFingerprint);
    }

    public LicenseStatusDto? Status { get => _status; private set => SetProperty(ref _status, value); }
    public string HardwareFingerprint { get => _hardwareFingerprint; private set => SetProperty(ref _hardwareFingerprint, value); }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ImportLicenseCommand { get; }
    public RelayCommand CopyFingerprintCommand { get; }

    public async Task InitializeAsync()
    {
        HardwareFingerprint = _licenseService.GetHardwareFingerprint();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Status = await _licenseService.GetLicenseStatusAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ImportLicenseAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "ملفات الترخيص (*.lic)|*.lic|كل الملفات (*.*)|*.*",
            Title = "استيراد ملف الترخيص"
        };
        if (dialog.ShowDialog() != true) return;

        IsBusy = true;
        try
        {
            var content = await File.ReadAllTextAsync(dialog.FileName);
            var result = await _licenseService.ActivateLicenseAsync(content);

            if (!result.Succeeded)
            {
                _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذّر تفعيل الترخيص.");
                return;
            }

            _dialogService.ShowInfo("تم تفعيل الترخيص بنجاح.");
            await LoadAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CopyFingerprint()
    {
        System.Windows.Clipboard.SetText(HardwareFingerprint);
        _dialogService.ShowInfo("تم نسخ بصمة الجهاز. أرسلها للجهة المرخِّصة لإصدار ترخيص جديد.");
    }
}
