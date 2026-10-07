using Microsoft.EntityFrameworkCore;
using PharmacyERP.Domain.Entities;
using System.Collections.Generic;

namespace PharmacyERP.Application.Common.Interfaces;

/// <summary>
/// Application-layer abstraction over the EF Core DbContext. The Application
/// layer depends only on this interface, never on Infrastructure, which keeps
/// Clean Architecture's dependency rule intact and makes use cases testable
/// with an in-memory provider.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<Branch> Branches { get; }
    DbSet<Warehouse> Warehouses { get; }
    DbSet<UserBranch> UserBranches { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<LoginHistory> LoginHistories { get; }

    DbSet<ItemCategory> ItemCategories { get; }
    DbSet<UnitOfMeasure> UnitsOfMeasure { get; }
    DbSet<Manufacturer> Manufacturers { get; }
    DbSet<Item> Items { get; }
    DbSet<Batch> Batches { get; }
    DbSet<StockTransaction> StockTransactions { get; }

    DbSet<Supplier> Suppliers { get; }
    DbSet<PurchaseOrder> PurchaseOrders { get; }
    DbSet<PurchaseOrderItem> PurchaseOrderItems { get; }
    DbSet<GoodsReceiptNote> GoodsReceiptNotes { get; }
    DbSet<GoodsReceiptItem> GoodsReceiptItems { get; }
    DbSet<PurchaseInvoice> PurchaseInvoices { get; }
    DbSet<PurchaseInvoiceItem> PurchaseInvoiceItems { get; }

    DbSet<Customer> Customers { get; }
    DbSet<SalesInvoice> SalesInvoices { get; }
    DbSet<SalesInvoiceItem> SalesInvoiceItems { get; }
    DbSet<SalesInvoiceItemBatch> SalesInvoiceItemBatches { get; }
    DbSet<SalesReturn> SalesReturns { get; }
    DbSet<SalesReturnItem> SalesReturnItems { get; }

    DbSet<Doctor> Doctors { get; }
    DbSet<Prescription> Prescriptions { get; }
    DbSet<PrescriptionItem> PrescriptionItems { get; }
    DbSet<InsuranceCompany> InsuranceCompanies { get; }
    DbSet<InsurancePolicy> InsurancePolicies { get; }
    DbSet<InsuranceClaim> InsuranceClaims { get; }

    DbSet<Employee> Employees { get; }
    DbSet<Shift> Shifts { get; }
    DbSet<Attendance> Attendances { get; }
    DbSet<Commission> Commissions { get; }
    DbSet<PayrollRun> PayrollRuns { get; }
    DbSet<PayrollRunLine> PayrollRunLines { get; }

    DbSet<ChartOfAccount> ChartOfAccounts { get; }
    DbSet<JournalEntry> JournalEntries { get; }
    DbSet<JournalEntryLine> JournalEntryLines { get; }
    DbSet<CashBox> CashBoxes { get; }
    DbSet<BankAccount> BankAccounts { get; }
    DbSet<ExpenseCategory> ExpenseCategories { get; }
    DbSet<Expense> Expenses { get; }
    DbSet<Receipt> Receipts { get; }
    DbSet<Payment> Payments { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}