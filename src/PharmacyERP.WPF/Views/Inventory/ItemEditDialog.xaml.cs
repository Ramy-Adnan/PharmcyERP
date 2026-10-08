using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PharmacyERP.WPF.ViewModels.Inventory;

namespace PharmacyERP.WPF.Views.Inventory;

public partial class ItemEditDialog : Window
{
    public ItemEditDialog(ItemEditViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += () => DialogResult = true;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ItemEditViewModel vm || vm.IsBusy) return;
        if (HasValidationErrors(this))
        { vm.ErrorMessage = "صحح الحقول غير الصالحة قبل الحفظ، خصوصاً عدد الوحدات والأسعار."; return; }
        await vm.SaveAsync();
    }

    private static bool HasValidationErrors(DependencyObject element)
    {
        if (Validation.GetHasError(element)) return true;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            if (HasValidationErrors(VisualTreeHelper.GetChild(element, i))) return true;
        return false;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
