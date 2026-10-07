using System.Windows.Controls;
using PharmacyERP.WPF.ViewModels.Reports;

namespace PharmacyERP.WPF.Views.Reports;

public partial class InventoryMovementReportView : UserControl
{
    public InventoryMovementReportView(InventoryMovementReportViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
