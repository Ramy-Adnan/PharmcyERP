using System.Windows;
using PharmacyERP.WPF.ViewModels.Hr;

namespace PharmacyERP.WPF.Views.Hr;

public partial class PayrollRunDetailDialog : Window
{
    public PayrollRunDetailDialog(PayrollRunDetailViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
