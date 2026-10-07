using System.Collections.ObjectModel;
using System.Linq;
using PharmacyERP.Application.Features.Security;
using PharmacyERP.Application.Features.Security.DTOs;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Security;

public class SelectablePermission : ViewModelBase
{
    private bool _isSelected;
    public int Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

public class PermissionModuleGroup
{
    public string Module { get; init; } = string.Empty;
    public List<SelectablePermission> Permissions { get; init; } = new();
}

/// <summary>ViewModel for the Add/Edit Role dialog, presenting permissions grouped by module with checkboxes.</summary>
public class RoleEditViewModel : ViewModelBase
{
    private readonly IRoleService _roleService;

    private int? _id;
    private string _name = string.Empty;
    private string? _description;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public RoleEditViewModel(IRoleService roleService)
    {
        _roleService = roleService;
        PermissionGroups = new ObservableCollection<PermissionModuleGroup>();
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public bool IsEditMode => _id.HasValue;
    public string DialogTitle => IsEditMode ? "تعديل الدور" : "إضافة دور جديد";

    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string? Description { get => _description; set => SetProperty(ref _description, value); }

    public ObservableCollection<PermissionModuleGroup> PermissionGroups { get; }

    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand SaveCommand { get; }
    public bool SavedSuccessfully { get; private set; }
    public event Action? RequestClose;

    public async Task LoadForCreateAsync()
    {
        _id = null;
        await LoadPermissionsAsync(selectedIds: Enumerable.Empty<int>());
    }

    public async Task LoadForEditAsync(int roleId)
    {
        var dto = await _roleService.GetForEditAsync(roleId);
        if (dto is null) return;

        _id = dto.Id;
        Name = dto.Name;
        Description = dto.Description;

        await LoadPermissionsAsync(dto.PermissionIds);

        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(DialogTitle));
    }

    private async Task LoadPermissionsAsync(IEnumerable<int> selectedIds)
    {
        var selectedSet = selectedIds.ToHashSet();
        var permissions = await _roleService.GetAllPermissionsAsync();

        PermissionGroups.Clear();
        foreach (var group in permissions.GroupBy(p => p.Module))
        {
            PermissionGroups.Add(new PermissionModuleGroup
            {
                Module = group.Key,
                Permissions = group.Select(p => new SelectablePermission
                {
                    Id = p.Id,
                    Code = p.Code,
                    DisplayName = p.DisplayName,
                    IsSelected = selectedSet.Contains(p.Id)
                }).ToList()
            });
        }
    }

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var dto = new RoleUpsertDto
            {
                Id = _id,
                Name = Name,
                Description = Description,
                PermissionIds = PermissionGroups.SelectMany(g => g.Permissions).Where(p => p.IsSelected).Select(p => p.Id).ToList()
            };

            var result = _id.HasValue
                ? await _roleService.UpdateAsync(dto)
                : await _roleService.CreateAsync(dto);

            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر حفظ الدور.";
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
