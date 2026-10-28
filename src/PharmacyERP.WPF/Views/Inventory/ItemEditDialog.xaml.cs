using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PharmacyERP.WPF.ViewModels.Inventory;

namespace PharmacyERP.WPF.Views.Inventory;

public partial class ItemEditDialog : Window
{
    public ItemEditDialog(ItemEditViewModel viewModel)
    {
        InitializeComponent(); DataContext = viewModel;
        viewModel.RequestClose += () => DialogResult = true;
        viewModel.NewItemReady += FocusNewItem;
        Loaded += (_, _) => FocusNewItem();
        PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            { e.Handled = true; if (CanSave()) await viewModel.SaveAsync(); }
            else if (e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            { e.Handled = true; if (CanSave()) await viewModel.SaveAndAddAnotherAsync(); }
            else if (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Control)
            { e.Handled = true; if (CanSave()) await viewModel.SaveAndReceiveAsync(); }
        };
        Closing += (_, e) => { if (viewModel.IsBusy) e.Cancel = true; };
    }

    private void FocusNewItem() { CodeBox.Focus(); CodeBox.SelectAll(); }
    private bool CanSave()
    {
        if (DataContext is not ItemEditViewModel vm || vm.IsBusy) return false;
        if (!HasValidationErrors(this)) return true;
        vm.ErrorMessage = "صحح الحقول غير الصالحة قبل الحفظ، خصوصاً عدد الوحدات والأسعار.";
        return false;
    }
    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    { if (CanSave()) await ((ItemEditViewModel)DataContext).SaveAsync(); }
    private async void SaveAndNext_Click(object sender, RoutedEventArgs e)
    { if (CanSave()) await ((ItemEditViewModel)DataContext).SaveAndAddAnotherAsync(); }
    private async void SaveAndReceive_Click(object sender, RoutedEventArgs e)
    { if (CanSave()) await ((ItemEditViewModel)DataContext).SaveAndReceiveAsync(); }
    private static bool HasValidationErrors(DependencyObject element)
    {
        if (Validation.GetHasError(element)) return true;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            if (HasValidationErrors(VisualTreeHelper.GetChild(element, i))) return true;
        return false;
    }
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    { if (DataContext is ItemEditViewModel { IsBusy: false } vm) DialogResult = vm.SavedSuccessfully; }
}
