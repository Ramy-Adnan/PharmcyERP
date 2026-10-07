using PharmacyERP.Domain.Common;
using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Domain.Entities;

/// <summary>
/// One payroll cycle for a period (typically a calendar month) covering
/// every active Employee. Generated as a Draft (editable deductions per
/// line), then Approved (locked), then Paid — the Paid transition is the
/// only one that posts a Salaries Expense journal entry to Accounting.
/// </summary>
public class PayrollRun : AuditableEntity
{
    public string Number { get; set; } = string.Empty;
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }

    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public PayrollRunStatus Status { get; set; } = PayrollRunStatus.Draft;

    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }

    /// <summary>Cash box or bank account the net pay was actually disbursed from — set only once Paid.</summary>
    public int? CashBoxId { get; set; }
    public CashBox? CashBox { get; set; }
    public int? BankAccountId { get; set; }
    public BankAccount? BankAccount { get; set; }

    public string? Notes { get; set; }

    public ICollection<PayrollRunLine> Lines { get; set; } = new List<PayrollRunLine>();
}
