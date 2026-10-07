using System.Collections.ObjectModel;
using PharmacyERP.Application.Features.Hr;
using PharmacyERP.Application.Features.Hr.DTOs;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Hr;

/// <summary>ViewModel for manually recording a sales commission for an employee (e.g. a bonus tied to a specific sale, not automatically calculated by the system).</summary>
public class CommissionCreateViewModel : ViewModelBase
{
    private readonly IHrService _hrService;

    private int _employeeId;
    private DateTime _commissionDate = DateTime.Today;
    private decimal _amount;
    private string? _notes;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public CommissionCreateViewModel(IHrService hrService)
    {
        _hrService = hrService;
        Employees = new ObservableCollection<EmployeeDto>();
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public ObservableCollection<EmployeeDto> Employees { get; }

    public int EmployeeId { get => _employeeId; set => SetProperty(ref _employeeId, value); }
    public DateTime CommissionDate { get => _commissionDate; set => SetProperty(ref _commissionDate, value); }
    public decimal Amount { get => _amount; set => SetProperty(ref _amount, value); }
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
        if (Amount <= 0) { ErrorMessage = "المبلغ يجب أن يكون أكبر من صفر."; return; }

        IsBusy = true;
        try
        {
            var dto = new CommissionCreateDto
            {
                EmployeeId = EmployeeId,
                CommissionDate = CommissionDate,
                Amount = Amount,
                Notes = Notes
            };

            var result = await _hrService.CreateCommissionAsync(dto);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر تسجيل العمولة.";
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
