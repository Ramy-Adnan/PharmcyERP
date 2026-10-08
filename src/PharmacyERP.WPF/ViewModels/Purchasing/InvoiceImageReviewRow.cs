using System.Collections.ObjectModel;
using PharmacyERP.Application.Features.Inventory.DTOs;
using PharmacyERP.Application.Features.Purchasing.Imports;
using PharmacyERP.Domain.Common;
using PharmacyERP.Domain.Enums;
using PharmacyERP.WPF.MVVM;
namespace PharmacyERP.WPF.ViewModels.Purchasing;
public sealed class InvoiceImageReviewRow : ViewModelBase
{
    public string SourceName { get; init; } = "";
    public string MatchHint { get; init; } = "";
    public string? ReadingNotes { get; init; }
    public decimal? PrintedLineTotal { get; init; }
    public IReadOnlyList<ItemDto> Items { get; init; } = Array.Empty<ItemDto>();
    private IReadOnlyList<UnitOfMeasureDto> _units = Array.Empty<UnitOfMeasureDto>();
    public IReadOnlyList<UnitOfMeasureDto> Units { get => _units; set => SetProperty(ref _units, value); }
    public IReadOnlyList<ItemCategoryDto> Categories { get; init; } = Array.Empty<ItemCategoryDto>();
    public IReadOnlyList<ItemForm> Forms { get; } = Enum.GetValues<ItemForm>();
    private ItemForm _form = ItemForm.Other;
    public ItemForm Form { get => _form; set { if (SetProperty(ref _form, value)) Changed(); } }
    public IReadOnlyList<InvoiceItemCandidate> Candidates { get; init; } = Array.Empty<InvoiceItemCandidate>();
    public string CandidateHint => string.Join(" | ", Candidates.Select(c => c.Name + " (" + c.BaseUnitName + ")"));
    private int? _existingItemId;
    public int? ExistingItemId
    {
        get => _existingItemId;
        set
        {
            if (!SetProperty(ref _existingItemId, value)) return;
            Reviewed = false;
            var item = Items.FirstOrDefault(i => i.Id == value);
            if (item is not null)
            {
                ItemName = item.Name;
                BaseUnitOfMeasureId = item.UnitOfMeasureId; CategoryId = item.CategoryId; Strength = item.Strength; Barcode = item.Barcode;
                BaseUnitsPerReceiveUnit = null; // A match never guesses packaging from old stock.
                Form = item.Form; RequiresPrescription = item.RequiresPrescription; IsControlledSubstance = item.IsControlledSubstance;
            }
            OnPropertyChanged(nameof(IsNew));
        }
    }
    public bool IsNew { get => !ExistingItemId.HasValue; set { if (value) ExistingItemId = null; } }
    private PurchasePricingType _purchaseType;
    public PurchasePricingType PurchaseType { get => _purchaseType; set { if (SetProperty(ref _purchaseType, value)) Reprice(); } }
    public decimal? LineTotal => Quantity.HasValue && UnitCost.HasValue ? Quantity.Value * UnitCost.Value : null;
    public decimal? BasePurchasePrice => BaseUnitsPerReceiveUnit > 0 && UnitCost.HasValue ? UnitCost.Value / BaseUnitsPerReceiveUnit.Value : null;
    public decimal? BaseSalePrice => BaseUnitsPerReceiveUnit > 0 && SalePrice.HasValue ? SalePrice.Value / BaseUnitsPerReceiveUnit.Value : null;
    private void Reprice() { SalePrice = UnitCost.HasValue ? SalePricePolicy.FromPurchasePrice(UnitCost.Value, PurchaseType) : null; Reviewed = false; }
    private void Changed() { Reviewed = false; OnPropertyChanged(nameof(LineTotal)); OnPropertyChanged(nameof(BasePurchasePrice)); OnPropertyChanged(nameof(BaseSalePrice)); }
    private string _itemName = "";
    public string ItemName { get => _itemName; set { if (SetProperty(ref _itemName, value)) { Changed(); } } }
    private string? _barcode = null;
    public string? Barcode { get => _barcode; set { if (SetProperty(ref _barcode, value)) { Changed(); } } }
    private string? _strength = null;
    public string? Strength { get => _strength; set { if (SetProperty(ref _strength, value)) { Changed(); } } }
    private int _categoryId = 0;
    public int CategoryId { get => _categoryId; set { if (SetProperty(ref _categoryId, value)) { Changed(); } } }
    private int _baseUnitOfMeasureId = 0;
    public int BaseUnitOfMeasureId { get => _baseUnitOfMeasureId; set { if (SetProperty(ref _baseUnitOfMeasureId, value)) { Changed(); } } }
    private string _receiveUnitName = "علبة";
    public string ReceiveUnitName { get => _receiveUnitName; set { if (SetProperty(ref _receiveUnitName, value)) { Changed(); } } }
    private int? _baseUnitsPerReceiveUnit = null;
    public int? BaseUnitsPerReceiveUnit { get => _baseUnitsPerReceiveUnit; set { if (SetProperty(ref _baseUnitsPerReceiveUnit, value)) { Changed(); } } }
    private int? _quantity = null;
    public int? Quantity { get => _quantity; set { if (SetProperty(ref _quantity, value)) { Changed(); } } }
    private int? _bonusQuantity = null;
    public int? BonusQuantity { get => _bonusQuantity; set { if (SetProperty(ref _bonusQuantity, value)) { Changed(); } } }
    private decimal? _unitCost = null;
    public decimal? UnitCost { get => _unitCost; set { if (SetProperty(ref _unitCost, value)) { Changed(); Reprice(); } } }
    private decimal? _salePrice = null;
    public decimal? SalePrice { get => _salePrice; set { if (SetProperty(ref _salePrice, value)) { Changed(); } } }
    private string _batchNumber = "";
    public string BatchNumber { get => _batchNumber; set { if (SetProperty(ref _batchNumber, value)) { Changed(); } } }
    private DateTime? _expiryDate = null;
    public DateTime? ExpiryDate { get => _expiryDate; set { if (SetProperty(ref _expiryDate, value)) { Changed(); } } }
    private bool _requiresPrescription = false;
    public bool RequiresPrescription { get => _requiresPrescription; set { if (SetProperty(ref _requiresPrescription, value)) { Changed(); } } }
    private bool _isControlledSubstance = false;
    public bool IsControlledSubstance { get => _isControlledSubstance; set { if (SetProperty(ref _isControlledSubstance, value)) { Changed(); } } }
    private bool _reviewed;
    public bool Reviewed { get => _reviewed; set => SetProperty(ref _reviewed, value); }
    public PurchaseImageImportLine ToInput() => new() { ExistingItemId = ExistingItemId, SourceName = SourceName, ItemName = ItemName, Barcode = Barcode, Strength = Strength,
        CategoryId = CategoryId, BaseUnitOfMeasureId = BaseUnitOfMeasureId, ReceiveUnitName = ReceiveUnitName, BaseUnitsPerReceiveUnit = BaseUnitsPerReceiveUnit ?? 0,
        Quantity = Quantity ?? -1, BonusQuantity = BonusQuantity ?? -1, UnitCost = UnitCost ?? -1, SalePrice = SalePrice, BatchNumber = BatchNumber, ExpiryDate = ExpiryDate ?? DateTime.MinValue,
        Form = Form, RequiresPrescription = RequiresPrescription, IsControlledSubstance = IsControlledSubstance, Reviewed = Reviewed };
}
