namespace PharmacyERP.Domain.Enums;

public enum AuditAction
{
    Created = 1,
    Updated = 2,
    Deleted = 3,
    LoginSuccess = 4,
    LoginFailed = 5,
    Logout = 6,
    PermissionDenied = 7
}
