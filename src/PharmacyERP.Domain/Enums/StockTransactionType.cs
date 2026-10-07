namespace PharmacyERP.Domain.Enums;

/// <summary>
/// Every possible reason a Batch's quantity changes. Every change to stock
/// anywhere in the system — Purchasing, Sales, manual correction — must be
/// recorded as a StockTransaction with one of these types, so the full
/// movement history of every batch can always be reconstructed for audits.
/// </summary>
public enum StockTransactionType
{
    PurchaseReceipt = 1,
    SaleIssue = 2,
    AdjustmentIncrease = 3,
    AdjustmentDecrease = 4,
    TransferOut = 5,
    TransferIn = 6,
    ExpiredWriteOff = 7,
    DamagedWriteOff = 8,
    ReturnFromCustomer = 9,
    ReturnToSupplier = 10,
    OpeningBalance = 11
}
