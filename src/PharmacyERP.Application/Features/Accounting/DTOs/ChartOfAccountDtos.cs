using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Application.Features.Accounting.DTOs;

public class ChartOfAccountDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AccountType Type { get; set; }
    public int? ParentAccountId { get; set; }
    public string? ParentAccountName { get; set; }
    public bool IsSystemAccount { get; set; }
    public bool IsActive { get; set; }
}

public class ChartOfAccountUpsertDto
{
    public int? Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AccountType Type { get; set; }
    public int? ParentAccountId { get; set; }
    public bool IsActive { get; set; } = true;
}
