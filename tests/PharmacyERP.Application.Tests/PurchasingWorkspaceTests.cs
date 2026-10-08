using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Purchasing.DTOs;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Domain.Entities;
using PharmacyERP.Domain.Enums;
using PharmacyERP.Infrastructure.Persistence;
using PharmacyERP.Infrastructure.Persistence.Interceptors;
using PharmacyERP.Infrastructure.Services;
using PharmacyERP.WPF.ViewModels.Purchasing;
using Xunit;

namespace PharmacyERP.Application.Tests;

public class PurchasingWorkspaceTests
{
    private readonly FakeDateTime _clock = new();
    private PurchasingService Service(ApplicationDbContext db) => new(db, new InventoryService(db, _clock), new AccountingService(db, _clock), _clock);
    private PurchasingWorkspaceViewModel Workspace(ApplicationDbContext db, ICurrentUserService? user = null) =>
        new(Service(db), new InventoryService(db, _clock), new BranchService(db, new PermissiveLicenseService()), user ?? new FakeCurrentUserService());
    private async Task<TestFixture> SeedAsync(ApplicationDbContext db)
    {
        var f = await TestDb.SeedBaselineAsync(db);
        db.Suppliers.Add(new Supplier { Code = "S1", Name = "شركة الأدوية", IsActive = true, PaymentTermsDays = 30 });
        foreach (var code in new[] { "1110", "1130", "1140", "2100" })
            db.ChartOfAccounts.Add(new ChartOfAccount { Code = code, Name = code, IsActive = true });
        await db.SaveChangesAsync(); return f;
    }
    private GoodsReceiptUpsertDto Receipt(TestFixture f, int supplierId) => new()
    {
        SupplierId = supplierId, BranchId = f.BranchId, WarehouseId = f.WarehouseId,
        PurchaseType = PurchasePricingType.ByHand, ReceiptDate = _clock.UtcNow.Date,
        Lines = new() { new() { ItemId = f.ItemId, BatchNumber = "B1", ExpiryDate = _clock.UtcNow.AddMonths(3), QuantityReceived = 2, UnitCost = 1000 } }
    };
    private void AddLine(PurchasingWorkspaceViewModel vm, TestFixture f)
    {
        vm.Receipt.ReceiptDate = _clock.UtcNow.Date; vm.Receipt.WarehouseId = f.WarehouseId;
        vm.Receipt.PurchaseType = PurchasePricingType.ByHand; vm.Receipt.AddLineCommand.Execute(null);
        var line = vm.Receipt.Lines.Single(); line.ItemId = f.ItemId; line.QuantityReceived = 2;
        line.UnitCost = 1000; line.SalePrice = 1350; line.BatchNumber = "B1"; line.ExpiryDate = _clock.UtcNow.AddMonths(3);
    }

