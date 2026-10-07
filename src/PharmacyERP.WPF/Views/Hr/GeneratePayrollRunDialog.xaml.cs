using System.Windows;
using PharmacyERP.WPF.ViewModels.Hr;

namespace PharmacyERP.WPF.Views.Hr;

public partial class GeneratePayrollRunDialog : Window
{
    public GeneratePayrollRunDialog(GeneratePayrollRunViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += () => DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
