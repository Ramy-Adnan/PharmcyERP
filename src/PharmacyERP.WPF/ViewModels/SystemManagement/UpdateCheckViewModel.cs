using PharmacyERP.Application.Features.Updates;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.SystemManagement;

/// <summary>
/// ViewModel behind the "Check for Updates" dialog. Checking is always safe
/// to run on demand; installing exits the running application immediately
/// after launching the downloaded installer, so the caller (the dialog's
/// code-behind) is responsible for calling Application.Current.Shutdown()
/// right after DownloadAndInstallCommand completes successfully.
/// </summary>
public class UpdateCheckViewModel : ViewModelBase
{
    private readonly IUpdateService _updateService;

    private UpdateCheckResultDto? _result;
    private int _downloadProgress;
    private bool _isChecking;
    private bool _isDownloading;
    private string _errorMessage = string.Empty;

    public UpdateCheckViewModel(IUpdateService updateService)
    {
        _updateService = updateService;
        CheckCommand = new AsyncRelayCommand(CheckAsync);
        DownloadAndInstallCommand = new AsyncRelayCommand(DownloadAndInstallAsync, () => Result?.IsUpdateAvailable == true && !IsDownloading);
    }

    public UpdateCheckResultDto? Result { get => _result; private set => SetProperty(ref _result, value); }
    public int DownloadProgress { get => _downloadProgress; private set => SetProperty(ref _downloadProgress, value); }
    public bool IsChecking { get => _isChecking; set => SetProperty(ref _isChecking, value); }
    public bool IsDownloading { get => _isDownloading; set => SetProperty(ref _isDownloading, value); }
    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }

    public AsyncRelayCommand CheckCommand { get; }
    public AsyncRelayCommand DownloadAndInstallCommand { get; }

    /// <summary>Set once DownloadAndInstallCommand completes, signalling the dialog's code-behind to shut the application down.</summary>
    public bool ReadyToRestart { get; private set; }

    public async Task CheckAsync()
    {
        IsChecking = true;
        ErrorMessage = string.Empty;
        try
        {
            Result = await _updateService.CheckForUpdateAsync();
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
        finally
        {
            IsChecking = false;
        }
    }

    private async Task DownloadAndInstallAsync()
    {
        if (Result?.DownloadUrl is null) return;

        IsDownloading = true;
        ErrorMessage = string.Empty;
        try
        {
            var progress = new Progress<int>(p => DownloadProgress = p);
            await _updateService.DownloadAndLaunchInstallerAsync(Result.DownloadUrl, progress);
            ReadyToRestart = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"تعذّر تنزيل التحديث: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
        }
    }
}
