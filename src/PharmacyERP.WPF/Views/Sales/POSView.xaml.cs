using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PharmacyERP.WPF.ViewModels.Sales;

namespace PharmacyERP.WPF.Views.Sales;

public partial class POSView : UserControl
{
    private readonly POSViewModel _viewModel;
    private readonly DispatcherTimer _scanTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private Window? _window;

    public POSView(POSViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _scanTimer.Tick += async (_, _) =>
        {
            _scanTimer.Stop();
            // HID readers without an Enter suffix: numeric barcode ends after an idle gap.
            var text = ScanBox.Text.Trim();
            if (IsLoaded && text.Length >= 3 && text.All(char.IsAsciiDigit))
                await _viewModel.SubmitSearchAsync(text, barcodeOnly: true);
        };
        Loaded += async (_, _) =>
        {
            _viewModel.ScanFocusRequested += FocusScanner;
            _window = Window.GetWindow(this);
            if (_window is not null) _window.PreviewKeyDown += WindowKeyDown;
            await _viewModel.InitializeAsync();
            FocusScanner();
        };
        Unloaded += (_, _) =>
        {
            _scanTimer.Stop();
            _viewModel.ScanFocusRequested -= FocusScanner;
            if (_window is not null) _window.PreviewKeyDown -= WindowKeyDown;
            _window = null;
        };
    }

    private void FocusScanner()
    {
        if (IsLoaded && _viewModel.CanScan)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (IsLoaded && _viewModel.CanScan) ScanBox.Focus();
            }));
    }
    private void ScanTextChanged(object sender, TextChangedEventArgs e)
    {
        _scanTimer.Stop();
        if (ScanBox.Text.Length > 0) _scanTimer.Start();
    }
    private async void ScanKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true; _scanTimer.Stop();
        await _viewModel.SubmitSearchAsync(ScanBox.Text);
    }
    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsVisible) return;
        if (e.Key == Key.F9 && _viewModel.CanScan) { e.Handled = true; _scanTimer.Stop(); ScanBox.Clear(); ScanBox.Focus(); }
        if (e.Key == Key.F12) { e.Handled = true; Checkout(); }
    }
    private void CheckoutClicked(object sender, RoutedEventArgs e) => Checkout();
    private void Checkout()
    {
        if (!CartGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !CartGrid.CommitEdit(DataGridEditingUnit.Row, true) || HasValidationError(this))
        { _viewModel.ErrorMessage = "راجع الحقول الحمراء وأدخل أرقاماً صحيحة قبل البيع."; return; }
        if (_scanTimer.IsEnabled || !string.IsNullOrWhiteSpace(ScanBox.Text))
        { _viewModel.ErrorMessage = "انتظر إضافة الباركود الأخير أو أكمل البحث قبل البيع."; return; }
        if (_viewModel.CheckoutCommand.CanExecute(null)) _viewModel.CheckoutCommand.Execute(null);
    }
    private static bool HasValidationError(DependencyObject node)
    {
        if (Validation.GetHasError(node)) return true;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            if (HasValidationError(VisualTreeHelper.GetChild(node, i))) return true;
        return false;
    }
}
