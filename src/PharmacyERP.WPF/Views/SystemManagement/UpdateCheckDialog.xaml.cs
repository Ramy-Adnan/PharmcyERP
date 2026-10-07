using System.Windows;
using PharmacyERP.WPF.ViewModels.SystemManagement;

namespace PharmacyERP.WPF.Views.SystemManagement;

public partial class UpdateCheckDialog : Window
{
    public UpdateCheckDialog(UpdateCheckViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.CheckAsync();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is UpdateCheckViewModel { ReadyToRestart: true })
        {
            // The installer has already been launched and is waiting to take over — the running
            // app must exit now so it can replace the executable and its dependent files.
            System.Windows.Application.Current.Shutdown();
            return;
        }

        Close();
    }
}
