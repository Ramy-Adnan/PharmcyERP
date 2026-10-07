using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Application.Features.Accounting.DTOs;

public class ReceiptDto
{
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public DateTime ReceiptDate { get; set; }
    public decimal Amount { get; set; }
    public string PayerName { get; set; } = string.Empty;
    public string? ReferenceType { get; set; }
    public CashSourceType SourceType { get; set; }
    public string? Notes { get; set; }
}

public class ReceiptCreateDto
{
    public int BranchId { get; set; }
    public DateTime ReceiptDate { get; set; } = DateTime.Today;
    public decimal Amount { get; set; }
    public string PayerName { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public CashSourceType SourceType { get; set; } = CashSourceType.Cash;
    public int? CashBoxId { get; set; }
    public int? BankAccountId { get; set; }
}

public class PaymentDto
{
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public string PayeeName { get; set; } = string.Empty;
    public string? ReferenceType { get; set; }
    public CashSourceType SourceType { get; set; }
    public string? Notes { get; set; }
}

public class PaymentCreateDto
{
    public int BranchId { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.Today;
    public decimal Amount { get; set; }
    public string PayeeName { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public CashSourceType SourceType { get; set; } = CashSourceType.Cash;
    public int? CashBoxId { get; set; }
    public int? BankAccountId { get; set; }
}
