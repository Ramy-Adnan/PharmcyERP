using System.Windows.Controls;
using PharmacyERP.WPF.ViewModels.SystemManagement;

namespace PharmacyERP.WPF.Views.SystemManagement;

public partial class LicenseView : UserControl
{
    public LicenseView(LicenseViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
