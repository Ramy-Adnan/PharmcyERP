using System.Windows.Controls;
using PharmacyERP.WPF.ViewModels.Accounting;

namespace PharmacyERP.WPF.Views.Accounting;

public partial class ExpensesView : UserControl
{
    public ExpensesView(ExpensesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
