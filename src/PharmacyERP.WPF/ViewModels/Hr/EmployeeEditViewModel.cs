using System.Collections.ObjectModel;
using PharmacyERP.Application.Features.Branches;
using PharmacyERP.Application.Features.Branches.DTOs;
using PharmacyERP.Application.Features.Hr;
using PharmacyERP.Application.Features.Hr.DTOs;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Hr;

public class EmployeeEditViewModel : ViewModelBase
{
    private readonly IHrService _hrService;
    private readonly IBranchService _branchService;

    private int? _id;
    private string _employeeCode = string.Empty;
    private string _fullName = string.Empty;
    private string? _nationalId;
    private string? _phone;
    private string? _address;
    private string _jobTitle = string.Empty;
    private DateTime _hireDate = DateTime.Today;
    private int _branchId;
    private int? _shiftId;
    private int? _userId;
    private decimal _monthlyBaseSalary;
    private bool _isActive = true;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public EmployeeEditViewModel(IHrService hrService, IBranchService branchService)
    {
        _hrService = hrService;
        _branchService = branchService;

        Branches = new ObservableCollection<BranchDto>();
        Shifts = new ObservableCollection<ShiftDto>();
        LinkableUsers = new ObservableCollection<LinkableUserDto>();

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public bool IsEditMode => _id.HasValue;
    public string DialogTitle => IsEditMode ? "تعديل بيانات الموظف" : "إضافة موظف جديد";

    public ObservableCollection<BranchDto> Branches { get; }
    public ObservableCollection<ShiftDto> Shifts { get; }
    public ObservableCollection<LinkableUserDto> LinkableUsers { get; }

    public string EmployeeCode { get => _employeeCode; set => SetProperty(ref _employeeCode, value); }
    public string FullName { get => _fullName; set => SetProperty(ref _fullName, value); }
    public string? NationalId { get => _nationalId; set => SetProperty(ref _nationalId, value); }
    public string? Phone { get => _phone; set => SetProperty(ref _phone, value); }
    public string? Address { get => _address; set => SetProperty(ref _address, value); }
    public string JobTitle { get => _jobTitle; set => SetProperty(ref _jobTitle, value); }
    public DateTime HireDate { get => _hireDate; set => SetProperty(ref _hireDate, value); }
    public int BranchId { get => _branchId; set => SetProperty(ref _branchId, value); }
    public int? ShiftId { get => _shiftId; set => SetProperty(ref _shiftId, value); }
    public int? UserId { get => _userId; set => SetProperty(ref _userId, value); }
    public decimal MonthlyBaseSalary { get => _monthlyBaseSalary; set => SetProperty(ref _monthlyBaseSalary, value); }
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand SaveCommand { get; }
    public bool SavedSuccessfully { get; private set; }
    public event Action? RequestClose;

    public async Task LoadForCreateAsync()
    {
        _id = null;
        await LoadLookupsAsync(currentUserId: null);
    }

    public async Task LoadForEditAsync(int id)
    {
        var dto = await _hrService.GetEmployeeForEditAsync(id);
        if (dto is null) return;

        await LoadLookupsAsync(dto.UserId);

        _id = dto.Id;
        EmployeeCode = dto.EmployeeCode;
        FullName = dto.FullName;
        NationalId = dto.NationalId;
        Phone = dto.Phone;
        Address = dto.Address;
        JobTitle = dto.JobTitle;
        HireDate = dto.HireDate;
        BranchId = dto.BranchId;
        ShiftId = dto.ShiftId;
        UserId = dto.UserId;
        MonthlyBaseSalary = dto.MonthlyBaseSalary;
        IsActive = dto.IsActive;

        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(DialogTitle));
    }

    private async Task LoadLookupsAsync(int? currentUserId)
    {
        var branches = await _branchService.GetAllAsync(includeInactive: false);
        Branches.Clear();
        foreach (var b in branches) Branches.Add(b);

        var shifts = await _hrService.GetShiftsAsync();
        Shifts.Clear();
        foreach (var s in shifts.Where(s => s.IsActive)) Shifts.Add(s);

        // The employee's already-linked user (if editing) must remain selectable even
        // though GetLinkableUsersAsync excludes users already linked to *some* employee.
        var linkableUsers = await _hrService.GetLinkableUsersAsync();
        LinkableUsers.Clear();
        foreach (var u in linkableUsers) LinkableUsers.Add(u);

        if (currentUserId.HasValue && LinkableUsers.All(u => u.Id != currentUserId.Value))
        {
            LinkableUsers.Add(new LinkableUserDto { Id = currentUserId.Value, FullName = "(المستخدم الحالي المرتبط)", Username = "" });
        }

        if (BranchId == 0 && Branches.Count > 0) BranchId = Branches.First().Id;
    }

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var dto = new EmployeeUpsertDto
            {
                Id = _id,
                EmployeeCode = EmployeeCode,
                FullName = FullName,
                NationalId = NationalId,
                Phone = Phone,
                Address = Address,
                JobTitle = JobTitle,
                HireDate = HireDate,
                BranchId = BranchId,
                ShiftId = ShiftId,
                UserId = UserId,
                MonthlyBaseSalary = MonthlyBaseSalary,
                IsActive = IsActive
            };

            var result = _id.HasValue
                ? await _hrService.UpdateEmployeeAsync(dto)
                : await _hrService.CreateEmployeeAsync(dto);

            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر حفظ بيانات الموظف.";
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
