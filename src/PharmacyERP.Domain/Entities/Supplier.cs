using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

/// <summary>A vendor the pharmacy purchases medicines and supplies from.</summary>
public class Supplier : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? TaxRegistrationNumber { get; set; }

    /// <summary>Standard credit period in days (0 = cash on delivery).</summary>
    public int PaymentTermsDays { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<PurchaseOrder> PurchaseOrders { get; set; } = new List<PurchaseOrder>();
    public ICollection<PurchaseInvoice> PurchaseInvoices { get; set; } = new List<PurchaseInvoice>();
}
