using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Accounting.DTOs;

namespace PharmacyERP.Application.Features.Accounting;

/// <summary>
/// Full financial accounting surface: Chart of Accounts, double-entry Journal
/// Entries, Cash/Bank registries, Expenses, and general Receipts/Payments.
/// Also exposes the automatic-posting entry points that Sales, Purchasing,
/// and Insurance call as a direct consequence of their own transactions
/// (a completed sale, a received purchase invoice, a paid insurance claim)
/// — those modules never write journal rows themselves, keeping every
/// financial posting rule centralized here.
/// </summary>
public interface IAccountingService
{
    // Chart of Accounts
    Task<List<ChartOfAccountDto>> GetAccountsAsync(bool includeInactive = true, CancellationToken cancellationToken = default);
    Task<ChartOfAccountUpsertDto?> GetAccountForEditAsync(int id, CancellationToken cancellationToken = default);
    Task<Result<ChartOfAccountDto>> CreateAccountAsync(ChartOfAccountUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result<ChartOfAccountDto>> UpdateAccountAsync(ChartOfAccountUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result> SetAccountActiveStatusAsync(int accountId, bool isActive, CancellationToken cancellationToken = default);

    // Journal Entries
    Task<List<JournalEntryDto>> GetJournalEntriesAsync(CancellationToken cancellationToken = default);
    Task<JournalEntryDetailDto?> GetJournalEntryDetailAsync(int id, CancellationToken cancellationToken = default);
    Task<Result<JournalEntryDto>> CreateManualEntryAsync(ManualJournalEntryUpsertDto dto, CancellationToken cancellationToken = default);

    // Cash boxes & bank accounts
    Task<List<CashBoxDto>> GetCashBoxesAsync(CancellationToken cancellationToken = default);
    Task<CashBoxUpsertDto?> GetCashBoxForEditAsync(int id, CancellationToken cancellationToken = default);
    Task<Result<CashBoxDto>> CreateCashBoxAsync(CashBoxUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result<CashBoxDto>> UpdateCashBoxAsync(CashBoxUpsertDto dto, CancellationToken cancellationToken = default);

    Task<List<BankAccountDto>> GetBankAccountsAsync(CancellationToken cancellationToken = default);
    Task<BankAccountUpsertDto?> GetBankAccountForEditAsync(int id, CancellationToken cancellationToken = default);
    Task<Result<BankAccountDto>> CreateBankAccountAsync(BankAccountUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result<BankAccountDto>> UpdateBankAccountAsync(BankAccountUpsertDto dto, CancellationToken cancellationToken = default);

    // Expense categories & expenses
    Task<List<ExpenseCategoryDto>> GetExpenseCategoriesAsync(CancellationToken cancellationToken = default);
    Task<ExpenseCategoryUpsertDto?> GetExpenseCategoryForEditAsync(int id, CancellationToken cancellationToken = default);
    Task<Result<ExpenseCategoryDto>> CreateExpenseCategoryAsync(ExpenseCategoryUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result<ExpenseCategoryDto>> UpdateExpenseCategoryAsync(ExpenseCategoryUpsertDto dto, CancellationToken cancellationToken = default);

    Task<List<ExpenseDto>> GetExpensesAsync(CancellationToken cancellationToken = default);
    Task<Result<ExpenseDto>> CreateExpenseAsync(ExpenseCreateDto dto, CancellationToken cancellationToken = default);

    // General receipts & payments
    Task<List<ReceiptDto>> GetReceiptsAsync(CancellationToken cancellationToken = default);
    Task<Result<ReceiptDto>> CreateReceiptAsync(ReceiptCreateDto dto, CancellationToken cancellationToken = default);

    Task<List<PaymentDto>> GetPaymentsAsync(CancellationToken cancellationToken = default);
    Task<Result<PaymentDto>> CreatePaymentAsync(PaymentCreateDto dto, CancellationToken cancellationToken = default);

    // Automatic postings triggered by other modules
    Task PostSalesInvoiceAsync(SalesInvoicePostingRequest request, CancellationToken cancellationToken = default);
    Task PostPurchaseInvoiceAsync(PurchaseInvoicePostingRequest request, CancellationToken cancellationToken = default);
    Task PostPurchaseInvoicePaymentAsync(PurchaseInvoicePaymentPostingRequest request, CancellationToken cancellationToken = default);
    Task PostInsuranceClaimPaymentAsync(InsuranceClaimPaymentPostingRequest request, CancellationToken cancellationToken = default);
    Task PostPayrollPaymentAsync(PayrollPaymentPostingRequest request, CancellationToken cancellationToken = default);
}
