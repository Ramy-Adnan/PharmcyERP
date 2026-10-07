using System.Windows;
using PharmacyERP.WPF.ViewModels.Prescriptions;

namespace PharmacyERP.WPF.Views.Prescriptions;

public partial class DoctorEditDialog : Window
{
    public DoctorEditDialog(DoctorEditViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += () => DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
