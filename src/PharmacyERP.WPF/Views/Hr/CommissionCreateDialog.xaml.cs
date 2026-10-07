using System.Windows;
using PharmacyERP.WPF.ViewModels.Hr;

namespace PharmacyERP.WPF.Views.Hr;

public partial class CommissionCreateDialog : Window
{
    public CommissionCreateDialog(CommissionCreateViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += () => DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
