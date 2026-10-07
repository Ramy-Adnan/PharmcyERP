using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Sales;
using PharmacyERP.Application.Features.Sales.DTOs;
using PharmacyERP.Domain.Enums;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.Views.Sales;

namespace PharmacyERP.WPF.ViewModels.Sales;

public class SalesInvoicesViewModel : ViewModelBase
{
    private readonly ISalesService _salesService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;

    private SalesInvoiceDto? _selectedInvoice;
    private bool _isBusy;

    public SalesInvoicesViewModel(ISalesService salesService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _salesService = salesService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        Invoices = new ObservableCollection<SalesInvoiceDto>();

        RefreshCommand = new AsyncRelayCommand(LoadInvoicesAsync);
        OpenDetailCommand = new AsyncRelayCommand(OpenDetailAsync, () => SelectedInvoice is not null);
    }

    public bool CanViewInvoices => _currentUserService.HasPermission("Sales.ViewInvoices");

    public ObservableCollection<SalesInvoiceDto> Invoices { get; }

    public SalesInvoiceDto? SelectedInvoice
    {
        get => _selectedInvoice;
        set { if (SetProperty(ref _selectedInvoice, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand OpenDetailCommand { get; }

    public async Task InitializeAsync() => await LoadInvoicesAsync();

    private async Task LoadInvoicesAsync()
    {
        IsBusy = true;
        try
        {
            var invoices = await _salesService.GetSalesInvoicesAsync();
            Invoices.Clear();
            foreach (var i in invoices) Invoices.Add(i);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OpenDetailAsync()
    {
        if (SelectedInvoice is null) return;

        var window = _dialogService.CreateDialog<SalesInvoiceDetailDialog>();
        var vm = (SalesInvoiceDetailViewModel)window.DataContext;
        await vm.LoadAsync(SelectedInvoice.Id);

        _dialogService.ShowDialog(window);
        await LoadInvoicesAsync();
    }
}
