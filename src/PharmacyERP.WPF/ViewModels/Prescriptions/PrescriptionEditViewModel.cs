using System.Collections.ObjectModel;
using PharmacyERP.Application.Features.Branches;
using PharmacyERP.Application.Features.Branches.DTOs;
using PharmacyERP.Application.Features.Inventory;
using PharmacyERP.Application.Features.Inventory.DTOs;
using PharmacyERP.Application.Features.Prescriptions;
using PharmacyERP.Application.Features.Prescriptions.DTOs;
using PharmacyERP.Application.Features.Sales;
using PharmacyERP.Application.Features.Sales.DTOs;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Prescriptions;

/// <summary>
/// ViewModel for creating a new Prescription. Lines are edited directly in a
/// DataGrid (item combo + quantity + dosage), matching the Purchase Order
/// editor pattern. Prescriptions are immutable once created except for
/// status transitions (cancel) and dispensing (which happens automatically
/// from POS checkout) — there is no "edit lines" mode.
/// </summary>
public class PrescriptionEditViewModel : ViewModelBase
{
    private readonly IPrescriptionService _prescriptionService;
    private readonly ISalesService _salesService;
    private readonly IInventoryService _inventoryService;
    private readonly IBranchService _branchService;

    private int _customerId;
    private int _doctorId;
    private int _branchId;
    private DateTime _prescriptionDate = DateTime.Today;
    private DateTime? _expiryDate;
    private string? _notes;
    private string _errorMessage = string.Empty;
    private bool _isBusy;
    private PrescriptionLineRow? _selectedLine;

    public PrescriptionEditViewModel(
        IPrescriptionService prescriptionService, ISalesService salesService,
        IInventoryService inventoryService, IBranchService branchService)
    {
        _prescriptionService = prescriptionService;
        _salesService = salesService;
        _inventoryService = inventoryService;
        _branchService = branchService;

        Customers = new ObservableCollection<CustomerDto>();
        Doctors = new ObservableCollection<DoctorDto>();
        Branches = new ObservableCollection<BranchDto>();
        AvailableItems = new ObservableCollection<ItemDto>();
        Lines = new ObservableCollection<PrescriptionLineRow>();

        AddLineCommand = new RelayCommand(() => Lines.Add(new PrescriptionLineRow()));
        RemoveLineCommand = new RelayCommand(() => { if (SelectedLine is not null) Lines.Remove(SelectedLine); }, () => SelectedLine is not null);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public string DialogTitle => "وصفة طبية جديدة";

    public ObservableCollection<CustomerDto> Customers { get; }
    public ObservableCollection<DoctorDto> Doctors { get; }
    public ObservableCollection<BranchDto> Branches { get; }
    public ObservableCollection<ItemDto> AvailableItems { get; }
    public ObservableCollection<PrescriptionLineRow> Lines { get; }

    public int CustomerId { get => _customerId; set => SetProperty(ref _customerId, value); }
    public int DoctorId { get => _doctorId; set => SetProperty(ref _doctorId, value); }
    public int BranchId { get => _branchId; set => SetProperty(ref _branchId, value); }
    public DateTime PrescriptionDate { get => _prescriptionDate; set => SetProperty(ref _prescriptionDate, value); }
    public DateTime? ExpiryDate { get => _expiryDate; set => SetProperty(ref _expiryDate, value); }
    public string? Notes { get => _notes; set => SetProperty(ref _notes, value); }

    public PrescriptionLineRow? SelectedLine
    {
        get => _selectedLine;
        set { if (SetProperty(ref _selectedLine, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public RelayCommand AddLineCommand { get; }
    public RelayCommand RemoveLineCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public bool SavedSuccessfully { get; private set; }
    public event Action? RequestClose;

    public async Task LoadForCreateAsync()
    {
        var customers = await _salesService.GetCustomersAsync();
        Customers.Clear();
        foreach (var c in customers.Where(c => c.IsActive)) Customers.Add(c);

        var doctors = await _prescriptionService.GetDoctorsAsync(includeInactive: false);
        Doctors.Clear();
        foreach (var d in doctors) Doctors.Add(d);

        var branches = await _branchService.GetAllAsync(includeInactive: false);
        Branches.Clear();
        foreach (var b in branches) Branches.Add(b);

        var items = await _inventoryService.GetItemsAsync();
        AvailableItems.Clear();
        foreach (var i in items.Where(i => i.IsActive && i.RequiresPrescription)) AvailableItems.Add(i);

        if (BranchId == 0 && Branches.Count > 0) BranchId = Branches.First().Id;
    }

    /// <summary>Called from the View's code-behind when the item ComboBox selection changes, to fill in the item name.</summary>
    public void ApplyItemSelection(PrescriptionLineRow line, int itemId)
    {
        var item = AvailableItems.FirstOrDefault(i => i.Id == itemId);
        if (item is null) return;

        line.ItemId = item.Id;
        line.ItemName = item.Name;
    }

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;

        if (CustomerId <= 0) { ErrorMessage = "الرجاء اختيار العميل."; return; }
        if (DoctorId <= 0) { ErrorMessage = "الرجاء اختيار الطبيب."; return; }
        if (BranchId <= 0) { ErrorMessage = "الرجاء اختيار الفرع."; return; }
        if (!Lines.Any()) { ErrorMessage = "يجب إضافة صنف واحد على الأقل."; return; }
        if (Lines.Any(l => l.ItemId <= 0)) { ErrorMessage = "الرجاء اختيار الصنف لكل سطر."; return; }
        if (Lines.Any(l => l.QuantityPrescribed <= 0)) { ErrorMessage = "الكمية يجب أن تكون أكبر من صفر لكل سطر."; return; }

        IsBusy = true;
        try
        {
            var dto = new PrescriptionUpsertDto
            {
                CustomerId = CustomerId,
                DoctorId = DoctorId,
                BranchId = BranchId,
                PrescriptionDate = PrescriptionDate,
                ExpiryDate = ExpiryDate,
                Notes = Notes,
                Lines = Lines.Select(l => new PrescriptionLineInputDto
                {
                    ItemId = l.ItemId,
                    QuantityPrescribed = l.QuantityPrescribed,
                    DosageInstructions = l.DosageInstructions
                }).ToList()
            };

            var result = await _prescriptionService.CreatePrescriptionAsync(dto);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر حفظ الوصفة الطبية.";
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
