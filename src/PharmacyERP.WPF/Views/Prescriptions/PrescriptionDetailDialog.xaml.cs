using System.Windows;
using PharmacyERP.WPF.ViewModels.Prescriptions;

namespace PharmacyERP.WPF.Views.Prescriptions;

public partial class PrescriptionDetailDialog : Window
{
    public PrescriptionDetailDialog(PrescriptionDetailViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
