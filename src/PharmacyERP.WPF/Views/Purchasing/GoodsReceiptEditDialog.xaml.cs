using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PharmacyERP.WPF.ViewModels.Purchasing;

namespace PharmacyERP.WPF.Views.Purchasing;

public partial class GoodsReceiptEditDialog : Window
{
    public GoodsReceiptEditDialog(GoodsReceiptEditViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += () => DialogResult = true;
        PreviewKeyDown += async (_, e) =>
        {
            if (e.Key != Key.S || Keyboard.Modifiers != ModifierKeys.Control) return;
            e.Handled = true;
            if (CanSave()) await viewModel.SaveAsync();
        };
        Closing += (_, e) => { if (viewModel.IsBusy) e.Cancel = true; };
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    { if (CanSave()) await ((GoodsReceiptEditViewModel)DataContext).SaveAsync(); }
    private bool CanSave()
    {
        if (DataContext is not GoodsReceiptEditViewModel vm || vm.IsBusy) return false;
        if (CommitAndValidate(this)) return true;
        vm.ErrorMessage = "صحح الحقول الحمراء وأدخل أرقاماً وتواريخ صحيحة قبل الحفظ.";
        return false;
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
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    { if (DataContext is GoodsReceiptEditViewModel { IsBusy: false }) DialogResult = false; }
}
