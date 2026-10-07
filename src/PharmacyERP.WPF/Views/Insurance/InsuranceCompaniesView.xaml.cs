using System.Windows.Controls;
using PharmacyERP.WPF.ViewModels.Insurance;

namespace PharmacyERP.WPF.Views.Insurance;

public partial class InsuranceCompaniesView : UserControl
{
    public InsuranceCompaniesView(InsuranceCompaniesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
