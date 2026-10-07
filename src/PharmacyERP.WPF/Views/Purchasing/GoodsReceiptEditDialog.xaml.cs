using System.Windows;
using System.Windows.Controls;
using PharmacyERP.WPF.ViewModels.Purchasing;

namespace PharmacyERP.WPF.Views.Purchasing;

public partial class GoodsReceiptEditDialog : Window
{
    public GoodsReceiptEditDialog(GoodsReceiptEditViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += () => DialogResult = true;
    }

    private void ItemCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        if (combo.DataContext is not GRLineRow line) return;
        if (combo.SelectedValue is not int itemId) return;
        if (DataContext is not GoodsReceiptEditViewModel vm) return;

        vm.ApplyItemSelection(line, itemId);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
