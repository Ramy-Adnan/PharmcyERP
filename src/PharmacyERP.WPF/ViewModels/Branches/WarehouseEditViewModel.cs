using PharmacyERP.Application.Features.Branches;
using PharmacyERP.Application.Features.Branches.DTOs;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Branches;

/// <summary>ViewModel for the Add/Edit Warehouse dialog, scoped to a single Branch.</summary>
public class WarehouseEditViewModel : ViewModelBase
{
    private readonly IBranchService _branchService;

    private int? _id;
    private int _branchId;
    private string _code = string.Empty;
    private string _name = string.Empty;
    private bool _isDefault;
    private bool _isActive = true;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public WarehouseEditViewModel(IBranchService branchService)
    {
        _branchService = branchService;
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public bool IsEditMode => _id.HasValue;
    public string DialogTitle => IsEditMode ? "تعديل مخزن" : "إضافة مخزن جديد";

    public string Code { get => _code; set => SetProperty(ref _code, value); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public bool IsDefault { get => _isDefault; set => SetProperty(ref _isDefault, value); }
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand SaveCommand { get; }
    public bool SavedSuccessfully { get; private set; }
    public event Action? RequestClose;

    public void LoadForCreate(int branchId)
    {
        _id = null;
        _branchId = branchId;
        IsActive = true;
        IsDefault = false;
    }

    public void LoadForEdit(WarehouseDto dto)
    {
        _id = dto.Id;
        _branchId = dto.BranchId;
        Code = dto.Code;
        Name = dto.Name;
        IsDefault = dto.IsDefault;
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
            var dto = new WarehouseUpsertDto
            {
                Id = _id,
                BranchId = _branchId,
                Code = Code,
                Name = Name,
                IsDefault = IsDefault,
                IsActive = IsActive
            };

            var result = _id.HasValue
                ? await _branchService.UpdateWarehouseAsync(dto)
                : await _branchService.CreateWarehouseAsync(dto);

            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر حفظ المخزن.";
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
