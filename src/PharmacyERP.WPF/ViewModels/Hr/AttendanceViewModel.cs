using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Hr;
using PharmacyERP.Application.Features.Hr.DTOs;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.Views.Hr;

namespace PharmacyERP.WPF.ViewModels.Hr;

/// <summary>
/// Attendance screen: a date-ranged log of every employee's check-in/out,
/// plus quick Check-In/Check-Out buttons for the currently selected employee
/// (used by a manager clocking staff in from a shared terminal) and a manual
/// record dialog for corrections.
/// </summary>
public class AttendanceViewModel : ViewModelBase
{
    private readonly IHrService _hrService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;

    private DateTime _fromDate = DateTime.Today.AddDays(-7);
    private DateTime _toDate = DateTime.Today;
    private EmployeeDto? _selectedEmployeeFilter;
    private AttendanceDto? _selectedAttendance;
    private bool _isBusy;

    public AttendanceViewModel(IHrService hrService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _hrService = hrService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        Employees = new ObservableCollection<EmployeeDto>();
        Records = new ObservableCollection<AttendanceDto>();

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        RecordAttendanceCommand = new AsyncRelayCommand(RecordAttendanceAsync, () => CanManage);
        CheckInCommand = new AsyncRelayCommand(CheckInAsync, () => CanManage && SelectedEmployeeFilter is not null);
        CheckOutCommand = new AsyncRelayCommand(CheckOutAsync, () => CanManage && SelectedEmployeeFilter is not null);
    }

    public bool CanManage => _currentUserService.HasPermission("Hr.ManageAttendance");

    public ObservableCollection<EmployeeDto> Employees { get; }
    public ObservableCollection<AttendanceDto> Records { get; }

    public DateTime FromDate { get => _fromDate; set => SetProperty(ref _fromDate, value); }
    public DateTime ToDate { get => _toDate; set => SetProperty(ref _toDate, value); }

    public EmployeeDto? SelectedEmployeeFilter
    {
        get => _selectedEmployeeFilter;
        set { if (SetProperty(ref _selectedEmployeeFilter, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    public AttendanceDto? SelectedAttendance { get => _selectedAttendance; set => SetProperty(ref _selectedAttendance, value); }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand RecordAttendanceCommand { get; }
    public AsyncRelayCommand CheckInCommand { get; }
    public AsyncRelayCommand CheckOutCommand { get; }

    public async Task InitializeAsync()
    {
        var employees = await _hrService.GetEmployeesAsync(includeInactive: false);
        Employees.Clear();
        foreach (var e in employees) Employees.Add(e);

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var records = await _hrService.GetAttendanceAsync(FromDate, ToDate, SelectedEmployeeFilter?.Id);
            Records.Clear();
            foreach (var r in records) Records.Add(r);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RecordAttendanceAsync()
    {
        var window = _dialogService.CreateDialog<AttendanceEditDialog>();
        var vm = (AttendanceEditViewModel)window.DataContext;
        await vm.LoadAsync();

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadAsync();
    }

    private async Task CheckInAsync()
    {
        if (SelectedEmployeeFilter is null) return;

        var result = await _hrService.CheckInAsync(SelectedEmployeeFilter.Id);
        if (!result.Succeeded)
        {
            _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر تسجيل الدخول.");
            return;
        }

        await LoadAsync();
    }

    private async Task CheckOutAsync()
    {
        if (SelectedEmployeeFilter is null) return;

        var result = await _hrService.CheckOutAsync(SelectedEmployeeFilter.Id);
        if (!result.Succeeded)
        {
            _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر تسجيل الخروج.");
            return;
        }

        await LoadAsync();
    }
}
