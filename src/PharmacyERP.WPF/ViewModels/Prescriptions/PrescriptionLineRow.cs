using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Prescriptions;

/// <summary>One editable line in the Prescription editor's line grid.</summary>
public class PrescriptionLineRow : ViewModelBase
{
    private int _itemId;
    private string _itemName = string.Empty;
    private int _quantityPrescribed;
    private string? _dosageInstructions;

    public int Id { get; set; }
    public int ItemId { get => _itemId; set => SetProperty(ref _itemId, value); }
    public string ItemName { get => _itemName; set => SetProperty(ref _itemName, value); }
    public int QuantityPrescribed { get => _quantityPrescribed; set => SetProperty(ref _quantityPrescribed, value); }
    public string? DosageInstructions { get => _dosageInstructions; set => SetProperty(ref _dosageInstructions, value); }
}
