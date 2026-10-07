using System.Windows.Controls;
using PharmacyERP.WPF.ViewModels.Accounting;

namespace PharmacyERP.WPF.Views.Accounting;

public partial class JournalEntriesView : UserControl
{
    public JournalEntriesView(JournalEntriesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
