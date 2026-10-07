using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

/// <summary>One prescribed drug line: the item, the prescribed quantity/dosage instructions, and how much of it has been dispensed so far.</summary>
public class PrescriptionItem : AuditableEntity
{
    public int PrescriptionId { get; set; }
    public Prescription Prescription { get; set; } = null!;

    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;

    public int QuantityPrescribed { get; set; }
    public int QuantityDispensed { get; set; }

    public string? DosageInstructions { get; set; }

    public int QuantityRemaining => Math.Max(0, QuantityPrescribed - QuantityDispensed);
}
