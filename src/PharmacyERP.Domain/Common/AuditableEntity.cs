namespace PharmacyERP.Domain.Common;

/// <summary>
/// Adds creation/modification audit fields and soft-delete support.
/// Every entity that must be tracked for compliance (pharmacy regulations
/// require a full audit trail) should inherit from this class.
/// </summary>
public abstract class AuditableEntity : BaseEntity
{
    public DateTime CreatedAtUtc { get; set; }
    public int? CreatedByUserId { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }
    public int? ModifiedByUserId { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public int? DeletedByUserId { get; set; }

    /// <summary>Concurrency token to prevent lost updates on multi-branch concurrent edits.</summary>
    public byte[]? RowVersion { get; set; }
}
