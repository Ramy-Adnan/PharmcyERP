using System.Windows.Controls;
using PharmacyERP.WPF.ViewModels.Prescriptions;

namespace PharmacyERP.WPF.Views.Prescriptions;

public partial class PrescriptionsView : UserControl
{
    public PrescriptionsView(PrescriptionsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
