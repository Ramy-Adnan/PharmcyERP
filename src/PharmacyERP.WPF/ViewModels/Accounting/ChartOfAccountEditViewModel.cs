using System.Collections.ObjectModel;
using PharmacyERP.Application.Features.Accounting;
using PharmacyERP.Application.Features.Accounting.DTOs;
using PharmacyERP.Domain.Enums;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Accounting;

public class ChartOfAccountEditViewModel : ViewModelBase
{
    private readonly IAccountingService _accountingService;

    private int? _id;
    private string _code = string.Empty;
    private string _name = string.Empty;
    private AccountType _type = AccountType.Asset;
    private int? _parentAccountId;
    private bool _isActive = true;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public ChartOfAccountEditViewModel(IAccountingService accountingService)
    {
        _accountingService = accountingService;

        AccountTypes = new ObservableCollection<AccountType>(Enum.GetValues<AccountType>());
        ParentAccounts = new ObservableCollection<ChartOfAccountDto>();

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public bool IsEditMode => _id.HasValue;
    public string DialogTitle => IsEditMode ? "تعديل الحساب" : "إضافة حساب جديد";

    public ObservableCollection<AccountType> AccountTypes { get; }
    public ObservableCollection<ChartOfAccountDto> ParentAccounts { get; }

    public string Code { get => _code; set => SetProperty(ref _code, value); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public AccountType Type { get => _type; set => SetProperty(ref _type, value); }
    public int? ParentAccountId { get => _parentAccountId; set => SetProperty(ref _parentAccountId, value); }
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
        await LoadParentAccountsAsync();
    }

    public async Task LoadForEditAsync(int id)
    {
        await LoadParentAccountsAsync();

        var dto = await _accountingService.GetAccountForEditAsync(id);
        if (dto is null) return;

        _id = dto.Id;
        Code = dto.Code;
        Name = dto.Name;
        Type = dto.Type;
        ParentAccountId = dto.ParentAccountId;
        IsActive = dto.IsActive;

        // Exclude self from the parent picker to prevent an account becoming its own ancestor.
        var self = ParentAccounts.FirstOrDefault(a => a.Id == dto.Id);
        if (self is not null) ParentAccounts.Remove(self);

        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(DialogTitle));
    }

    private async Task LoadParentAccountsAsync()
    {
        var accounts = await _accountingService.GetAccountsAsync(includeInactive: false);
        ParentAccounts.Clear();
        foreach (var a in accounts) ParentAccounts.Add(a);
    }

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var dto = new ChartOfAccountUpsertDto
            {
                Id = _id,
                Code = Code,
                Name = Name,
                Type = Type,
                ParentAccountId = ParentAccountId,
                IsActive = IsActive
            };

            var result = _id.HasValue
                ? await _accountingService.UpdateAccountAsync(dto)
                : await _accountingService.CreateAccountAsync(dto);

            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر حفظ الحساب.";
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
