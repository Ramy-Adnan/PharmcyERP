using System.Windows.Controls;
using PharmacyERP.WPF.ViewModels.Hr;

namespace PharmacyERP.WPF.Views.Hr;

public partial class ShiftsView : UserControl
{
    public ShiftsView(ShiftsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
