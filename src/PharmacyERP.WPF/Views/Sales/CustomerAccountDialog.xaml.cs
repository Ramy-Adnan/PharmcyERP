using System.Windows;
using System.Windows.Controls;
using PharmacyERP.WPF.ViewModels.Sales;

namespace PharmacyERP.WPF.Views.Sales;

public partial class CustomerAccountDialog : Window
{
    public CustomerAccountDialog(CustomerAccountViewModel viewModel)
    { InitializeComponent(); DataContext = viewModel; }
    private void PayClicked(object sender, RoutedEventArgs e)
    {
        var vm = (CustomerAccountViewModel)DataContext;
        if (Validation.GetHasError(AmountBox)) { vm.Message = "أدخل مبلغاً صحيحاً."; return; }
        if (vm.PayCommand.CanExecute(null)) vm.PayCommand.Execute(null);
    }
}
