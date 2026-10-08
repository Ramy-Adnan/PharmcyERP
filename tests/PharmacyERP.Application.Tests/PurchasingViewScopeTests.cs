using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Accounting;
using PharmacyERP.Application.Features.Branches;
using PharmacyERP.Application.Features.Inventory;
using PharmacyERP.Application.Features.Purchasing;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Domain.Entities;
using PharmacyERP.Infrastructure.Persistence;
using PharmacyERP.Infrastructure.Persistence.Interceptors;
using PharmacyERP.Infrastructure.Services;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.ViewModels.Purchasing;
using Xunit;

namespace PharmacyERP.Application.Tests;

public class PurchasingViewScopeTests
{
    // Keep real relational queries pending so the EF concurrency detector is
    // exercised. InMemory queries complete synchronously and hide this defect.
    private sealed class PendingReads : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly int _expected;
        private int _count;
        public bool Enabled { get; set; }
        public PendingReads(int expected) => _expected = expected;
        public Task Started => _started.Task;
        public void Release() => _release.TrySetResult();
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Enabled)
            {
                if (Interlocked.Increment(ref _count) >= _expected) _started.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "pharmacy-view-scopes-" + Guid.NewGuid().ToString("N"));
        public ServiceProvider Provider { get; }
        public PendingReads Reads { get; }
        public Fixture(int expected)
        {
            Directory.CreateDirectory(_directory);
            Reads = new(expected);
            var clock = new FakeDateTime();
            var user = new FakeCurrentUserService(); user.ClearSession();
            var services = new ServiceCollection();
            services.AddSingleton<IDateTime>(clock);
            services.AddSingleton<ICurrentUserService>(user);
            services.AddSingleton(new AuditableEntitySaveChangesInterceptor(user, clock));
            services.AddScoped<ApplicationDbContext>(sp => new SqliteTestDb(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseSqlite($"Data Source={Path.Combine(_directory, "pharmacy.sqlite")};Pooling=False")
                    .AddInterceptors(Reads).Options,
                sp.GetRequiredService<AuditableEntitySaveChangesInterceptor>()));
            services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
            services.AddScoped<IInventoryService, InventoryService>();
            services.AddScoped<IAccountingService, AccountingService>();
            services.AddScoped<IPurchasingService, PurchasingService>();
            services.AddScoped<IBranchService>(sp => new BranchService(sp.GetRequiredService<ApplicationDbContext>(), new PermissiveLicenseService()));
            services.AddScoped<PurchasingWorkspaceViewModel>();
            Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }
        public async Task SeedAsync()
        {
            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureCreatedAsync(); await TestDb.SeedBaselineAsync(db);
            db.Suppliers.Add(new Supplier { Code = "S1", Name = "مورد", IsActive = true });
            await db.SaveChangesAsync(); Reads.Enabled = true;
        }
        public async ValueTask DisposeAsync()
        {
            Reads.Release(); await Provider.DisposeAsync();
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task WizardAndFourHistoryPages_CanLoadTogether_WhileAnotherScreenIsStillLoading()
    {
        await using var fixture = new Fixture(expected: 6); await fixture.SeedAsync();
        var factory = fixture.Provider.GetRequiredService<IServiceScopeFactory>();
        using var previousPage = new ViewDataScope(factory);
        using var wizard = new ViewDataScope(factory);
        using var suppliers = new ViewDataScope(factory);
        using var orders = new ViewDataScope(factory);
        using var receipts = new ViewDataScope(factory);
        using var invoices = new ViewDataScope(factory);
        var scopes = new[] { previousPage, wizard, suppliers, orders, receipts, invoices };
        var contexts = scopes.Select(s => s.Services.GetRequiredService<ApplicationDbContext>()).ToArray();
        contexts.Distinct().Should().HaveCount(6);
        // All services within a page must retain the same context for atomic posting.
        foreach (var scope in scopes)
            scope.Services.GetRequiredService<IApplicationDbContext>().Should().BeSameAs(scope.Services.GetRequiredService<ApplicationDbContext>());
        var vm = wizard.Services.GetRequiredService<PurchasingWorkspaceViewModel>();
        var tasks = new Task[] {
            previousPage.Services.GetRequiredService<IPurchasingService>().GetSuppliersAsync(),
            vm.InitializeAsync(),
            suppliers.Services.GetRequiredService<IPurchasingService>().GetSuppliersAsync(),
            orders.Services.GetRequiredService<IPurchasingService>().GetPurchaseOrdersAsync(),
            receipts.Services.GetRequiredService<IPurchasingService>().GetGoodsReceiptNotesAsync(),
            invoices.Services.GetRequiredService<IPurchasingService>().GetPurchaseInvoicesAsync()
        };
        try
        {
            await fixture.Reads.Started.WaitAsync(TimeSpan.FromSeconds(10));
            vm.IsBusy.Should().BeTrue(); tasks.Should().OnlyContain(t => !t.IsCompleted);
        }
        finally { fixture.Reads.Release(); await Task.WhenAll(tasks); }
        vm.ErrorMessage.Should().BeEmpty(); vm.IsBusy.Should().BeFalse();
        vm.Suppliers.Should().ContainSingle(); vm.Receipt.AvailableItems.Should().ContainSingle();
        (await (Task<List<PharmacyERP.Application.Features.Purchasing.DTOs.SupplierDto>>)tasks[2]).Should().ContainSingle();
    }

    [Fact]
    public async Task PendingQueryAgainstASharedContext_ReproducesTheReportedConcurrencyException()
    {
        await using var fixture = new Fixture(expected: 1); await fixture.SeedAsync();
        using var page = new ViewDataScope(fixture.Provider.GetRequiredService<IServiceScopeFactory>());
        var service = page.Services.GetRequiredService<IPurchasingService>();
        var loading = service.GetSuppliersAsync();
        try
        {
            await fixture.Reads.Started.WaitAsync(TimeSpan.FromSeconds(10));
            Func<Task> secondQuery = () => service.GetGoodsReceiptNotesAsync();
            await secondQuery.Should().ThrowAsync<InvalidOperationException>().WithMessage("*second operation*");
        }
        finally { fixture.Reads.Release(); await loading; }
    }

    [Fact]
    public async Task DisposingOneScreen_ReleasesItsContext_AndLeavesAnotherScreenUsable()
    {
        await using var fixture = new Fixture(expected: 1); await fixture.SeedAsync(); fixture.Reads.Release();
        var factory = fixture.Provider.GetRequiredService<IServiceScopeFactory>();
        var closed = new ViewDataScope(factory);
        using var open = new ViewDataScope(factory);
        var oldContext = closed.Services.GetRequiredService<ApplicationDbContext>(); closed.Dispose();
        Func<Task> oldQuery = () => oldContext.Suppliers.ToListAsync();
        await oldQuery.Should().ThrowAsync<ObjectDisposedException>();
        (await open.Services.GetRequiredService<IPurchasingService>().GetSuppliersAsync()).Should().ContainSingle();
    }
}
