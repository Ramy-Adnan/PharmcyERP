using System.Windows;
using PharmacyERP.WPF.ViewModels.Accounting;

namespace PharmacyERP.WPF.Views.Accounting;

public partial class JournalEntryDetailDialog : Window
{
    public JournalEntryDetailDialog(JournalEntryDetailViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
