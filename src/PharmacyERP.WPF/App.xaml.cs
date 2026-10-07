using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PharmacyERP.Application;
using PharmacyERP.Infrastructure;
using PharmacyERP.Infrastructure.Persistence;
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

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Debug()
            .WriteTo.File(
                path: Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "log-.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30)
            .CreateLogger();

        try
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((_, config) =>
                {
                    config.SetBasePath(AppDomain.CurrentDomain.BaseDirectory);
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
                    config.AddEnvironmentVariables();
                })
                .UseSerilog()
                .ConfigureServices((context, services) =>
                {
                    services.AddApplicationServices();
                    services.AddInfrastructureServices(context.Configuration);

                    services.AddSingleton<ISessionService, SessionService>();
                    services.AddSingleton<INavigationService, NavigationService>();
                    services.AddSingleton<IDialogService, DialogService>();
                    services.AddSingleton<IReportExportService, ReportExportService>();

                    services.AddTransient<LoginViewModel>();
                    services.AddTransient<LoginView>();
                    services.AddTransient<MainShellViewModel>();
                    services.AddTransient<MainShellView>();

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
                    services.AddTransient<CustomersViewModel>();
                    services.AddTransient<CustomersView>();
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
            await dbContext.Database.MigrateAsync();

            var seeder = scopedProvider.GetRequiredService<PharmacyERP.Infrastructure.Persistence.Seed.ApplicationDbSeeder>();
            await seeder.SeedAsync();

            // Resolving INavigationService for the first time through scopedProvider makes the
            // singleton instance permanently hold scopedProvider as its IServiceProvider, so every
            // Window/View it (and MainShellViewModel, transitively) later resolves shares this
            // same long-lived scope instead of the invalid root provider.
            var navigationService = scopedProvider.GetRequiredService<INavigationService>();
            navigationService.ShowLogin();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application failed to start");
            MessageBox.Show(
                "تعذر بدء تشغيل التطبيق. الرجاء التأكد من إعدادات الاتصال بقاعدة البيانات والمحاولة مرة أخرى." +
                Environment.NewLine + Environment.NewLine + ex.Message,
                "خطأ في بدء التشغيل", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

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
