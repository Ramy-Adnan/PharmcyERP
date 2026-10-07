using System.Windows.Controls;
using PharmacyERP.WPF.ViewModels.Security;

namespace PharmacyERP.WPF.Views.Security;

public partial class UsersView : UserControl
{
    public UsersView(UsersViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
