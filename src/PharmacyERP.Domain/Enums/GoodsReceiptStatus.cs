namespace PharmacyERP.Domain.Enums;

/// <summary>Draft receipts can still be edited; Posted receipts have already created Batches and StockTransactions and are locked.</summary>
public enum GoodsReceiptStatus
{
    Draft = 1,
    Posted = 2,
    Cancelled = 3
}
