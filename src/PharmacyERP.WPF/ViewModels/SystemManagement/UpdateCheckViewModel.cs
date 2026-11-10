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
    private bool _readyToRestart;
    private CancellationTokenSource? _downloadCancellation;
    private string _errorMessage = string.Empty;

    public UpdateCheckViewModel(IUpdateService updateService)
    {
        _updateService = updateService;
        CheckCommand = new AsyncRelayCommand(CheckAsync, () => !IsDownloading);
        DownloadAndInstallCommand = new AsyncRelayCommand(DownloadAndInstallAsync, () => Result?.IsUpdateAvailable == true && !IsDownloading && !IsChecking);
    }

    public UpdateCheckResultDto? Result
    {
        get => _result;
        private set { if (SetProperty(ref _result, value)) OnPropertyChanged(nameof(IsUpToDate)); }
    }
    public int DownloadProgress { get => _downloadProgress; private set => SetProperty(ref _downloadProgress, value); }
    public bool IsChecking
    {
        get => _isChecking;
        set { if (SetProperty(ref _isChecking, value)) OnPropertyChanged(nameof(IsUpToDate)); }
    }
    public bool IsDownloading { get => _isDownloading; set => SetProperty(ref _isDownloading, value); }
    public string ErrorMessage
    {
        get => _errorMessage;
        set { if (SetProperty(ref _errorMessage, value)) OnPropertyChanged(nameof(IsUpToDate)); }
    }
    public bool IsUpToDate => !IsChecking && Result is { IsUpdateAvailable: false } && string.IsNullOrEmpty(ErrorMessage);

    public AsyncRelayCommand CheckCommand { get; }
    public AsyncRelayCommand DownloadAndInstallCommand { get; }

    /// <summary>Set once DownloadAndInstallCommand completes, signalling the dialog's code-behind to shut the application down.</summary>
    public bool ReadyToRestart { get => _readyToRestart; private set => SetProperty(ref _readyToRestart, value); }

    public void CancelDownload() => _downloadCancellation?.Cancel();

    public async Task CheckAsync()
    {
        IsChecking = true;
        ErrorMessage = string.Empty;
        try
        {
            Result = await _updateService.CheckForUpdateAsync();
            ErrorMessage = Result.ErrorMessage ?? string.Empty;
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
        finally
        {
            IsChecking = false;
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task DownloadAndInstallAsync()
    {
        if (Result?.DownloadUrl is null) return;

        IsDownloading = true;
        ErrorMessage = string.Empty;
        ReadyToRestart = false;
        DownloadProgress = 0;
        _downloadCancellation = new CancellationTokenSource();
        try
        {
            var progress = new Progress<int>(p => DownloadProgress = p);
            await _updateService.DownloadAndLaunchInstallerAsync(Result.DownloadUrl, progress,
                _downloadCancellation.Token, expectedSha256: Result.Sha256);
            ReadyToRestart = true;
        }
        catch (OperationCanceledException) when (_downloadCancellation.IsCancellationRequested)
        {
            ErrorMessage = "تم إلغاء تنزيل التحديث.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"تعذّر تنزيل التحديث: {ex.Message}";
        }
        finally
        {
            _downloadCancellation.Dispose();
            _downloadCancellation = null;
            IsDownloading = false;
        }
    }
}
