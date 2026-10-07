using System.Windows;
using PharmacyERP.WPF.ViewModels.Hr;

namespace PharmacyERP.WPF.Views.Hr;

public partial class EmployeeEditDialog : Window
{
    public EmployeeEditDialog(EmployeeEditViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += () => DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
