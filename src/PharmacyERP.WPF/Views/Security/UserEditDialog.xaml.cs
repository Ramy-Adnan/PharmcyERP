using System.Windows;
using System.Windows.Controls;
using PharmacyERP.WPF.ViewModels.Security;

namespace PharmacyERP.WPF.Views.Security;

public partial class UserEditDialog : Window
{
    public UserEditDialog(UserEditViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += () => DialogResult = true;
    }

    private void PasswordBoxControl_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is UserEditViewModel vm && sender is PasswordBox pb)
            vm.Password = pb.Password;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
