using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PharmacyERP.WPF.ViewModels.Purchasing;

namespace PharmacyERP.WPF.Views.Purchasing;

public partial class RecordPaymentDialog : Window
{
    public RecordPaymentDialog(RecordPaymentViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += () => DialogResult = true;
        Closing += (_, e) =>
        {
            if (!viewModel.IsBusy && !viewModel.HasPendingPayment) return;
            e.Cancel = true;
            viewModel.ErrorMessage = "انتظر اكتمال الحفظ أو تحقق من الدفعة السابقة قبل إغلاق النافذة.";
        };
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RecordPaymentViewModel vm || vm.IsBusy) return;
        if (!vm.HasPendingPayment && HasValidationError(this)) { vm.ErrorMessage = "صحح مبلغ الدفعة قبل الحفظ."; return; }
        await vm.SaveAsync();
    }
    private static bool HasValidationError(DependencyObject element)
    {
        if (Validation.GetHasError(element)) return true;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            if (HasValidationError(VisualTreeHelper.GetChild(element, i))) return true;
        return false;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    { if (DataContext is RecordPaymentViewModel { IsBusy: false, HasPendingPayment: false }) DialogResult = false; }
}
