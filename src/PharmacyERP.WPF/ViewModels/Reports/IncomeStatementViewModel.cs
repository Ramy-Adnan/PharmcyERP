using System.Collections.ObjectModel;
using PharmacyERP.Application.Features.Branches;
using PharmacyERP.Application.Features.Branches.DTOs;
using PharmacyERP.Application.Features.Reports;
using PharmacyERP.Application.Features.Reports.DTOs;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;

namespace PharmacyERP.WPF.ViewModels.Reports;

public class IncomeStatementViewModel : ViewModelBase
{
    private readonly IReportingService _reportingService;
    private readonly IBranchService _branchService;
    private readonly IReportExportService _exportService;

    private DateTime _fromDate = DateTime.Today.AddDays(-30);
    private DateTime _toDate = DateTime.Today;
    private BranchDto? _selectedBranch;
    private IncomeStatementDto? _result;
    private bool _isBusy;

    public IncomeStatementViewModel(IReportingService reportingService, IBranchService branchService, IReportExportService exportService)
    {
        _reportingService = reportingService;
        _branchService = branchService;
        _exportService = exportService;

        Branches = new ObservableCollection<BranchDto>();

        RunReportCommand = new AsyncRelayCommand(RunReportAsync);
        ExportCsvCommand = new RelayCommand(ExportCsv, () => Result is not null);
        PrintCommand = new RelayCommand(Print, () => Result is not null);
    }

    public ObservableCollection<BranchDto> Branches { get; }

    public DateTime FromDate { get => _fromDate; set => SetProperty(ref _fromDate, value); }
    public DateTime ToDate { get => _toDate; set => SetProperty(ref _toDate, value); }
    public BranchDto? SelectedBranch { get => _selectedBranch; set => SetProperty(ref _selectedBranch, value); }

    public IncomeStatementDto? Result { get => _result; private set => SetProperty(ref _result, value); }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand RunReportCommand { get; }
    public RelayCommand ExportCsvCommand { get; }
    public RelayCommand PrintCommand { get; }

    public async Task InitializeAsync()
    {
        var branches = await _branchService.GetAllAsync(includeInactive: false);
        Branches.Clear();
        foreach (var b in branches) Branches.Add(b);

        await RunReportAsync();
    }

    private async Task RunReportAsync()
    {
        IsBusy = true;
        try
        {
            Result = await _reportingService.GetIncomeStatementAsync(FromDate, ToDate, SelectedBranch?.Id);
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private (string[] Headers, List<string[]> Rows) BuildTable()
    {
        var headers = new[] { "البند", "المبلغ" };
        var rows = new List<string[]>
        {
            new[] { "إجمالي الإيرادات", Result!.TotalRevenue.ToString("N2") },
            new[] { "تكلفة البضاعة المباعة", Result.TotalCogs.ToString("N2") },
            new[] { "إجمالي الربح", Result.GrossProfit.ToString("N2") },
        };
        foreach (var expense in Result.ExpensesByCategory)
            rows.Add(new[] { $"مصروف: {expense.CategoryName}", expense.Amount.ToString("N2") });

        rows.Add(new[] { "إجمالي المصاريف", Result.TotalExpenses.ToString("N2") });
        rows.Add(new[] { "صافي الربح", Result.NetProfit.ToString("N2") });

        return (headers, rows);
    }

    private void ExportCsv()
    {
        var (headers, rows) = BuildTable();
        _exportService.ExportToCsv($"قائمة-الدخل-{FromDate:yyyy-MM-dd}-{ToDate:yyyy-MM-dd}.csv", headers, rows);
    }

    private void Print()
    {
        var (headers, rows) = BuildTable();
        _exportService.PrintTable($"قائمة الدخل من {FromDate:yyyy-MM-dd} إلى {ToDate:yyyy-MM-dd}", headers, rows);
    }
}
