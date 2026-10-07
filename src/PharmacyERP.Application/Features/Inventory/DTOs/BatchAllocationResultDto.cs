namespace PharmacyERP.Application.Features.Inventory.DTOs;

/// <summary>One batch's contribution toward fulfilling a FEFO stock issuance — returned so the caller (Sales module) can record exactly which batches/costs a sold line came from.</summary>
public class BatchAllocationResultDto
{
    public int BatchId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public int QuantityTaken { get; set; }
    public decimal UnitCost { get; set; }
    public DateTime ExpiryDate { get; set; }
}
