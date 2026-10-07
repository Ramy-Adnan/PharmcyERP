using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Hr;
using PharmacyERP.Application.Features.Hr.DTOs;
using PharmacyERP.Domain.Enums;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.Views.Hr;

namespace PharmacyERP.WPF.ViewModels.Hr;

/// <summary>
/// ViewModel for reviewing a single Payroll Run: adjust per-employee
/// deductions while still in Draft, then Approve, then Mark Paid. Each
/// transition is one-way (Draft → Approved → Paid) matching the domain's
/// PayrollRunStatus lifecycle.
/// </summary>
public class PayrollRunDetailViewModel : ViewModelBase
{
    private readonly IHrService _hrService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;

    private PayrollRunDto? _header;
    private PayrollRunLineDto? _selectedLine;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public PayrollRunDetailViewModel(IHrService hrService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _hrService = hrService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        Lines = new ObservableCollection<PayrollRunLineDto>();

        SaveDeductionCommand = new AsyncRelayCommand(SaveDeductionAsync, () => CanEdit && SelectedLine is not null);
        ApproveCommand = new AsyncRelayCommand(ApproveAsync, () => CanManage && Header?.Status == PayrollRunStatus.Draft);
        MarkPaidCommand = new AsyncRelayCommand(MarkPaidAsync, () => CanManage && Header?.Status == PayrollRunStatus.Approved);
    }

    public bool CanManage => _currentUserService.HasPermission("Hr.ManagePayroll");
    public bool CanEdit => CanManage && Header?.Status == PayrollRunStatus.Draft;

    public PayrollRunDto? Header { get => _header; private set => SetProperty(ref _header, value); }
    public ObservableCollection<PayrollRunLineDto> Lines { get; }

    public PayrollRunLineDto? SelectedLine
    {
        get => _selectedLine;
        set
        {
            if (SetProperty(ref _selectedLine, value))
            {
                EditableDeductions = value?.Deductions ?? 0;
                EditableDeductionNotes = value?.DeductionNotes;
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public decimal EditableDeductions { get; set; }
    public string? EditableDeductionNotes { get; set; }

    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand SaveDeductionCommand { get; }
    public AsyncRelayCommand ApproveCommand { get; }
    public AsyncRelayCommand MarkPaidCommand { get; }

    /// <summary>Set by the dialog code-behind when Mark Paid completes, so the parent list screen knows to refresh.</summary>
    public bool RunChanged { get; private set; }

    public async Task LoadAsync(int payrollRunId)
    {
        var detail = await _hrService.GetPayrollRunDetailAsync(payrollRunId);
        if (detail is null) return;

        Header = detail.Header;
        Lines.Clear();
        foreach (var line in detail.Lines) Lines.Add(line);

        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private async Task SaveDeductionAsync()
    {
        if (SelectedLine is null || Header is null) return;

        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var dto = new UpdatePayrollLineDto
            {
                PayrollRunLineId = SelectedLine.Id,
                Deductions = EditableDeductions,
                DeductionNotes = EditableDeductionNotes
            };

            var result = await _hrService.UpdatePayrollLineAsync(dto);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر حفظ الاستقطاع.";
                return;
            }

            RunChanged = true;
            await LoadAsync(Header.Id);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApproveAsync()
    {
        if (Header is null) return;
        if (!_dialogService.Confirm($"هل تريد اعتماد دورة الرواتب '{Header.Number}'؟ لن يمكن تعديل الاستقطاعات بعد الاعتماد.")) return;

        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var result = await _hrService.ApprovePayrollRunAsync(Header.Id);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر اعتماد دورة الرواتب.";
                return;
            }

            RunChanged = true;
            await LoadAsync(Header.Id);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task MarkPaidAsync()
    {
        if (Header is null) return;

        var window = _dialogService.CreateDialog<MarkPayrollRunPaidDialog>();
        var vm = (MarkPayrollRunPaidViewModel)window.DataContext;
        await vm.LoadAsync(Header.Id, Header.Number, Header.TotalNetPay);

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
        {
            RunChanged = true;
            await LoadAsync(Header.Id);
        }
    }
}
