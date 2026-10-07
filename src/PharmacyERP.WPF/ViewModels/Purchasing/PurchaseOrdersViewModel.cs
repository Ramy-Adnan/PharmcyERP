using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Purchasing;
using PharmacyERP.Application.Features.Purchasing.DTOs;
using PharmacyERP.Domain.Enums;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.Views.Purchasing;

namespace PharmacyERP.WPF.ViewModels.Purchasing;

public class PurchaseOrdersViewModel : ViewModelBase
{
    private readonly IPurchasingService _purchasingService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;

    private PurchaseOrderDto? _selectedOrder;
    private bool _isBusy;

    public PurchaseOrdersViewModel(IPurchasingService purchasingService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _purchasingService = purchasingService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        Orders = new ObservableCollection<PurchaseOrderDto>();

        RefreshCommand = new AsyncRelayCommand(LoadOrdersAsync);
        AddOrderCommand = new AsyncRelayCommand(AddOrderAsync, () => CanManage);
        EditOrderCommand = new AsyncRelayCommand(EditOrderAsync, () => CanManage && SelectedOrder is not null && SelectedOrder.Status == PurchaseOrderStatus.Draft);
        SubmitOrderCommand = new AsyncRelayCommand(SubmitOrderAsync, () => CanManage && SelectedOrder is not null && SelectedOrder.Status == PurchaseOrderStatus.Draft);
        CancelOrderCommand = new AsyncRelayCommand(CancelOrderAsync, () => CanManage && SelectedOrder is not null && SelectedOrder.Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Submitted or PurchaseOrderStatus.PartiallyReceived);
        DeleteOrderCommand = new AsyncRelayCommand(DeleteOrderAsync, () => CanManage && SelectedOrder is not null && SelectedOrder.Status == PurchaseOrderStatus.Draft);
    }

    public bool CanManage => _currentUserService.HasPermission("Purchasing.ManageOrders");

    public ObservableCollection<PurchaseOrderDto> Orders { get; }

    public PurchaseOrderDto? SelectedOrder
    {
        get => _selectedOrder;
        set
        {
            if (SetProperty(ref _selectedOrder, value))
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand AddOrderCommand { get; }
    public AsyncRelayCommand EditOrderCommand { get; }
    public AsyncRelayCommand SubmitOrderCommand { get; }
    public AsyncRelayCommand CancelOrderCommand { get; }
    public AsyncRelayCommand DeleteOrderCommand { get; }

    public async Task InitializeAsync() => await LoadOrdersAsync();

    private async Task LoadOrdersAsync()
    {
        IsBusy = true;
        try
        {
            var orders = await _purchasingService.GetPurchaseOrdersAsync();
            Orders.Clear();
            foreach (var o in orders) Orders.Add(o);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddOrderAsync()
    {
        var window = _dialogService.CreateDialog<PurchaseOrderEditDialog>();
        var vm = (PurchaseOrderEditViewModel)window.DataContext;
        await vm.LoadForCreateAsync();

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadOrdersAsync();
    }

    private async Task EditOrderAsync()
    {
        if (SelectedOrder is null) return;

        var window = _dialogService.CreateDialog<PurchaseOrderEditDialog>();
        var vm = (PurchaseOrderEditViewModel)window.DataContext;
        await vm.LoadForEditAsync(SelectedOrder.Id);

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadOrdersAsync();
    }

    private async Task SubmitOrderAsync()
    {
        if (SelectedOrder is null) return;
        if (!_dialogService.Confirm($"هل تريد اعتماد أمر الشراء '{SelectedOrder.Number}'؟ لن يمكن تعديله بعد الاعتماد.")) return;

        var result = await _purchasingService.SubmitPurchaseOrderAsync(SelectedOrder.Id);
        if (!result.Succeeded)
        {
            _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر اعتماد أمر الشراء.");
            return;
        }

        await LoadOrdersAsync();
    }

    private async Task CancelOrderAsync()
    {
        if (SelectedOrder is null) return;
        if (!_dialogService.Confirm($"هل تريد إلغاء أمر الشراء '{SelectedOrder.Number}'؟")) return;

        var result = await _purchasingService.CancelPurchaseOrderAsync(SelectedOrder.Id);
        if (!result.Succeeded)
        {
            _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر إلغاء أمر الشراء.");
            return;
        }

        await LoadOrdersAsync();
    }

    private async Task DeleteOrderAsync()
    {
        if (SelectedOrder is null) return;
        if (!_dialogService.Confirm($"هل أنت متأكد من حذف أمر الشراء '{SelectedOrder.Number}'؟")) return;

        var result = await _purchasingService.DeletePurchaseOrderAsync(SelectedOrder.Id);
        if (!result.Succeeded)
        {
            _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر حذف أمر الشراء.");
            return;
        }

        await LoadOrdersAsync();
    }
}
