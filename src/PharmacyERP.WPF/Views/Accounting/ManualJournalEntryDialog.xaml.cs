using System.Windows;
using System.Windows.Controls;
using PharmacyERP.WPF.ViewModels.Accounting;

namespace PharmacyERP.WPF.Views.Accounting;

public partial class ManualJournalEntryDialog : Window
{
    public ManualJournalEntryDialog(ManualJournalEntryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += () => DialogResult = true;
    }

    private void AccountCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        if (combo.DataContext is not JournalEntryLineRow line) return;
        if (combo.SelectedValue is not int accountId) return;
        if (DataContext is not ManualJournalEntryViewModel vm) return;

        vm.ApplyAccountSelection(line, accountId);
    }

    /// <summary>
    /// DataGrid text-column edits (DebitAmount/CreditAmount) don't raise
    /// ObservableCollection change notifications, so the running total shown
    /// to the cashier would go stale after typing a value. Recalculating here
    /// on every cell-edit-ended keeps the balance indicator live.
    /// </summary>
    private void LinesGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (DataContext is ManualJournalEntryViewModel vm)
            Dispatcher.BeginInvoke(new Action(vm.RecalculateTotals));
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
