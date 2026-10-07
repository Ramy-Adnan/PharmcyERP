using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using PharmacyERP.Application.Features.Sales.DTOs;
using PharmacyERP.Domain.Enums;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.ViewModels.SystemManagement;

namespace PharmacyERP.WPF.Views.SystemManagement;

public partial class ReceiptSettingsView : UserControl
{
    private readonly ReceiptSettingsViewModel _viewModel;
    private readonly IDialogService _dialogs;
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool _initialized;

    public ReceiptSettingsView(ReceiptSettingsViewModel viewModel, IDialogService dialogs)
    {
        InitializeComponent(); DataContext = _viewModel = viewModel;
        _dialogs = dialogs;
        _previewTimer.Tick += (_, _) => { _previewTimer.Stop(); UpdatePreview(); };
        Loaded += (_, _) =>
        {
            _viewModel.PropertyChanged += SettingsChanged;
            _viewModel.SaveCompleted += SaveCompleted;
            if (!_initialized) { _viewModel.Initialize(); _initialized = true; }
            UpdatePreview();
        };
        Unloaded += (_, _) =>
        {
            _previewTimer.Stop(); _viewModel.PropertyChanged -= SettingsChanged;
            _viewModel.SaveCompleted -= SaveCompleted;
        };
    }

    private void SaveCompleted(object? sender, ReceiptSettingsSaveResult result)
    {
        if (result.Succeeded) _dialogs.ShowInfo(result.Message, "تم حفظ الإعدادات");
        else _dialogs.ShowError(result.Message, "تعذر حفظ الإعدادات");
    }

    private void SettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ReceiptSettingsViewModel.StatusMessage) or nameof(ReceiptSettingsViewModel.HasUnsavedChanges)) return;
        _previewTimer.Stop(); _previewTimer.Start();
    }

    private void ChooseLogo_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanManage) return;
        var dialog = new OpenFileDialog { Title = "اختيار شعار الصيدلية", Filter = "صور الشعار (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg", Multiselect = false };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { _viewModel.SetLogo(ReceiptLogo.Import(dialog.FileName)); }
        catch (Exception ex) { _viewModel.ShowError($"تعذر قراءة الشعار: {ex.Message}"); }
    }

    private void UpdatePreview()
    {
        var sample = new SalesInvoiceDetailDto
        {
            Header = new()
            {
                Number = "SI-DEMO-001", BranchName = _viewModel.BranchName, CashierName = "الصيدلاني",
                CustomerName = "عميل مسجل", SaleAtUtc = DateTime.UtcNow,
                SubTotal = 6000, TotalAmount = 6000, PaymentMethod = PaymentMethod.Credit, InitialPaymentAmount = 4000
            },
            Lines = new()
            {
                new() { ItemName = "باراسيتامول 500mg", Quantity = 2, UnitPrice = 2000, LineTotal = 4000 },
                new() { ItemName = "فيتامين C", Quantity = 1, UnitPrice = 2000, LineTotal = 2000 }
            }
        };
        try { ReceiptPreview.Document = ReceiptDocumentBuilder.Build(sample, _viewModel.Snapshot()); }
        catch (Exception ex) { _viewModel.ShowError($"تعذرت المعاينة. احذف الشعار أو اختر صورة جديدة. {ex.Message}"); }
    }
}
