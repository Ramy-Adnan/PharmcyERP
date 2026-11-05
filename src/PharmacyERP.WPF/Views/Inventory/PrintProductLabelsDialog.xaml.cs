using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.ViewModels.Inventory;

namespace PharmacyERP.WPF.Views.Inventory;

public partial class PrintProductLabelsDialog : Window
{
    private readonly ViewDataScope _scope;
    private readonly PrintProductLabelsViewModel _viewModel;
    private readonly ProductLabelPrinter _printer;
    private bool _printing, _closeRequested, _closeApproved;

    public PrintProductLabelsDialog(IServiceScopeFactory scopeFactory, ProductLabelPrinter printer)
    {
        _scope = new ViewDataScope(scopeFactory); _printer = printer;
        try
        {
            _viewModel = _scope.Services.GetRequiredService<PrintProductLabelsViewModel>();
            InitializeComponent(); DataContext = _viewModel;
            _viewModel.PropertyChanged += ViewModelChanged;
            Loaded += (_, _) => UpdatePreview();
            Closed += (_, _) => { _viewModel.PropertyChanged -= ViewModelChanged; _scope.Dispose(); };
            PreviewKeyDown += async (_, e) =>
            {
                if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control)
                { e.Handled = true; await PrintAsync(); }
            };
        }
        catch { _scope.Dispose(); throw; }
    }

    private void ViewModelChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(PrintProductLabelsViewModel.PreviewLabel)) UpdatePreview(); }
    private void UpdatePreview()
    {
        PreviewHost.Child = null; PreviewError.Text = string.Empty;
        if (_viewModel.PreviewLabel is not { } request) return;
        try { PreviewHost.Child = ProductLabelPreview.Create(request); }
        catch (ArgumentException ex) { PreviewError.Text = ex.Message; }
    }
    private static bool HasValidationError(DependencyObject node)
    {
        if (Validation.GetHasError(node)) return true;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            if (HasValidationError(VisualTreeHelper.GetChild(node, i))) return true;
        return false;
    }
    private async Task PrintAsync()
    {
        if (_printing || _closeRequested || !_viewModel.CanPrint) return;
        if (HasValidationError(this)) { StatusText.Text = "صحح عدد الملصقات والمقاس قبل الطباعة."; return; }
        _printing = true; StatusText.Text = string.Empty;
        try
        {
            var request = await _viewModel.PreparePrintAsync();
            if (request is null) return;
            // Validate the complete layout before showing the Windows print dialog.
            ProductLabelDocumentBuilder.BuildLabel(request);
            if (_printer.Print(request)) StatusText.Text = $"أُرسلت {request.Copies} ملصقات إلى الطابعة.";
        }
        catch (Exception ex) { StatusText.Text = $"تعذرت الطباعة: {ex.Message}"; }
        finally { _printing = false; }
    }
    private async void PrintClicked(object sender, RoutedEventArgs e) => await PrintAsync();
    private async void RefreshClicked(object sender, RoutedEventArgs e)
    { if (!_printing && !_closeRequested) { StatusText.Text = string.Empty; await _viewModel.RefreshAsync(); UpdatePreview(); } }
    private void CloseClicked(object sender, RoutedEventArgs e) => Close();
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_closeApproved) { base.OnClosing(e); return; }
        e.Cancel = true; base.OnClosing(e);
        if (_printing || _closeRequested) return;
        _closeRequested = true; IsEnabled = false;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () =>
        {
            await _viewModel.PrepareToCloseAsync();
            _closeApproved = true; Close();
        }));
    }
}
