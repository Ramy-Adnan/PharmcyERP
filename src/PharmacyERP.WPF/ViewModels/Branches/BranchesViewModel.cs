using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Branches;
using PharmacyERP.Application.Features.Branches.DTOs;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.Views.Branches;

namespace PharmacyERP.WPF.ViewModels.Branches;

/// <summary>
/// Master-detail ViewModel: a grid of Branches on the left, and the
/// warehouses belonging to the selected branch on the right. This is the
/// screen a pharmacy-chain administrator uses to onboard a new branch and
/// its storage locations before any operational module (Inventory, Sales)
/// can be used there.
/// </summary>
public class BranchesViewModel : ViewModelBase
{
    private readonly IBranchService _branchService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;

    private BranchDto? _selectedBranch;
    private WarehouseDto? _selectedWarehouse;
    private bool _isBusy;

    public BranchesViewModel(IBranchService branchService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _branchService = branchService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        Branches = new ObservableCollection<BranchDto>();
        Warehouses = new ObservableCollection<WarehouseDto>();

        RefreshCommand = new AsyncRelayCommand(LoadBranchesAsync);
        AddBranchCommand = new AsyncRelayCommand(AddBranchAsync, () => CanManage);
        EditBranchCommand = new AsyncRelayCommand(EditBranchAsync, () => CanManage && SelectedBranch is not null);
        ToggleBranchStatusCommand = new AsyncRelayCommand(ToggleBranchStatusAsync, () => CanManage && SelectedBranch is not null && !SelectedBranch.IsMainBranch);
        DeleteBranchCommand = new AsyncRelayCommand(DeleteBranchAsync, () => CanManage && SelectedBranch is not null && !SelectedBranch.IsMainBranch);

        AddWarehouseCommand = new AsyncRelayCommand(AddWarehouseAsync, () => CanManage && SelectedBranch is not null);
        EditWarehouseCommand = new AsyncRelayCommand(EditWarehouseAsync, () => CanManage && SelectedWarehouse is not null);
        DeleteWarehouseCommand = new AsyncRelayCommand(DeleteWarehouseAsync, () => CanManage && SelectedWarehouse is not null && !SelectedWarehouse.IsDefault);
    }

    public bool CanManage => _currentUserService.HasPermission("Branches.Manage");

    public ObservableCollection<BranchDto> Branches { get; }
    public ObservableCollection<WarehouseDto> Warehouses { get; }

    public BranchDto? SelectedBranch
    {
        get => _selectedBranch;
        set
        {
            if (SetProperty(ref _selectedBranch, value))
            {
                _ = LoadWarehousesAsync();
                RaiseAllCanExecuteChanged();
            }
        }
    }

    public WarehouseDto? SelectedWarehouse
    {
        get => _selectedWarehouse;
        set
        {
            if (SetProperty(ref _selectedWarehouse, value))
                RaiseAllCanExecuteChanged();
        }
    }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand AddBranchCommand { get; }
    public AsyncRelayCommand EditBranchCommand { get; }
    public AsyncRelayCommand ToggleBranchStatusCommand { get; }
    public AsyncRelayCommand DeleteBranchCommand { get; }
    public AsyncRelayCommand AddWarehouseCommand { get; }
    public AsyncRelayCommand EditWarehouseCommand { get; }
    public AsyncRelayCommand DeleteWarehouseCommand { get; }

    public async Task InitializeAsync() => await LoadBranchesAsync();

    private async Task LoadBranchesAsync()
    {
        IsBusy = true;
        try
        {
            var branches = await _branchService.GetAllAsync();
            Branches.Clear();
            foreach (var b in branches) Branches.Add(b);
            SelectedBranch = Branches.FirstOrDefault();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadWarehousesAsync()
    {
        Warehouses.Clear();
        if (SelectedBranch is null) return;

        var warehouses = await _branchService.GetWarehousesAsync(SelectedBranch.Id);
        foreach (var w in warehouses) Warehouses.Add(w);
    }

    private async Task AddBranchAsync()
    {
        var window = _dialogService.CreateDialog<BranchEditDialog>();
        var vm = (BranchEditViewModel)window.DataContext;
        vm.LoadForCreate();

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadBranchesAsync();
    }

    private async Task EditBranchAsync()
    {
        if (SelectedBranch is null) return;

        var window = _dialogService.CreateDialog<BranchEditDialog>();
        var vm = (BranchEditViewModel)window.DataContext;
        vm.LoadForEdit(SelectedBranch);

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadBranchesAsync();
    }

    private async Task ToggleBranchStatusAsync()
    {
        if (SelectedBranch is null) return;

        var newStatus = !SelectedBranch.IsActive;
        var confirmMessage = newStatus
            ? $"هل تريد تفعيل الفرع '{SelectedBranch.Name}'؟"
            : $"هل تريد تعطيل الفرع '{SelectedBranch.Name}'؟ لن يتمكن المستخدمون من العمل عليه.";

        if (!_dialogService.Confirm(confirmMessage)) return;

        var result = await _branchService.SetActiveStatusAsync(SelectedBranch.Id, newStatus);
        if (!result.Succeeded)
        {
            _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر تنفيذ العملية.");
            return;
        }

        await LoadBranchesAsync();
    }

    private async Task DeleteBranchAsync()
    {
        if (SelectedBranch is null) return;
        if (!_dialogService.Confirm($"هل أنت متأكد من حذف الفرع '{SelectedBranch.Name}'؟ لا يمكن التراجع عن هذا الإجراء.")) return;

        var result = await _branchService.DeleteAsync(SelectedBranch.Id);
        if (!result.Succeeded)
        {
            _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر حذف الفرع.");
            return;
        }

        await LoadBranchesAsync();
    }

    private async Task AddWarehouseAsync()
    {
        if (SelectedBranch is null) return;

        var window = _dialogService.CreateDialog<WarehouseEditDialog>();
        var vm = (WarehouseEditViewModel)window.DataContext;
        vm.LoadForCreate(SelectedBranch.Id);

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadWarehousesAsync();
    }

    private async Task EditWarehouseAsync()
    {
        if (SelectedWarehouse is null) return;

        var window = _dialogService.CreateDialog<WarehouseEditDialog>();
        var vm = (WarehouseEditViewModel)window.DataContext;
        vm.LoadForEdit(SelectedWarehouse);

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadWarehousesAsync();
    }

    private async Task DeleteWarehouseAsync()
    {
        if (SelectedWarehouse is null) return;
        if (!_dialogService.Confirm($"هل أنت متأكد من حذف المخزن '{SelectedWarehouse.Name}'؟")) return;

        var result = await _branchService.DeleteWarehouseAsync(SelectedWarehouse.Id);
        if (!result.Succeeded)
        {
            _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر حذف المخزن.");
            return;
        }

        await LoadWarehousesAsync();
    }

    private void RaiseAllCanExecuteChanged()
    {
        // AsyncRelayCommand listens to CommandManager.RequerySuggested automatically on UI interaction,
        // but we force an immediate refresh right after selection changes for snappy button enable/disable.
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }
}
