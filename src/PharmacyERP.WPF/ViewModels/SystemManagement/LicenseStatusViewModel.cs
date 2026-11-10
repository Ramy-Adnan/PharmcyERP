using System.Windows.Threading;
using PharmacyERP.Application.Features.Licensing;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.SystemManagement;

/// <summary>Live license status for the shell; enforcement itself stays in the services/database boundary.</summary>
public sealed class LicenseStatusViewModel : ViewModelBase, IDisposable
{
    private readonly ILicenseService _licenses;
    private readonly DispatcherTimer _timer;
    private bool _isRefreshing;
    private bool _isReadOnly = true;
    private bool _showRenewal;
    private string _message = "جارٍ التحقق من الترخيص…";

    public LicenseStatusViewModel(ILicenseService licenses)
    {
        _licenses = licenses;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += RefreshTick;
        _timer.Start();
    }

    public bool IsReadOnly { get => _isReadOnly; private set => SetProperty(ref _isReadOnly, value); }
    public bool ShowBanner => IsReadOnly || _showRenewal;
    public string Message { get => _message; private set => SetProperty(ref _message, value); }

    public async Task RefreshAsync()
    {
        if (_isRefreshing) return;
        _isRefreshing = true;
        try
        {
            var status = await _licenses.GetLicenseStatusAsync();
            IsReadOnly = !status.IsValid;
            _showRenewal = status.IsValid && status.DaysUntilExpiry is <= 14;
            OnPropertyChanged(nameof(ShowBanner));
            Message = IsReadOnly
                ? $"وضع القراءة فقط — {status.Message}"
                : status.DaysUntilExpiry is <= 14
                    ? $"الترخيص صالح؛ متبقٍ {status.DaysUntilExpiry} يومًا. يمكنك تجديده من شاشة الترخيص."
                    : "الترخيص صالح";
        }
        catch
        {
            IsReadOnly = true;
            OnPropertyChanged(nameof(ShowBanner));
            Message = "تعذر التحقق من الترخيص. تبقى القراءة والنسخ الاحتياطي متاحة؛ افتح شاشة الترخيص.";
        }
        finally { _isRefreshing = false; }
    }

    private async void RefreshTick(object? sender, EventArgs args) => await RefreshAsync();
    public void Dispose() { _timer.Stop(); _timer.Tick -= RefreshTick; }
}
