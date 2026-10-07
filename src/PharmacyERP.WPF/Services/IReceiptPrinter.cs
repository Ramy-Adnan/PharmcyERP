using PharmacyERP.Application.Features.Sales.DTOs;

namespace PharmacyERP.WPF.Services;

public interface IReceiptPrinter
{
    void Print(SalesInvoiceDetailDto invoice);
}
