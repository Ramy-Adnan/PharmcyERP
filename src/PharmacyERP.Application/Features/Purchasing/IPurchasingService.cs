using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Purchasing.DTOs;

namespace PharmacyERP.Application.Features.Purchasing;

/// <summary>
/// Full Suppliers → Purchase Orders → Goods Receipt → Purchase Invoices
/// workflow. Posting a GoodsReceiptNote is the integration point with
/// Inventory: internally it calls IInventoryService.ReceiveBatchAsync for
/// every line so Batches/StockTransactions stay authoritative and the
/// Purchasing module never touches stock quantities directly.
/// </summary>
public interface IPurchasingService
{
    // Suppliers
    Task<List<SupplierDto>> GetSuppliersAsync(CancellationToken cancellationToken = default);
    Task<SupplierUpsertDto?> GetSupplierForEditAsync(int id, CancellationToken cancellationToken = default);
    Task<Result<SupplierDto>> CreateSupplierAsync(SupplierUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result<SupplierDto>> UpdateSupplierAsync(SupplierUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result> SetSupplierActiveStatusAsync(int supplierId, bool isActive, CancellationToken cancellationToken = default);

    // Purchase Orders
    Task<List<PurchaseOrderDto>> GetPurchaseOrdersAsync(CancellationToken cancellationToken = default);
    Task<PurchaseOrderUpsertDto?> GetPurchaseOrderForEditAsync(int id, CancellationToken cancellationToken = default);
    Task<List<PurchaseOrderLineDto>> GetPurchaseOrderLinesAsync(int purchaseOrderId, CancellationToken cancellationToken = default);
    Task<Result<PurchaseOrderDto>> CreatePurchaseOrderAsync(PurchaseOrderUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result<PurchaseOrderDto>> UpdatePurchaseOrderAsync(PurchaseOrderUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result> SubmitPurchaseOrderAsync(int purchaseOrderId, CancellationToken cancellationToken = default);
    Task<Result> CancelPurchaseOrderAsync(int purchaseOrderId, CancellationToken cancellationToken = default);
    Task<Result> DeletePurchaseOrderAsync(int purchaseOrderId, CancellationToken cancellationToken = default);

    /// <summary>Outstanding (not-yet-fully-received) lines for a Submitted/PartiallyReceived PO, used to pre-fill a new Goods Receipt.</summary>
    Task<List<PurchaseOrderLineDto>> GetOutstandingLinesForReceiptAsync(int purchaseOrderId, CancellationToken cancellationToken = default);

    // Goods Receipt
    Task<List<GoodsReceiptNoteDto>> GetGoodsReceiptNotesAsync(CancellationToken cancellationToken = default);
    Task<GoodsReceiptUpsertDto?> GetGoodsReceiptForEditAsync(int id, CancellationToken cancellationToken = default);
    Task<List<GoodsReceiptLineDto>> GetGoodsReceiptLinesAsync(int goodsReceiptNoteId, CancellationToken cancellationToken = default);
    Task<Result<GoodsReceiptNoteDto>> CreateGoodsReceiptAsync(GoodsReceiptUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result<GoodsReceiptNoteDto>> UpdateGoodsReceiptAsync(GoodsReceiptUpsertDto dto, CancellationToken cancellationToken = default);

    /// <summary>Commits a Draft receipt: creates/tops up Batches, writes StockTransactions, and reconciles the linked PurchaseOrder's received quantities/status.</summary>
    Task<Result> PostGoodsReceiptAsync(int goodsReceiptNoteId, int? performedByUserId, CancellationToken cancellationToken = default);
    Task<Result> DeleteGoodsReceiptAsync(int goodsReceiptNoteId, CancellationToken cancellationToken = default);

    // Purchase Invoices
    Task<List<PurchaseInvoiceDto>> GetPurchaseInvoicesAsync(CancellationToken cancellationToken = default);
    Task<List<PurchaseInvoiceLineDto>> GetPurchaseInvoiceLinesAsync(int purchaseInvoiceId, CancellationToken cancellationToken = default);
    Task<Result<PurchaseInvoiceDto>> CreatePurchaseInvoiceAsync(PurchaseInvoiceUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result> RecordPaymentAsync(RecordPaymentDto dto, CancellationToken cancellationToken = default);
    Task<Result> CancelPurchaseInvoiceAsync(int purchaseInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Pre-fills invoice lines from an already-posted Goods Receipt so the accountant doesn't retype quantities/costs.</summary>
    Task<PurchaseInvoiceUpsertDto?> PrefillInvoiceFromGoodsReceiptAsync(int goodsReceiptNoteId, CancellationToken cancellationToken = default);
}
