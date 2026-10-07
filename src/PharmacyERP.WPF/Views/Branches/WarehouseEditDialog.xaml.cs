using System.Windows;
using PharmacyERP.WPF.ViewModels.Branches;

namespace PharmacyERP.WPF.Views.Branches;

public partial class WarehouseEditDialog : Window
{
    public WarehouseEditDialog(WarehouseEditViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += () => DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
