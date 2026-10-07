using System.Windows;
using System.Windows.Controls;
using PharmacyERP.WPF.ViewModels.Security;

namespace PharmacyERP.WPF.Views.Security;

public partial class ResetPasswordDialog : Window
{
    public ResetPasswordDialog(ResetPasswordViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += () => DialogResult = true;
    }

    private void NewPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ResetPasswordViewModel vm && sender is PasswordBox pb)
            vm.NewPassword = pb.Password;
    }

    private void ConfirmPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ResetPasswordViewModel vm && sender is PasswordBox pb)
            vm.ConfirmPassword = pb.Password;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
