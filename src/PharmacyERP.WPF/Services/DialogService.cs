using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace PharmacyERP.WPF.Services;

public class DialogService : IDialogService
{
    private readonly IServiceProvider _serviceProvider;

    public DialogService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public bool Confirm(string message, string title = "تأكيد") =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowError(string message, string title = "خطأ") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInfo(string message, string title = "معلومة") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public TWindow CreateDialog<TWindow>() where TWindow : Window
    {
        var window = _serviceProvider.GetRequiredService<TWindow>();
        window.Owner = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? System.Windows.Application.Current.MainWindow;
        return window;
    }

    public bool? ShowDialog(Window window) => window.ShowDialog();
}
