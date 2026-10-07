using System.Windows.Controls;
using PharmacyERP.WPF.ViewModels.Reports;

namespace PharmacyERP.WPF.Views.Reports;

public partial class TrialBalanceView : UserControl
{
    public TrialBalanceView(TrialBalanceViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
