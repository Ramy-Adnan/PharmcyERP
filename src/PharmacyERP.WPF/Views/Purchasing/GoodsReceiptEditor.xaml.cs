using System.Windows;
using System.Windows.Controls;
using PharmacyERP.WPF.ViewModels.Purchasing;

namespace PharmacyERP.WPF.Views.Purchasing;

public partial class GoodsReceiptEditor : UserControl
{
    public GoodsReceiptEditor() => InitializeComponent();

    public static readonly DependencyProperty HeaderSelectionEnabledProperty = DependencyProperty.Register(
        nameof(HeaderSelectionEnabled), typeof(bool), typeof(GoodsReceiptEditor), new PropertyMetadata(true));
    public bool HeaderSelectionEnabled
    {
        get => (bool)GetValue(HeaderSelectionEnabledProperty);
        set => SetValue(HeaderSelectionEnabledProperty, value);
    }

    private void ItemCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && combo.DataContext is GRLineRow line && combo.SelectedValue is int itemId
            && DataContext is GoodsReceiptEditViewModel vm)
            vm.ApplyItemSelection(line, itemId);
    }
}
