using System.Windows;
using PharmacyERP.WPF.ViewModels.Inventory;

namespace PharmacyERP.WPF.Views.Inventory;

public partial class ItemBatchesDialog : Window
{
    public ItemBatchesDialog(ItemBatchesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
