using System.Windows.Controls;
using PharmacyERP.WPF.ViewModels.Sales;

namespace PharmacyERP.WPF.Views.Sales;

public partial class SalesInvoicesView : UserControl
{
    public SalesInvoicesView(SalesInvoicesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
