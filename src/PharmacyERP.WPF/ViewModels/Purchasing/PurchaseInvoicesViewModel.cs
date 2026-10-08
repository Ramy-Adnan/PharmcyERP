using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Purchasing;
using PharmacyERP.Application.Features.Purchasing.DTOs;
using PharmacyERP.Domain.Enums;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.Views.Purchasing;

namespace PharmacyERP.WPF.ViewModels.Purchasing;

public class PurchaseInvoicesViewModel : ViewModelBase
{
    private readonly IPurchasingService _purchasingService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;

    private PurchaseInvoiceDto? _selectedInvoice;
    private bool _isBusy;

    public PurchaseInvoicesViewModel(IPurchasingService purchasingService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _purchasingService = purchasingService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        Invoices = new ObservableCollection<PurchaseInvoiceDto>();

        RefreshCommand = new AsyncRelayCommand(LoadInvoicesAsync, () => !IsBusy);
        AddInvoiceCommand = new AsyncRelayCommand(AddInvoiceAsync, () => !IsBusy && CanManage);
        RecordPaymentCommand = new AsyncRelayCommand(RecordPaymentAsync,
            () => !IsBusy && CanManage && SelectedInvoice is not null && SelectedInvoice.Status is PurchaseInvoiceStatus.Unpaid or PurchaseInvoiceStatus.PartiallyPaid);
        CancelInvoiceCommand = new AsyncRelayCommand(CancelInvoiceAsync,
            () => !IsBusy && CanManage && SelectedInvoice is not null && SelectedInvoice.Status is PurchaseInvoiceStatus.Unpaid or PurchaseInvoiceStatus.PartiallyPaid);
    }

    public bool CanManage => _currentUserService.HasPermission("Purchasing.ManageInvoices");

    public ObservableCollection<PurchaseInvoiceDto> Invoices { get; }

    public PurchaseInvoiceDto? SelectedInvoice
    {
        get => _selectedInvoice;
        set
        {
            if (SetProperty(ref _selectedInvoice, value))
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { if (SetProperty(ref _isBusy, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand AddInvoiceCommand { get; }
    public AsyncRelayCommand RecordPaymentCommand { get; }
    public AsyncRelayCommand CancelInvoiceCommand { get; }

    public async Task InitializeAsync() => await LoadInvoicesAsync();

    private async Task LoadInvoicesAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var invoices = await _purchasingService.GetPurchaseInvoicesAsync();
            Invoices.Clear();
            foreach (var i in invoices) Invoices.Add(i);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddInvoiceAsync()
    {
        var window = _dialogService.CreateDialog<PurchaseInvoiceEditDialog>();
        var vm = (PurchaseInvoiceEditViewModel)window.DataContext;
        await vm.LoadForCreateAsync();

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadInvoicesAsync();
    }

    private async Task RecordPaymentAsync()
    {
        if (SelectedInvoice is null) return;

        var window = _dialogService.CreateDialog<RecordPaymentDialog>();
        var vm = (RecordPaymentViewModel)window.DataContext;
        vm.Load(SelectedInvoice.Id, SelectedInvoice.Number, SelectedInvoice.AmountDue);

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadInvoicesAsync();
    }

    private async Task CancelInvoiceAsync()
    {
        if (SelectedInvoice is null) return;
        if (!_dialogService.Confirm($"هل تريد إلغاء فاتورة الشراء '{SelectedInvoice.Number}'؟")) return;

        var result = await _purchasingService.CancelPurchaseInvoiceAsync(SelectedInvoice.Id);
        if (!result.Succeeded)
        {
            _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر إلغاء الفاتورة.");
            return;
        }

        await LoadInvoicesAsync();
    }
}
