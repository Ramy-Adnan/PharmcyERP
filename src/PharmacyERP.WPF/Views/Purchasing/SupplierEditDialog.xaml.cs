using System.Windows;
using PharmacyERP.WPF.ViewModels.Purchasing;

namespace PharmacyERP.WPF.Views.Purchasing;

public partial class SupplierEditDialog : Window
{
    public SupplierEditDialog(SupplierEditViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += () => DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
