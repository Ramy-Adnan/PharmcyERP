using System.Windows;
using PharmacyERP.WPF.ViewModels.Inventory;

namespace PharmacyERP.WPF.Views.Inventory;

public partial class ItemEditDialog : Window
{
    public ItemEditDialog(ItemEditViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += () => DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
