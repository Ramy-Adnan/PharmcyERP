using System.Windows.Controls;
using PharmacyERP.WPF.ViewModels.Accounting;

namespace PharmacyERP.WPF.Views.Accounting;

public partial class ChartOfAccountsView : UserControl
{
    public ChartOfAccountsView(ChartOfAccountsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
