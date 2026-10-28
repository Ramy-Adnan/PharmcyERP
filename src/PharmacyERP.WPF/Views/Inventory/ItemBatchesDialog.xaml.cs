using System.Windows;
using System.ComponentModel;
using System.Windows.Threading;
using PharmacyERP.WPF.ViewModels.Inventory;

namespace PharmacyERP.WPF.Views.Inventory;

public partial class ItemBatchesDialog : Window
{
    private readonly ItemBatchesViewModel _viewModel;
    private bool _closeApproved, _closeRequested;
    public ItemBatchesDialog(ItemBatchesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_closeApproved) { base.OnClosing(e); return; }
        e.Cancel = true;
        base.OnClosing(e);
        if (_closeRequested) return;
        _closeRequested = true;
        IsEnabled = false;
        // Defer first: closing may complete synchronously, so never call Close
        // from inside the original WPF Closing stack.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () =>
        {
            await _viewModel.PrepareToCloseAsync();
            _closeApproved = true;
            Close();
        }));
    }
}
