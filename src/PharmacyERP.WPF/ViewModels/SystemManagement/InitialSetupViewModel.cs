using Microsoft.Data.SqlClient;
using PharmacyERP.Infrastructure.Setup;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.SystemManagement;

public sealed class InitialSetupViewModel : ViewModelBase
{
    private string _server = @".\SQLEXPRESS", _database = "PharmacyERP", _sqlUsername = string.Empty;
    private bool _windowsAuthentication = true, _createNew, _trustServerCertificate = true, _isBusy;
    private string _statusMessage = "اختر إنشاء قاعدة جديدة أو الاتصال بقاعدة الصيدلية الحالية، ثم اختبر الاتصال.";
    private string _errorMessage = string.Empty;
    private SetupDatabaseStatus? _checkedStatus;
    private string? _checkedConnection;
    private readonly string? _environmentConnection;
    public InitialSetupViewModel(string? initialConnectionString, bool environmentOverride)
    {
        EnvironmentOverride = environmentOverride;
        _environmentConnection = environmentOverride ? initialConnectionString : null;
        if (!string.IsNullOrWhiteSpace(initialConnectionString))
        {
            try
            {
                var builder = new SqlConnectionStringBuilder(initialConnectionString);
                _server = builder.DataSource;
                _database = builder.InitialCatalog;
                _windowsAuthentication = builder.IntegratedSecurity;
                _sqlUsername = builder.UserID;
                _trustServerCertificate = builder.TrustServerCertificate;
                InitialSqlPassword = builder.Password;
            }
            catch (ArgumentException) { }
        }
    }

    public bool EnvironmentOverride { get; }
    public string InitialSqlPassword { get; private set; } = string.Empty;
    public void ForgetInitialSqlPassword() => InitialSqlPassword = string.Empty;
    public string Server { get => _server; set { if (SetProperty(ref _server, value)) InvalidateCheck(); } }
    public string Database { get => _database; set { if (SetProperty(ref _database, value)) InvalidateCheck(); } }
    public string SqlUsername { get => _sqlUsername; set { if (SetProperty(ref _sqlUsername, value)) InvalidateCheck(); } }
    public bool WindowsAuthentication { get => _windowsAuthentication; set { if (SetProperty(ref _windowsAuthentication, value)) { InvalidateCheck(); OnPropertyChanged(nameof(SqlAuthentication)); OnPropertyChanged(nameof(CanEditSqlCredentials)); } } }
    public bool SqlAuthentication => !WindowsAuthentication;
    public bool CreateNew { get => _createNew; set { if (SetProperty(ref _createNew, value)) { InvalidateCheck(); OnPropertyChanged(nameof(UseExisting)); } } }
    public bool UseExisting { get => !_createNew; set { if (value) CreateNew = false; } }
    public bool TrustServerCertificate { get => _trustServerCertificate; set { if (SetProperty(ref _trustServerCertificate, value)) InvalidateCheck(); } }
    public bool IsBusy { get => _isBusy; set { if (SetProperty(ref _isBusy, value)) { OnPropertyChanged(nameof(CanInteract)); OnPropertyChanged(nameof(CanContinue)); OnPropertyChanged(nameof(CanEditConnection)); OnPropertyChanged(nameof(CanEditSqlCredentials)); } } }
    public bool CanInteract => !IsBusy;
    public bool CanEditConnection => !IsBusy && !EnvironmentOverride;
    public bool CanEditSqlCredentials => CanEditConnection && SqlAuthentication;
    public bool CanContinue => !IsBusy && _checkedStatus is not null;
    public bool NeedsAdministrator => _checkedStatus is not null && !_checkedStatus.HasAdministrator;
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public SetupDatabaseStatus? CheckedStatus => _checkedStatus;
    public DatabaseSetupMode Mode => CreateNew ? DatabaseSetupMode.CreateNew : DatabaseSetupMode.UseExisting;

    public string BuildConnectionString(string sqlPassword)
    {
        // Environment settings retain deployment/developer precedence. The UI
        // may choose CreateNew for this exact target, but cannot test/save one
        // database while the main Host later connects to another.
        if (EnvironmentOverride)
            return SetupDatabaseService.ValidateConnectionString(_environmentConnection ?? string.Empty).ConnectionString;
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = Server.Trim(), InitialCatalog = Database.Trim(), IntegratedSecurity = WindowsAuthentication,
            Encrypt = true, TrustServerCertificate = TrustServerCertificate, MultipleActiveResultSets = true,
            PersistSecurityInfo = false, ConnectTimeout = 10,
        };
        if (!WindowsAuthentication) { builder.UserID = SqlUsername.Trim(); builder.Password = sqlPassword; }
        return SetupDatabaseService.ValidateConnectionString(builder.ConnectionString).ConnectionString;
    }

    public async Task CheckAsync(string sqlPassword, SetupDatabaseService service)
    {
        InvalidateCheck();
        ErrorMessage = string.Empty;
        IsBusy = true;
        StatusMessage = "جارٍ الاتصال بالخادم والتحقق من هوية القاعدة…";
        try
        {
            var connection = BuildConnectionString(sqlPassword);
            var status = await service.CheckAsync(connection, Mode);
            _checkedStatus = status;
            _checkedConnection = connection;
            StatusMessage = status.HasAdministrator ? "الاتصال ناجح. تم التعرف على قاعدة الصيدلية وحساب المدير؛ ستبقى البيانات وكلمات المرور الحالية كما هي."
                : CreateNew ? "الاتصال ناجح والاسم متاح. حدد كلمة مرور المدير، ثم احفظ لإنشاء القاعدة والجداول."
                : "الاتصال ناجح. هذه القاعدة تحتاج تهيئة حساب المدير؛ حدد كلمة المرور لإكمال الإعداد.";
        }
        catch (Exception ex)
        {
            StatusMessage = "لم يكتمل اختبار الاتصال. راجع الرسالة أدناه ثم أعد المحاولة.";
            ErrorMessage = SetupDatabaseService.SafeFailureMessage(ex);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(NeedsAdministrator));
            OnPropertyChanged(nameof(CanContinue));
        }
    }

    public bool IsCheckedConnection(string connection) => _checkedStatus is not null && connection == _checkedConnection;
    public void InvalidateCheck()
    {
        _checkedStatus = null; _checkedConnection = null;
        StatusMessage = "اختبر الاتصال بعد تحديد الخادم والقاعدة. لا يُحفظ أو يُنشأ شيء أثناء الاختبار.";
        OnPropertyChanged(nameof(CanContinue)); OnPropertyChanged(nameof(NeedsAdministrator));
    }
}
