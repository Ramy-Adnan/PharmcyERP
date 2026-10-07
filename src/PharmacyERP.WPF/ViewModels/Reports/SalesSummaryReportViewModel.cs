using System.Collections.ObjectModel;
using PharmacyERP.Application.Features.Branches;
using PharmacyERP.Application.Features.Branches.DTOs;
using PharmacyERP.Application.Features.Reports;
using PharmacyERP.Application.Features.Reports.DTOs;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;

namespace PharmacyERP.WPF.ViewModels.Reports;

public class SalesSummaryReportViewModel : ViewModelBase
{
    private readonly IReportingService _reportingService;
    private readonly IBranchService _branchService;
    private readonly IReportExportService _exportService;

    private DateTime _fromDate = DateTime.Today.AddDays(-7);
    private DateTime _toDate = DateTime.Today;
    private BranchDto? _selectedBranch;
    private SalesSummaryDto? _result;
    private bool _isBusy;

    public SalesSummaryReportViewModel(IReportingService reportingService, IBranchService branchService, IReportExportService exportService)
    {
        _reportingService = reportingService;
        _branchService = branchService;
        _exportService = exportService;

        Branches = new ObservableCollection<BranchDto>();
        ByPaymentMethod = new ObservableCollection<PaymentMethodAmountDto>();

        RunReportCommand = new AsyncRelayCommand(RunReportAsync);
        ExportCsvCommand = new RelayCommand(ExportCsv, () => Result is not null);
        PrintCommand = new RelayCommand(Print, () => Result is not null);
    }

    public ObservableCollection<BranchDto> Branches { get; }
    public ObservableCollection<PaymentMethodAmountDto> ByPaymentMethod { get; }

    public DateTime FromDate { get => _fromDate; set => SetProperty(ref _fromDate, value); }
    public DateTime ToDate { get => _toDate; set => SetProperty(ref _toDate, value); }
    public BranchDto? SelectedBranch { get => _selectedBranch; set => SetProperty(ref _selectedBranch, value); }

    public SalesSummaryDto? Result { get => _result; private set => SetProperty(ref _result, value); }

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
            Result = await _reportingService.GetSalesSummaryAsync(FromDate, ToDate, SelectedBranch?.Id);

            ByPaymentMethod.Clear();
            foreach (var p in Result.ByPaymentMethod) ByPaymentMethod.Add(p);

            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private (string[] Headers, List<string[]> Rows) BuildTable()
    {
        var headers = new[] { "البند", "القيمة" };
        var rows = new List<string[]>
        {
            new[] { "عدد الفواتير", Result!.TotalInvoices.ToString() },
            new[] { "المبيعات قبل الضريبة", Result.GrossSales.ToString("N2") },
            new[] { "إجمالي الضريبة", Result.TotalTax.ToString("N2") },
            new[] { "إجمالي الخصم", Result.TotalDiscount.ToString("N2") },
            new[] { "صافي المبيعات", Result.NetSales.ToString("N2") },
        };
        foreach (var p in Result.ByPaymentMethod)
            rows.Add(new[] { $"طريقة الدفع: {p.PaymentMethod} ({p.InvoiceCount} فاتورة)", p.Amount.ToString("N2") });

        return (headers, rows);
    }

    private void ExportCsv()
    {
        var (headers, rows) = BuildTable();
        _exportService.ExportToCsv($"ملخص-المبيعات-{FromDate:yyyy-MM-dd}-{ToDate:yyyy-MM-dd}.csv", headers, rows);
    }

    private void Print()
    {
        var (headers, rows) = BuildTable();
        _exportService.PrintTable($"ملخص المبيعات من {FromDate:yyyy-MM-dd} إلى {ToDate:yyyy-MM-dd}", headers, rows);
    }
}
