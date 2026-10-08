using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using PharmacyERP.WPF.ViewModels.Purchasing;
using PharmacyERP.WPF.ViewModels.Inventory;
using PharmacyERP.WPF.Views.Inventory;
using PharmacyERP.WPF.Services;

namespace PharmacyERP.WPF.Views.Purchasing;

public partial class PurchasingWorkspaceView : UserControl
{
    private readonly IDialogService _dialogs;
    public PurchasingWorkspaceView(PurchasingWorkspaceViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent(); DataContext = viewModel;
        _dialogs = services.GetRequiredService<IDialogService>();
        // Resolve only the history pages the user's permissions allow.
        if (viewModel.CanManageSuppliers) SuppliersHistory.Content = services.GetRequiredService<SuppliersView>();
        if (viewModel.CanManageOrders) OrdersHistory.Content = services.GetRequiredService<PurchaseOrdersView>();
        if (viewModel.CanReceiveGoods) ReceiptsHistory.Content = services.GetRequiredService<GoodsReceiptsView>();
        if (viewModel.CanManageInvoices) InvoicesHistory.Content = services.GetRequiredService<PurchaseInvoicesView>();
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }

    private async void AddItemClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PurchasingWorkspaceViewModel vm || !vm.CanManageItems || vm.IsBusy || !vm.IsGoodsStep) return;
        if (!CommitAndValidate(this)) { vm.ReportInputError(); return; }
        try
        {
            var dialog = _dialogs.CreateDialog<ItemEditDialog>();
            var itemEditor = (ItemEditViewModel)dialog.DataContext;
            await itemEditor.LoadForCreateAsync();
            if (_dialogs.ShowDialog(dialog) != true || !itemEditor.SavedSuccessfully || !itemEditor.SavedItemId.HasValue) return;
            await vm.Receipt.ReloadItemsAsync();
            vm.Receipt.AddLineCommand.Execute(null);
            vm.Receipt.ApplyItemSelection(vm.Receipt.Lines.Last(), itemEditor.SavedItemId.Value);
        }
        catch (Exception ex) { _dialogs.ShowError("تعذر إضافة بطاقة العلاج. " + ex.Message); }
    }

    private void SavingMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!CanSaveInputs(sender)) e.Handled = true;
    }
    private void SavingKeyDown(object sender, KeyEventArgs e)
    {
        if ((e.Key == Key.Enter || e.Key == Key.Space) && !CanSaveInputs(sender)) e.Handled = true;
    }
    private bool CanSaveInputs(object sender)
    {
        if (DataContext is not PurchasingWorkspaceViewModel vm || sender is not Button button) return true;
        if (button.Command != vm.NextCommand && button.Command != vm.SaveDraftCommand && button.Command != vm.SaveSupplierCommand && button.Command != vm.PayCommand) return true;
        if (CommitAndValidate(this)) return true;
        vm.ReportInputError(); return false;
    }
    private static bool CommitAndValidate(DependencyObject node)
    {
        if (node is UIElement element && !element.IsVisible) return true;
        if (node is DataGrid grid && (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true))) return false;
        if (Validation.GetHasError(node)) return false;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            if (!CommitAndValidate(VisualTreeHelper.GetChild(node, i))) return false;
        return true;
    }
}
