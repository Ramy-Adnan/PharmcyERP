using System.Windows;
using PharmacyERP.WPF.ViewModels;

namespace PharmacyERP.WPF.Views;

public partial class MainShellView : Window
{
    public MainShellView(MainShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) =>
        {
            viewModel.Branding.Activate();
            await viewModel.Licensing.RefreshAsync();
            if (viewModel.Licensing.IsReadOnly && viewModel.CanManageLicense)
                viewModel.NavigateToLicenseCommand.Execute(null);
        };
        Closed += (_, _) => viewModel.Branding.Dispose();

        if (viewModel.NavigateToDashboardCommand.CanExecute(null))
            viewModel.NavigateToDashboardCommand.Execute(null);
    }
}
