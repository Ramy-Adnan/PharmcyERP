using PharmacyERP.Application.Features.Purchasing;
using PharmacyERP.Application.Features.Purchasing.DTOs;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Purchasing;

public class SupplierEditViewModel : ViewModelBase
{
    private readonly IPurchasingService _purchasingService;

    private int? _id;
    private string _code = string.Empty;
    private string _name = string.Empty;
    private string? _contactPerson;
    private string? _phone;
    private string? _email;
    private string? _address;
    private string? _taxRegistrationNumber;
    private int _paymentTermsDays;
    private bool _isActive = true;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public SupplierEditViewModel(IPurchasingService purchasingService)
    {
        _purchasingService = purchasingService;
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public bool IsEditMode => _id.HasValue;
    public string DialogTitle => IsEditMode ? "تعديل بيانات المورد" : "إضافة مورد جديد";

    public string Code { get => _code; set => SetProperty(ref _code, value); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string? ContactPerson { get => _contactPerson; set => SetProperty(ref _contactPerson, value); }
    public string? Phone { get => _phone; set => SetProperty(ref _phone, value); }
    public string? Email { get => _email; set => SetProperty(ref _email, value); }
    public string? Address { get => _address; set => SetProperty(ref _address, value); }
    public string? TaxRegistrationNumber { get => _taxRegistrationNumber; set => SetProperty(ref _taxRegistrationNumber, value); }
    public int PaymentTermsDays { get => _paymentTermsDays; set => SetProperty(ref _paymentTermsDays, value); }
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

    public async Task LoadForEditAsync(int supplierId)
    {
        var dto = await _purchasingService.GetSupplierForEditAsync(supplierId);
        if (dto is null) return;

        _id = dto.Id;
        Code = dto.Code;
        Name = dto.Name;
        ContactPerson = dto.ContactPerson;
        Phone = dto.Phone;
        Email = dto.Email;
        Address = dto.Address;
        TaxRegistrationNumber = dto.TaxRegistrationNumber;
        PaymentTermsDays = dto.PaymentTermsDays;
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
            var dto = new SupplierUpsertDto
            {
                Id = _id,
                Code = Code,
                Name = Name,
                ContactPerson = ContactPerson,
                Phone = Phone,
                Email = Email,
                Address = Address,
                TaxRegistrationNumber = TaxRegistrationNumber,
                PaymentTermsDays = PaymentTermsDays,
                IsActive = IsActive
            };

            var result = _id.HasValue
                ? await _purchasingService.UpdateSupplierAsync(dto)
                : await _purchasingService.CreateSupplierAsync(dto);

            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر حفظ بيانات المورد.";
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
