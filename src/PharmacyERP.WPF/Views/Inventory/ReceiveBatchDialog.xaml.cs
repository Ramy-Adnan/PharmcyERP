using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PharmacyERP.WPF.ViewModels.Inventory;

namespace PharmacyERP.WPF.Views.Inventory;

public partial class ReceiveBatchDialog : Window
{
    public ReceiveBatchDialog(ReceiveBatchViewModel viewModel)
    {
        InitializeComponent(); DataContext = viewModel;
        viewModel.RequestClose += () => DialogResult = true;
        viewModel.NextBatchReady += FocusBatchNumber;
        Loaded += (_, _) => FocusBatchNumber();
        PreviewKeyDown += async (_, e) =>
        {
            if (e.Key != Key.S || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
            if (Keyboard.Modifiers != ModifierKeys.Control && Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Shift)) return;
            e.Handled = true;
            if (!CanSave()) return;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) await viewModel.SaveAndAddAnotherAsync();
            else await viewModel.SaveAsync();
        };
        Closing += (_, e) => { if (viewModel.IsBusy) e.Cancel = true; };
    }
    private void FocusBatchNumber() { BatchNumberBox.Focus(); BatchNumberBox.SelectAll(); }
    private bool CanSave()
    {
        if (DataContext is not ReceiveBatchViewModel vm || vm.IsBusy) return false;
        if (!HasValidationErrors(this)) return true;
        vm.ErrorMessage = "صحح الكمية والأسعار والتواريخ غير الصالحة قبل استلام الدفعة.";
        return false;
    }
    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    { if (CanSave()) await ((ReceiveBatchViewModel)DataContext).SaveAsync(); }
    private async void SaveAndNext_Click(object sender, RoutedEventArgs e)
    { if (CanSave()) await ((ReceiveBatchViewModel)DataContext).SaveAndAddAnotherAsync(); }
    private static bool HasValidationErrors(DependencyObject element)
    {
        if (Validation.GetHasError(element)) return true;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            if (HasValidationErrors(VisualTreeHelper.GetChild(element, i))) return true;
        return false;
    }
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    { if (DataContext is ReceiveBatchViewModel { IsBusy: false } vm) DialogResult = vm.SavedSuccessfully; }
}
