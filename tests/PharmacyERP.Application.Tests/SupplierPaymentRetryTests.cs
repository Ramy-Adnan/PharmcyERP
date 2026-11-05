using System.Reflection;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Accounting.DTOs;
using PharmacyERP.Application.Features.Purchasing;
using PharmacyERP.Application.Features.Purchasing.DTOs;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Domain.Entities;
using PharmacyERP.Infrastructure.Persistence;
using PharmacyERP.Infrastructure.Services;
using PharmacyERP.WPF.ViewModels.Purchasing;
using Xunit;

namespace PharmacyERP.Application.Tests;

public partial class PurchasingWorkspaceTests
{
    private async Task<PurchaseInvoice> PaymentInvoiceAsync(ApplicationDbContext db, int branchId, string number = "PI-RETRY")
    {
        var invoice = new PurchaseInvoice { Number = number, SupplierId = (await db.Suppliers.SingleAsync()).Id,
            BranchId = branchId, InvoiceDate = _clock.UtcNow, TotalAmount = 1000, SubTotal = 1000 };
        db.PurchaseInvoices.Add(invoice); await db.SaveChangesAsync(); return invoice;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SupplierPayment_RetryCannotPostTwiceOrReuseTheIdentityForAnotherAmountOrInvoice(bool relational)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using ApplicationDbContext db = relational ? RelationalDb(connection) : TestDb.CreateContext(_clock);
        if (relational) await db.Database.EnsureCreatedAsync();
        var fixture = await SeedAsync(db); var invoice = await PaymentInvoiceAsync(db, fixture.BranchId);
        var other = await PaymentInvoiceAsync(db, fixture.BranchId, "PI-OTHER");
        var service = Service(db); var request = new RecordPaymentDto { PurchaseInvoiceId = invoice.Id, Amount = 100 };
        (await service.RecordPaymentAsync(request)).Succeeded.Should().BeTrue();
        (await service.RecordPaymentAsync(request)).Succeeded.Should().BeTrue();
        (await db.PurchaseInvoices.AsNoTracking().SingleAsync(i => i.Id == invoice.Id)).AmountPaid.Should().Be(100);
        (await db.JournalEntries.CountAsync()).Should().Be(1);
        var mismatch = new RecordPaymentDto { RequestId = request.RequestId, PurchaseInvoiceId = invoice.Id, Amount = 200 };
        (await service.RecordPaymentAsync(mismatch)).Succeeded.Should().BeFalse();
        mismatch.Amount = 100; mismatch.PurchaseInvoiceId = other.Id;
        (await service.RecordPaymentAsync(mismatch)).Succeeded.Should().BeFalse();
        (await db.PurchaseInvoices.AsNoTracking().SingleAsync(i => i.Id == other.Id)).AmountPaid.Should().Be(0);
        (await service.RecordPaymentAsync(new() { PurchaseInvoiceId = invoice.Id, Amount = 100 })).Succeeded.Should().BeTrue();
        (await db.PurchaseInvoices.AsNoTracking().SingleAsync(i => i.Id == invoice.Id)).AmountPaid.Should().Be(200);
        (await db.JournalEntries.CountAsync()).Should().Be(2);
        (await db.JournalEntryLines.ToListAsync()).Sum(l => l.DebitAmount).Should().Be(200);
    }

