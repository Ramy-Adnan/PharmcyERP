using System.Windows;
using PharmacyERP.WPF.ViewModels;

namespace PharmacyERP.WPF.Views;

public partial class MainShellView : Window
{
    public MainShellView(MainShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => viewModel.Branding.Activate();
        Closed += (_, _) => viewModel.Branding.Dispose();

        if (viewModel.NavigateToDashboardCommand.CanExecute(null))
            viewModel.NavigateToDashboardCommand.Execute(null);
    }
}
