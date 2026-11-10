using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PharmacyERP.Application;
using PharmacyERP.Infrastructure;
using PharmacyERP.Infrastructure.Persistence;
using PharmacyERP.Infrastructure.Persistence.Seed;
using PharmacyERP.Infrastructure.Persistence.Interceptors;
using PharmacyERP.Infrastructure.Setup;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Backup;
using PharmacyERP.Application.Features.Licensing;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.ViewModels;
using PharmacyERP.WPF.ViewModels.Accounting;
using PharmacyERP.WPF.ViewModels.Branches;
using PharmacyERP.WPF.ViewModels.Insurance;
using PharmacyERP.WPF.ViewModels.Inventory;
using PharmacyERP.WPF.ViewModels.Prescriptions;
using PharmacyERP.WPF.ViewModels.Hr;
using PharmacyERP.WPF.ViewModels.Purchasing;
using PharmacyERP.WPF.ViewModels.Reports;
using PharmacyERP.WPF.ViewModels.Sales;
using PharmacyERP.WPF.ViewModels.Security;
using PharmacyERP.WPF.ViewModels.SystemManagement;
using PharmacyERP.WPF.Views;
using PharmacyERP.WPF.Views.Accounting;
using PharmacyERP.WPF.Views.Branches;
using PharmacyERP.WPF.Views.Hr;
using PharmacyERP.WPF.Views.Insurance;
using PharmacyERP.WPF.Views.Inventory;
using PharmacyERP.WPF.Views.Prescriptions;
using PharmacyERP.WPF.Views.Purchasing;
using PharmacyERP.WPF.Views.Reports;
using PharmacyERP.WPF.Views.Sales;
using PharmacyERP.WPF.Views.Security;
using PharmacyERP.WPF.Views.SystemManagement;
using Serilog;

namespace PharmacyERP.WPF;