    [Fact]
    public async Task SupplierPayment_ReadsFreshInvoiceBalanceInsteadOfAStaleTrackedInvoice()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = RelationalDb(connection); await db.Database.EnsureCreatedAsync();
        var fixture = await SeedAsync(db); var invoice = await PaymentInvoiceAsync(db, fixture.BranchId);
        await db.PurchaseInvoices.Where(i => i.Id == invoice.Id).ExecuteUpdateAsync(s => s.SetProperty(i => i.AmountPaid, 300));
        var result = await Service(db).RecordPaymentAsync(new() { PurchaseInvoiceId = invoice.Id, Amount = 200 });
        result.Succeeded.Should().BeTrue();
        (await db.PurchaseInvoices.AsNoTracking().SingleAsync()).AmountPaid.Should().Be(500);
    }

    public class LostSupplierPaymentResponse : DispatchProxy
    {
        public IPurchasingService Target = null!;
        public bool LoseNextResponse = true;
        public readonly List<(Guid Id, int InvoiceId, decimal Amount)> Attempts = new();
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var result = targetMethod!.Invoke(Target, args);
            if (targetMethod.Name != nameof(IPurchasingService.RecordPaymentAsync)) return result;
            var dto = (RecordPaymentDto)args![0]!; Attempts.Add((dto.RequestId, dto.PurchaseInvoiceId, dto.Amount));
            if (!LoseNextResponse) return result;
            LoseNextResponse = false; return LoseAsync((Task<Result>)result!);
        }
        private static async Task<Result> LoseAsync(Task<Result> response)
        { await response; throw new IOException("Payment saved, response lost"); }
    }

    [Fact]
    public async Task SupplierWorkspace_LostPaymentResponseKeepsTheOriginalRequestUntilVerified()
    {
        await using var db = TestDb.CreateContext(_clock); var fixture = await SeedAsync(db);
        var invoice = await PaymentInvoiceAsync(db, fixture.BranchId);
        var proxy = DispatchProxy.Create<IPurchasingService, LostSupplierPaymentResponse>();
        var interception = (LostSupplierPaymentResponse)proxy; interception.Target = Service(db);
        var vm = new PurchasingWorkspaceViewModel(proxy, new InventoryService(db, _clock),
            new BranchService(db, new PermissiveLicenseService()), new FakeCurrentUserService());
        await vm.InitializeAsync(); await vm.ResumeInvoiceAsync(invoice.Id); vm.PaymentAmount = 100;
        await vm.PayAsync(); vm.HasPendingPayment.Should().BeTrue(); vm.ErrorMessage.Should().Contain("التحقق");
        vm.PaymentAmount = 300; vm.PaymentAmount.Should().Be(100); vm.CanEditPaymentAmount.Should().BeFalse();
        await vm.StartNewAsync(); vm.Step.Should().Be(PurchaseStep.Payment);
        await vm.PayAsync(); vm.ErrorMessage.Should().BeEmpty(); vm.HasPendingPayment.Should().BeFalse();
        vm.PaymentAmount.Should().Be(0); vm.Invoice!.AmountPaid.Should().Be(100);
        (await db.JournalEntries.CountAsync()).Should().Be(1);
        interception.Attempts.Should().HaveCount(2);
        interception.Attempts[0].Should().Be(interception.Attempts[1]);
        vm.PaymentAmount = 100; await vm.PayAsync(); vm.Invoice.AmountPaid.Should().Be(200);
        (await db.JournalEntries.CountAsync()).Should().Be(2);
        interception.Attempts[2].Id.Should().NotBe(interception.Attempts[0].Id);
    }

    [Fact]
    public async Task SupplierPaymentDialog_LostResponseCannotChangeThePaymentAndClosesAfterBusyEnds()
    {
        await using var db = TestDb.CreateContext(_clock); var fixture = await SeedAsync(db);
        var invoice = await PaymentInvoiceAsync(db, fixture.BranchId);
        var proxy = DispatchProxy.Create<IPurchasingService, LostSupplierPaymentResponse>();
        var interception = (LostSupplierPaymentResponse)proxy; interception.Target = Service(db);
        var vm = new RecordPaymentViewModel(proxy); vm.Load(invoice.Id, invoice.Number, 1000); vm.Amount = 100;
        var closed = 0; vm.RequestClose += () => { vm.IsBusy.Should().BeFalse(); closed++; };
        await vm.SaveAsync(); vm.HasPendingPayment.Should().BeTrue(); vm.SavedSuccessfully.Should().BeFalse();
        vm.Amount = 400; vm.Amount.Should().Be(100);
        await vm.SaveAsync(); vm.HasPendingPayment.Should().BeFalse(); vm.SavedSuccessfully.Should().BeTrue(); closed.Should().Be(1);
        (await db.PurchaseInvoices.SingleAsync()).AmountPaid.Should().Be(100); (await db.JournalEntries.CountAsync()).Should().Be(1);
        await vm.SaveAsync(); closed.Should().Be(1); interception.Attempts.Should().HaveCount(2);
    }

    [Fact]
    public void SupplierPaymentJournalIdentity_PreservesAllGuidBitsWithinTheExistingColumn()
    {
        var first = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        var second = Guid.Parse("01234567-89ab-cdef-0123-456789abcdee");
        var number = PurchaseInvoicePaymentPostingRequest.JournalNumberFor(first);
        number.Length.Should().Be(29); number.Should().MatchRegex("^PP-[A-Z2-7]{26}$");
        PurchaseInvoicePaymentPostingRequest.JournalNumberFor(first).Should().Be(number);
        PurchaseInvoicePaymentPostingRequest.JournalNumberFor(second).Should().NotBe(number);
    }
}
