using PharmacyERP.Domain.Common;
using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Domain.Entities;

/// <summary>
/// A medical prescription for a specific Customer, written by a Doctor,
/// authorizing dispensing of one or more prescription-only Items up to the
/// prescribed quantity. A prescription is "consumed" incrementally as sales
/// are made against it (a customer may fill it across multiple visits),
/// tracked per-line via PrescriptionItem.QuantityDispensed.
/// </summary>
public class Prescription : AuditableEntity
{
    public string Number { get; set; } = string.Empty;

    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public int DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;

    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public DateTime PrescriptionDate { get; set; } = DateTime.Today;
    public DateTime? ExpiryDate { get; set; }

    public PrescriptionStatus Status { get; set; } = PrescriptionStatus.Active;
    public string? Notes { get; set; }

    public ICollection<PrescriptionItem> Items { get; set; } = new List<PrescriptionItem>();
    public ICollection<SalesInvoice> SalesInvoices { get; set; } = new List<SalesInvoice>();
}
