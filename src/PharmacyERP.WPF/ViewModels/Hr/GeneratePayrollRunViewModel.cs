using System.Collections.ObjectModel;
using PharmacyERP.Application.Features.Branches;
using PharmacyERP.Application.Features.Branches.DTOs;
using PharmacyERP.Application.Features.Hr;
using PharmacyERP.Application.Features.Hr.DTOs;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Hr;

/// <summary>
/// ViewModel for generating a new Payroll Run for a branch over a period.
/// Generation computes each active employee's base salary plus any unpaid
/// commissions earned within the period — it does not post anything to
/// Accounting; only MarkPayrollRunPaidAsync does, once the run is approved
/// and actually paid out.
/// </summary>
public class GeneratePayrollRunViewModel : ViewModelBase
{
    private readonly IHrService _hrService;
    private readonly IBranchService _branchService;

    private int _branchId;
    private DateTime _periodStart = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _periodEnd = new(DateTime.Today.Year, DateTime.Today.Month, DateTime.DaysInMonth(DateTime.Today.Year, DateTime.Today.Month));
    private string? _notes;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public GeneratePayrollRunViewModel(IHrService hrService, IBranchService branchService)
    {
        _hrService = hrService;
        _branchService = branchService;

        Branches = new ObservableCollection<BranchDto>();
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public ObservableCollection<BranchDto> Branches { get; }

    public int BranchId { get => _branchId; set => SetProperty(ref _branchId, value); }
    public DateTime PeriodStart { get => _periodStart; set => SetProperty(ref _periodStart, value); }
    public DateTime PeriodEnd { get => _periodEnd; set => SetProperty(ref _periodEnd, value); }
    public string? Notes { get => _notes; set => SetProperty(ref _notes, value); }

    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand SaveCommand { get; }
    public bool SavedSuccessfully { get; private set; }
    public event Action? RequestClose;

    public async Task LoadAsync()
    {
        var branches = await _branchService.GetAllAsync(includeInactive: false);
        Branches.Clear();
        foreach (var b in branches) Branches.Add(b);

        if (BranchId == 0 && Branches.Count > 0) BranchId = Branches.First().Id;
    }

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;

        if (BranchId <= 0) { ErrorMessage = "الرجاء اختيار الفرع."; return; }
        if (PeriodEnd.Date < PeriodStart.Date) { ErrorMessage = "تاريخ نهاية الفترة لا يمكن أن يسبق تاريخ البداية."; return; }

        IsBusy = true;
        try
        {
            var dto = new GeneratePayrollRunDto
            {
                BranchId = BranchId,
                PeriodStart = PeriodStart,
                PeriodEnd = PeriodEnd,
                Notes = Notes
            };

            var result = await _hrService.GeneratePayrollRunAsync(dto);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر إنشاء دورة الرواتب.";
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
