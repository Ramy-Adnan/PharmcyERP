using PharmacyERP.Application.Features.Security;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Security;

/// <summary>ViewModel for the small "reset password" dialog opened from the Users grid.</summary>
public class ResetPasswordViewModel : ViewModelBase
{
    private readonly IUserService _userService;

    private int _userId;
    private string _userDisplayName = string.Empty;
    private string _newPassword = string.Empty;
    private string _confirmPassword = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public ResetPasswordViewModel(IUserService userService)
    {
        _userService = userService;
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public string UserDisplayName { get => _userDisplayName; set => SetProperty(ref _userDisplayName, value); }
    public string NewPassword { get => _newPassword; set => SetProperty(ref _newPassword, value); }
    public string ConfirmPassword { get => _confirmPassword; set => SetProperty(ref _confirmPassword, value); }
    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand SaveCommand { get; }
    public bool SavedSuccessfully { get; private set; }
    public event Action? RequestClose;

    public void Load(int userId, string userDisplayName)
    {
        _userId = userId;
        UserDisplayName = userDisplayName;
    }

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;

        if (NewPassword != ConfirmPassword)
        {
            ErrorMessage = "كلمتا المرور غير متطابقتين.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _userService.ResetPasswordAsync(_userId, NewPassword);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر تغيير كلمة المرور.";
                return;
            }

            SavedSuccessfully = true;
            RequestClose?.Invoke();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
