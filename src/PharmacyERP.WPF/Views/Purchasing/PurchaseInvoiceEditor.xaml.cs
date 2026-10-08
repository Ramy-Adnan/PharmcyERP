using System.Windows;
using System.Windows.Controls;
using PharmacyERP.WPF.ViewModels.Purchasing;

namespace PharmacyERP.WPF.Views.Purchasing;

public partial class PurchaseInvoiceEditor : UserControl
{
    public PurchaseInvoiceEditor() => InitializeComponent();

    public static readonly DependencyProperty HeaderSelectionEnabledProperty = DependencyProperty.Register(
        nameof(HeaderSelectionEnabled), typeof(bool), typeof(PurchaseInvoiceEditor), new PropertyMetadata(true));
    public bool HeaderSelectionEnabled
    {
        get => (bool)GetValue(HeaderSelectionEnabledProperty);
        set => SetValue(HeaderSelectionEnabledProperty, value);
    }

    private void ItemCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && combo.DataContext is PILineRow line && combo.SelectedValue is int itemId
            && DataContext is PurchaseInvoiceEditViewModel vm)
            vm.ApplyItemSelection(line, itemId);
    }
}
