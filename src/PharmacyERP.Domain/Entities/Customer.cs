using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

/// <summary>
/// A registered pharmacy customer. Walk-in sales do not require a Customer
/// record (SalesInvoice.CustomerId is nullable) — this is only for customers
/// the pharmacy wants to track (regulars, credit accounts, loyalty). Customer
/// Groups and price-list-based discounts are introduced in a later phase;
/// for now every customer buys at the standard item/batch sale price.
/// </summary>
public class Customer : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<SalesInvoice> SalesInvoices { get; set; } = new List<SalesInvoice>();
}
