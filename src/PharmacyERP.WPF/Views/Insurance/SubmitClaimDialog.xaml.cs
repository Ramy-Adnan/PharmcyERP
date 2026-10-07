using System.Windows;
using PharmacyERP.WPF.ViewModels.Insurance;

namespace PharmacyERP.WPF.Views.Insurance;

public partial class SubmitClaimDialog : Window
{
    public SubmitClaimDialog(SubmitClaimViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += () => DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
