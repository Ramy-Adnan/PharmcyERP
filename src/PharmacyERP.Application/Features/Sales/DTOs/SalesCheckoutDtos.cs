using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Application.Features.Sales.DTOs;

public class SaleLineInputDto
{
    public int? ItemSaleUnitId { get; set; }
    public int ItemId { get; set; }
    public bool SellAsPackage { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TaxRatePercent { get; set; }
    public decimal DiscountAmount { get; set; }
}

public class SalesCheckoutDto
{
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public int BranchId { get; set; }
    public int WarehouseId { get; set; }
    public int? CustomerId { get; set; }

    /// <summary>Set when this sale fulfills (fully or partially) an existing prescription — required if any line's Item.RequiresPrescription is true.</summary>
    public int? PrescriptionId { get; set; }

    public decimal DiscountAmount { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public decimal AmountTendered { get; set; }
    public string? Notes { get; set; }
    public List<SaleLineInputDto> Lines { get; set; } = new();
}
