using System.Collections.ObjectModel;
using Microsoft.Win32;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Backup;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;

namespace PharmacyERP.WPF.ViewModels.SystemManagement;

/// <summary>Serializes backup/transfer actions and closes the current session after a restore attempt.</summary>
public class BackupViewModel : ViewModelBase
{
    private readonly IBackupService _backupService;
    private readonly IDataTransferService _dataTransferService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;
    private BackupFileDto? _selectedBackup;
    private bool _isBusy;
    private bool _initialized;
    private string _statusMessage = string.Empty;

    public BackupViewModel(IBackupService backupService, IDataTransferService dataTransferService,
        IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _backupService = backupService;
        _dataTransferService = dataTransferService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;
        Backups = new ObservableCollection<BackupFileDto>();
        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        CreateBackupCommand = new AsyncRelayCommand(CreateBackupAsync, () => CanManage && !IsBusy);
        ExportBundleCommand = new AsyncRelayCommand(ExportBundleAsync, () => CanManage && !IsBusy);
        ImportBundleCommand = new AsyncRelayCommand(ImportBundleAsync, () => CanManage && !IsBusy);
        RestoreBackupCommand = new AsyncRelayCommand(RestoreBackupAsync, () => CanManage && !IsBusy && SelectedBackup is not null);
        DeleteBackupCommand = new AsyncRelayCommand(DeleteBackupAsync, () => CanManage && !IsBusy && SelectedBackup is not null);
    }

