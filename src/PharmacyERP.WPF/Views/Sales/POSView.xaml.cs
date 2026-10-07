using System.Windows.Controls;
using PharmacyERP.WPF.ViewModels.Sales;

namespace PharmacyERP.WPF.Views.Sales;

public partial class POSView : UserControl
{
    public POSView(POSViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
