using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;

namespace PharmacyERP.WPF.Views.Sales;

public partial class POSWindow : Window
{
    private readonly POSView _view;
    private bool _approved, _checking;
    public POSWindow(IServiceScopeFactory scopeFactory)
    {
        InitializeComponent();
        _view = new POSView(scopeFactory);
        Workspace.Content = _view;
        Closing += ClosingWorkspace;
        Closed += (_, _) => { Workspace.Content = null; _view.Dispose(); Owner?.Activate(); };
        PreviewKeyDown += (_, e) =>
        {
            // Escape closes popups and editors first. Ctrl+Esc is a deliberate workspace exit.
            if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; Close(); }
        };
    }
    private async void ClosingWorkspace(object? sender, CancelEventArgs e)
    {
        if (_approved) return;
        e.Cancel = true;
        if (_checking) return;
        if (!_view.ViewModel.CanLeave)
        {
            MessageBox.Show(this, _view.ViewModel.CheckoutUncertain ? "تحقق من نتيجة البيع السابق قبل مغادرة نقطة البيع." : "انتظر اكتمال العملية الحالية ثم عد للرئيسية.", "نقطة البيع", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _checking = true;
        try
        {
            if ((_view.ViewModel.CartLines.Count > 0 || !string.IsNullOrWhiteSpace(_view.ViewModel.SearchText)) && MessageBox.Show(this,
                "توجد فاتورة غير مكتملة. العودة للرئيسية ستلغي السلة الحالية. هل تريد العودة؟", "العودة للرئيسية", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            _view.StopInput(); IsEnabled = false;
            await _view.ViewModel.PrepareToCloseAsync();
            _approved = true; Close();
        }
        finally { _checking = false; }
    }
}
