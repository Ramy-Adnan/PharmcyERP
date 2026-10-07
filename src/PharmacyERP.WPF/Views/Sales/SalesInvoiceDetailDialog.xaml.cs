using System.Windows;
using PharmacyERP.WPF.ViewModels.Sales;

namespace PharmacyERP.WPF.Views.Sales;

public partial class SalesInvoiceDetailDialog : Window
{
    public SalesInvoiceDetailDialog(SalesInvoiceDetailViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
