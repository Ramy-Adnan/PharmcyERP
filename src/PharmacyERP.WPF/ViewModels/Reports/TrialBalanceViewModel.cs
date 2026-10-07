using System.Collections.ObjectModel;
using PharmacyERP.Application.Features.Branches;
using PharmacyERP.Application.Features.Branches.DTOs;
using PharmacyERP.Application.Features.Reports;
using PharmacyERP.Application.Features.Reports.DTOs;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;

namespace PharmacyERP.WPF.ViewModels.Reports;

public class TrialBalanceViewModel : ViewModelBase
{
    private readonly IReportingService _reportingService;
    private readonly IBranchService _branchService;
    private readonly IReportExportService _exportService;

    private DateTime _asOfDate = DateTime.Today;
    private BranchDto? _selectedBranch;
    private TrialBalanceDto? _result;
    private bool _isBusy;

    public TrialBalanceViewModel(IReportingService reportingService, IBranchService branchService, IReportExportService exportService)
    {
        _reportingService = reportingService;
        _branchService = branchService;
        _exportService = exportService;

        Branches = new ObservableCollection<BranchDto>();
        Lines = new ObservableCollection<TrialBalanceLineDto>();

        RunReportCommand = new AsyncRelayCommand(RunReportAsync);
        ExportCsvCommand = new RelayCommand(ExportCsv, () => Lines.Count > 0);
        PrintCommand = new RelayCommand(Print, () => Lines.Count > 0);
    }

    public ObservableCollection<BranchDto> Branches { get; }
    public ObservableCollection<TrialBalanceLineDto> Lines { get; }

    public DateTime AsOfDate { get => _asOfDate; set => SetProperty(ref _asOfDate, value); }

    /// <summary>Null selection means "كل الفروع" (all branches combined).</summary>
    public BranchDto? SelectedBranch { get => _selectedBranch; set => SetProperty(ref _selectedBranch, value); }

    public TrialBalanceDto? Result { get => _result; private set => SetProperty(ref _result, value); }

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
            Result = await _reportingService.GetTrialBalanceAsync(AsOfDate, SelectedBranch?.Id);
            Lines.Clear();
            foreach (var line in Result.Lines) Lines.Add(line);

            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ExportCsv()
    {
        var headers = new[] { "رمز الحساب", "اسم الحساب", "النوع", "مدين", "دائن", "الرصيد" };
        var rows = Lines.Select(l => (IReadOnlyList<string>)new[]
        {
            l.AccountCode, l.AccountName, l.AccountType.ToString(),
            l.TotalDebit.ToString("N2"), l.TotalCredit.ToString("N2"), l.Balance.ToString("N2")
        });

        _exportService.ExportToCsv($"ميزان-المراجعة-{AsOfDate:yyyy-MM-dd}.csv", headers, rows);
    }

    private void Print()
    {
        var headers = new[] { "رمز الحساب", "اسم الحساب", "النوع", "مدين", "دائن", "الرصيد" };
        var rows = Lines.Select(l => (IReadOnlyList<string>)new[]
        {
            l.AccountCode, l.AccountName, l.AccountType.ToString(),
            l.TotalDebit.ToString("N2"), l.TotalCredit.ToString("N2"), l.Balance.ToString("N2")
        });

        var footer = $"الإجمالي — مدين: {Result?.TotalDebit:N2}   دائن: {Result?.TotalCredit:N2}";
        _exportService.PrintTable($"ميزان المراجعة كما في {AsOfDate:yyyy-MM-dd}", headers, rows, footer);
    }
}
