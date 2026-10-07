using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

/// <summary>
/// A sales commission earned by an employee (typically a percentage of a
/// specific sale a cashier/rep closed). Entered manually or computed
/// elsewhere and recorded here; SalesInvoiceId is optional since some
/// commissions are bonuses not tied to one specific invoice. Consumed by
/// the next Payroll run for that employee and then marked paid.
/// </summary>
public class Commission : AuditableEntity
{
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    public int? SalesInvoiceId { get; set; }
    public SalesInvoice? SalesInvoice { get; set; }

    public DateTime CommissionDate { get; set; } = DateTime.Today;
    public decimal Amount { get; set; }
    public string? Notes { get; set; }

    public bool IsPaid { get; set; }
    public int? PayrollRunLineId { get; set; }
    public PayrollRunLine? PayrollRunLine { get; set; }
}
