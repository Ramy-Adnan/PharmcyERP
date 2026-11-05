using PharmacyERP.Application.Features.Inventory.DTOs;
namespace PharmacyERP.WPF.ViewModels.Purchasing;
public abstract class PurchaseUnitRowBase : PurchasePricingViewModel
{
    private int? _itemSaleUnitId;
    private IReadOnlyList<PurchaseUnitOption> _purchaseUnits = Array.Empty<PurchaseUnitOption>();
    public IReadOnlyList<PurchaseUnitOption> PurchaseUnits
    {
        get => _purchaseUnits;
        set
        {
            if (!SetProperty(ref _purchaseUnits, value)) return;
            OnPropertyChanged(nameof(SelectedPurchaseUnit));
            OnPropertyChanged(nameof(PurchaseUnitFactor));
        }
    }
    public int PurchaseUnitFactor => SelectedPurchaseUnit?.BaseUnitCount ?? 1;
    public PurchaseUnitOption? SelectedPurchaseUnit { get => PurchaseUnits.FirstOrDefault(u => u.Id == ItemSaleUnitId); set { if (value is not null) ItemSaleUnitId = value.Id; } }
    public int? ItemSaleUnitId
    {
        get => _itemSaleUnitId;
        set
        {
            var previous = PurchaseUnits.FirstOrDefault(u => u.Id == _itemSaleUnitId)?.BaseUnitCount ?? 1;
            if (!SetProperty(ref _itemSaleUnitId, value)) return;
            var next = PurchaseUnits.FirstOrDefault(u => u.Id == value)?.BaseUnitCount ?? 1;
            ConvertUnitPrices(previous, next);
            OnPropertyChanged(nameof(PurchaseUnitDescription));
            OnPropertyChanged(nameof(SelectedPurchaseUnit));
            OnPropertyChanged(nameof(PurchaseUnitFactor));
        }
    }
    protected virtual void ConvertUnitPrices(int previousFactor, int nextFactor) =>
        UnitCost = Math.Round(UnitCost / previousFactor * nextFactor, 2, MidpointRounding.AwayFromZero);
    public abstract decimal UnitCost { get; set; }
    public string PurchaseUnitDescription { get; set; } = string.Empty;
}
