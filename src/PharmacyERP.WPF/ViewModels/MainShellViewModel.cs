using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;
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

namespace PharmacyERP.WPF.ViewModels;

/// <summary>
/// ViewModel for the main application shell shown after a successful login.
/// Hosts the navigation menu (filtered by the user's permissions) and swaps
/// the active module's View into the content area. Every module in the
/// roadmap follows this same pattern: a NavigateToXxx command here and a
/// menu button in MainShellView.xaml.
/// </summary>
public class MainShellViewModel : ViewModelBase
{
    private readonly ISessionService _sessionService;
    private readonly INavigationService _navigationService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IDialogService _dialogService;

    private object? _currentView;

    public MainShellViewModel(ISessionService sessionService, INavigationService navigationService, IServiceProvider serviceProvider, IDialogService dialogService,
        PharmacyBrandingViewModel branding)
    {
        _sessionService = sessionService;
        _navigationService = navigationService;
        _serviceProvider = serviceProvider;
        _dialogService = dialogService;
        Branding = branding;

        LogoutCommand = new RelayCommand(Logout);
        NavigateToDashboardCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(DashboardView)));
        NavigateToBranchesCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(BranchesView)));
        NavigateToUsersCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(UsersView)));
        NavigateToRolesCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(RolesView)));

        NavigateToItemsCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(ItemsView)));
        NavigateToStockOverviewCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(StockOverviewView)));
        NavigateToItemLookupsCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(ItemLookupsView)));

        NavigateToPurchasingCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(PurchasingWorkspaceView)), () => CanAccessPurchasing);

        NavigateToPosCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(POSView)));
        NavigateToCustomersCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(CustomersView)));
        NavigateToSalesInvoicesCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(SalesInvoicesView)));

        NavigateToDoctorsCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(DoctorsView)));
        NavigateToPrescriptionsCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(PrescriptionsView)));

        NavigateToInsuranceCompaniesCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(InsuranceCompaniesView)));
        NavigateToInsurancePoliciesCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(InsurancePoliciesView)));
        NavigateToInsuranceClaimsCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(InsuranceClaimsView)));

        NavigateToChartOfAccountsCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(ChartOfAccountsView)));
        NavigateToJournalEntriesCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(JournalEntriesView)));
        NavigateToCashAndBankCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(CashAndBankView)));
        NavigateToExpenseCategoriesCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(ExpenseCategoriesView)));
        NavigateToExpensesCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(ExpensesView)));
        NavigateToReceiptsAndPaymentsCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(ReceiptsAndPaymentsView)));

        NavigateToSalesSummaryReportCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(SalesSummaryReportView)));
        NavigateToInventoryMovementReportCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(InventoryMovementReportView)));
        NavigateToTrialBalanceCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(TrialBalanceView)));
        NavigateToIncomeStatementCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(IncomeStatementView)));

        NavigateToEmployeesCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(EmployeesView)));
        NavigateToShiftsCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(ShiftsView)));
        NavigateToAttendanceCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(AttendanceView)));
        NavigateToCommissionsCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(CommissionsView)));
        NavigateToPayrollRunsCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(PayrollRunsView)));

        NavigateToLicenseCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(LicenseView)));
        NavigateToBackupCommand = new RelayCommand(() => CurrentView = _serviceProvider.GetService(typeof(BackupView)));
        NavigateToReceiptSettingsCommand = new RelayCommand(
            () => CurrentView = _serviceProvider.GetService(typeof(ReceiptSettingsView)), () => CanConfigureSettings);
        CheckForUpdatesCommand = new RelayCommand(CheckForUpdates);
    }

    public string WelcomeMessage =>
        _sessionService.CurrentSession is null
            ? string.Empty
            : $"مرحباً، {_sessionService.CurrentSession.FullName}";

    public PharmacyBrandingViewModel Branding { get; }

    public string RoleAndBranch =>
        _sessionService.CurrentSession is null
            ? string.Empty
            : $"{_sessionService.CurrentSession.RoleName} — {_sessionService.CurrentSession.BranchName}";

    public bool CanManageBranches => _sessionService.CurrentSession?.Permissions.Contains("Branches.Manage") ?? false;
    public bool CanManageUsers => _sessionService.CurrentSession?.Permissions.Contains("Security.ManageUsers") ?? false;
    public bool CanManageRoles => _sessionService.CurrentSession?.Permissions.Contains("Security.ManageRoles") ?? false;

    public bool CanManageItems => _sessionService.CurrentSession?.Permissions.Contains("Inventory.ManageItems") ?? false;
    public bool CanViewStock =>
        (_sessionService.CurrentSession?.Permissions.Contains("Inventory.ViewStock") ?? false) ||
        (_sessionService.CurrentSession?.Permissions.Contains("Inventory.ReceiveStock") ?? false) ||
        (_sessionService.CurrentSession?.Permissions.Contains("Inventory.AdjustStock") ?? false);
    public bool CanManageInventoryLookups => _sessionService.CurrentSession?.Permissions.Contains("Inventory.ManageLookups") ?? false;

    public bool CanAccessPurchasing => CanManageSuppliers || CanManagePurchaseOrders || CanReceiveGoods || CanManagePurchaseInvoices;
    public bool CanManageSuppliers => _sessionService.CurrentSession?.Permissions.Contains("Purchasing.ManageSuppliers") ?? false;
    public bool CanManagePurchaseOrders => _sessionService.CurrentSession?.Permissions.Contains("Purchasing.ManageOrders") ?? false;
    public bool CanReceiveGoods => _sessionService.CurrentSession?.Permissions.Contains("Purchasing.ReceiveGoods") ?? false;
    public bool CanManagePurchaseInvoices => _sessionService.CurrentSession?.Permissions.Contains("Purchasing.ManageInvoices") ?? false;

    public bool CanUsePos => _sessionService.CurrentSession?.Permissions.Contains("Sales.UsePos") ?? false;
    public bool CanManageCustomers => _sessionService.CurrentSession?.Permissions.Contains("Sales.ManageCustomers") ?? false;
    public bool CanViewSalesInvoices => _sessionService.CurrentSession?.Permissions.Contains("Sales.ViewInvoices") ?? false;

    public bool CanManagePrescriptions => _sessionService.CurrentSession?.Permissions.Contains("Prescriptions.Manage") ?? false;
    public bool CanManageInsuranceCompanies => _sessionService.CurrentSession?.Permissions.Contains("Insurance.ManageCompanies") ?? false;
    public bool CanManageInsurancePolicies => _sessionService.CurrentSession?.Permissions.Contains("Insurance.ManagePolicies") ?? false;
    public bool CanManageInsuranceClaims => _sessionService.CurrentSession?.Permissions.Contains("Insurance.ManageClaims") ?? false;

    public bool CanManageChartOfAccounts => _sessionService.CurrentSession?.Permissions.Contains("Accounting.ManageChartOfAccounts") ?? false;
    public bool CanManageJournalEntries => _sessionService.CurrentSession?.Permissions.Contains("Accounting.ManageJournalEntries") ?? false;
    public bool CanManageCashAndBank => _sessionService.CurrentSession?.Permissions.Contains("Accounting.ManageCashAndBank") ?? false;
    public bool CanManageExpenses => _sessionService.CurrentSession?.Permissions.Contains("Accounting.ManageExpenses") ?? false;
    public bool CanManageReceiptsAndPayments => _sessionService.CurrentSession?.Permissions.Contains("Accounting.ManageReceiptsPayments") ?? false;

    public bool CanViewDashboard => _sessionService.CurrentSession?.Permissions.Contains("Dashboard.View") ?? false;
    public bool CanViewReports => _sessionService.CurrentSession?.Permissions.Contains("Reports.View") ?? false;

    public bool CanManageHrEmployees => _sessionService.CurrentSession?.Permissions.Contains("Hr.ManageEmployees") ?? false;
    public bool CanManageHrAttendance => _sessionService.CurrentSession?.Permissions.Contains("Hr.ManageAttendance") ?? false;
    public bool CanManageHrCommissions => _sessionService.CurrentSession?.Permissions.Contains("Hr.ManageCommissions") ?? false;
    public bool CanManageHrPayroll => _sessionService.CurrentSession?.Permissions.Contains("Hr.ManagePayroll") ?? false;

    public bool CanManageBackup => _sessionService.CurrentSession?.Permissions.Contains("System.ManageBackup") ?? false;
    public bool CanManageLicense => _sessionService.CurrentSession?.Permissions.Contains("System.ManageLicense") ?? false;
    public bool CanConfigureReceipts => CanUsePos || CanManageBranches;
    public bool CanConfigureSettings => CanConfigureReceipts || CanManagePurchaseInvoices;

    public object? CurrentView { get => _currentView; set => SetProperty(ref _currentView, value); }

    public RelayCommand LogoutCommand { get; }
    public RelayCommand NavigateToDashboardCommand { get; }
    public RelayCommand NavigateToBranchesCommand { get; }
    public RelayCommand NavigateToUsersCommand { get; }
    public RelayCommand NavigateToRolesCommand { get; }
    public RelayCommand NavigateToItemsCommand { get; }
    public RelayCommand NavigateToStockOverviewCommand { get; }
    public RelayCommand NavigateToItemLookupsCommand { get; }
    public RelayCommand NavigateToPurchasingCommand { get; }
    public RelayCommand NavigateToPosCommand { get; }
    public RelayCommand NavigateToCustomersCommand { get; }
    public RelayCommand NavigateToSalesInvoicesCommand { get; }
    public RelayCommand NavigateToDoctorsCommand { get; }
    public RelayCommand NavigateToPrescriptionsCommand { get; }
    public RelayCommand NavigateToInsuranceCompaniesCommand { get; }
    public RelayCommand NavigateToInsurancePoliciesCommand { get; }
    public RelayCommand NavigateToInsuranceClaimsCommand { get; }
    public RelayCommand NavigateToChartOfAccountsCommand { get; }
    public RelayCommand NavigateToJournalEntriesCommand { get; }
    public RelayCommand NavigateToCashAndBankCommand { get; }
    public RelayCommand NavigateToExpenseCategoriesCommand { get; }
    public RelayCommand NavigateToExpensesCommand { get; }
    public RelayCommand NavigateToReceiptsAndPaymentsCommand { get; }
    public RelayCommand NavigateToSalesSummaryReportCommand { get; }
    public RelayCommand NavigateToInventoryMovementReportCommand { get; }
    public RelayCommand NavigateToTrialBalanceCommand { get; }
    public RelayCommand NavigateToIncomeStatementCommand { get; }
    public RelayCommand NavigateToEmployeesCommand { get; }
    public RelayCommand NavigateToShiftsCommand { get; }
    public RelayCommand NavigateToAttendanceCommand { get; }
    public RelayCommand NavigateToCommissionsCommand { get; }
    public RelayCommand NavigateToPayrollRunsCommand { get; }
    public RelayCommand NavigateToLicenseCommand { get; }
    public RelayCommand NavigateToBackupCommand { get; }
    public RelayCommand NavigateToReceiptSettingsCommand { get; }
    public RelayCommand CheckForUpdatesCommand { get; }

    private void Logout(object? parameter)
    {
        _sessionService.End();
        _navigationService.ShowLogin();
    }

    private void CheckForUpdates(object? parameter)
    {
        var window = _dialogService.CreateDialog<UpdateCheckDialog>();
        _dialogService.ShowDialog(window);
    }
}
