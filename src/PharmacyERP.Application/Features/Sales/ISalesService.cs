using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Sales.DTOs;

namespace PharmacyERP.Application.Features.Sales;

/// <summary>
/// Full point-of-sale workflow: customer management, item lookup for the POS
/// screen, checkout (which allocates stock via IInventoryService.IssueStockFefoAsync
/// under the hood), sales history, and customer returns (which restock the
/// exact batches originally sold via IInventoryService.RestockBatchAsync).
/// The Sales module never edits Batch.QuantityOnHand or writes StockTransactions
/// directly — every quantity change flows through the Inventory module so the
/// stock ledger has one single source of truth regardless of which module
/// triggered the movement.
/// </summary>
public interface ISalesService
{
    // Customers
    Task<List<CustomerDto>> GetCustomersAsync(CancellationToken cancellationToken = default);
    Task<CustomerUpsertDto?> GetCustomerForEditAsync(int id, CancellationToken cancellationToken = default);
    Task<Result<CustomerDto>> CreateCustomerAsync(CustomerUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result<CustomerDto>> UpdateCustomerAsync(CustomerUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result> SetCustomerActiveStatusAsync(int customerId, bool isActive, CancellationToken cancellationToken = default);

    // POS item lookup
    Task<List<SaleItemLookupDto>> SearchSaleItemsAsync(string searchText, int warehouseId, CancellationToken cancellationToken = default);

    // Checkout
    Task<Result<SalesInvoiceDto>> CheckoutAsync(SalesCheckoutDto dto, int cashierUserId, CancellationToken cancellationToken = default);

    // Sales history
    Task<List<SalesInvoiceDto>> GetSalesInvoicesAsync(DateTime? fromUtc = null, DateTime? toUtc = null, CancellationToken cancellationToken = default);
    Task<SalesInvoiceDetailDto?> GetSalesInvoiceDetailAsync(int id, CancellationToken cancellationToken = default);
    Task<Result> CancelSalesInvoiceAsync(int salesInvoiceId, int? performedByUserId, CancellationToken cancellationToken = default);

    // Returns
    Task<List<SalesReturnDto>> GetSalesReturnsAsync(CancellationToken cancellationToken = default);
    Task<Result<SalesReturnDto>> ProcessReturnAsync(SalesReturnRequestDto dto, int processedByUserId, CancellationToken cancellationToken = default);
}
