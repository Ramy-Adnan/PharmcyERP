using PharmacyERP.Application.Features.Sales;
using PharmacyERP.Application.Features.Sales.DTOs;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Sales;

public class CustomerEditViewModel : ViewModelBase
{
    private readonly ISalesService _salesService;

    private int? _id;
    private string _code = string.Empty;
    private string _name = string.Empty;
    private string? _phone;
    private string? _address;
    private bool _isActive = true;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public CustomerEditViewModel(ISalesService salesService)
    {
        _salesService = salesService;
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public bool IsEditMode => _id.HasValue;
    public string DialogTitle => IsEditMode ? "تعديل بيانات العميل" : "إضافة عميل جديد";

    public string Code { get => _code; set => SetProperty(ref _code, value); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string? Phone { get => _phone; set => SetProperty(ref _phone, value); }
    public string? Address { get => _address; set => SetProperty(ref _address, value); }
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
        var dto = await _salesService.GetCustomerForEditAsync(id);
        if (dto is null) return;

        _id = dto.Id;
        Code = dto.Code;
        Name = dto.Name;
        Phone = dto.Phone;
        Address = dto.Address;
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
            var dto = new CustomerUpsertDto
            {
                Id = _id,
                Code = Code,
                Name = Name,
                Phone = Phone,
                Address = Address,
                IsActive = IsActive
            };

            var result = _id.HasValue
                ? await _salesService.UpdateCustomerAsync(dto)
                : await _salesService.CreateCustomerAsync(dto);

            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر حفظ بيانات العميل.";
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
