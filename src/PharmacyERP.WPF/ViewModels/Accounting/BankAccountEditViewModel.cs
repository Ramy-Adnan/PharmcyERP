using System.Collections.ObjectModel;
using PharmacyERP.Application.Features.Accounting;
using PharmacyERP.Application.Features.Accounting.DTOs;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Accounting;

public class BankAccountEditViewModel : ViewModelBase
{
    private readonly IAccountingService _accountingService;

    private int? _id;
    private string _name = string.Empty;
    private string _bankName = string.Empty;
    private string _accountNumber = string.Empty;
    private int _accountId;
    private bool _isActive = true;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public BankAccountEditViewModel(IAccountingService accountingService)
    {
        _accountingService = accountingService;
        Accounts = new ObservableCollection<ChartOfAccountDto>();
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public bool IsEditMode => _id.HasValue;
    public string DialogTitle => IsEditMode ? "تعديل الحساب البنكي" : "إضافة حساب بنكي جديد";

    public ObservableCollection<ChartOfAccountDto> Accounts { get; }

    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string BankName { get => _bankName; set => SetProperty(ref _bankName, value); }
    public string AccountNumber { get => _accountNumber; set => SetProperty(ref _accountNumber, value); }
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
        await LoadAccountsAsync();
    }

    public async Task LoadForEditAsync(int id)
    {
        await LoadAccountsAsync();

        var dto = await _accountingService.GetBankAccountForEditAsync(id);
        if (dto is null) return;

        _id = dto.Id;
        Name = dto.Name;
        BankName = dto.BankName;
        AccountNumber = dto.AccountNumber;
        AccountId = dto.AccountId;
        IsActive = dto.IsActive;

        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(DialogTitle));
    }

    private async Task LoadAccountsAsync()
    {
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
            var dto = new BankAccountUpsertDto
            {
                Id = _id,
                Name = Name,
                BankName = BankName,
                AccountNumber = AccountNumber,
                AccountId = AccountId,
                IsActive = IsActive
            };

            var result = _id.HasValue
                ? await _accountingService.UpdateBankAccountAsync(dto)
                : await _accountingService.CreateBankAccountAsync(dto);

            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر حفظ الحساب البنكي.";
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
