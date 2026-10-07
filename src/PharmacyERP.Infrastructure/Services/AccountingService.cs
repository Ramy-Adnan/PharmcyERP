using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Accounting;
using PharmacyERP.Application.Features.Accounting.DTOs;
using PharmacyERP.Domain.Entities;
using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Infrastructure.Services;

/// <summary>
/// Implements the full accounting surface plus the automatic-posting rules
/// other modules trigger. Every automatic posting resolves its control
/// accounts (Cash, Inventory, Accounts Payable, Sales Revenue, Tax Payable,
/// Input Tax, Accounts Receivable - Insurance) by their fixed seeded account
/// Code — see the *Code constants below — so postings never depend on
/// display names an accountant might later rename in the Chart of Accounts screen.
/// </summary>
public class AccountingService : IAccountingService
{
    private const string CashAccountCode = "1110";
    private const string BankAccountCode = "1120";
    private const string InventoryAccountCode = "1130";
    private const string InputTaxAccountCode = "1140";
    private const string InsuranceReceivableAccountCode = "1150";
    private const string AccountsPayableAccountCode = "2100";
    private const string TaxPayableAccountCode = "2200";
    private const string SalesRevenueAccountCode = "4100";
    private const string SalariesExpenseAccountCode = "5200";

    private readonly IApplicationDbContext _context;
    private readonly IDateTime _dateTime;

    public AccountingService(IApplicationDbContext context, IDateTime dateTime)
    {
        _context = context;
        _dateTime = dateTime;
    }

    // ===================== Chart of Accounts =====================

    public async Task<List<ChartOfAccountDto>> GetAccountsAsync(bool includeInactive = true, CancellationToken cancellationToken = default)
    {
        var query = _context.ChartOfAccounts.Include(a => a.ParentAccount).AsQueryable();
        if (!includeInactive) query = query.Where(a => a.IsActive);

        var accounts = await query.OrderBy(a => a.Code).ToListAsync(cancellationToken);
        return accounts.Select(MapAccountToDto).ToList();
    }

    public async Task<ChartOfAccountUpsertDto?> GetAccountForEditAsync(int id, CancellationToken cancellationToken = default)
    {
        var account = await _context.ChartOfAccounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null) return null;

