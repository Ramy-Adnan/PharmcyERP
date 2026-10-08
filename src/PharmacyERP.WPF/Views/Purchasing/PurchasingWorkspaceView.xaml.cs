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

public partial class PurchasingWorkspaceView : UserControl, IDisposable
{
    private readonly IDialogService _dialogs;
    private readonly List<ViewDataScope> _dataScopes = new();

    public PurchasingWorkspaceView(IServiceScopeFactory scopeFactory)
    {
        try
        {
            // WPF raises Loaded for embedded history pages as well as the wizard.
            // Give each page its own context instead of resolving all of them from
            // the application's long-lived scope.
            var workspaceServices = CreateDataScope(scopeFactory);
            var viewModel = workspaceServices.GetRequiredService<PurchasingWorkspaceViewModel>();
            InitializeComponent(); DataContext = viewModel;
            _dialogs = workspaceServices.GetRequiredService<IDialogService>();
            if (viewModel.CanManageSuppliers) SuppliersHistory.Content = CreateDataScope(scopeFactory).GetRequiredService<SuppliersView>();
            if (viewModel.CanManageOrders) OrdersHistory.Content = CreateDataScope(scopeFactory).GetRequiredService<PurchaseOrdersView>();
            if (viewModel.CanReceiveGoods) ReceiptsHistory.Content = CreateDataScope(scopeFactory).GetRequiredService<GoodsReceiptsView>();
            if (viewModel.CanManageInvoices) InvoicesHistory.Content = CreateDataScope(scopeFactory).GetRequiredService<PurchaseInvoicesView>();
            Loaded += async (_, _) => await viewModel.InitializeAsync();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private IServiceProvider CreateDataScope(IServiceScopeFactory factory)
    {
        var scope = new ViewDataScope(factory);
        _dataScopes.Add(scope);
        return scope.Services;
    }

    // DI disposes the view with the owning application scope. Do not dispose on
    // Unloaded: tab changes can unload a page while its async query is running.
    public void Dispose()
    {
        foreach (var scope in _dataScopes.AsEnumerable().Reverse()) scope.Dispose();
        _dataScopes.Clear();
    }

    private async void ImportPhotoClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PurchasingWorkspaceViewModel vm || vm.IsBusy || !vm.IsGoodsStep) return;
        if (vm.ReceiptId.HasValue || vm.Receipt.Lines.Count > 0) { _dialogs.ShowError("ابدأ استلاماً جديداً فارغاً لاستيراد الصورة؛ احفظ السند الحالي أولاً."); return; }
        if (vm.SelectedSupplierId <= 0 || vm.Receipt.BranchId <= 0 || vm.Receipt.WarehouseId <= 0) { _dialogs.ShowError("اختر المورد والفرع والمخزن أولاً."); return; }
        try
        {
            var dialog = _dialogs.CreateDialog<InvoiceImageImportDialog>();
            var editor = (InvoiceImageImportViewModel)dialog.DataContext;
            await editor.InitializeAsync(vm.SelectedSupplierId, vm.Receipt.BranchId, vm.Receipt.WarehouseId);
            if (_dialogs.ShowDialog(dialog) == true && editor.SavedReceiptId.HasValue) await vm.ResumeReceiptAsync(editor.SavedReceiptId.Value);
        }
        catch (Exception) { _dialogs.ShowError("تعذر فتح استيراد الفاتورة. راجع الاتصال وإعدادات النظام."); }
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
