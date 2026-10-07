using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Backup;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;

namespace PharmacyERP.WPF.ViewModels.SystemManagement;

/// <summary>
/// ViewModel for creating and restoring native SQL Server database backups.
/// Restoring is a destructive, disruptive operation (it forcibly drops every
/// connection to the database, including the application's own), so it
/// requires a strongly-worded confirmation and the caller must expect the
/// application needs restarting immediately afterward.
/// </summary>
public class BackupViewModel : ViewModelBase
{
    private readonly IBackupService _backupService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;

    private BackupFileDto? _selectedBackup;
    private bool _isBusy;

    public BackupViewModel(IBackupService backupService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _backupService = backupService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        Backups = new ObservableCollection<BackupFileDto>();

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        CreateBackupCommand = new AsyncRelayCommand(CreateBackupAsync, () => CanManage);
        RestoreBackupCommand = new AsyncRelayCommand(RestoreBackupAsync, () => CanManage && SelectedBackup is not null);
        DeleteBackupCommand = new AsyncRelayCommand(DeleteBackupAsync, () => CanManage && SelectedBackup is not null);
    }

    public bool CanManage => _currentUserService.HasPermission("System.ManageBackup");

    public string BackupFolder => _backupService.BackupFolder;

    public ObservableCollection<BackupFileDto> Backups { get; }

    public BackupFileDto? SelectedBackup
    {
        get => _selectedBackup;
        set { if (SetProperty(ref _selectedBackup, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand CreateBackupCommand { get; }
    public AsyncRelayCommand RestoreBackupCommand { get; }
    public AsyncRelayCommand DeleteBackupCommand { get; }

    public async Task InitializeAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var backups = await _backupService.GetBackupHistoryAsync();
            Backups.Clear();
            foreach (var b in backups) Backups.Add(b);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CreateBackupAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _backupService.CreateBackupAsync();
            if (!result.Succeeded)
            {
                _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر إنشاء النسخة الاحتياطية.");
                return;
            }

            _dialogService.ShowInfo($"تم إنشاء نسخة احتياطية بنجاح: {result.Value!.FileName}");
            await LoadAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RestoreBackupAsync()
    {
        if (SelectedBackup is null) return;

        var confirmed = _dialogService.Confirm(
            $"تحذير: استعادة النسخة الاحتياطية '{SelectedBackup.FileName}' ستستبدل كل البيانات الحالية بشكل نهائي ولا يمكن التراجع عنها، " +
            "وستُغلق كل الجلسات المتصلة بقاعدة البيانات (بما فيها هذا التطبيق) فوراً أثناء التنفيذ. " +
            "تأكد من إنشاء نسخة احتياطية للوضع الحالي أولاً إن لزم الأمر. هل تريد المتابعة؟",
            "تأكيد الاستعادة");
        if (!confirmed) return;

        IsBusy = true;
        try
        {
            var result = await _backupService.RestoreBackupAsync(SelectedBackup.FullPath);
            if (!result.Succeeded)
            {
                _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "فشلت عملية الاستعادة.");
                return;
            }

            _dialogService.ShowInfo("تمت الاستعادة بنجاح. الرجاء إغلاق التطبيق وإعادة تشغيله الآن.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteBackupAsync()
    {
        if (SelectedBackup is null) return;
        if (!_dialogService.Confirm($"هل تريد حذف ملف النسخة الاحتياطية '{SelectedBackup.FileName}' نهائياً؟")) return;

        var result = await _backupService.DeleteBackupAsync(SelectedBackup.FullPath);
        if (!result.Succeeded)
        {
            _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر حذف الملف.");
            return;
        }

        await LoadAsync();
    }
}
