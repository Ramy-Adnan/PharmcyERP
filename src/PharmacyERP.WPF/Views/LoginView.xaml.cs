using System.Windows;
using PharmacyERP.WPF.ViewModels;

namespace PharmacyERP.WPF.Views;

/// <summary>
/// Code-behind reads PasswordBox.Password directly (it is intentionally not a
/// bindable DependencyProperty — WPF avoids exposing raw passwords through the
/// binding/visual-tree infrastructure for security reasons) and forwards it to
/// the ViewModel's AttemptLoginAsync method.
/// </summary>
public partial class LoginView : Window
{
    public LoginView(LoginViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm)
        {
            await vm.AttemptLoginAsync(PasswordBoxControl.Password);
            PasswordBoxControl.Clear();
        }
    }
}
