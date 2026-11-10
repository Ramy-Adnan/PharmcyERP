using System.Windows;
using PharmacyERP.WPF.ViewModels.SystemManagement;

namespace PharmacyERP.WPF.Views.SystemManagement;

public partial class UpdateCheckDialog : Window
{
    private bool _closeRequested;
    private bool _installerLaunched;

    public UpdateCheckDialog(UpdateCheckViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += ViewModelPropertyChanged;
        Closed += (_, _) => viewModel.PropertyChanged -= ViewModelPropertyChanged;
        Closing += (_, args) =>
        {
            if (!_installerLaunched && viewModel.IsDownloading)
            {
                args.Cancel = true;
                _closeRequested = true;
                viewModel.CancelDownload();
            }
        };
        Loaded += async (_, _) => await viewModel.CheckAsync();
    }

    private void ViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UpdateCheckViewModel.ReadyToRestart) &&
            sender is UpdateCheckViewModel { ReadyToRestart: true })
        {
            _installerLaunched = true;
            System.Windows.Application.Current.Shutdown();
        }
        else if (_closeRequested && e.PropertyName == nameof(UpdateCheckViewModel.IsDownloading) &&
                 sender is UpdateCheckViewModel { IsDownloading: false })
            Close();
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
