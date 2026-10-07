using System.Windows;
using PharmacyERP.Application.Features.Auth;
using PharmacyERP.Application.Features.Auth.DTOs;
using PharmacyERP.WPF.MVVM;
using Serilog;
using PharmacyERP.WPF.Services;

namespace PharmacyERP.WPF.ViewModels;

public class LoginViewModel : ViewModelBase
{
    private readonly IAuthService _authService;
    private readonly ISessionService _sessionService;
    private readonly INavigationService _navigationService;

    private string _username = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public LoginViewModel(IAuthService authService, ISessionService sessionService, INavigationService navigationService)
    {
        _authService = authService;
        _sessionService = sessionService;
        _navigationService = navigationService;

        LoginCommand = new AsyncRelayCommand(ExecuteLoginAsync, _ => !IsBusy && !string.IsNullOrWhiteSpace(Username));
    }

    public string Username
    {
        get => _username;
        set
        {
            if (SetProperty(ref _username, value))
                OnPropertyChanged(nameof(CanAttemptLogin));
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
                OnPropertyChanged(nameof(CanAttemptLogin));
        }
    }

    public bool CanAttemptLogin => !IsBusy && !string.IsNullOrWhiteSpace(Username);

    public AsyncRelayCommand LoginCommand { get; }

    /// <summary>
    /// Bound from the View's code-behind because PasswordBox does not support
    /// secure two-way binding to a string property — see LoginView.xaml.cs.
    /// </summary>
    public async Task AttemptLoginAsync(string password)
    {
        await ExecuteLoginAsync(password);
    }

    private async Task ExecuteLoginAsync(object? passwordParameter)
    {
        var password = passwordParameter as string ?? string.Empty;

        ErrorMessage = string.Empty;
        IsBusy = true;

        LoginResultDto session;
        try
        {
            var request = new LoginRequestDto
            {
                Username = Username.Trim(),
                Password = password,
                MachineName = Environment.MachineName
            };

            var result = await _authService.LoginAsync(request);

            if (!result.Succeeded || result.Value is null)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر تسجيل الدخول.";
                return;
            }

            session = result.Value;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Authentication threw an unexpected exception for user {Username}", Username);
            ErrorMessage = $"تعذّر التحقق من بيانات الدخول: {GetRootCauseMessage(ex)}";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        // Opening the main window is deliberately OUTSIDE the authentication try/catch above.
        // A failure while constructing the shell (a missing DI registration, a broken binding,
        // a failing dashboard query) is NOT a login failure — reporting it as one sends
        // troubleshooting in completely the wrong direction, which is exactly what happened
        // before this was split apart.
        try
        {
            _sessionService.Start(session);
            _navigationService.ShowMainShell();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Login succeeded but the main shell failed to open");
            _sessionService.End();
            ErrorMessage = $"تم التحقق من الدخول بنجاح، لكن تعذّر فتح الواجهة الرئيسية: {GetRootCauseMessage(ex)}";
        }
    }

    /// <summary>
    /// Walks to the innermost exception, which is almost always the one that actually names
    /// the problem — the outer layers are usually generic wrappers such as
    /// TargetInvocationException or XamlParseException.
    /// </summary>
    private static string GetRootCauseMessage(Exception ex)
    {
        var current = ex;
        while (current.InnerException is not null) current = current.InnerException;
        return current.Message;
    }
}
