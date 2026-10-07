using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Accounting;
using PharmacyERP.Application.Features.Accounting.DTOs;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.Views.Accounting;

namespace PharmacyERP.WPF.ViewModels.Accounting;

public class ExpenseCategoriesViewModel : ViewModelBase
{
    private readonly IAccountingService _accountingService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;

    private ExpenseCategoryDto? _selectedCategory;
    private bool _isBusy;

    public ExpenseCategoriesViewModel(IAccountingService accountingService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _accountingService = accountingService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        Categories = new ObservableCollection<ExpenseCategoryDto>();

        RefreshCommand = new AsyncRelayCommand(LoadCategoriesAsync);
        AddCategoryCommand = new AsyncRelayCommand(AddCategoryAsync, () => CanManage);
        EditCategoryCommand = new AsyncRelayCommand(EditCategoryAsync, () => CanManage && SelectedCategory is not null);
    }

    public bool CanManage => _currentUserService.HasPermission("Accounting.ManageExpenses");

    public ObservableCollection<ExpenseCategoryDto> Categories { get; }

    public ExpenseCategoryDto? SelectedCategory
    {
        get => _selectedCategory;
        set { if (SetProperty(ref _selectedCategory, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand AddCategoryCommand { get; }
    public AsyncRelayCommand EditCategoryCommand { get; }

    public async Task InitializeAsync() => await LoadCategoriesAsync();

    private async Task LoadCategoriesAsync()
    {
        IsBusy = true;
        try
        {
            var categories = await _accountingService.GetExpenseCategoriesAsync();
            Categories.Clear();
            foreach (var c in categories) Categories.Add(c);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddCategoryAsync()
    {
        var window = _dialogService.CreateDialog<ExpenseCategoryEditDialog>();
        var vm = (ExpenseCategoryEditViewModel)window.DataContext;
        await vm.LoadForCreateAsync();

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadCategoriesAsync();
    }

    private async Task EditCategoryAsync()
    {
        if (SelectedCategory is null) return;

        var window = _dialogService.CreateDialog<ExpenseCategoryEditDialog>();
        var vm = (ExpenseCategoryEditViewModel)window.DataContext;
        await vm.LoadForEditAsync(SelectedCategory.Id);

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadCategoriesAsync();
    }
}