/// <summary>
/// Composition root of the entire application. Builds a generic Host so we
/// get configuration binding, DI, and Serilog logging with the same
/// conventions ASP.NET Core uses — keeping the desktop app aligned with
/// modern .NET practices instead of a hand-rolled ServiceLocator.
/// </summary>
public partial class App : System.Windows.Application
{
    private IHost? _host;
    private IServiceScope? _appScope;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += (_, args) =>
        {
            if (args.Exception is not LicenseWriteDeniedException denied) return;
            MessageBox.Show(denied.Message, "الترخيص — وضع القراءة فقط", MessageBoxButton.OK, MessageBoxImage.Information);
            args.Handled = true;
        };
        var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmacyERP", "logs");
        Directory.CreateDirectory(logDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Debug()
            .WriteTo.File(
                path: Path.Combine(logDirectory, "log-.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30)
            .CreateLogger();

        var initialAdminPassword = new InitialAdminPasswordProvider();
        try
        {
            var setup = await PrepareConnectionAsync();
            if (setup is null) { Shutdown(); return; }
            initialAdminPassword = new InitialAdminPasswordProvider(setup.InitialPassword);
            setup = setup with { InitialPassword = null };
            _host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((_, config) =>
                {
                    config.SetBasePath(AppDomain.CurrentDomain.BaseDirectory);
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = setup.ConnectionString
                    });
                    config.AddEnvironmentVariables();
                })
                .UseSerilog()
                .ConfigureServices((context, services) =>
                {
                    services.AddApplicationServices();
                    services.AddInfrastructureServices(context.Configuration);
                    services.AddSingleton<IInitialAdminPasswordProvider>(initialAdminPassword);
                    services.AddSingleton<LicenseStatusViewModel>();

                    services.AddSingleton<ISessionService, SessionService>();
                    services.AddSingleton<INavigationService, NavigationService>();
                    services.AddSingleton<IDialogService, DialogService>();
                    services.AddSingleton<IReportExportService, ReportExportService>();
                    services.AddSingleton<IReceiptSettingsStore, ReceiptSettingsStore>();
                    services.AddSingleton<IReceiptPrinter, ThermalReceiptPrinter>();
                    services.AddSingleton<ProductLabelPrinter>();
                    services.AddTransient<PrintProductLabelsViewModel>();
                    services.AddTransient<PrintProductLabelsDialog>();

                    services.AddTransient<LoginViewModel>();
                    services.AddTransient<LoginView>();
                    services.AddTransient<MainShellViewModel>();
                    services.AddTransient<PharmacyBrandingViewModel>();
                    services.AddTransient<MainShellView>();
                    services.AddTransient<ReceiptSettingsViewModel>();
                    services.AddTransient<ReceiptSettingsView>();
                    services.AddTransient<InvoiceAiSettingsViewModel>();
                    services.AddTransient<InvoiceAiSettingsView>();

                    // Branches & Warehouses module
                    services.AddTransient<BranchesViewModel>();
                    services.AddTransient<BranchesView>();
                    services.AddTransient<BranchEditViewModel>();
                    services.AddTransient<BranchEditDialog>();
                    services.AddTransient<WarehouseEditViewModel>();
                    services.AddTransient<WarehouseEditDialog>();

                    // Users module
                    services.AddTransient<UsersViewModel>();
                    services.AddTransient<UsersView>();
                    services.AddTransient<UserEditViewModel>();
                    services.AddTransient<UserEditDialog>();
                    services.AddTransient<ResetPasswordViewModel>();
                    services.AddTransient<ResetPasswordDialog>();

                    // Roles & Permissions module
                    services.AddTransient<RolesViewModel>();
                    services.AddTransient<RolesView>();
                    services.AddTransient<RoleEditViewModel>();
                    services.AddTransient<RoleEditDialog>();

                    // Inventory module
                    services.AddTransient<ItemLookupsViewModel>();
                    services.AddTransient<ItemLookupsView>();
                    services.AddTransient<ItemsViewModel>();
                    services.AddTransient<ItemsView>();
                    services.AddTransient<ItemEditViewModel>();
                    services.AddTransient<ItemEditDialog>();
                    services.AddTransient<ItemBatchesViewModel>();
                    services.AddTransient<ItemBatchesDialog>();
                    services.AddTransient<ReceiveBatchViewModel>();
                    services.AddTransient<ReceiveBatchDialog>();
                    services.AddTransient<StockAdjustmentViewModel>();
                    services.AddTransient<StockAdjustmentDialog>();
                    services.AddTransient<StockOverviewViewModel>();
                    services.AddTransient<StockOverviewView>();

                    services.AddTransient<PurchasingWorkspaceViewModel>();
                    services.AddTransient<PurchasingWorkspaceView>();
                    services.AddTransient<InvoiceImageImportViewModel>();
                    services.AddTransient<InvoiceImageImportDialog>();

                    // Purchasing module (Suppliers, Purchase Orders, Goods Receipts, Purchase Invoices)
                    services.AddTransient<SuppliersViewModel>();
                    services.AddTransient<SuppliersView>();
                    services.AddTransient<SupplierEditViewModel>();
                    services.AddTransient<SupplierEditDialog>();
                    services.AddTransient<PurchaseOrdersViewModel>();
                    services.AddTransient<PurchaseOrdersView>();
                    services.AddTransient<PurchaseOrderEditViewModel>();
                    services.AddTransient<PurchaseOrderEditDialog>();
                    services.AddTransient<GoodsReceiptsViewModel>();
                    services.AddTransient<GoodsReceiptsView>();
                    services.AddTransient<GoodsReceiptEditViewModel>();
                    services.AddTransient<GoodsReceiptEditDialog>();
                    services.AddTransient<PurchaseInvoicesViewModel>();
                    services.AddTransient<PurchaseInvoicesView>();
                    services.AddTransient<PurchaseInvoiceEditViewModel>();
                    services.AddTransient<PurchaseInvoiceEditDialog>();
                    services.AddTransient<RecordPaymentViewModel>();
                    services.AddTransient<RecordPaymentDialog>();

                    // Sales module (POS, Customers, Sales Invoices, Returns)
                    services.AddTransient<POSViewModel>();
                    services.AddTransient<POSView>();
                    services.AddTransient<POSWindow>();
                    services.AddSingleton<ProductImageStore>();
                    services.AddTransient<CustomersViewModel>();
                    services.AddTransient<CustomersView>();
                    services.AddTransient<CustomerAccountViewModel>();
                    services.AddTransient<CustomerAccountDialog>();
                    services.AddTransient<CustomerEditViewModel>();
                    services.AddTransient<CustomerEditDialog>();
                    services.AddTransient<SalesInvoicesViewModel>();
                    services.AddTransient<SalesInvoicesView>();
                    services.AddTransient<SalesInvoiceDetailViewModel>();
                    services.AddTransient<SalesInvoiceDetailDialog>();

                    // Prescriptions module (Doctors, Prescriptions)
                    services.AddTransient<DoctorsViewModel>();
                    services.AddTransient<DoctorsView>();
                    services.AddTransient<DoctorEditViewModel>();
                    services.AddTransient<DoctorEditDialog>();
                    services.AddTransient<PrescriptionsViewModel>();
                    services.AddTransient<PrescriptionsView>();
                    services.AddTransient<PrescriptionEditViewModel>();
                    services.AddTransient<PrescriptionEditDialog>();
                    services.AddTransient<PrescriptionDetailViewModel>();
                    services.AddTransient<PrescriptionDetailDialog>();

                    // Insurance module (Companies, Policies, Claims)
                    services.AddTransient<InsuranceCompaniesViewModel>();
                    services.AddTransient<InsuranceCompaniesView>();
                    services.AddTransient<InsuranceCompanyEditViewModel>();
                    services.AddTransient<InsuranceCompanyEditDialog>();
                    services.AddTransient<InsurancePoliciesViewModel>();
                    services.AddTransient<InsurancePoliciesView>();
                    services.AddTransient<InsurancePolicyEditViewModel>();
                    services.AddTransient<InsurancePolicyEditDialog>();
                    services.AddTransient<InsuranceClaimsViewModel>();
                    services.AddTransient<InsuranceClaimsView>();
                    services.AddTransient<SubmitClaimViewModel>();
                    services.AddTransient<SubmitClaimDialog>();
                    services.AddTransient<ProcessClaimViewModel>();
                    services.AddTransient<ProcessClaimDialog>();

                    // Accounting module (Chart of Accounts, Journal Entries, Cash & Bank, Expenses, Receipts/Payments)
                    services.AddTransient<ChartOfAccountsViewModel>();
                    services.AddTransient<ChartOfAccountsView>();
                    services.AddTransient<ChartOfAccountEditViewModel>();
                    services.AddTransient<ChartOfAccountEditDialog>();
                    services.AddTransient<JournalEntriesViewModel>();
                    services.AddTransient<JournalEntriesView>();
                    services.AddTransient<JournalEntryDetailViewModel>();
                    services.AddTransient<JournalEntryDetailDialog>();
                    services.AddTransient<ManualJournalEntryViewModel>();
                    services.AddTransient<ManualJournalEntryDialog>();
                    services.AddTransient<CashAndBankViewModel>();
                    services.AddTransient<CashAndBankView>();
                    services.AddTransient<CashBoxEditViewModel>();
                    services.AddTransient<CashBoxEditDialog>();
                    services.AddTransient<BankAccountEditViewModel>();
                    services.AddTransient<BankAccountEditDialog>();
                    services.AddTransient<ExpenseCategoriesViewModel>();
                    services.AddTransient<ExpenseCategoriesView>();
                    services.AddTransient<ExpenseCategoryEditViewModel>();
                    services.AddTransient<ExpenseCategoryEditDialog>();
                    services.AddTransient<ExpensesViewModel>();
                    services.AddTransient<ExpensesView>();
                    services.AddTransient<ExpenseEditViewModel>();
                    services.AddTransient<ExpenseEditDialog>();
                    services.AddTransient<ReceiptsAndPaymentsViewModel>();
                    services.AddTransient<ReceiptsAndPaymentsView>();
                    services.AddTransient<ReceiptEditViewModel>();
                    services.AddTransient<ReceiptEditDialog>();
                    services.AddTransient<PaymentEditViewModel>();
                    services.AddTransient<PaymentEditDialog>();

                    // Reports & Dashboard module
                    services.AddTransient<DashboardViewModel>();
                    services.AddTransient<DashboardView>();
                    services.AddTransient<SalesSummaryReportViewModel>();
                    services.AddTransient<SalesSummaryReportView>();
                    services.AddTransient<InventoryMovementReportViewModel>();
                    services.AddTransient<InventoryMovementReportView>();
                    services.AddTransient<TrialBalanceViewModel>();
                    services.AddTransient<TrialBalanceView>();
                    services.AddTransient<IncomeStatementViewModel>();
                    services.AddTransient<IncomeStatementView>();

                    // HR module (Employees, Shifts, Attendance, Commissions, Payroll)
                    services.AddTransient<EmployeesViewModel>();
                    services.AddTransient<EmployeesView>();
                    services.AddTransient<EmployeeEditViewModel>();
                    services.AddTransient<EmployeeEditDialog>();
                    services.AddTransient<ShiftsViewModel>();
                    services.AddTransient<ShiftsView>();
                    services.AddTransient<ShiftEditViewModel>();
                    services.AddTransient<ShiftEditDialog>();
                    services.AddTransient<AttendanceViewModel>();
                    services.AddTransient<AttendanceView>();
                    services.AddTransient<AttendanceEditViewModel>();
                    services.AddTransient<AttendanceEditDialog>();
                    services.AddTransient<CommissionsViewModel>();
                    services.AddTransient<CommissionsView>();
                    services.AddTransient<CommissionCreateViewModel>();
                    services.AddTransient<CommissionCreateDialog>();
                    services.AddTransient<PayrollRunsViewModel>();
                    services.AddTransient<PayrollRunsView>();
                    services.AddTransient<GeneratePayrollRunViewModel>();
                    services.AddTransient<GeneratePayrollRunDialog>();
                    services.AddTransient<PayrollRunDetailViewModel>();
                    services.AddTransient<PayrollRunDetailDialog>();
                    services.AddTransient<MarkPayrollRunPaidViewModel>();
                    services.AddTransient<MarkPayrollRunPaidDialog>();

                    // System module (License, Backup & Restore, Update check)
                    services.AddTransient<LicenseViewModel>();
                    services.AddTransient<LicenseView>();
                    services.AddTransient<BackupViewModel>();
                    services.AddTransient<BackupView>();
                    services.AddTransient<UpdateCheckViewModel>();
                    services.AddTransient<UpdateCheckDialog>();
                })
                .Build();

            await _host.StartAsync();

            // A single DbContext-scoped IServiceScope is created once and kept alive for
            // the entire application lifetime (disposed in OnExit). WPF is not a per-request
            // framework like ASP.NET Core, so the usual "one scope per HTTP request" pattern
            // does not apply — instead every Singleton service that eventually needs a Scoped
            // dependency (IApplicationDbContext, and anything resolved through IServiceProvider
            // by NavigationService/DialogService/MainShellViewModel) must be resolved through
            // THIS scope rather than the host's root provider, or EF Core's scope validation
            // throws "Cannot resolve scoped service from root provider".
            _appScope = _host.Services.CreateScope();
            var scopedProvider = _appScope.ServiceProvider;

            // Ensure the database exists and is up to date. In production this step is
            // typically replaced by running migrations during deployment, but it keeps
            // first-run setup painless during development.
            var dbContext = scopedProvider.GetRequiredService<ApplicationDbContext>();
            var licenseRuntime = scopedProvider.GetRequiredService<LicenseRuntimeState>();
            using (licenseRuntime.BeginBootstrap())
            {
                await dbContext.Database.MigrateAsync();
                var seeder = scopedProvider.GetRequiredService<ApplicationDbSeeder>();
                await seeder.SeedAsync();
            }
            initialAdminPassword.Clear();

            // Resolving INavigationService for the first time through scopedProvider makes the
            // singleton instance permanently hold scopedProvider as its IServiceProvider, so every
            // Window/View it (and MainShellViewModel, transitively) later resolves shares this
            // same long-lived scope instead of the invalid root provider.
            var navigationService = scopedProvider.GetRequiredService<INavigationService>();
            navigationService.ShowLogin();
            ShutdownMode = ShutdownMode.OnLastWindowClose;
        }
        catch (Exception ex)
        {
            // Connection strings and database errors may contain credentials; log the type only.
            Log.Fatal("Application failed to start ({ErrorType})", ex.GetType().Name);
            MessageBox.Show(
                "تعذر بدء تشغيل التطبيق. الرجاء التأكد من إعدادات الاتصال بقاعدة البيانات والمحاولة مرة أخرى." +
                Environment.NewLine + Environment.NewLine + SetupDatabaseService.SafeFailureMessage(ex),
                "خطأ في بدء التشغيل", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
        finally { initialAdminPassword.Clear(); }
    }

    private async Task<StartupConnection?> PrepareConnectionAsync()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        var environmentConnection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        var environmentOverride = !string.IsNullOrWhiteSpace(environmentConnection);
        var store = new SetupConnectionStore();
        string? stored = null;
        try { stored = store.Load(); }
        catch (Exception) { /* The wizard can replace an unreadable, user-bound settings file. */ }
        var connection = environmentOverride ? environmentConnection : stored ?? configuration.GetConnectionString("DefaultConnection");
        var forceSetup = Environment.GetCommandLineArgs().Any(arg => string.Equals(arg, "--setup", StringComparison.OrdinalIgnoreCase));
        if (!forceSetup && !string.IsNullOrWhiteSpace(connection))
        {
            try
            {
                var status = await new SetupDatabaseService().CheckAsync(connection, DatabaseSetupMode.UseExisting);
                if (status.IsPharmacyDatabase && (status.HasAdministrator || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PHARMACYERP_INITIAL_ADMIN_PASSWORD"))))
                    return new(connection, null);
            }
            catch (Exception ex)
            {
                MessageBox.Show(SetupDatabaseService.SafeFailureMessage(ex) + "\nيمكنك تصحيح الاتصال في نافذة الإعداد.",
                    "إعداد الاتصال", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        var wizard = new InitialSetupWindow(store, connection, environmentOverride);
        if (wizard.ShowDialog() != true || string.IsNullOrWhiteSpace(wizard.ResultConnectionString)) return null;
        return new(wizard.ResultConnectionString, wizard.TakeInitialAdminPassword());
    }

    private sealed record StartupConnection(string ConnectionString, string? InitialPassword);

    protected override async void OnExit(ExitEventArgs e)
    {
        _appScope?.Dispose();

        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
