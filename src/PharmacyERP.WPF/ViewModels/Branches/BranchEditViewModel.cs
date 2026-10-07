using System.Collections.ObjectModel;
using PharmacyERP.Application.Features.Branches;
using PharmacyERP.Application.Features.Branches.DTOs;
using PharmacyERP.Domain.Enums;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;

namespace PharmacyERP.WPF.ViewModels.Branches;

/// <summary>ViewModel for the Add/Edit Branch dialog.</summary>
public class BranchEditViewModel : ViewModelBase
{
    private readonly IBranchService _branchService;
    private readonly IDialogService _dialogService;

    private int? _id;
    private string _code = string.Empty;
    private string _name = string.Empty;
    private BranchType _type = BranchType.SubBranch;
    private string? _address;
    private string? _phone;
    private string? _taxRegistrationNumber;
    private string? _licenseNumber;
    private bool _isActive = true;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public BranchEditViewModel(IBranchService branchService, IDialogService dialogService)
    {
        _branchService = branchService;
        _dialogService = dialogService;

        BranchTypes = new ObservableCollection<BranchType>(Enum.GetValues<BranchType>());
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public bool IsEditMode => _id.HasValue;
    public string DialogTitle => IsEditMode ? "تعديل بيانات الفرع" : "إضافة فرع جديد";

    public ObservableCollection<BranchType> BranchTypes { get; }

    public string Code { get => _code; set => SetProperty(ref _code, value); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public BranchType Type { get => _type; set => SetProperty(ref _type, value); }
    public string? Address { get => _address; set => SetProperty(ref _address, value); }
    public string? Phone { get => _phone; set => SetProperty(ref _phone, value); }
    public string? TaxRegistrationNumber { get => _taxRegistrationNumber; set => SetProperty(ref _taxRegistrationNumber, value); }
    public string? LicenseNumber { get => _licenseNumber; set => SetProperty(ref _licenseNumber, value); }
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand SaveCommand { get; }

    /// <summary>Result set by SaveAsync; the dialog's code-behind reads this to close with DialogResult = true.</summary>
    public bool SavedSuccessfully { get; private set; }

    public void LoadForCreate()
    {
        _id = null;
        IsActive = true;
    }

    public void LoadForEdit(BranchDto dto)
    {
        _id = dto.Id;
        Code = dto.Code;
        Name = dto.Name;
        Type = dto.Type;
        Address = dto.Address;
        Phone = dto.Phone;
        TaxRegistrationNumber = dto.TaxRegistrationNumber;
        LicenseNumber = dto.LicenseNumber;
        IsActive = dto.IsActive;
        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(DialogTitle));
    }

    public event Action? RequestClose;

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var dto = new BranchUpsertDto
            {
                Id = _id,
                Code = Code,
                Name = Name,
                Type = Type,
                Address = Address,
                Phone = Phone,
                TaxRegistrationNumber = TaxRegistrationNumber,
                LicenseNumber = LicenseNumber,
                IsActive = IsActive
            };

            var result = _id.HasValue
                ? await _branchService.UpdateAsync(dto)
                : await _branchService.CreateAsync(dto);

            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر حفظ الفرع.";
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
