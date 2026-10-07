using System.Windows;
using PharmacyERP.WPF.ViewModels;

namespace PharmacyERP.WPF.Views;

public partial class MainShellView : Window
{
    public MainShellView(MainShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        if (viewModel.NavigateToDashboardCommand.CanExecute(null))
            viewModel.NavigateToDashboardCommand.Execute(null);
    }
}
