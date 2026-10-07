using System.Windows;
using PharmacyERP.WPF.ViewModels.Inventory;

namespace PharmacyERP.WPF.Views.Inventory;

public partial class StockAdjustmentDialog : Window
{
    public StockAdjustmentDialog(StockAdjustmentViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += () => DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