    /// <summary>The view closes the process rather than reusing pre-restore tracked entities.</summary>
    public event Action? RestartRequired;
    public bool CanManage => _currentUserService.HasPermission("System.ManageBackup");
    public string BackupFolder => _backupService.BackupFolder;
    public ObservableCollection<BackupFileDto> Backups { get; }
    public BackupFileDto? SelectedBackup
    {
        get => _selectedBackup;
        set { if (SetProperty(ref _selectedBackup, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }
    public bool IsBusy
    {
        get => _isBusy;
        private set { if (SetProperty(ref _isBusy, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand CreateBackupCommand { get; }
    public AsyncRelayCommand ExportBundleCommand { get; }
    public AsyncRelayCommand ImportBundleCommand { get; }
    public AsyncRelayCommand RestoreBackupCommand { get; }
    public AsyncRelayCommand DeleteBackupCommand { get; }

    public async Task InitializeAsync()
    {
        if (!_initialized && !IsBusy) await LoadAsync();
    }

    private async Task ReloadHistoryAsync()
    {
        var backups = await _backupService.GetBackupHistoryAsync();
        Backups.Clear();
        foreach (var backup in backups) Backups.Add(backup);
        _initialized = true;
    }

    private async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await ReloadHistoryAsync(); }
        catch (Exception ex) { _dialogService.ShowError($"تعذر قراءة النسخ الاحتياطية: {ex.Message}"); }
        finally { IsBusy = false; }
    }

    private async Task CreateBackupAsync()
    {
        if (!CanManage || IsBusy) return;
        IsBusy = true;
        StatusMessage = "جارٍ إنشاء نسخة احتياطية لقاعدة البيانات…";
        try
        {
            var result = await _backupService.CreateBackupAsync();
            if (!result.Succeeded)
            {
                StatusMessage = "لم تُنشأ النسخة الاحتياطية.";
                _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر إنشاء النسخة الاحتياطية.");
                return;
            }
            StatusMessage = $"تم إنشاء نسخة قاعدة البيانات: {result.Value!.FileName}";
            _dialogService.ShowInfo(StatusMessage);
            await ReloadHistoryAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = "تعذّر إكمال النسخ الاحتياطي.";
            _dialogService.ShowError($"{StatusMessage}{Environment.NewLine}{ex.Message}");
        }
        finally { IsBusy = false; }
    }

    private async Task ExportBundleAsync()
    {
        if (!CanManage || IsBusy) return;
        IsBusy = true;
        try
        {
            var dialog = new SaveFileDialog
            {
                Title = "حفظ حزمة نقل الصيدلية",
                Filter = "حزمة نقل الصيدلية (*.zip)|*.zip",
                DefaultExt = ".zip",
                AddExtension = true,
                OverwritePrompt = false,
                FileName = $"PharmacyERP-transfer-{DateTime.Now:yyyyMMdd-HHmmss}.zip"
            };
            var selectedNewFile = false;
            while (dialog.ShowDialog() == true)
            {
                if (!System.IO.File.Exists(dialog.FileName)) { selectedNewFile = true; break; }
                _dialogService.ShowError("اختر اسماً جديداً لحزمة النقل؛ لن يُستبدل ملف موجود مسبقاً.");
            }
            if (!selectedNewFile) return;
            StatusMessage = "جارٍ تجهيز قاعدة البيانات وإعدادات الصيدلية وصور المنتجات في حزمة نقل…";
            var result = await _dataTransferService.ExportBundleAsync(dialog.FileName);
            if (!result.Succeeded)
            {
                StatusMessage = "لم تُصدّر حزمة النقل.";
                _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر تصدير حزمة النقل.");
                return;
            }
            StatusMessage = $"تم حفظ حزمة النقل: {result.Value!.FullPath}";
            _dialogService.ShowInfo($"{StatusMessage}{Environment.NewLine}انقل هذا الملف إلى الجهاز الجديد. الترخيص وإعدادات مفاتيح الخدمات تُجهّز على الجهاز الجديد بشكل منفصل.");
            await ReloadHistoryAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = "تعذّر إكمال تصدير حزمة النقل.";
            _dialogService.ShowError($"{StatusMessage}{Environment.NewLine}{ex.Message}");
        }
        finally { IsBusy = false; }
    }

    private async Task ImportBundleAsync()
    {
        if (!CanManage || IsBusy) return;
        IsBusy = true;
        var restoreAttempted = false;
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "اختيار حزمة نقل الصيدلية",
                Filter = "حزمة نقل الصيدلية (*.zip)|*.zip",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog() != true) return;
            StatusMessage = "جارٍ فحص حزمة النقل…";
            var inspected = await _dataTransferService.InspectBundleAsync(dialog.FileName);
            if (!inspected.Succeeded)
            {
                StatusMessage = "لم تُستورد أي بيانات.";
                _dialogService.ShowError(inspected.Errors.FirstOrDefault() ?? "تعذر فحص حزمة النقل.");
                return;
            }
            var preview = inspected.Value!;
            var pharmacy = string.IsNullOrWhiteSpace(preview.PharmacyName) ? "غير مذكور؛ تحقق من مصدر الحزمة" : preview.PharmacyName;
            var confirmed = _dialogService.Confirm(
                $"الصيدلية في الحزمة: {pharmacy}{Environment.NewLine}" +
                $"قاعدة البيانات: {preview.DatabaseName}{Environment.NewLine}" +
                $"تاريخ الحزمة: {preview.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}{Environment.NewLine}" +
                $"صور المنتجات: {preview.ProductImageCount:N0}{Environment.NewLine}" +
                $"إعدادات الاسم والشعار والوصل: {(preview.HasReceiptSettings ? "موجودة" : "غير موجودة")}{Environment.NewLine}{Environment.NewLine}" +
                "هذه العملية تستبدل بيانات قاعدة الصيدلية الحالية بالبيانات الموجودة في الحزمة، وتنقل إعدادات الصيدلية والصور المرفقة. " +
                "سيتم إنشاء نسخة أمان قبل الاستبدال؛ إذا تعذر إنشاؤها تتوقف العملية. أغلق البرنامج على الأجهزة الأخرى أولاً. " +
                "بعد المحاولة سيُغلق البرنامج للحفاظ على اتساق البيانات، ثم أعد فتحه. هل تأكدت أن الحزمة تخص الصيدلية المطلوبة وتريد الاستيراد؟",
                "تأكيد استبدال بيانات الصيدلية");
            if (!confirmed) { StatusMessage = "أُلغي الاستيراد، ولم تتغير البيانات."; return; }
            StatusMessage = "جارٍ التحقق من الملفات وإنشاء نسخة أمان واستيراد البيانات…";
            restoreAttempted = true;
            var result = await _dataTransferService.ImportBundleAsync(dialog.FileName);
            if (!result.Succeeded)
            {
                StatusMessage = "لم تكتمل عملية الاستيراد.";
                _dialogService.ShowError($"{result.Errors.FirstOrDefault() ?? StatusMessage}{Environment.NewLine}سيُغلق البرنامج الآن. أعد فتحه وتحقق من النتيجة قبل متابعة العمل.");
                return;
            }
            StatusMessage = "تم استيراد بيانات الصيدلية بنجاح.";
            _dialogService.ShowInfo($"{StatusMessage}{Environment.NewLine}سيُغلق البرنامج الآن؛ أعد فتحه قبل متابعة العمل.");
        }
        catch (Exception ex)
        {
            StatusMessage = "تعذّر إكمال الاستيراد.";
            _dialogService.ShowError($"{StatusMessage}{Environment.NewLine}{ex.Message}" +
                (restoreAttempted ? $"{Environment.NewLine}سيُغلق البرنامج الآن؛ أعد فتحه وتحقق من النتيجة." : string.Empty));
        }
        finally
        {
            if (restoreAttempted) RestartRequired?.Invoke();
            else IsBusy = false;
        }
    }

    private async Task RestoreBackupAsync()
    {
        if (!CanManage || IsBusy || SelectedBackup is null) return;
        var selected = SelectedBackup;
        IsBusy = true;
        var restoreAttempted = false;
        try
        {
            if (!_dialogService.Confirm(
                $"ستستبدل النسخة '{selected.FileName}' بيانات قاعدة الصيدلية الحالية. " +
                "سننشئ نسخة أمان من الوضع الحالي أولاً؛ إذا فشلت لن تبدأ الاستعادة. " +
                "أغلق البرنامج على الأجهزة الأخرى؛ ستُقطع اتصالات قاعدة البيانات أثناء الاستعادة. " +
                "بعد المحاولة سيُغلق هذا البرنامج وتحتاج إلى إعادة فتحه. هل تريد المتابعة؟",
                "تأكيد استعادة قاعدة البيانات")) return;
            StatusMessage = "جارٍ إنشاء نسخة أمان قبل الاستعادة…";
            var safety = await _backupService.CreateBackupAsync();
            if (!safety.Succeeded)
            {
                StatusMessage = "لم تبدأ الاستعادة لأن إنشاء نسخة الأمان فشل.";
                _dialogService.ShowError($"{StatusMessage}{Environment.NewLine}{safety.Errors.FirstOrDefault()}");
                return;
            }
            StatusMessage = $"حُفظت نسخة الأمان {safety.Value!.FileName}. جارٍ الاستعادة…";
            restoreAttempted = true;
            var result = await _backupService.RestoreBackupAsync(selected.FullPath);
            if (!result.Succeeded)
            {
                _dialogService.ShowError($"{result.Errors.FirstOrDefault() ?? "فشلت عملية الاستعادة."}{Environment.NewLine}نسخة الأمان: {safety.Value.FullPath}{Environment.NewLine}سيُغلق البرنامج الآن؛ أعد فتحه وتحقق من النتيجة.");
                return;
            }
            _dialogService.ShowInfo("تمت استعادة قاعدة البيانات بنجاح. سيُغلق البرنامج الآن؛ أعد فتحه قبل متابعة العمل.");
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"تعذّر إكمال الاستعادة: {ex.Message}" +
                (restoreAttempted ? $"{Environment.NewLine}سيُغلق البرنامج الآن؛ أعد فتحه وتحقق من النتيجة." : string.Empty));
        }
        finally
        {
            if (restoreAttempted) RestartRequired?.Invoke();
            else IsBusy = false;
        }
    }

    private async Task DeleteBackupAsync()
    {
        if (!CanManage || IsBusy || SelectedBackup is null) return;
        var selected = SelectedBackup;
        IsBusy = true;
        try
        {
            if (!_dialogService.Confirm($"هل تريد حذف ملف النسخة الاحتياطية '{selected.FileName}' نهائياً؟")) return;
            var result = await _backupService.DeleteBackupAsync(selected.FullPath);
            if (!result.Succeeded)
            {
                _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر حذف الملف.");
                return;
            }
            StatusMessage = "تم حذف ملف النسخة المحددة.";
            await ReloadHistoryAsync();
        }
        catch (Exception ex) { _dialogService.ShowError($"تعذر حذف النسخة الاحتياطية: {ex.Message}"); }
        finally { IsBusy = false; }
    }
}