        return new ChartOfAccountUpsertDto
        {
            Id = account.Id,
            Code = account.Code,
            Name = account.Name,
            Type = account.Type,
            ParentAccountId = account.ParentAccountId,
            IsActive = account.IsActive
        };
    }

    public async Task<Result<ChartOfAccountDto>> CreateAccountAsync(ChartOfAccountUpsertDto dto, CancellationToken cancellationToken = default)
    {
        var validation = await ValidateAccountAsync(dto, cancellationToken);
        if (validation is not null) return Result<ChartOfAccountDto>.Failure(validation);

        var account = new ChartOfAccount
        {
            Code = dto.Code.Trim(),
            Name = dto.Name.Trim(),
            Type = dto.Type,
            ParentAccountId = dto.ParentAccountId,
            IsSystemAccount = false,
            IsActive = dto.IsActive
        };

        _context.ChartOfAccounts.Add(account);
        await _context.SaveChangesAsync(cancellationToken);

        var reloaded = await _context.ChartOfAccounts.Include(a => a.ParentAccount).FirstAsync(a => a.Id == account.Id, cancellationToken);
        return Result<ChartOfAccountDto>.Success(MapAccountToDto(reloaded));
    }

    public async Task<Result<ChartOfAccountDto>> UpdateAccountAsync(ChartOfAccountUpsertDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Id is null) return Result<ChartOfAccountDto>.Failure("معرّف الحساب مطلوب.");

        var account = await _context.ChartOfAccounts.FirstOrDefaultAsync(a => a.Id == dto.Id, cancellationToken);
        if (account is null) return Result<ChartOfAccountDto>.Failure("الحساب غير موجود.");

        if (account.IsSystemAccount)
            return Result<ChartOfAccountDto>.Failure("لا يمكن تعديل حساب نظام أساسي (يُستخدم في الترحيل التلقائي).");

        var validation = await ValidateAccountAsync(dto, cancellationToken);
        if (validation is not null) return Result<ChartOfAccountDto>.Failure(validation);

        account.Code = dto.Code.Trim();
        account.Name = dto.Name.Trim();
        account.Type = dto.Type;
        account.ParentAccountId = dto.ParentAccountId;
        account.IsActive = dto.IsActive;

        await _context.SaveChangesAsync(cancellationToken);

        var reloaded = await _context.ChartOfAccounts.Include(a => a.ParentAccount).FirstAsync(a => a.Id == account.Id, cancellationToken);
        return Result<ChartOfAccountDto>.Success(MapAccountToDto(reloaded));
    }

    public async Task<Result> SetAccountActiveStatusAsync(int accountId, bool isActive, CancellationToken cancellationToken = default)
    {
        var account = await _context.ChartOfAccounts.FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (account is null) return Result.Failure("الحساب غير موجود.");

        if (!isActive && account.IsSystemAccount)
            return Result.Failure("لا يمكن تعطيل حساب نظام أساسي.");

        account.IsActive = isActive;
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // ===================== Journal Entries =====================

    public async Task<List<JournalEntryDto>> GetJournalEntriesAsync(CancellationToken cancellationToken = default)
    {
        var entries = await _context.JournalEntries
            .Include(j => j.Branch)
            .Include(j => j.Lines)
            .OrderByDescending(j => j.EntryDate).ThenByDescending(j => j.Id)
            .Take(1000)
            .ToListAsync(cancellationToken);

        return entries.Select(MapEntryToDto).ToList();
    }

    public async Task<JournalEntryDetailDto?> GetJournalEntryDetailAsync(int id, CancellationToken cancellationToken = default)
    {
        var entry = await _context.JournalEntries
            .Include(j => j.Branch)
            .Include(j => j.Lines).ThenInclude(l => l.Account)
            .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

        if (entry is null) return null;

        return new JournalEntryDetailDto
        {
            Header = MapEntryToDto(entry),
            Lines = entry.Lines.Select(MapLineToDto).ToList()
        };
    }

    public async Task<Result<JournalEntryDto>> CreateManualEntryAsync(ManualJournalEntryUpsertDto dto, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Description)) return Result<JournalEntryDto>.Failure("وصف القيد مطلوب.");
        if (dto.Lines.Count < 2) return Result<JournalEntryDto>.Failure("يجب أن يحتوي القيد على سطرين على الأقل.");
        if (dto.Lines.Any(l => l.DebitAmount > 0 && l.CreditAmount > 0))
            return Result<JournalEntryDto>.Failure("لا يمكن أن يحتوي سطر واحد على مبلغ مدين ودائن في نفس الوقت.");
        if (dto.Lines.Any(l => l.DebitAmount == 0 && l.CreditAmount == 0))
            return Result<JournalEntryDto>.Failure("كل سطر يجب أن يحتوي على مبلغ مدين أو دائن.");

        var totalDebit = Math.Round(dto.Lines.Sum(l => l.DebitAmount), 2);
        var totalCredit = Math.Round(dto.Lines.Sum(l => l.CreditAmount), 2);
        if (totalDebit != totalCredit)
            return Result<JournalEntryDto>.Failure($"القيد غير متوازن: إجمالي المدين {totalDebit:N2} لا يساوي إجمالي الدائن {totalCredit:N2}.");

        var branchExists = await _context.Branches.AnyAsync(b => b.Id == dto.BranchId, cancellationToken);
        if (!branchExists) return Result<JournalEntryDto>.Failure("الفرع غير موجود.");

        var accountIds = dto.Lines.Select(l => l.AccountId).Distinct().ToList();
        var validAccountCount = await _context.ChartOfAccounts.CountAsync(a => accountIds.Contains(a.Id) && a.IsActive, cancellationToken);
        if (validAccountCount != accountIds.Count)
            return Result<JournalEntryDto>.Failure("أحد الحسابات المحددة غير موجود أو غير نشط.");

        var entry = new JournalEntry
        {
            Number = await GenerateNumberAsync("JE"),
            BranchId = dto.BranchId,
            EntryDate = dto.EntryDate,
            Description = dto.Description.Trim(),
            IsPosted = true,
            PostedAtUtc = _dateTime.UtcNow
        };

        _context.JournalEntries.Add(entry);
        await _context.SaveChangesAsync(cancellationToken);

        foreach (var line in dto.Lines)
        {
            _context.JournalEntryLines.Add(new JournalEntryLine
            {
                JournalEntryId = entry.Id,
                AccountId = line.AccountId,
                DebitAmount = line.DebitAmount,
                CreditAmount = line.CreditAmount,
                Description = line.Description
            });
        }
        await _context.SaveChangesAsync(cancellationToken);

        var detail = await GetJournalEntryDetailAsync(entry.Id, cancellationToken);
        return Result<JournalEntryDto>.Success(detail!.Header);
    }

    // ===================== Cash boxes & bank accounts =====================

    public async Task<List<CashBoxDto>> GetCashBoxesAsync(CancellationToken cancellationToken = default)
    {
        var boxes = await _context.CashBoxes.Include(c => c.Branch).Include(c => c.Account).OrderBy(c => c.Name).ToListAsync(cancellationToken);
        return boxes.Select(c => new CashBoxDto
        {
            Id = c.Id, BranchName = c.Branch.Name, Name = c.Name, AccountName = $"{c.Account.Code} - {c.Account.Name}", IsActive = c.IsActive
        }).ToList();
    }

    public async Task<CashBoxUpsertDto?> GetCashBoxForEditAsync(int id, CancellationToken cancellationToken = default)
    {
        var box = await _context.CashBoxes.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (box is null) return null;

        return new CashBoxUpsertDto { Id = box.Id, BranchId = box.BranchId, Name = box.Name, AccountId = box.AccountId, IsActive = box.IsActive };
    }

    public async Task<Result<CashBoxDto>> CreateCashBoxAsync(CashBoxUpsertDto dto, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return Result<CashBoxDto>.Failure("اسم الصندوق مطلوب.");

        var branchExists = await _context.Branches.AnyAsync(b => b.Id == dto.BranchId, cancellationToken);
        if (!branchExists) return Result<CashBoxDto>.Failure("الفرع غير موجود.");

        var accountExists = await _context.ChartOfAccounts.AnyAsync(a => a.Id == dto.AccountId, cancellationToken);
        if (!accountExists) return Result<CashBoxDto>.Failure("الحساب المحاسبي المحدد غير موجود.");

        var box = new CashBox { BranchId = dto.BranchId, Name = dto.Name.Trim(), AccountId = dto.AccountId, IsActive = dto.IsActive };
        _context.CashBoxes.Add(box);
        await _context.SaveChangesAsync(cancellationToken);

        var reloaded = await _context.CashBoxes.Include(c => c.Branch).Include(c => c.Account).FirstAsync(c => c.Id == box.Id, cancellationToken);
        return Result<CashBoxDto>.Success(new CashBoxDto { Id = reloaded.Id, BranchName = reloaded.Branch.Name, Name = reloaded.Name, AccountName = $"{reloaded.Account.Code} - {reloaded.Account.Name}", IsActive = reloaded.IsActive });
    }

    public async Task<Result<CashBoxDto>> UpdateCashBoxAsync(CashBoxUpsertDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Id is null) return Result<CashBoxDto>.Failure("معرّف الصندوق مطلوب.");
        if (string.IsNullOrWhiteSpace(dto.Name)) return Result<CashBoxDto>.Failure("اسم الصندوق مطلوب.");

        var box = await _context.CashBoxes.FirstOrDefaultAsync(c => c.Id == dto.Id, cancellationToken);
        if (box is null) return Result<CashBoxDto>.Failure("الصندوق غير موجود.");

        box.BranchId = dto.BranchId;
        box.Name = dto.Name.Trim();
        box.AccountId = dto.AccountId;
        box.IsActive = dto.IsActive;

        await _context.SaveChangesAsync(cancellationToken);

        var reloaded = await _context.CashBoxes.Include(c => c.Branch).Include(c => c.Account).FirstAsync(c => c.Id == box.Id, cancellationToken);
        return Result<CashBoxDto>.Success(new CashBoxDto { Id = reloaded.Id, BranchName = reloaded.Branch.Name, Name = reloaded.Name, AccountName = $"{reloaded.Account.Code} - {reloaded.Account.Name}", IsActive = reloaded.IsActive });
    }

    public async Task<List<BankAccountDto>> GetBankAccountsAsync(CancellationToken cancellationToken = default)
    {
        var banks = await _context.BankAccounts.Include(b => b.Account).OrderBy(b => b.Name).ToListAsync(cancellationToken);
        return banks.Select(MapBankToDto).ToList();
    }

    public async Task<BankAccountUpsertDto?> GetBankAccountForEditAsync(int id, CancellationToken cancellationToken = default)
    {
        var bank = await _context.BankAccounts.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (bank is null) return null;

        return new BankAccountUpsertDto
        {
            Id = bank.Id, Name = bank.Name, BankName = bank.BankName, AccountNumber = bank.AccountNumber,
            AccountId = bank.AccountId, IsActive = bank.IsActive
        };
    }

    public async Task<Result<BankAccountDto>> CreateBankAccountAsync(BankAccountUpsertDto dto, CancellationToken cancellationToken = default)
    {
        var validation = ValidateBankAccount(dto);
        if (validation is not null) return Result<BankAccountDto>.Failure(validation);

        var accountExists = await _context.ChartOfAccounts.AnyAsync(a => a.Id == dto.AccountId, cancellationToken);
        if (!accountExists) return Result<BankAccountDto>.Failure("الحساب المحاسبي المحدد غير موجود.");

        var bank = new BankAccount
        {
            Name = dto.Name.Trim(), BankName = dto.BankName.Trim(), AccountNumber = dto.AccountNumber.Trim(),
            AccountId = dto.AccountId, IsActive = dto.IsActive
        };
        _context.BankAccounts.Add(bank);
        await _context.SaveChangesAsync(cancellationToken);

        var reloaded = await _context.BankAccounts.Include(b => b.Account).FirstAsync(b => b.Id == bank.Id, cancellationToken);
        return Result<BankAccountDto>.Success(MapBankToDto(reloaded));
    }

    public async Task<Result<BankAccountDto>> UpdateBankAccountAsync(BankAccountUpsertDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Id is null) return Result<BankAccountDto>.Failure("معرّف الحساب البنكي مطلوب.");

        var validation = ValidateBankAccount(dto);
        if (validation is not null) return Result<BankAccountDto>.Failure(validation);

        var bank = await _context.BankAccounts.FirstOrDefaultAsync(b => b.Id == dto.Id, cancellationToken);
        if (bank is null) return Result<BankAccountDto>.Failure("الحساب البنكي غير موجود.");

        bank.Name = dto.Name.Trim();
        bank.BankName = dto.BankName.Trim();
        bank.AccountNumber = dto.AccountNumber.Trim();
        bank.AccountId = dto.AccountId;
        bank.IsActive = dto.IsActive;

        await _context.SaveChangesAsync(cancellationToken);

        var reloaded = await _context.BankAccounts.Include(b => b.Account).FirstAsync(b => b.Id == bank.Id, cancellationToken);
        return Result<BankAccountDto>.Success(MapBankToDto(reloaded));
    }

    // ===================== Expense categories & expenses =====================

    public async Task<List<ExpenseCategoryDto>> GetExpenseCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var categories = await _context.ExpenseCategories.Include(c => c.DefaultExpenseAccount).OrderBy(c => c.Name).ToListAsync(cancellationToken);
        return categories.Select(c => new ExpenseCategoryDto
        {
            Id = c.Id, Name = c.Name, DefaultExpenseAccountName = $"{c.DefaultExpenseAccount.Code} - {c.DefaultExpenseAccount.Name}", IsActive = c.IsActive
        }).ToList();
    }

    public async Task<ExpenseCategoryUpsertDto?> GetExpenseCategoryForEditAsync(int id, CancellationToken cancellationToken = default)
    {
        var category = await _context.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null) return null;

        return new ExpenseCategoryUpsertDto
        {
            Id = category.Id,
            Name = category.Name,
            DefaultExpenseAccountId = category.DefaultExpenseAccountId,
            IsActive = category.IsActive
        };
    }

    public async Task<Result<ExpenseCategoryDto>> CreateExpenseCategoryAsync(ExpenseCategoryUpsertDto dto, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return Result<ExpenseCategoryDto>.Failure("اسم فئة المصروف مطلوب.");

        var nameTaken = await _context.ExpenseCategories.AnyAsync(c => c.Name == dto.Name.Trim() && c.Id != dto.Id, cancellationToken);
        if (nameTaken) return Result<ExpenseCategoryDto>.Failure("اسم فئة المصروف مستخدم مسبقاً.");

        var accountExists = await _context.ChartOfAccounts.AnyAsync(a => a.Id == dto.DefaultExpenseAccountId, cancellationToken);
        if (!accountExists) return Result<ExpenseCategoryDto>.Failure("حساب المصروف الافتراضي غير موجود.");

        var category = new ExpenseCategory { Name = dto.Name.Trim(), DefaultExpenseAccountId = dto.DefaultExpenseAccountId, IsActive = dto.IsActive };
        _context.ExpenseCategories.Add(category);
        await _context.SaveChangesAsync(cancellationToken);

        var reloaded = await _context.ExpenseCategories.Include(c => c.DefaultExpenseAccount).FirstAsync(c => c.Id == category.Id, cancellationToken);
        return Result<ExpenseCategoryDto>.Success(new ExpenseCategoryDto { Id = reloaded.Id, Name = reloaded.Name, DefaultExpenseAccountName = $"{reloaded.DefaultExpenseAccount.Code} - {reloaded.DefaultExpenseAccount.Name}", IsActive = reloaded.IsActive });
    }

    public async Task<Result<ExpenseCategoryDto>> UpdateExpenseCategoryAsync(ExpenseCategoryUpsertDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Id is null) return Result<ExpenseCategoryDto>.Failure("معرّف فئة المصروف مطلوب.");
        if (string.IsNullOrWhiteSpace(dto.Name)) return Result<ExpenseCategoryDto>.Failure("اسم فئة المصروف مطلوب.");

        var category = await _context.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == dto.Id, cancellationToken);
        if (category is null) return Result<ExpenseCategoryDto>.Failure("فئة المصروف غير موجودة.");

        var nameTaken = await _context.ExpenseCategories.AnyAsync(c => c.Name == dto.Name.Trim() && c.Id != dto.Id, cancellationToken);
        if (nameTaken) return Result<ExpenseCategoryDto>.Failure("اسم فئة المصروف مستخدم مسبقاً.");

        category.Name = dto.Name.Trim();
        category.DefaultExpenseAccountId = dto.DefaultExpenseAccountId;
        category.IsActive = dto.IsActive;

        await _context.SaveChangesAsync(cancellationToken);

        var reloaded = await _context.ExpenseCategories.Include(c => c.DefaultExpenseAccount).FirstAsync(c => c.Id == category.Id, cancellationToken);
        return Result<ExpenseCategoryDto>.Success(new ExpenseCategoryDto { Id = reloaded.Id, Name = reloaded.Name, DefaultExpenseAccountName = $"{reloaded.DefaultExpenseAccount.Code} - {reloaded.DefaultExpenseAccount.Name}", IsActive = reloaded.IsActive });
    }

    public async Task<List<ExpenseDto>> GetExpensesAsync(CancellationToken cancellationToken = default)
    {
        var expenses = await _context.Expenses
            .Include(e => e.Branch).Include(e => e.ExpenseCategory)
            .OrderByDescending(e => e.ExpenseDate).Take(500)
            .ToListAsync(cancellationToken);

        return expenses.Select(e => new ExpenseDto
        {
            Id = e.Id, Number = e.Number, BranchName = e.Branch.Name, CategoryName = e.ExpenseCategory.Name,
            ExpenseDate = e.ExpenseDate, Amount = e.Amount, Description = e.Description, SourceType = e.SourceType
        }).ToList();
    }

    public async Task<Result<ExpenseDto>> CreateExpenseAsync(ExpenseCreateDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Amount <= 0) return Result<ExpenseDto>.Failure("مبلغ المصروف يجب أن يكون أكبر من صفر.");

        var sourceValidation = await ValidateCashSourceAsync(dto.SourceType, dto.CashBoxId, dto.BankAccountId, cancellationToken);
        if (sourceValidation is not null) return Result<ExpenseDto>.Failure(sourceValidation);

        var category = await _context.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == dto.ExpenseCategoryId, cancellationToken);
        if (category is null) return Result<ExpenseDto>.Failure("فئة المصروف غير موجودة.");

        var sourceAccountId = await ResolveSourceAccountIdAsync(dto.SourceType, dto.CashBoxId, dto.BankAccountId, cancellationToken);

        var journalEntry = await CreateAndPostEntryAsync(
            dto.BranchId, dto.ExpenseDate, $"مصروف: {category.Name}",
            referenceType: "Expense", referenceId: null,
            lines: new[]
            {
                (category.DefaultExpenseAccountId, dto.Amount, 0m, (string?)dto.Description),
                (sourceAccountId, 0m, dto.Amount, (string?)null)
            }, cancellationToken);

        var expense = new Expense
        {
            Number = await GenerateNumberAsync("EXP"),
            BranchId = dto.BranchId,
            ExpenseCategoryId = dto.ExpenseCategoryId,
            ExpenseDate = dto.ExpenseDate,
            Amount = dto.Amount,
            Description = dto.Description,
            SourceType = dto.SourceType,
            CashBoxId = dto.CashBoxId,
            BankAccountId = dto.BankAccountId,
            JournalEntryId = journalEntry.Id
        };

        _context.Expenses.Add(expense);
        await _context.SaveChangesAsync(cancellationToken);

        return Result<ExpenseDto>.Success(new ExpenseDto
        {
            Id = expense.Id, Number = expense.Number, BranchName = (await _context.Branches.FirstAsync(b => b.Id == dto.BranchId, cancellationToken)).Name,
            CategoryName = category.Name, ExpenseDate = expense.ExpenseDate, Amount = expense.Amount, Description = expense.Description, SourceType = expense.SourceType
        });
    }

    // ===================== General receipts & payments =====================

    public async Task<List<ReceiptDto>> GetReceiptsAsync(CancellationToken cancellationToken = default)
    {
        var receipts = await _context.Receipts.Include(r => r.Branch).OrderByDescending(r => r.ReceiptDate).Take(500).ToListAsync(cancellationToken);
        return receipts.Select(r => new ReceiptDto
        {
            Id = r.Id, Number = r.Number, BranchName = r.Branch.Name, ReceiptDate = r.ReceiptDate, Amount = r.Amount,
            PayerName = r.PayerName, ReferenceType = r.ReferenceType, SourceType = r.SourceType, Notes = r.Notes
        }).ToList();
    }

    public async Task<Result<ReceiptDto>> CreateReceiptAsync(ReceiptCreateDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Amount <= 0) return Result<ReceiptDto>.Failure("مبلغ الإيصال يجب أن يكون أكبر من صفر.");
        if (string.IsNullOrWhiteSpace(dto.PayerName)) return Result<ReceiptDto>.Failure("اسم الدافع مطلوب.");

        var sourceValidation = await ValidateCashSourceAsync(dto.SourceType, dto.CashBoxId, dto.BankAccountId, cancellationToken);
        if (sourceValidation is not null) return Result<ReceiptDto>.Failure(sourceValidation);

        var sourceAccountId = await ResolveSourceAccountIdAsync(dto.SourceType, dto.CashBoxId, dto.BankAccountId, cancellationToken);
        var revenueAccount = await GetSystemAccountAsync(SalesRevenueAccountCode, cancellationToken);

        var journalEntry = await CreateAndPostEntryAsync(
            dto.BranchId, dto.ReceiptDate, $"إيصال قبض من {dto.PayerName}",
            referenceType: "Receipt", referenceId: null,
            lines: new[]
            {
                (sourceAccountId, dto.Amount, 0m, (string?)null),
                (revenueAccount.Id, 0m, dto.Amount, (string?)dto.Notes)
            }, cancellationToken);

        var receipt = new Receipt
        {
            Number = await GenerateNumberAsync("RCT"),
            BranchId = dto.BranchId, ReceiptDate = dto.ReceiptDate, Amount = dto.Amount, PayerName = dto.PayerName.Trim(),
            Notes = dto.Notes, SourceType = dto.SourceType, CashBoxId = dto.CashBoxId, BankAccountId = dto.BankAccountId,
            JournalEntryId = journalEntry.Id
        };

        _context.Receipts.Add(receipt);
        await _context.SaveChangesAsync(cancellationToken);

        var branchName = (await _context.Branches.FirstAsync(b => b.Id == dto.BranchId, cancellationToken)).Name;
        return Result<ReceiptDto>.Success(new ReceiptDto
        {
            Id = receipt.Id, Number = receipt.Number, BranchName = branchName, ReceiptDate = receipt.ReceiptDate,
            Amount = receipt.Amount, PayerName = receipt.PayerName, ReferenceType = receipt.ReferenceType, SourceType = receipt.SourceType, Notes = receipt.Notes
        });
    }

    public async Task<List<PaymentDto>> GetPaymentsAsync(CancellationToken cancellationToken = default)
    {
        var payments = await _context.Payments.Include(p => p.Branch).OrderByDescending(p => p.PaymentDate).Take(500).ToListAsync(cancellationToken);
        return payments.Select(p => new PaymentDto
        {
            Id = p.Id, Number = p.Number, BranchName = p.Branch.Name, PaymentDate = p.PaymentDate, Amount = p.Amount,
            PayeeName = p.PayeeName, ReferenceType = p.ReferenceType, SourceType = p.SourceType, Notes = p.Notes
        }).ToList();
    }

    public async Task<Result<PaymentDto>> CreatePaymentAsync(PaymentCreateDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Amount <= 0) return Result<PaymentDto>.Failure("مبلغ الدفعة يجب أن يكون أكبر من صفر.");
        if (string.IsNullOrWhiteSpace(dto.PayeeName)) return Result<PaymentDto>.Failure("اسم المستفيد مطلوب.");

        var sourceValidation = await ValidateCashSourceAsync(dto.SourceType, dto.CashBoxId, dto.BankAccountId, cancellationToken);
        if (sourceValidation is not null) return Result<PaymentDto>.Failure(sourceValidation);

        var sourceAccountId = await ResolveSourceAccountIdAsync(dto.SourceType, dto.CashBoxId, dto.BankAccountId, cancellationToken);
        var payableAccount = await GetSystemAccountAsync(AccountsPayableAccountCode, cancellationToken);

        var journalEntry = await CreateAndPostEntryAsync(
            dto.BranchId, dto.PaymentDate, $"دفعة إلى {dto.PayeeName}",
            referenceType: "Payment", referenceId: null,
            lines: new[]
            {
                (payableAccount.Id, dto.Amount, 0m, (string?)dto.Notes),
                (sourceAccountId, 0m, dto.Amount, (string?)null)
            }, cancellationToken);

        var payment = new Payment
        {
            Number = await GenerateNumberAsync("PMT"),
            BranchId = dto.BranchId, PaymentDate = dto.PaymentDate, Amount = dto.Amount, PayeeName = dto.PayeeName.Trim(),
            Notes = dto.Notes, SourceType = dto.SourceType, CashBoxId = dto.CashBoxId, BankAccountId = dto.BankAccountId,
            JournalEntryId = journalEntry.Id
        };

        _context.Payments.Add(payment);
        await _context.SaveChangesAsync(cancellationToken);

        var branchName = (await _context.Branches.FirstAsync(b => b.Id == dto.BranchId, cancellationToken)).Name;
        return Result<PaymentDto>.Success(new PaymentDto
        {
            Id = payment.Id, Number = payment.Number, BranchName = branchName, PaymentDate = payment.PaymentDate,
            Amount = payment.Amount, PayeeName = payment.PayeeName, ReferenceType = payment.ReferenceType, SourceType = payment.SourceType, Notes = payment.Notes
        });
    }

    // ===================== Automatic postings =====================

    public async Task PostSalesInvoiceAsync(SalesInvoicePostingRequest request, CancellationToken cancellationToken = default)
    {
        var cashAccount = await GetSystemAccountAsync(CashAccountCode, cancellationToken);
        var revenueAccount = await GetSystemAccountAsync(SalesRevenueAccountCode, cancellationToken);
        var taxPayableAccount = await GetSystemAccountAsync(TaxPayableAccountCode, cancellationToken);

        var lines = new List<(int accountId, decimal debit, decimal credit, string? description)>
        {
            (cashAccount.Id, request.TotalAmount, 0m, null),
            (revenueAccount.Id, 0m, request.NetRevenueAmount, null)
        };
        if (request.TaxAmount > 0) lines.Add((taxPayableAccount.Id, 0m, request.TaxAmount, null));

        await CreateAndPostEntryAsync(
            request.BranchId, request.SaleDate, $"إيراد مبيعات - فاتورة {request.SalesInvoiceNumber}",
            referenceType: "SalesInvoice", referenceId: request.SalesInvoiceId, lines, cancellationToken);
    }

    public async Task PostPurchaseInvoiceAsync(PurchaseInvoicePostingRequest request, CancellationToken cancellationToken = default)
    {
        var inventoryAccount = await GetSystemAccountAsync(InventoryAccountCode, cancellationToken);
        var inputTaxAccount = await GetSystemAccountAsync(InputTaxAccountCode, cancellationToken);
        var payableAccount = await GetSystemAccountAsync(AccountsPayableAccountCode, cancellationToken);

        var lines = new List<(int accountId, decimal debit, decimal credit, string? description)>
        {
            (inventoryAccount.Id, request.NetInventoryAmount, 0m, null),
            (payableAccount.Id, 0m, request.TotalAmount, null)
        };
        if (request.TaxAmount > 0) lines.Insert(1, (inputTaxAccount.Id, request.TaxAmount, 0m, null));

        await CreateAndPostEntryAsync(
            request.BranchId, request.InvoiceDate, $"فاتورة شراء {request.PurchaseInvoiceNumber}",
            referenceType: "PurchaseInvoice", referenceId: request.PurchaseInvoiceId, lines, cancellationToken);
    }

    public async Task PostPurchaseInvoicePaymentAsync(PurchaseInvoicePaymentPostingRequest request, CancellationToken cancellationToken = default)
    {
        var payableAccount = await GetSystemAccountAsync(AccountsPayableAccountCode, cancellationToken);
        var cashAccount = await GetSystemAccountAsync(CashAccountCode, cancellationToken);

        var lines = new[]
        {
            (payableAccount.Id, request.Amount, 0m, (string?)null),
            (cashAccount.Id, 0m, request.Amount, (string?)null)
        };

        await CreateAndPostEntryAsync(
            request.BranchId, request.PaymentDate, $"دفعة على فاتورة شراء {request.PurchaseInvoiceNumber}",
            referenceType: "PurchaseInvoicePayment", referenceId: request.PurchaseInvoiceId, lines, cancellationToken);
    }

    public async Task PostInsuranceClaimPaymentAsync(InsuranceClaimPaymentPostingRequest request, CancellationToken cancellationToken = default)
    {
        var bankAccount = await GetSystemAccountAsync(BankAccountCode, cancellationToken);
        var insuranceReceivableAccount = await GetSystemAccountAsync(InsuranceReceivableAccountCode, cancellationToken);

        var lines = new[]
        {
            (bankAccount.Id, request.Amount, 0m, (string?)null),
            (insuranceReceivableAccount.Id, 0m, request.Amount, (string?)null)
        };

        var journalEntry = await CreateAndPostEntryAsync(
            request.BranchId, request.PaymentDate, $"سداد مطالبة تأمين {request.ClaimNumber} من {request.InsuranceCompanyName}",
            referenceType: "InsuranceClaim", referenceId: request.InsuranceClaimId, lines, cancellationToken);

        _context.Receipts.Add(new Receipt
        {
            Number = await GenerateNumberAsync("RCT"),
            BranchId = request.BranchId,
            ReceiptDate = request.PaymentDate,
            Amount = request.Amount,
            PayerName = request.InsuranceCompanyName,
            Notes = $"سداد مطالبة تأمين رقم {request.ClaimNumber}",
            SourceType = CashSourceType.Bank,
            ReferenceType = "InsuranceClaim",
            ReferenceId = request.InsuranceClaimId,
            JournalEntryId = journalEntry.Id
        });
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task PostPayrollPaymentAsync(PayrollPaymentPostingRequest request, CancellationToken cancellationToken = default)
    {
        var salariesExpenseAccount = await GetSystemAccountAsync(SalariesExpenseAccountCode, cancellationToken);
        var cashAccount = await GetSystemAccountAsync(CashAccountCode, cancellationToken);

        var lines = new[]
        {
            (salariesExpenseAccount.Id, request.Amount, 0m, (string?)null),
            (cashAccount.Id, 0m, request.Amount, (string?)null)
        };

        var journalEntry = await CreateAndPostEntryAsync(
            request.BranchId, request.PaymentDate, $"صرف رواتب - دورة {request.PayrollRunNumber}",
            referenceType: "PayrollRun", referenceId: request.PayrollRunId, lines, cancellationToken);

        _context.Payments.Add(new Payment
        {
            Number = await GenerateNumberAsync("PMT"),
            BranchId = request.BranchId,
            PaymentDate = request.PaymentDate,
            Amount = request.Amount,
            PayeeName = $"رواتب الموظفين - دورة {request.PayrollRunNumber}",
            Notes = $"صرف رواتب دورة {request.PayrollRunNumber}",
            SourceType = CashSourceType.Cash,
            ReferenceType = "PayrollRun",
            ReferenceId = request.PayrollRunId,
            JournalEntryId = journalEntry.Id
        });
        await _context.SaveChangesAsync(cancellationToken);
    }

    // ===================== Internal helpers =====================

    private async Task<JournalEntry> CreateAndPostEntryAsync(
        int branchId, DateTime entryDate, string description, string? referenceType, int? referenceId,
        IEnumerable<(int accountId, decimal debit, decimal credit, string? description)> lines,
        CancellationToken cancellationToken)
    {
        var entry = new JournalEntry
        {
            Number = await GenerateNumberAsync("JE"),
            BranchId = branchId,
            EntryDate = entryDate,
            Description = description,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            IsPosted = true,
            PostedAtUtc = _dateTime.UtcNow
        };

        _context.JournalEntries.Add(entry);
        await _context.SaveChangesAsync(cancellationToken);

        foreach (var (accountId, debit, credit, lineDescription) in lines)
        {
            _context.JournalEntryLines.Add(new JournalEntryLine
            {
                JournalEntryId = entry.Id,
                AccountId = accountId,
                DebitAmount = debit,
                CreditAmount = credit,
                Description = lineDescription
            });
        }
        await _context.SaveChangesAsync(cancellationToken);

        return entry;
    }

    private async Task<ChartOfAccount> GetSystemAccountAsync(string code, CancellationToken cancellationToken)
    {
        var account = await _context.ChartOfAccounts.FirstOrDefaultAsync(a => a.Code == code, cancellationToken);
        if (account is null)
            throw new InvalidOperationException($"الحساب المحاسبي الأساسي برمز '{code}' غير موجود. تأكد من تشغيل عملية التهيئة الأولية (Seeding) لقاعدة البيانات.");
        return account;
    }

    private async Task<int> ResolveSourceAccountIdAsync(CashSourceType sourceType, int? cashBoxId, int? bankAccountId, CancellationToken cancellationToken)
    {
        if (sourceType == CashSourceType.Cash)
        {
            var box = await _context.CashBoxes.FirstAsync(c => c.Id == cashBoxId!.Value, cancellationToken);
            return box.AccountId;
        }

        var bank = await _context.BankAccounts.FirstAsync(b => b.Id == bankAccountId!.Value, cancellationToken);
        return bank.AccountId;
    }

    private async Task<string?> ValidateCashSourceAsync(CashSourceType sourceType, int? cashBoxId, int? bankAccountId, CancellationToken cancellationToken)
    {
        if (sourceType == CashSourceType.Cash)
        {
            if (cashBoxId is null) return "الرجاء اختيار صندوق النقدية.";
            var exists = await _context.CashBoxes.AnyAsync(c => c.Id == cashBoxId.Value && c.IsActive, cancellationToken);
            if (!exists) return "صندوق النقدية المحدد غير موجود أو غير نشط.";
        }
        else
        {
            if (bankAccountId is null) return "الرجاء اختيار الحساب البنكي.";
            var exists = await _context.BankAccounts.AnyAsync(b => b.Id == bankAccountId.Value && b.IsActive, cancellationToken);
            if (!exists) return "الحساب البنكي المحدد غير موجود أو غير نشط.";
        }

        return null;
    }

    private async Task<string?> ValidateAccountAsync(ChartOfAccountUpsertDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.Code)) return "رمز الحساب مطلوب.";
        if (string.IsNullOrWhiteSpace(dto.Name)) return "اسم الحساب مطلوب.";

        var codeTaken = await _context.ChartOfAccounts.AnyAsync(a => a.Code == dto.Code.Trim() && a.Id != dto.Id, cancellationToken);
        if (codeTaken) return "رمز الحساب مستخدم مسبقاً.";

        if (dto.ParentAccountId.HasValue)
        {
            var parentExists = await _context.ChartOfAccounts.AnyAsync(a => a.Id == dto.ParentAccountId.Value, cancellationToken);
            if (!parentExists) return "الحساب الأب المحدد غير موجود.";
            if (dto.ParentAccountId.Value == dto.Id) return "لا يمكن أن يكون الحساب أباً لنفسه.";
        }

        return null;
    }

    private static string? ValidateBankAccount(BankAccountUpsertDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return "اسم الحساب مطلوب.";
        if (string.IsNullOrWhiteSpace(dto.BankName)) return "اسم البنك مطلوب.";
        if (string.IsNullOrWhiteSpace(dto.AccountNumber)) return "رقم الحساب مطلوب.";
        return null;
    }

    private async Task<string> GenerateNumberAsync(string prefix)
    {
        int count = prefix switch
        {
            "JE" => await _context.JournalEntries.CountAsync(),
            "EXP" => await _context.Expenses.CountAsync(),
            "RCT" => await _context.Receipts.CountAsync(),
            "PMT" => await _context.Payments.CountAsync(),
            _ => 0
        };
        return $"{prefix}-{DateTime.UtcNow:yyyyMM}-{count + 1:D5}";
    }

    private static ChartOfAccountDto MapAccountToDto(ChartOfAccount a) => new()
    {
        Id = a.Id, Code = a.Code, Name = a.Name, Type = a.Type,
        ParentAccountId = a.ParentAccountId, ParentAccountName = a.ParentAccount?.Name,
        IsSystemAccount = a.IsSystemAccount, IsActive = a.IsActive
    };

    private static JournalEntryDto MapEntryToDto(JournalEntry j) => new()
    {
        Id = j.Id, Number = j.Number, BranchName = j.Branch.Name, EntryDate = j.EntryDate, Description = j.Description,
        ReferenceType = j.ReferenceType, ReferenceId = j.ReferenceId, IsPosted = j.IsPosted,
        TotalDebit = j.Lines.Sum(l => l.DebitAmount), TotalCredit = j.Lines.Sum(l => l.CreditAmount)
    };

    private static JournalEntryLineDto MapLineToDto(JournalEntryLine l) => new()
    {
        Id = l.Id, AccountId = l.AccountId, AccountCode = l.Account.Code, AccountName = l.Account.Name,
        DebitAmount = l.DebitAmount, CreditAmount = l.CreditAmount, Description = l.Description
    };

    private static BankAccountDto MapBankToDto(BankAccount b) => new()
    {
        Id = b.Id, Name = b.Name, BankName = b.BankName, AccountNumber = b.AccountNumber,
        AccountName = $"{b.Account.Code} - {b.Account.Name}", IsActive = b.IsActive
    };
}
