using PharmacyERP.Application.Features.Prescriptions;
using PharmacyERP.Application.Features.Prescriptions.DTOs;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Prescriptions;

public class DoctorEditViewModel : ViewModelBase
{
    private readonly IPrescriptionService _prescriptionService;

    private int? _id;
    private string _fullName = string.Empty;
    private string? _licenseNumber;
    private string? _specialty;
    private string? _phone;
    private bool _isActive = true;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public DoctorEditViewModel(IPrescriptionService prescriptionService)
    {
        _prescriptionService = prescriptionService;
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public bool IsEditMode => _id.HasValue;
    public string DialogTitle => IsEditMode ? "تعديل بيانات الطبيب" : "إضافة طبيب جديد";

    public string FullName { get => _fullName; set => SetProperty(ref _fullName, value); }
    public string? LicenseNumber { get => _licenseNumber; set => SetProperty(ref _licenseNumber, value); }
    public string? Specialty { get => _specialty; set => SetProperty(ref _specialty, value); }
    public string? Phone { get => _phone; set => SetProperty(ref _phone, value); }
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand SaveCommand { get; }
    public bool SavedSuccessfully { get; private set; }
    public event Action? RequestClose;

    public void LoadForCreate()
    {
        _id = null;
        IsActive = true;
    }

    public async Task LoadForEditAsync(int id)
    {
        var dto = await _prescriptionService.GetDoctorForEditAsync(id);
        if (dto is null) return;

        _id = dto.Id;
        FullName = dto.FullName;
        LicenseNumber = dto.LicenseNumber;
        Specialty = dto.Specialty;
        Phone = dto.Phone;
        IsActive = dto.IsActive;

        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(DialogTitle));
    }

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var dto = new DoctorUpsertDto
            {
                Id = _id,
                FullName = FullName,
                LicenseNumber = LicenseNumber,
                Specialty = Specialty,
                Phone = Phone,
                IsActive = IsActive
            };

            var result = _id.HasValue
                ? await _prescriptionService.UpdateDoctorAsync(dto)
                : await _prescriptionService.CreateDoctorAsync(dto);

            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر حفظ بيانات الطبيب.";
                return;
            }

            SavedSuccessfully = true;
            RequestClose?.Invoke();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
