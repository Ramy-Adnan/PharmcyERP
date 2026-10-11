using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.IO;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Microsoft.Extensions.DependencyInjection;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.ViewModels.Sales;
using PharmacyERP.WPF.ViewModels;

namespace PharmacyERP.WPF.Views.Sales;

public partial class POSView : UserControl, IDisposable
{
    private readonly POSViewModel _viewModel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ViewDataScope _dataScope;
    private readonly DispatcherTimer _scanTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private Window? _window;
    private bool _disposed;
    public POSViewModel ViewModel => _viewModel;
    public PharmacyBrandingViewModel Branding { get; }
    public static readonly DependencyProperty ProductCardWidthProperty = DependencyProperty.Register(nameof(ProductCardWidth), typeof(double), typeof(POSView), new PropertyMetadata(180d));
    public double ProductCardWidth { get => (double)GetValue(ProductCardWidthProperty); set => SetValue(ProductCardWidthProperty, value); }

    public POSView(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        _dataScope = new ViewDataScope(scopeFactory);
        try
        {
            _viewModel = _dataScope.Services.GetRequiredService<POSViewModel>();
            Branding = _dataScope.Services.GetRequiredService<PharmacyBrandingViewModel>();
            Branding.Activate();
            InitializeComponent();
            DataContext = _viewModel;
        }
        catch { _dataScope.Dispose(); throw; }
        _clockTimer.Tick += (_, _) => UpdateClock();
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
            UpdateClock(); _clockTimer.Start(); ApplyResponsiveLayout();
            _viewModel.ScanFocusRequested += FocusScanner;
            _window = Window.GetWindow(this);
            if (_window is not null) _window.PreviewKeyDown += WindowKeyDown;
            await _viewModel.InitializeAsync();
            await Dispatcher.InvokeAsync(ApplyResponsiveLayout, DispatcherPriority.Loaded);
            FocusScanner();
        };
        Unloaded += (_, _) =>
        {
            _scanTimer.Stop(); _clockTimer.Stop();
            _viewModel.ScanFocusRequested -= FocusScanner;
            if (_window is not null) _window.PreviewKeyDown -= WindowKeyDown;
            _window = null;
        };
    }

    // Keep the context alive across WPF unload/reload and pending queries. DI owns disposal.
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _scanTimer.Stop(); _clockTimer.Stop();
        _viewModel.ScanFocusRequested -= FocusScanner;
        if (_window is not null) _window.PreviewKeyDown -= WindowKeyDown;
        _dataScope.Dispose();
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString("HH:mm:ss");
        DateText.Text = now.ToString("dd / MM / yyyy");
    }

    private void LayoutSizeChanged(object sender, SizeChangedEventArgs e) => ApplyResponsiveLayout();
    private void ApplyResponsiveLayout()
    {
        if (CatalogPanel is null || ActualWidth <= 0) return;
        // Keep the payment section bounded when Windows scaling reduces usable height.
        PaymentOptionsScroll.MaxHeight = Math.Clamp(ActualHeight * 0.32, 120, 300);
        BasketColumn.Width = new GridLength(Math.Clamp(ActualWidth * 0.36, 420, 580));
        var available = Math.Max(310, ActualWidth - BasketColumn.Width.Value - 128);
        var columns = Math.Clamp((int)(available / 170), 2, 6);
        ProductCardWidth = Math.Max(135, (available - 18) / columns - 10);
    }

    public void StopInput() { _scanTimer.Stop(); _clockTimer.Stop(); }
    private void ReturnHomeClicked(object sender, RoutedEventArgs e) => Window.GetWindow(this)?.Close();
    private async void AllProductsClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Categories.Count > 0) await _viewModel.BrowseCategoryAsync(_viewModel.Categories[0]);
    }
    private void ChooseProductImageClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: POSProductCard card }) return;
        var picker = new OpenFileDialog { Title = "اختيار صورة الصنف", Filter = "صور المنتجات|*.png;*.jpg;*.jpeg", CheckFileExists = true };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            if (new FileInfo(picker.FileName).Length > 5 * 1024 * 1024) throw new InvalidOperationException("اختر صورة لا يتجاوز حجمها 5MB.");
            // Decode once, release the source file and store a small preview suitable for catalog cards.
            using var input = File.OpenRead(picker.FileName);
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 512; bitmap.StreamSource = input; bitmap.EndInit(); bitmap.Freeze();
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = new MemoryStream(); encoder.Save(output);
            _viewModel.SetProductImage(card, output.ToArray());
        }
        catch (Exception ex) { _viewModel.ErrorMessage = $"تعذر حفظ صورة الصنف: {ex.Message}"; }
        FocusScanner();
    }

    private void SearchClicked(object sender, RoutedEventArgs e)
    { ScanBox.Focus(); ScanBox.SelectAll(); }

    private void CancelClicked(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CancelTransactionCommand.CanExecute(null)) return;
        if (MessageBox.Show(Window.GetWindow(this), "إلغاء المعاملة الحالية وتفريغ السلة؟", "إلغاء المعاملة", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        _scanTimer.Stop(); ScanBox.Clear();
        _viewModel.CancelTransactionCommand.Execute(null);
        FocusScanner();
    }

    private async void CustomerProfileClicked(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.HasCreditCustomer) { CustomerBox.Focus(); CustomerBox.IsDropDownOpen = true; return; }
        await _viewModel.ShowCustomerProfileAsync(async id =>
        {
            // The profile's account/payment queries have their own context and lifetime.
            using var scope = new ViewDataScope(_scopeFactory);
            var dialog = scope.Services.GetRequiredService<CustomerAccountDialog>();
            dialog.Owner = Window.GetWindow(this);
            await ((CustomerAccountViewModel)dialog.DataContext).LoadAsync(id);
            dialog.ShowDialog();
        });
    }

    private void FocusScanner()
    {
        if (IsLoaded && _viewModel.CanScan)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (IsLoaded && _viewModel.CanScan) ScanBox.Focus();
            }));
    }
    private void SaleUnitClosed(object sender, EventArgs e) => FocusScanner();
    private void SaleUnitKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; FocusScanner(); }
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
