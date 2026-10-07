using System.Windows.Controls;
using PharmacyERP.WPF.ViewModels.Accounting;

namespace PharmacyERP.WPF.Views.Accounting;

public partial class ExpenseCategoriesView : UserControl
{
    public ExpenseCategoriesView(ExpenseCategoriesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
