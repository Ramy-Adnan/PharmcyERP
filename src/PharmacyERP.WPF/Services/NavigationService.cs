using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PharmacyERP.WPF.Views;

namespace PharmacyERP.WPF.Services;

public class NavigationService : INavigationService
{
    private readonly IServiceProvider _serviceProvider;

    public NavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void ShowLogin()
    {
        var login = _serviceProvider.GetRequiredService<LoginView>();
        System.Windows.Application.Current.MainWindow = login;
        login.Show();
        CloseOtherWindows(login);
    }

    public void ShowMainShell()
    {
        var shell = _serviceProvider.GetRequiredService<MainShellView>();
        System.Windows.Application.Current.MainWindow = shell;
        shell.Show();
        CloseOtherWindows(shell);
    }

    private static void CloseOtherWindows(Window keep)
    {
        foreach (Window window in System.Windows.Application.Current.Windows.Cast<Window>().ToList())
        {
            if (!ReferenceEquals(window, keep))
                window.Close();
        }
    }
}
