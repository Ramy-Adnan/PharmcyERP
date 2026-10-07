using System.Windows.Controls;
using PharmacyERP.WPF.ViewModels.Branches;

namespace PharmacyERP.WPF.Views.Branches;

public partial class BranchesView : UserControl
{
    public BranchesView(BranchesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
