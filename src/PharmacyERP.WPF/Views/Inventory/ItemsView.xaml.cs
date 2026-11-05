using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.ViewModels.Inventory;

namespace PharmacyERP.WPF.Views.Inventory;

public partial class ItemsView : UserControl
{
    private readonly IServiceScopeFactory _scopeFactory;
    private ViewDataScope? _dataScope;
    private ItemsViewModel? _viewModel;
    private string _savedSearch = string.Empty;

    public ItemsView(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        InitializeComponent();
        Loaded += ViewLoaded;
        Unloaded += ViewUnloaded;
    }

    private async void ViewLoaded(object sender, RoutedEventArgs e)
    {
        if (_dataScope is not null) return;
        var scope = new ViewDataScope(_scopeFactory);
        try
        {
            var viewModel = scope.Services.GetRequiredService<ItemsViewModel>();
            _dataScope = scope; _viewModel = viewModel;
            viewModel.SearchText = _savedSearch;
            DataContext = viewModel;
            await viewModel.InitializeAsync();
        }
        catch
        {
            if (ReferenceEquals(_dataScope, scope)) { _dataScope = null; _viewModel = null; DataContext = null; }
            scope.Dispose();
            throw;
        }
    }

    private async void ViewUnloaded(object sender, RoutedEventArgs e)
    {
        // Detach the retired instance before awaiting. A new Loaded event may
        // create another scope while the old query is still finishing.
        var scope = _dataScope;
        var viewModel = _viewModel;
        _dataScope = null; _viewModel = null;
        if (viewModel is null) { scope?.Dispose(); return; }
        _savedSearch = viewModel.SearchText;
        if (ReferenceEquals(DataContext, viewModel)) DataContext = null;
        viewModel.Suspend();
        try { await viewModel.WaitForIdleAsync(); }
        finally { scope?.Dispose(); }
    }
}