    [Fact]
    public async Task FourSteps_CarrySupplierStockPricesAndInvoice_ThenTrackPartialAndFullPayment()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db);
        var vm = Workspace(db); await vm.InitializeAsync();
        vm.Step.Should().Be(PurchaseStep.Supplier); await vm.ContinueAsync(); vm.ErrorMessage.Should().NotBeEmpty();
        vm.SelectedSupplierId = (await db.Suppliers.SingleAsync()).Id;
        await vm.ContinueAsync(); vm.Step.Should().Be(PurchaseStep.Goods);
        AddLine(vm, f); await vm.SaveDraftAsync(); var receiptId = vm.ReceiptId;
        receiptId.Should().NotBeNull(); (await db.Batches.CountAsync()).Should().Be(0);
        await vm.SaveDraftAsync(); (await db.GoodsReceiptNotes.CountAsync()).Should().Be(1);
        await vm.ContinueAsync(); vm.ErrorMessage.Should().BeEmpty(); vm.Step.Should().Be(PurchaseStep.Invoice);
        var batch = await db.Batches.SingleAsync(); batch.QuantityOnHand.Should().Be(2); batch.SalePriceOverride.Should().Be(1350);
        vm.InvoiceEditor.SupplierId.Should().Be(vm.SelectedSupplierId);
        vm.InvoiceEditor.GoodsReceiptNoteId.Should().Be(receiptId); vm.InvoiceEditor.PurchaseType.Should().Be(PurchasePricingType.ByHand);
        vm.InvoiceEditor.Lines.Single().UnitCost.Should().Be(1000); vm.InvoiceEditor.DueDate.Should().Be(vm.InvoiceEditor.InvoiceDate.AddDays(30));
        await vm.ContinueAsync(); vm.ErrorMessage.Should().BeEmpty(); vm.Step.Should().Be(PurchaseStep.Payment);
        vm.Invoice!.TotalAmount.Should().Be(2000); vm.Invoice.AmountDue.Should().Be(2000);
        vm.PaymentAmount = 1000; await vm.PayAsync(); vm.Invoice.AmountPaid.Should().Be(1000); vm.Invoice.AmountDue.Should().Be(1000);
        vm.PaymentStatus.Should().Be("مسددة جزئياً"); vm.PaymentAmount.Should().Be(0);
        vm.PaymentAmount = 1500; await vm.PayAsync(); vm.ErrorMessage.Should().NotBeEmpty(); vm.Invoice.AmountDue.Should().Be(1000);
        vm.PayInFullCommand.Execute(null); await vm.PayAsync(); vm.ErrorMessage.Should().BeEmpty();
        vm.Invoice.AmountDue.Should().Be(0); vm.PaymentStatus.Should().Be("مسددة بالكامل");
        (await db.Batches.CountAsync()).Should().Be(1); (await db.PurchaseInvoices.CountAsync()).Should().Be(1);
        (await db.JournalEntries.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task ResumeDraftAndPostedReceipt_ReusesTheDocuments_AndAllowsCreditToBePaidLater()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db); var supplierId = (await db.Suppliers.SingleAsync()).Id;
        var service = Service(db); var draft = await service.CreateGoodsReceiptAsync(Receipt(f, supplierId));
        var vm = Workspace(db); await vm.InitializeAsync(); await vm.ResumeReceiptAsync(draft.Value!.Id);
        vm.Step.Should().Be(PurchaseStep.Goods); vm.SelectedSupplierId.Should().Be(supplierId);
        await vm.ContinueAsync(); await vm.ContinueAsync(); vm.Step.Should().Be(PurchaseStep.Payment);
        var invoiceId = vm.Invoice!.Id; await vm.StartNewAsync();
        (await db.PurchaseInvoices.SingleAsync()).AmountDue.Should().Be(2000);
        var reopen = Workspace(db); await reopen.InitializeAsync(); await reopen.ResumeReceiptAsync(draft.Value.Id);
        reopen.Step.Should().Be(PurchaseStep.Payment); reopen.Invoice!.Id.Should().Be(invoiceId);
        reopen.PaymentAmount = 500; await reopen.PayAsync(); reopen.Invoice.AmountDue.Should().Be(1500);
        (await db.Batches.CountAsync()).Should().Be(1); (await db.GoodsReceiptNotes.CountAsync()).Should().Be(1);
        (await db.PurchaseInvoices.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task FailedValidation_StaysOnTheStepWithoutCreatingStock_AndInlineSupplierIsSelected()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await TestDb.SeedBaselineAsync(db);
        var vm = Workspace(db); await vm.InitializeAsync(); vm.Suppliers.Should().BeEmpty();
        vm.SupplierEditor.Code = "NEW"; vm.SupplierEditor.Name = "مورد جديد"; await vm.SaveSupplierAsync();
        vm.SelectedSupplierId.Should().Be((await db.Suppliers.SingleAsync()).Id); vm.Message.Should().NotBeEmpty();
        await vm.ContinueAsync(); await vm.ContinueAsync(); vm.Step.Should().Be(PurchaseStep.Goods);
        vm.ErrorMessage.Should().NotBeEmpty(); (await db.GoodsReceiptNotes.CountAsync()).Should().Be(0);
        AddLine(vm, f); vm.Receipt.Lines[0].QuantityReceived = 0;
        await vm.ContinueAsync(); vm.Step.Should().Be(PurchaseStep.Goods); (await db.Batches.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task InvoiceRetry_ReturnsTheSavedInvoiceWithoutPostingAnotherLiability()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db); var service = Service(db);
        var receipt = await service.CreateGoodsReceiptAsync(Receipt(f, (await db.Suppliers.SingleAsync()).Id));
        (await service.PostGoodsReceiptAsync(receipt.Value!.Id, null)).Succeeded.Should().BeTrue();
        var request = (await service.PrefillInvoiceFromGoodsReceiptAsync(receipt.Value.Id))!;
        var first = await service.CreatePurchaseInvoiceAsync(request); var second = await service.CreatePurchaseInvoiceAsync(request);
        first.Succeeded.Should().BeTrue(); second.Succeeded.Should().BeTrue(); second.Value!.Id.Should().Be(first.Value!.Id);
        (await db.PurchaseInvoices.CountAsync()).Should().Be(1); (await db.JournalEntries.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task InvoiceStep_UpdatesTheDisplayedTotalAndPostsTheSameAmountIncludingLineAndInvoiceDiscounts()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db); var service = Service(db);
        var draft = await service.CreateGoodsReceiptAsync(Receipt(f, (await db.Suppliers.SingleAsync()).Id));
        (await service.PostGoodsReceiptAsync(draft.Value!.Id, null)).Succeeded.Should().BeTrue();
        var vm = Workspace(db); await vm.InitializeAsync(); await vm.ResumeReceiptAsync(draft.Value.Id);
        vm.InvoiceEditor.GrandTotal.Should().Be(2000);
        var notifications = new List<string?>();
        vm.InvoiceEditor.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        var line = vm.InvoiceEditor.Lines.Single(); line.TaxRatePercent = 10; line.DiscountAmount = 100;
        notifications.Should().Contain(nameof(vm.InvoiceEditor.GrandTotal));
        vm.InvoiceEditor.DiscountAmount = 50; vm.InvoiceEditor.GrandTotal.Should().Be(2050);
        await vm.ContinueAsync(); vm.ErrorMessage.Should().BeEmpty(); vm.Invoice!.TotalAmount.Should().Be(2050);
        vm.Invoice.DiscountAmount.Should().Be(150); vm.Invoice.AmountDue.Should().Be(2050);
        (await db.JournalEntryLines.SumAsync(l => l.DebitAmount)).Should().Be(2050);
        (await db.JournalEntryLines.SumAsync(l => l.CreditAmount)).Should().Be(2050);
    }

    [Fact]
    public async Task InvoiceStep_SumsRoundedLineTotalsSoTheVisibleAndStoredAmountsAgree()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db); var service = Service(db);
        var request = Receipt(f, (await db.Suppliers.SingleAsync()).Id); request.Lines[0].QuantityReceived = 1; request.Lines[0].UnitCost = 0.05m;
        request.Lines.Add(new() { ItemId = f.ItemId, BatchNumber = "B2", ExpiryDate = _clock.UtcNow.AddMonths(3), QuantityReceived = 1, UnitCost = 0.05m });
        var draft = await service.CreateGoodsReceiptAsync(request);
        (await service.PostGoodsReceiptAsync(draft.Value!.Id, null)).Succeeded.Should().BeTrue();
        var vm = Workspace(db); await vm.InitializeAsync(); await vm.ResumeReceiptAsync(draft.Value.Id);
        foreach (var line in vm.InvoiceEditor.Lines) line.TaxRatePercent = 10;
        vm.InvoiceEditor.GrandTotal.Should().Be(0.12m);
        await vm.ContinueAsync(); vm.ErrorMessage.Should().BeEmpty(); vm.Invoice!.TotalAmount.Should().Be(0.12m);
        vm.Invoice.TaxAmount.Should().Be(0.02m);
        (await db.JournalEntryLines.SumAsync(l => l.DebitAmount)).Should().Be(0.12m);
        (await db.JournalEntryLines.SumAsync(l => l.CreditAmount)).Should().Be(0.12m);
    }

    [Fact]
    public async Task InvoicePermissionOnly_CanResumeAndPay_ButCannotReceiveStockOrCreateSuppliers()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db); var service = Service(db);
        var invoice = await service.CreatePurchaseInvoiceAsync(new() { SupplierId = (await db.Suppliers.SingleAsync()).Id,
            BranchId = f.BranchId, Lines = new() { new() { ItemId = f.ItemId, Quantity = 2, UnitCost = 1000 } } });
        var vm = Workspace(db, new InvoicesOnlyUser()); await vm.InitializeAsync();
        await vm.ContinueAsync(); vm.Step.Should().Be(PurchaseStep.Supplier); vm.ErrorMessage.Should().Contain("صلاحية");
        vm.SupplierEditor.Code = "DENIED"; vm.SupplierEditor.Name = "DENIED"; await vm.SaveSupplierAsync();
        (await db.Suppliers.CountAsync()).Should().Be(1);
        await vm.ResumeInvoiceAsync(invoice.Value!.Id); vm.Step.Should().Be(PurchaseStep.Payment);
        vm.PaymentAmount = 1000; await vm.PayAsync(); vm.Invoice!.AmountDue.Should().Be(1000);
        (await db.Batches.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RelationalReceiptFailure_RollsBackEarlierLinesAndCanBeRetriedOnceCorrected()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = RelationalDb(connection); await db.Database.EnsureCreatedAsync(); var f = await SeedAsync(db);
        var request = Receipt(f, (await db.Suppliers.SingleAsync()).Id); request.ReceiptDate = _clock.UtcNow.AddDays(-2).Date;
        request.Lines.Add(new() { ItemId = f.ItemId, BatchNumber = "EXPIRED", ExpiryDate = _clock.UtcNow.AddDays(-1).Date, QuantityReceived = 1, UnitCost = 1000 });
        var service = Service(db); var draft = await service.CreateGoodsReceiptAsync(request);
        (await service.PostGoodsReceiptAsync(draft.Value!.Id, null)).Succeeded.Should().BeFalse();
        (await db.Batches.CountAsync()).Should().Be(0); (await db.StockTransactions.CountAsync()).Should().Be(0);
        (await db.GoodsReceiptNotes.SingleAsync()).Status.Should().Be(GoodsReceiptStatus.Draft);
        var edit = (await service.GetGoodsReceiptForEditAsync(draft.Value.Id))!; edit.Lines[1].ExpiryDate = _clock.UtcNow.AddMonths(3);
        (await service.UpdateGoodsReceiptAsync(edit)).Succeeded.Should().BeTrue();
        (await service.PostGoodsReceiptAsync(draft.Value.Id, null)).Succeeded.Should().BeTrue();
        (await db.Batches.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task RelationalInvoiceFailure_RollsBackTheInvoiceAndLeavesThePostedStockAvailable()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = RelationalDb(connection); await db.Database.EnsureCreatedAsync(); var f = await SeedAsync(db);
        var service = Service(db); var draft = await service.CreateGoodsReceiptAsync(Receipt(f, (await db.Suppliers.SingleAsync()).Id));
        (await service.PostGoodsReceiptAsync(draft.Value!.Id, null)).Succeeded.Should().BeTrue();
        db.ChartOfAccounts.Remove(await db.ChartOfAccounts.SingleAsync(a => a.Code == "2100")); await db.SaveChangesAsync();
        var request = (await service.PrefillInvoiceFromGoodsReceiptAsync(draft.Value.Id))!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreatePurchaseInvoiceAsync(request));
        (await db.PurchaseInvoices.CountAsync()).Should().Be(0); (await db.JournalEntries.CountAsync()).Should().Be(0);
        (await db.Batches.SingleAsync()).QuantityOnHand.Should().Be(2);
    }

    [Fact]
    public async Task RelationalPaymentFailure_DoesNotIncreaseAmountPaidOrWriteAnEntry()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = RelationalDb(connection); await db.Database.EnsureCreatedAsync(); var f = await SeedAsync(db);
        var service = Service(db); var invoice = await service.CreatePurchaseInvoiceAsync(new() { SupplierId = (await db.Suppliers.SingleAsync()).Id,
            BranchId = f.BranchId, Lines = new() { new() { ItemId = f.ItemId, Quantity = 2, UnitCost = 1000 } } });
        db.ChartOfAccounts.Remove(await db.ChartOfAccounts.SingleAsync(a => a.Code == "1110")); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordPaymentAsync(new() { PurchaseInvoiceId = invoice.Value!.Id, Amount = 500 }));
        (await db.PurchaseInvoices.SingleAsync()).AmountPaid.Should().Be(0); (await db.JournalEntries.CountAsync()).Should().Be(1);
    }
    private SqliteTestDb RelationalDb(SqliteConnection connection) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options,
        new AuditableEntitySaveChangesInterceptor(new FakeCurrentUserService(), _clock));
    private sealed class InvoicesOnlyUser : ICurrentUserService
    {
        public int? UserId => 1; public string? UserName => "accountant"; public int? CurrentBranchId => 1;
        public IReadOnlyCollection<string> Permissions => new[] { "Purchasing.ManageInvoices" };
        public bool HasPermission(string code) => Permissions.Contains(code);
        public void SetSession(int id, string name, int branchId, IEnumerable<string> permissions) { }
        public void ClearSession() { }
    }
}
