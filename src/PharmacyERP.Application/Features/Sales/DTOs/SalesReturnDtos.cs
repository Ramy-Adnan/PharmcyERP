namespace PharmacyERP.Application.Features.Sales.DTOs;

public class SalesReturnLineInputDto
{
    public int SalesInvoiceItemId { get; set; }
    public int Quantity { get; set; }
}

public class SalesReturnRequestDto
{
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public int SalesInvoiceId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public List<SalesReturnLineInputDto> Lines { get; set; } = new();
}

public class SalesReturnDto
{
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string SalesInvoiceNumber { get; set; } = string.Empty;
    public DateTime ReturnAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string ProcessedByUserName { get; set; } = string.Empty;
    public int LineCount { get; set; }
}
