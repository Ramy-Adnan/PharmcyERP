using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Security;
using PharmacyERP.Application.Features.Security.DTOs;
using PharmacyERP.Domain.Enums;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.Views.Security;

namespace PharmacyERP.WPF.ViewModels.Security;

public class UsersViewModel : ViewModelBase
{
    private readonly IUserService _userService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;

    private UserDto? _selectedUser;
    private bool _isBusy;

    public UsersViewModel(IUserService userService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _userService = userService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        Users = new ObservableCollection<UserDto>();

        RefreshCommand = new AsyncRelayCommand(LoadUsersAsync);
        AddUserCommand = new AsyncRelayCommand(AddUserAsync, () => CanManage);
        EditUserCommand = new AsyncRelayCommand(EditUserAsync, () => CanManage && SelectedUser is not null);
        ToggleStatusCommand = new AsyncRelayCommand(ToggleStatusAsync, () => CanManage && SelectedUser is not null);
        ResetPasswordCommand = new AsyncRelayCommand(ResetPasswordAsync, () => CanManage && SelectedUser is not null);
        UnlockCommand = new AsyncRelayCommand(UnlockAsync, () => CanManage && SelectedUser is not null && SelectedUser.Status == UserStatus.Locked);
    }

    public bool CanManage => _currentUserService.HasPermission("Security.ManageUsers");

    public ObservableCollection<UserDto> Users { get; }

    public UserDto? SelectedUser
    {
        get => _selectedUser;
        set
        {
            if (SetProperty(ref _selectedUser, value))
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand AddUserCommand { get; }
    public AsyncRelayCommand EditUserCommand { get; }
    public AsyncRelayCommand ToggleStatusCommand { get; }
    public AsyncRelayCommand ResetPasswordCommand { get; }
    public AsyncRelayCommand UnlockCommand { get; }

    public async Task InitializeAsync() => await LoadUsersAsync();

    private async Task LoadUsersAsync()
    {
        IsBusy = true;
        try
        {
            var users = await _userService.GetAllAsync();
            Users.Clear();
            foreach (var u in users) Users.Add(u);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddUserAsync()
    {
        var window = _dialogService.CreateDialog<UserEditDialog>();
        var vm = (UserEditViewModel)window.DataContext;
        await vm.LoadForCreateAsync();

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadUsersAsync();
    }

    private async Task EditUserAsync()
    {
        if (SelectedUser is null) return;

        var window = _dialogService.CreateDialog<UserEditDialog>();
        var vm = (UserEditViewModel)window.DataContext;
        await vm.LoadForEditAsync(SelectedUser.Id);

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadUsersAsync();
    }

    private async Task ToggleStatusAsync()
    {
        if (SelectedUser is null) return;

        var activate = SelectedUser.Status != UserStatus.Active;
        var message = activate
            ? $"هل تريد تفعيل المستخدم '{SelectedUser.FullName}'؟"
            : $"هل تريد تعطيل المستخدم '{SelectedUser.FullName}'؟ لن يتمكن من تسجيل الدخول.";

        if (!_dialogService.Confirm(message)) return;

        var result = await _userService.SetStatusAsync(SelectedUser.Id, activate);
        if (!result.Succeeded)
        {
            _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر تنفيذ العملية.");
            return;
        }

        await LoadUsersAsync();
    }

    private async Task ResetPasswordAsync()
    {
        if (SelectedUser is null) return;

        var window = _dialogService.CreateDialog<ResetPasswordDialog>();
        var vm = (ResetPasswordViewModel)window.DataContext;
        vm.Load(SelectedUser.Id, SelectedUser.FullName);

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            _dialogService.ShowInfo("تم تغيير كلمة المرور بنجاح.");
    }

    private async Task UnlockAsync()
    {
        if (SelectedUser is null) return;

        var result = await _userService.UnlockAsync(SelectedUser.Id);
        if (!result.Succeeded)
        {
            _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر إلغاء القفل.");
            return;
        }

        await LoadUsersAsync();
    }
}
