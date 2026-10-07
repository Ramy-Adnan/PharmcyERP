using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

/// <summary>
/// A prescribing physician referenced on Prescriptions. Kept as a simple
/// registry (not a full contacts/CRM module) — pharmacies need to record who
/// prescribed a controlled substance for regulatory audit trails, not manage
/// a relationship with the doctor.
/// </summary>
public class Doctor : AuditableEntity
{
    public string FullName { get; set; } = string.Empty;
    public string? LicenseNumber { get; set; }
    public string? Specialty { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Prescription> Prescriptions { get; set; } = new List<Prescription>();
}
