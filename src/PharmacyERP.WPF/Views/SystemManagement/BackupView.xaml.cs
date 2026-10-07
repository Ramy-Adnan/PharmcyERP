using System.Windows.Controls;
using PharmacyERP.WPF.ViewModels.SystemManagement;

namespace PharmacyERP.WPF.Views.SystemManagement;

public partial class BackupView : UserControl
{
    public BackupView(BackupViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
