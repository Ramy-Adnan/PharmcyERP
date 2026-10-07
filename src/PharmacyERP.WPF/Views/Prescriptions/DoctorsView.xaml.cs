using System.Windows.Controls;
using PharmacyERP.WPF.ViewModels.Prescriptions;

namespace PharmacyERP.WPF.Views.Prescriptions;

public partial class DoctorsView : UserControl
{
    public DoctorsView(DoctorsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
