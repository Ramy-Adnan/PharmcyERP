using System.Windows.Controls;
using PharmacyERP.WPF.ViewModels.Insurance;

namespace PharmacyERP.WPF.Views.Insurance;

public partial class InsurancePoliciesView : UserControl
{
    public InsurancePoliciesView(InsurancePoliciesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
