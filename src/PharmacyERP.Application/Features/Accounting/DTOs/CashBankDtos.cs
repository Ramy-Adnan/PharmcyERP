namespace PharmacyERP.Application.Features.Accounting.DTOs;

public class CashBoxDto
{
    public int Id { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class CashBoxUpsertDto
{
    public int? Id { get; set; }
    public int BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int AccountId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class BankAccountDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class BankAccountUpsertDto
{
    public int? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public int AccountId { get; set; }
    public bool IsActive { get; set; } = true;
}
