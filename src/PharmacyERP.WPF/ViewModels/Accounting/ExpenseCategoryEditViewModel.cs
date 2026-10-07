using System.Collections.ObjectModel;
using PharmacyERP.Application.Features.Accounting;
using PharmacyERP.Application.Features.Accounting.DTOs;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Accounting;

public class ExpenseCategoryEditViewModel : ViewModelBase
{
    private readonly IAccountingService _accountingService;

    private int? _id;
    private string _name = string.Empty;
    private int _defaultExpenseAccountId;
    private bool _isActive = true;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public ExpenseCategoryEditViewModel(IAccountingService accountingService)
    {
        _accountingService = accountingService;
        Accounts = new ObservableCollection<ChartOfAccountDto>();
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public bool IsEditMode => _id.HasValue;
    public string DialogTitle => IsEditMode ? "تعديل فئة المصروف" : "إضافة فئة مصروف جديدة";

    public ObservableCollection<ChartOfAccountDto> Accounts { get; }

    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public int DefaultExpenseAccountId { get => _defaultExpenseAccountId; set => SetProperty(ref _defaultExpenseAccountId, value); }
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

        var dto = await _accountingService.GetExpenseCategoryForEditAsync(id);
        if (dto is null) return;

        _id = dto.Id;
        Name = dto.Name;
        DefaultExpenseAccountId = dto.DefaultExpenseAccountId;
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
            var dto = new ExpenseCategoryUpsertDto
            {
                Id = _id,
                Name = Name,
                DefaultExpenseAccountId = DefaultExpenseAccountId,
                IsActive = IsActive
            };

            var result = _id.HasValue
                ? await _accountingService.UpdateExpenseCategoryAsync(dto)
                : await _accountingService.CreateExpenseCategoryAsync(dto);

            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر حفظ فئة المصروف.";
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
