using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Inventory;
using PharmacyERP.Application.Features.Inventory.DTOs;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Inventory;

/// <summary>ViewModel for the manual Stock Adjustment dialog (correcting a batch's quantity, with a mandatory reason for audit purposes).</summary>
public class StockAdjustmentViewModel : ViewModelBase
{
    private readonly IInventoryService _inventoryService;
    private readonly ICurrentUserService _currentUserService;

    private int _batchId;
    private string _batchDisplayName = string.Empty;
    private int _currentQuantity;
    private int _quantityDelta;
    private string _reason = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public StockAdjustmentViewModel(IInventoryService inventoryService, ICurrentUserService currentUserService)
    {
        _inventoryService = inventoryService;
        _currentUserService = currentUserService;

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public string BatchDisplayName { get => _batchDisplayName; private set => SetProperty(ref _batchDisplayName, value); }
    public int CurrentQuantity { get => _currentQuantity; private set => SetProperty(ref _currentQuantity, value); }

    public int QuantityDelta
    {
        get => _quantityDelta;
        set
        {
            if (SetProperty(ref _quantityDelta, value))
                OnPropertyChanged(nameof(ResultingQuantity));
        }
    }

    public int ResultingQuantity => CurrentQuantity + QuantityDelta;

    public string Reason { get => _reason; set => SetProperty(ref _reason, value); }

    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand SaveCommand { get; }
    public bool SavedSuccessfully { get; private set; }
    public event Action? RequestClose;

    public void Load(int batchId, string batchDisplayName, int currentQuantity)
    {
        _batchId = batchId;
        BatchDisplayName = batchDisplayName;
        CurrentQuantity = currentQuantity;
        QuantityDelta = 0;
    }

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var dto = new StockAdjustmentDto { BatchId = _batchId, QuantityDelta = QuantityDelta, Reason = Reason };
            var result = await _inventoryService.AdjustStockAsync(dto, _currentUserService.UserId);

            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر تنفيذ التعديل.";
                return;
            }

            SavedSuccessfully = true;
            RequestClose?.Invoke();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
