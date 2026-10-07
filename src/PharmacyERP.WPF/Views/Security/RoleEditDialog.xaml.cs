using System.Windows;
using PharmacyERP.WPF.ViewModels.Security;

namespace PharmacyERP.WPF.Views.Security;

public partial class RoleEditDialog : Window
{
    public RoleEditDialog(RoleEditViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += () => DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
