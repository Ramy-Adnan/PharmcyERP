using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Security;
using PharmacyERP.Application.Features.Security.DTOs;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.Views.Security;

namespace PharmacyERP.WPF.ViewModels.Security;

public class RolesViewModel : ViewModelBase
{
    private readonly IRoleService _roleService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;

    private RoleDto? _selectedRole;
    private bool _isBusy;

    public RolesViewModel(IRoleService roleService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _roleService = roleService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        Roles = new ObservableCollection<RoleDto>();

        RefreshCommand = new AsyncRelayCommand(LoadRolesAsync);
        AddRoleCommand = new AsyncRelayCommand(AddRoleAsync, () => CanManage);
        EditRoleCommand = new AsyncRelayCommand(EditRoleAsync, () => CanManage && SelectedRole is not null && !SelectedRole.IsSystemRole);
        DeleteRoleCommand = new AsyncRelayCommand(DeleteRoleAsync, () => CanManage && SelectedRole is not null && !SelectedRole.IsSystemRole);
    }

    public bool CanManage => _currentUserService.HasPermission("Security.ManageRoles");

    public ObservableCollection<RoleDto> Roles { get; }

    public RoleDto? SelectedRole
    {
        get => _selectedRole;
        set
        {
            if (SetProperty(ref _selectedRole, value))
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand AddRoleCommand { get; }
    public AsyncRelayCommand EditRoleCommand { get; }
    public AsyncRelayCommand DeleteRoleCommand { get; }

    public async Task InitializeAsync() => await LoadRolesAsync();

    private async Task LoadRolesAsync()
    {
        IsBusy = true;
        try
        {
            var roles = await _roleService.GetAllAsync();
            Roles.Clear();
            foreach (var r in roles) Roles.Add(r);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddRoleAsync()
    {
        var window = _dialogService.CreateDialog<RoleEditDialog>();
        var vm = (RoleEditViewModel)window.DataContext;
        await vm.LoadForCreateAsync();

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadRolesAsync();
    }

    private async Task EditRoleAsync()
    {
        if (SelectedRole is null) return;

        var window = _dialogService.CreateDialog<RoleEditDialog>();
        var vm = (RoleEditViewModel)window.DataContext;
        await vm.LoadForEditAsync(SelectedRole.Id);

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadRolesAsync();
    }

    private async Task DeleteRoleAsync()
    {
        if (SelectedRole is null) return;
        if (!_dialogService.Confirm($"هل أنت متأكد من حذف الدور '{SelectedRole.Name}'؟")) return;

        var result = await _roleService.DeleteAsync(SelectedRole.Id);
        if (!result.Succeeded)
        {
            _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر حذف الدور.");
            return;
        }

        await LoadRolesAsync();
    }
}
