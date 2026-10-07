using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Accounting;
using PharmacyERP.Application.Features.Accounting.DTOs;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.Views.Accounting;

namespace PharmacyERP.WPF.ViewModels.Accounting;

/// <summary>Combined screen for general Receipts and Payments — both are simple append-only cash/bank movement logs shown side by side.</summary>
public class ReceiptsAndPaymentsViewModel : ViewModelBase
{
    private readonly IAccountingService _accountingService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;

    private bool _isBusy;

    public ReceiptsAndPaymentsViewModel(IAccountingService accountingService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _accountingService = accountingService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        Receipts = new ObservableCollection<ReceiptDto>();
        Payments = new ObservableCollection<PaymentDto>();

        RefreshCommand = new AsyncRelayCommand(LoadAllAsync);
        AddReceiptCommand = new AsyncRelayCommand(AddReceiptAsync, () => CanManage);
        AddPaymentCommand = new AsyncRelayCommand(AddPaymentAsync, () => CanManage);
    }

    public bool CanManage => _currentUserService.HasPermission("Accounting.ManageReceiptsPayments");

    public ObservableCollection<ReceiptDto> Receipts { get; }
    public ObservableCollection<PaymentDto> Payments { get; }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand AddReceiptCommand { get; }
    public AsyncRelayCommand AddPaymentCommand { get; }

    public async Task InitializeAsync() => await LoadAllAsync();

    private async Task LoadAllAsync()
    {
        IsBusy = true;
        try
        {
            var receipts = await _accountingService.GetReceiptsAsync();
            Receipts.Clear();
            foreach (var r in receipts) Receipts.Add(r);

            var payments = await _accountingService.GetPaymentsAsync();
            Payments.Clear();
            foreach (var p in payments) Payments.Add(p);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddReceiptAsync()
    {
        var window = _dialogService.CreateDialog<ReceiptEditDialog>();
        var vm = (ReceiptEditViewModel)window.DataContext;
        await vm.LoadAsync();

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadAllAsync();
    }

    private async Task AddPaymentAsync()
    {
        var window = _dialogService.CreateDialog<PaymentEditDialog>();
        var vm = (PaymentEditViewModel)window.DataContext;
        await vm.LoadAsync();

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadAllAsync();
    }
}
