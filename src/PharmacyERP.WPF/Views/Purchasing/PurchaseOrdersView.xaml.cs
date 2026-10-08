using System.Windows;
using System.Windows.Controls;
using PharmacyERP.WPF.ViewModels.Purchasing;

namespace PharmacyERP.WPF.Views.Purchasing;

public partial class PurchaseOrdersView : UserControl
{
    public PurchaseOrdersView(PurchaseOrdersViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) =>
        {
            try { await viewModel.InitializeAsync(); }
            catch (Exception ex)
            {
                if (IsLoaded) MessageBox.Show("تعذر تحميل السجلات. راجع الاتصال ثم اضغط تحديث. " + ex.Message,
                    "تحميل المشتريات", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
    }
}
