using System.Collections.ObjectModel;
using PharmacyERP.Application.Features.Accounting;
using PharmacyERP.Application.Features.Accounting.DTOs;
using PharmacyERP.Application.Features.Branches;
using PharmacyERP.Application.Features.Branches.DTOs;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Accounting;

public class CashBoxEditViewModel : ViewModelBase
{
    private readonly IAccountingService _accountingService;
    private readonly IBranchService _branchService;

    private int? _id;
    private int _branchId;
    private string _name = string.Empty;
    private int _accountId;
    private bool _isActive = true;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public CashBoxEditViewModel(IAccountingService accountingService, IBranchService branchService)
    {
        _accountingService = accountingService;
        _branchService = branchService;

        Branches = new ObservableCollection<BranchDto>();
        Accounts = new ObservableCollection<ChartOfAccountDto>();

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public bool IsEditMode => _id.HasValue;
    public string DialogTitle => IsEditMode ? "تعديل الصندوق" : "إضافة صندوق نقدي جديد";

    public ObservableCollection<BranchDto> Branches { get; }
    public ObservableCollection<ChartOfAccountDto> Accounts { get; }

    public int BranchId { get => _branchId; set => SetProperty(ref _branchId, value); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public int AccountId { get => _accountId; set => SetProperty(ref _accountId, value); }
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand SaveCommand { get; }
    public bool SavedSuccessfully { get; private set; }
    public event Action? RequestClose;

    public async Task LoadForCreateAsync()
    {
        _id = null;
        IsActive = true;
        await LoadLookupsAsync();
    }

    public async Task LoadForEditAsync(int id)
    {
        await LoadLookupsAsync();

        var dto = await _accountingService.GetCashBoxForEditAsync(id);
        if (dto is null) return;

        _id = dto.Id;
        BranchId = dto.BranchId;
        Name = dto.Name;
        AccountId = dto.AccountId;
        IsActive = dto.IsActive;

        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(DialogTitle));
    }

    private async Task LoadLookupsAsync()
    {
        var branches = await _branchService.GetAllAsync(includeInactive: false);
        Branches.Clear();
        foreach (var b in branches) Branches.Add(b);

        var accounts = await _accountingService.GetAccountsAsync(includeInactive: false);
        Accounts.Clear();
        foreach (var a in accounts) Accounts.Add(a);
    }

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var dto = new CashBoxUpsertDto
            {
                Id = _id,
                BranchId = BranchId,
                Name = Name,
                AccountId = AccountId,
                IsActive = IsActive
            };

            var result = _id.HasValue
                ? await _accountingService.UpdateCashBoxAsync(dto)
                : await _accountingService.CreateCashBoxAsync(dto);

            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر حفظ الصندوق.";
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
