using System.ComponentModel;
using System.Windows;
using PharmacyERP.Infrastructure.Persistence.Seed;
using PharmacyERP.Infrastructure.Setup;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.ViewModels.SystemManagement;

namespace PharmacyERP.WPF.Views.SystemManagement;

public partial class InitialSetupWindow : Window
{
    private readonly SetupConnectionStore _store;
    private readonly SetupDatabaseService _databaseService = new();
    private InitialSetupViewModel ViewModel { get; }
    public string? ResultConnectionString { get; private set; }
    public string? InitialAdminPassword { get; private set; }
    public string? TakeInitialAdminPassword()
    {
        var password = InitialAdminPassword;
        InitialAdminPassword = null;
        return password;
    }

    public InitialSetupWindow(SetupConnectionStore store, string? initialConnectionString = null, bool environmentOverride = false)
    {
        _store = store;
        ViewModel = new(initialConnectionString, environmentOverride);
        InitializeComponent();
        DataContext = ViewModel;
        SqlPasswordBox.Password = ViewModel.InitialSqlPassword;
        ViewModel.ForgetInitialSqlPassword();
        Closed += (_, _) => { AdminPasswordBox.Clear(); AdminConfirmationBox.Clear(); SqlPasswordBox.Clear(); };
    }

    private void SqlPasswordChanged(object sender, RoutedEventArgs e) => ViewModel.InvalidateCheck();
    private async void TestConnectionClicked(object sender, RoutedEventArgs e)
    {
        AdminPasswordBox.Clear(); AdminConfirmationBox.Clear();
        await ViewModel.CheckAsync(SqlPasswordBox.Password, _databaseService);
    }

    private async void ContinueClicked(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanContinue) return;
        ViewModel.ErrorMessage = string.Empty;
        try
        {
            var connection = ViewModel.BuildConnectionString(SqlPasswordBox.Password);
            if (!ViewModel.IsCheckedConnection(connection))
            {
                ViewModel.InvalidateCheck();
                throw new InvalidOperationException("تغيّرت إعدادات الاتصال؛ اختبرها مرة أخرى قبل الحفظ.");
            }
            var needsAdmin = ViewModel.NeedsAdministrator;
            if (needsAdmin)
            {
                InitialSetupValidation.ValidateAdministratorPassword(AdminPasswordBox.Password);
                if (AdminPasswordBox.Password != AdminConfirmationBox.Password)
                    throw new InvalidOperationException("كلمتا مرور المدير غير متطابقتين.");
            }
            if (!ViewModel.CreateNew && ViewModel.CheckedStatus?.IsEmpty == true && MessageBox.Show(this,
                "القاعدة المختارة فارغة تماماً. سيتم إنشاء جداول Pharmacy ERP وحساب المدير داخلها. هل تريد المتابعة؟",
                "تهيئة قاعدة فارغة", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;

            ViewModel.IsBusy = true;
            var current = await _databaseService.PrepareAsync(connection, ViewModel.Mode);
            // A changed database is never silently re-provisioned after a successful test.
            if (current.HasAdministrator && needsAdmin)
                throw new InvalidOperationException("تغيّرت حالة القاعدة أثناء الإعداد. اختبر الاتصال مرة أخرى قبل المتابعة.");
            if (!current.HasAdministrator && !needsAdmin)
                throw new InvalidOperationException("أصبحت القاعدة تحتاج مديراً أولاً. اختبر الاتصال مرة أخرى وأدخل كلمة المرور.");
            _store.Save(connection);
            ResultConnectionString = connection;
            InitialAdminPassword = needsAdmin ? AdminPasswordBox.Password : null;
            AdminPasswordBox.Clear(); AdminConfirmationBox.Clear(); SqlPasswordBox.Clear();
            DialogResult = true;
        }
        catch (Exception ex) { ViewModel.ErrorMessage = SetupDatabaseService.SafeFailureMessage(ex); }
        finally { ViewModel.IsBusy = false; }
    }

    private void CancelClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy) return;
        InitialAdminPassword = null;
        AdminPasswordBox.Clear(); AdminConfirmationBox.Clear(); SqlPasswordBox.Clear();
        DialogResult = false;
    }
    private void WindowClosing(object? sender, CancelEventArgs e) { if (ViewModel.IsBusy && DialogResult != true) e.Cancel = true; }
}
