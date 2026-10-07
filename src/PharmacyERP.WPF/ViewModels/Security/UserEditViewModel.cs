using System.Collections.ObjectModel;
using PharmacyERP.Application.Features.Branches;
using PharmacyERP.Application.Features.Security;
using PharmacyERP.Application.Features.Security.DTOs;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Security;

public class SelectableBranch : ViewModelBase
{
    private bool _isSelected;
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

/// <summary>ViewModel for the Add/Edit User dialog, including role assignment, default branch, and additional-branch access.</summary>
public class UserEditViewModel : ViewModelBase
{
    private readonly IUserService _userService;
    private readonly IRoleService _roleService;
    private readonly IBranchService _branchService;

    private int? _id;
    private string _fullName = string.Empty;
    private string _username = string.Empty;
    private string? _email;
    private string? _phoneNumber;
    private int _roleId;
    private int _defaultBranchId;
    private string _password = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public UserEditViewModel(IUserService userService, IRoleService roleService, IBranchService branchService)
    {
        _userService = userService;
        _roleService = roleService;
        _branchService = branchService;

        Roles = new ObservableCollection<RoleDto>();
        Branches = new ObservableCollection<BranchDtoOption>();
        AdditionalBranches = new ObservableCollection<SelectableBranch>();

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public bool IsEditMode => _id.HasValue;
    public string DialogTitle => IsEditMode ? "تعديل بيانات المستخدم" : "إضافة مستخدم جديد";
    public string PasswordFieldLabel => IsEditMode ? "كلمة مرور جديدة (اتركها فارغة لعدم التغيير)" : "كلمة المرور";

    public ObservableCollection<RoleDto> Roles { get; }
    public ObservableCollection<BranchDtoOption> Branches { get; }
    public ObservableCollection<SelectableBranch> AdditionalBranches { get; }

    public string FullName { get => _fullName; set => SetProperty(ref _fullName, value); }
    public string Username { get => _username; set => SetProperty(ref _username, value); }
    public string? Email { get => _email; set => SetProperty(ref _email, value); }
    public string? PhoneNumber { get => _phoneNumber; set => SetProperty(ref _phoneNumber, value); }
    public int RoleId { get => _roleId; set => SetProperty(ref _roleId, value); }
    public int DefaultBranchId
    {
        get => _defaultBranchId;
        set
        {
            if (SetProperty(ref _defaultBranchId, value))
                RefreshAdditionalBranchSelectability();
        }
    }
    public string Password { get => _password; set => SetProperty(ref _password, value); }

    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand SaveCommand { get; }
    public bool SavedSuccessfully { get; private set; }
    public event Action? RequestClose;

    public async Task LoadForCreateAsync()
    {
        _id = null;
        await LoadLookupsAsync();
    }

    public async Task LoadForEditAsync(int userId)
    {
        await LoadLookupsAsync();

        var dto = await _userService.GetForEditAsync(userId);
        if (dto is null) return;

        _id = dto.Id;
        FullName = dto.FullName;
        Username = dto.Username;
        Email = dto.Email;
        PhoneNumber = dto.PhoneNumber;
        RoleId = dto.RoleId;
        DefaultBranchId = dto.DefaultBranchId;

        foreach (var branch in AdditionalBranches)
            branch.IsSelected = dto.AdditionalBranchIds.Contains(branch.Id);

        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(DialogTitle));
        OnPropertyChanged(nameof(PasswordFieldLabel));
    }

    private async Task LoadLookupsAsync()
    {
        var roles = await _roleService.GetAllAsync();
        Roles.Clear();
        foreach (var r in roles) Roles.Add(r);

        var branches = await _branchService.GetAllAsync(includeInactive: false);
        Branches.Clear();
        AdditionalBranches.Clear();
        foreach (var b in branches)
        {
            Branches.Add(new BranchDtoOption { Id = b.Id, Name = b.Name });
            AdditionalBranches.Add(new SelectableBranch { Id = b.Id, Name = b.Name });
        }

        if (RoleId == 0 && Roles.Count > 0) RoleId = Roles.First().Id;
        if (DefaultBranchId == 0 && Branches.Count > 0) DefaultBranchId = Branches.First().Id;
    }

    private void RefreshAdditionalBranchSelectability()
    {
        // The default branch is implicit access; prevent selecting it again in the additional-access list.
        foreach (var branch in AdditionalBranches.Where(b => b.Id == DefaultBranchId))
            branch.IsSelected = false;
    }

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var dto = new UserUpsertDto
            {
                Id = _id,
                FullName = FullName,
                Username = Username,
                Email = Email,
                PhoneNumber = PhoneNumber,
                RoleId = RoleId,
                DefaultBranchId = DefaultBranchId,
                AdditionalBranchIds = AdditionalBranches.Where(b => b.IsSelected && b.Id != DefaultBranchId).Select(b => b.Id).ToList(),
                Password = string.IsNullOrWhiteSpace(Password) ? null : Password
            };

            var result = _id.HasValue
                ? await _userService.UpdateAsync(dto)
                : await _userService.CreateAsync(dto);

            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر حفظ بيانات المستخدم.";
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

/// <summary>Lightweight branch option used to populate the default-branch ComboBox.</summary>
public class BranchDtoOption
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
}
