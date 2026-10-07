using System.Collections.ObjectModel;
using PharmacyERP.Application.Features.Branches;
using PharmacyERP.Application.Features.Branches.DTOs;
using PharmacyERP.Application.Features.Inventory;
using PharmacyERP.Application.Features.Inventory.DTOs;
using PharmacyERP.Application.Features.Reports;
using PharmacyERP.Application.Features.Reports.DTOs;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;

namespace PharmacyERP.WPF.ViewModels.Reports;

public class InventoryMovementReportViewModel : ViewModelBase
{
    private readonly IReportingService _reportingService;
    private readonly IBranchService _branchService;
    private readonly IInventoryService _inventoryService;
    private readonly IReportExportService _exportService;

    private DateTime _fromDate = DateTime.Today.AddDays(-7);
    private DateTime _toDate = DateTime.Today;
    private BranchDto? _selectedBranch;
    private ItemDto? _selectedItem;
    private bool _isBusy;

    public InventoryMovementReportViewModel(
        IReportingService reportingService, IBranchService branchService,
        IInventoryService inventoryService, IReportExportService exportService)
    {
        _reportingService = reportingService;
        _branchService = branchService;
        _inventoryService = inventoryService;
        _exportService = exportService;

        Branches = new ObservableCollection<BranchDto>();
        Items = new ObservableCollection<ItemDto>();
        Lines = new ObservableCollection<InventoryMovementLineDto>();

        RunReportCommand = new AsyncRelayCommand(RunReportAsync);
        ExportCsvCommand = new RelayCommand(ExportCsv, () => Lines.Count > 0);
        PrintCommand = new RelayCommand(Print, () => Lines.Count > 0);
    }

    public ObservableCollection<BranchDto> Branches { get; }
    public ObservableCollection<ItemDto> Items { get; }
    public ObservableCollection<InventoryMovementLineDto> Lines { get; }

    public DateTime FromDate { get => _fromDate; set => SetProperty(ref _fromDate, value); }
    public DateTime ToDate { get => _toDate; set => SetProperty(ref _toDate, value); }
    public BranchDto? SelectedBranch { get => _selectedBranch; set => SetProperty(ref _selectedBranch, value); }

    /// <summary>Null selection means "كل الأصناف" (every item).</summary>
    public ItemDto? SelectedItem { get => _selectedItem; set => SetProperty(ref _selectedItem, value); }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand RunReportCommand { get; }
    public RelayCommand ExportCsvCommand { get; }
    public RelayCommand PrintCommand { get; }

    public async Task InitializeAsync()
    {
        var branches = await _branchService.GetAllAsync(includeInactive: false);
        Branches.Clear();
        foreach (var b in branches) Branches.Add(b);

        var items = await _inventoryService.GetItemsAsync();
        Items.Clear();
        foreach (var i in items) Items.Add(i);

        await RunReportAsync();
    }

    private async Task RunReportAsync()
    {
        IsBusy = true;
        try
        {
            var lines = await _reportingService.GetInventoryMovementAsync(FromDate, ToDate, SelectedBranch?.Id, SelectedItem?.Id);
            Lines.Clear();
            foreach (var line in lines) Lines.Add(line);

            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private (string[] Headers, IEnumerable<IReadOnlyList<string>> Rows) BuildTable()
    {
        var headers = new[] { "التاريخ والوقت", "رمز الصنف", "اسم الصنف", "المخزن", "نوع الحركة", "التغير", "الرصيد بعد الحركة", "المرجع" };
        var rows = Lines.Select(l => (IReadOnlyList<string>)new[]
        {
            l.TransactionAtUtc.ToString("yyyy-MM-dd HH:mm"), l.ItemCode, l.ItemName, l.WarehouseName,
            l.Type.ToString(), l.QuantityChange.ToString(), l.ResultingQuantityOnHand.ToString(), l.ReferenceType ?? ""
        });
        return (headers, rows);
    }

    private void ExportCsv()
    {
        var (headers, rows) = BuildTable();
        _exportService.ExportToCsv($"حركة-المخزون-{FromDate:yyyy-MM-dd}-{ToDate:yyyy-MM-dd}.csv", headers, rows);
    }

    private void Print()
    {
        var (headers, rows) = BuildTable();
        _exportService.PrintTable($"تقرير حركة المخزون من {FromDate:yyyy-MM-dd} إلى {ToDate:yyyy-MM-dd}", headers, rows);
    }
}
