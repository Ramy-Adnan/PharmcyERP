using System.Windows.Controls;
using PharmacyERP.WPF.ViewModels.Reports;

namespace PharmacyERP.WPF.Views.Reports;

public partial class DashboardView : UserControl
{
    public DashboardView(DashboardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
