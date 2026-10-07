using System.Windows;
using System.Windows.Controls;
using PharmacyERP.WPF.ViewModels.Prescriptions;

namespace PharmacyERP.WPF.Views.Prescriptions;

public partial class PrescriptionEditDialog : Window
{
    public PrescriptionEditDialog(PrescriptionEditViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += () => DialogResult = true;
    }

    private void ItemCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        if (combo.DataContext is not PrescriptionLineRow line) return;
        if (combo.SelectedValue is not int itemId) return;
        if (DataContext is not PrescriptionEditViewModel vm) return;

        vm.ApplyItemSelection(line, itemId);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
