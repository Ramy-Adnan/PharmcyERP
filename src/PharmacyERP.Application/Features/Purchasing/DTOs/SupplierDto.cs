namespace PharmacyERP.Application.Features.Purchasing.DTOs;

public class SupplierDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? TaxRegistrationNumber { get; set; }
    public int PaymentTermsDays { get; set; }
    public bool IsActive { get; set; }
    public int OpenPurchaseOrderCount { get; set; }
    public decimal TotalOutstandingBalance { get; set; }
}

public class SupplierUpsertDto
{
    public int? Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? TaxRegistrationNumber { get; set; }
    public int PaymentTermsDays { get; set; }
    public bool IsActive { get; set; } = true;
}
