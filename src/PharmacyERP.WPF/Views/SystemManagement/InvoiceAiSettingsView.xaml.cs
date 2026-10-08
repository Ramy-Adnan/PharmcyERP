using System.Windows;
using System.Windows.Controls;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.ViewModels.SystemManagement;

namespace PharmacyERP.WPF.Views.SystemManagement;

public partial class InvoiceAiSettingsView : UserControl
{
    private readonly InvoiceAiSettingsViewModel _viewModel;
    private bool _initialized;
    public InvoiceAiSettingsView(InvoiceAiSettingsViewModel viewModel, IDialogService dialogs)
    {
        _viewModel = viewModel; InitializeComponent(); DataContext = viewModel;
        viewModel.ClearPasswordRequested += (_, _) => ApiKeyBox.Clear();
        viewModel.SaveCompleted += (_, result) =>
        {
            if (!IsLoaded) return;
            if (result.Succeeded) dialogs.ShowInfo(result.Message, "إعدادات قراءة الصور");
            else dialogs.ShowError(result.Message, "إعدادات قراءة الصور");
        };
        Loaded += (_, _) => { if (!_initialized) { viewModel.Initialize(); _initialized = true; } };
        Unloaded += (_, _) => { ApiKeyBox.Clear(); viewModel.NewApiKey = string.Empty; };
    }
    private void ApiKeyChanged(object sender, RoutedEventArgs e) => _viewModel.NewApiKey = ApiKeyBox.Password;
}
