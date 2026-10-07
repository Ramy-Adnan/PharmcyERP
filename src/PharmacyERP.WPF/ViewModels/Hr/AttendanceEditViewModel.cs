using System.Collections.ObjectModel;
using PharmacyERP.Application.Features.Hr;
using PharmacyERP.Application.Features.Hr.DTOs;
using PharmacyERP.Domain.Enums;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Hr;

/// <summary>ViewModel for manually recording/correcting an attendance entry (e.g. an employee forgot to check in, or a manager records an excused absence).</summary>
public class AttendanceEditViewModel : ViewModelBase
{
    private readonly IHrService _hrService;

    private int _employeeId;
    private DateTime _attendanceDate = DateTime.Today;
    private AttendanceStatus _status = AttendanceStatus.Present;
    private string? _notes;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public AttendanceEditViewModel(IHrService hrService)
    {
        _hrService = hrService;

        Employees = new ObservableCollection<EmployeeDto>();
        Statuses = new ObservableCollection<AttendanceStatus>(Enum.GetValues<AttendanceStatus>());

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public ObservableCollection<EmployeeDto> Employees { get; }
    public ObservableCollection<AttendanceStatus> Statuses { get; }

    public int EmployeeId { get => _employeeId; set => SetProperty(ref _employeeId, value); }
    public DateTime AttendanceDate { get => _attendanceDate; set => SetProperty(ref _attendanceDate, value); }
    public AttendanceStatus Status { get => _status; set => SetProperty(ref _status, value); }
    public string? Notes { get => _notes; set => SetProperty(ref _notes, value); }

    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand SaveCommand { get; }
    public bool SavedSuccessfully { get; private set; }
    public event Action? RequestClose;

    public async Task LoadAsync()
    {
        var employees = await _hrService.GetEmployeesAsync(includeInactive: false);
        Employees.Clear();
        foreach (var e in employees) Employees.Add(e);

        if (EmployeeId == 0 && Employees.Count > 0) EmployeeId = Employees.First().Id;
    }

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;

        if (EmployeeId <= 0) { ErrorMessage = "الرجاء اختيار الموظف."; return; }

        IsBusy = true;
        try
        {
            var dto = new AttendanceUpsertDto
            {
                EmployeeId = EmployeeId,
                AttendanceDate = AttendanceDate,
                Status = Status,
                Notes = Notes
            };

            var result = await _hrService.RecordAttendanceAsync(dto);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر تسجيل الحضور.";
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
