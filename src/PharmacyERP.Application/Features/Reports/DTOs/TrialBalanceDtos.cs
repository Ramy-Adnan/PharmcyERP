using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Application.Features.Reports.DTOs;

public class TrialBalanceLineDto
{
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public AccountType AccountType { get; set; }
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public decimal Balance => TotalDebit - TotalCredit;
}

public class TrialBalanceDto
{
    public DateTime AsOfDate { get; set; }
    public List<TrialBalanceLineDto> Lines { get; set; } = new();
    public decimal TotalDebit => Lines.Sum(l => l.TotalDebit);
    public decimal TotalCredit => Lines.Sum(l => l.TotalCredit);

    /// <summary>Must be true for a mathematically valid ledger — a mismatch means a posting bug, not a business condition, so the report screen flags it prominently.</summary>
    public bool IsBalanced => Math.Round(TotalDebit - TotalCredit, 2) == 0m;
}
