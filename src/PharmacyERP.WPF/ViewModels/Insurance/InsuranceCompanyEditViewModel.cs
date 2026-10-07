using PharmacyERP.Application.Features.Insurance;
using PharmacyERP.Application.Features.Insurance.DTOs;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Insurance;

public class InsuranceCompanyEditViewModel : ViewModelBase
{
    private readonly IInsuranceService _insuranceService;

    private int? _id;
    private string _name = string.Empty;
    private string? _contactPhone;
    private string? _contactEmail;
    private bool _isActive = true;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public InsuranceCompanyEditViewModel(IInsuranceService insuranceService)
    {
        _insuranceService = insuranceService;
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public bool IsEditMode => _id.HasValue;
    public string DialogTitle => IsEditMode ? "تعديل شركة التأمين" : "إضافة شركة تأمين جديدة";

    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string? ContactPhone { get => _contactPhone; set => SetProperty(ref _contactPhone, value); }
    public string? ContactEmail { get => _contactEmail; set => SetProperty(ref _contactEmail, value); }
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
        var dto = await _insuranceService.GetCompanyForEditAsync(id);
        if (dto is null) return;

        _id = dto.Id;
        Name = dto.Name;
        ContactPhone = dto.ContactPhone;
        ContactEmail = dto.ContactEmail;
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
            var dto = new InsuranceCompanyUpsertDto
            {
                Id = _id,
                Name = Name,
                ContactPhone = ContactPhone,
                ContactEmail = ContactEmail,
                IsActive = IsActive
            };

            var result = _id.HasValue
                ? await _insuranceService.UpdateCompanyAsync(dto)
                : await _insuranceService.CreateCompanyAsync(dto);

            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر حفظ شركة التأمين.";
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
