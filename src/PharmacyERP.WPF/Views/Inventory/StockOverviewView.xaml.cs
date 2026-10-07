using System.Windows.Controls;
using PharmacyERP.WPF.ViewModels.Inventory;

namespace PharmacyERP.WPF.Views.Inventory;

public partial class StockOverviewView : UserControl
{
    public StockOverviewView(StockOverviewViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
