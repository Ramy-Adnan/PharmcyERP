using PharmacyERP.Domain.Common;
using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Domain.Entities;

/// <summary>
/// Immutable record of every create/update/delete and every security-relevant
/// event in the system. Required for pharmacy regulatory compliance
/// (controlled-substance handling, financial audits).
/// </summary>
public class AuditLog : BaseEntity
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public int? UserId { get; set; }
    public string? UserName { get; set; }

    public AuditAction Action { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public string? EntityId { get; set; }

    public string? OldValuesJson { get; set; }
    public string? NewValuesJson { get; set; }

    public string? IpAddress { get; set; }
    public string? MachineName { get; set; }
}
