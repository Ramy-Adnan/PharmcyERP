using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PharmacyERP.WPF.ViewModels.Purchasing;

namespace PharmacyERP.WPF.Views.Purchasing;

public partial class GoodsReceiptEditor : UserControl
{
    public GoodsReceiptEditor()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is GoodsReceiptEditViewModel previous) previous.QuickItemAdded -= FocusBatchNumber;
            if (e.NewValue is GoodsReceiptEditViewModel current) current.QuickItemAdded += FocusBatchNumber;
        };
    }

    private void EditorPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F2) return;
        e.Handled = true; QuickSearchBox.Focus(); QuickSearchBox.SelectAll();
    }
    private void QuickSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not GoodsReceiptEditViewModel vm) return;
        if (e.Key == Key.Enter) { e.Handled = true; vm.AddQuickItem(); }
        else if (e.Key == Key.Down && vm.QuickMatches.Count > 0)
        {
            e.Handled = true; QuickResults.SelectedIndex = 0; QuickResults.Focus();
            if (QuickResults.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem item) item.Focus();
        }
    }
    private void QuickResultKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not GoodsReceiptEditViewModel vm) return;
        e.Handled = true; vm.AddQuickItem();
    }
    private void QuickResultDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is GoodsReceiptEditViewModel vm && vm.SelectedQuickItem is not null) vm.AddQuickItem();
    }
    private void FocusBatchNumber(GRLineRow row)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (!IsVisible || !ReceiptLinesGrid.Items.Contains(row)) return;
            ReceiptLinesGrid.ScrollIntoView(row); ReceiptLinesGrid.SelectedItem = row;
            ReceiptLinesGrid.CurrentCell = new DataGridCellInfo(row, ReceiptLinesGrid.Columns[2]);
            ReceiptLinesGrid.Focus(); ReceiptLinesGrid.BeginEdit();
        }));
    }

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
