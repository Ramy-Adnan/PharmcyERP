using System.Windows.Controls;
using PharmacyERP.WPF.ViewModels.Reports;

namespace PharmacyERP.WPF.Views.Reports;

public partial class IncomeStatementView : UserControl
{
    public IncomeStatementView(IncomeStatementViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
