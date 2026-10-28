using System.Collections.ObjectModel;
using PharmacyERP.Application.Features.Inventory;
using PharmacyERP.Application.Features.Inventory.DTOs;
using PharmacyERP.Domain.Enums;
using PharmacyERP.Domain.Common;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Inventory;

public class LookupOption
{
    public int? Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

/// <summary>ViewModel for the Add/Edit Item (medicine master record) dialog.</summary>
public class ItemEditViewModel : PurchasePricingViewModel
{
    private readonly IInventoryService _inventoryService;

    private int? _id;
    private string _code = string.Empty;
    private string? _barcode, _baseUnitBarcode;
    private int _unitsPerPackage = 1;
    private string _packageUnitName = "علبة";
    private string _name = string.Empty;
    private string? _genericName;
    private string? _strength;
    private ItemForm _form = ItemForm.Tablet;
    private int _categoryId;
    private int _unitOfMeasureId;
    private int? _manufacturerId;
    private bool _requiresPrescription;
    private bool _isControlledSubstance;
    private decimal _defaultSalePrice;
    private decimal _defaultPurchasePrice;
    private decimal _taxRatePercent;
    private int _reorderPoint;
    private int _minStockLevel;
    private int _maxStockLevel;
    private bool _isActive = true;
    private string _errorMessage = string.Empty;
    private bool _isBusy;
    private string _successMessage = string.Empty;

    public ItemEditViewModel(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService;

        SaleUnits = new ObservableCollection<ItemSaleUnitDto>();
        AddSaleUnitCommand = new RelayCommand(() => SaleUnits.Add(new ItemSaleUnitDto()));
        Categories = new ObservableCollection<LookupOption>();
        Units = new ObservableCollection<LookupOption>();
        Manufacturers = new ObservableCollection<LookupOption>();
        Forms = new ObservableCollection<ItemForm>(Enum.GetValues<ItemForm>());

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
        CalculateSalePriceCommand = new RelayCommand(() => DefaultSalePrice = SalePricePolicy.FromPurchasePrice(DefaultPurchasePrice, PurchaseType));
    }

    protected override void OnPurchaseTypeChanged() => DefaultSalePrice = SalePricePolicy.FromPurchasePrice(DefaultPurchasePrice, PurchaseType);

    public bool IsEditMode => _id.HasValue;
    public string DialogTitle => IsEditMode ? "تعديل بطاقة الصنف" : "إضافة صنف جديد";

    public ObservableCollection<ItemSaleUnitDto> SaleUnits { get; }
    public RelayCommand AddSaleUnitCommand { get; }
    public ObservableCollection<LookupOption> Categories { get; }
    public ObservableCollection<LookupOption> Units { get; }
    public ObservableCollection<LookupOption> Manufacturers { get; }
    public ObservableCollection<ItemForm> Forms { get; }

    public string Code { get => _code; set => SetProperty(ref _code, value); }
    public string? Barcode { get => _barcode; set => SetProperty(ref _barcode, value); }
    public string? BaseUnitBarcode { get => _baseUnitBarcode; set => SetProperty(ref _baseUnitBarcode, value); }
    public int UnitsPerPackage { get => _unitsPerPackage; set => SetProperty(ref _unitsPerPackage, value); }
    public string PackageUnitName { get => _packageUnitName; set => SetProperty(ref _packageUnitName, value); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string? GenericName { get => _genericName; set => SetProperty(ref _genericName, value); }
    public string? Strength { get => _strength; set => SetProperty(ref _strength, value); }
    public ItemForm Form { get => _form; set => SetProperty(ref _form, value); }
    public int CategoryId { get => _categoryId; set => SetProperty(ref _categoryId, value); }
    public int UnitOfMeasureId { get => _unitOfMeasureId; set => SetProperty(ref _unitOfMeasureId, value); }
    public int? ManufacturerId { get => _manufacturerId; set => SetProperty(ref _manufacturerId, value); }
    public bool RequiresPrescription { get => _requiresPrescription; set => SetProperty(ref _requiresPrescription, value); }
    public bool IsControlledSubstance { get => _isControlledSubstance; set => SetProperty(ref _isControlledSubstance, value); }
    public decimal DefaultSalePrice { get => _defaultSalePrice; set => SetProperty(ref _defaultSalePrice, value); }
    public decimal DefaultPurchasePrice
    {
        get => _defaultPurchasePrice;
        set
        {
            if (SetProperty(ref _defaultPurchasePrice, value))
                DefaultSalePrice = SalePricePolicy.FromPurchasePrice(value, PurchaseType);
        }
    }
    public decimal TaxRatePercent { get => _taxRatePercent; set => SetProperty(ref _taxRatePercent, value); }
    public int ReorderPoint { get => _reorderPoint; set => SetProperty(ref _reorderPoint, value); }
    public int MinStockLevel { get => _minStockLevel; set => SetProperty(ref _minStockLevel, value); }
    public int MaxStockLevel { get => _maxStockLevel; set => SetProperty(ref _maxStockLevel, value); }
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; set { if (SetProperty(ref _isBusy, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }
    public string SuccessMessage { get => _successMessage; private set => SetProperty(ref _successMessage, value); }

    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand CalculateSalePriceCommand { get; }
    public bool SavedSuccessfully { get; private set; }
    public int? SavedItemId { get; private set; }
    public string SavedItemCode { get; private set; } = string.Empty;
    public string SavedItemName { get; private set; } = string.Empty;
    public bool ReceiveStockAfterSave { get; private set; }
    public bool CanReceiveStock { get; set; }
    public event Action? NewItemReady;
    public event Action? RequestClose;

    public async Task LoadForCreateAsync()
    {
        ResetForNextItem();
        SavedSuccessfully = false; SavedItemId = null; ReceiveStockAfterSave = false;
        SuccessMessage = ErrorMessage = string.Empty;
        PurchaseType = PurchasePricingType.Other;
        DefaultPurchasePrice = 0;
        DefaultSalePrice = 0;
        IsActive = true;
        await LoadLookupsAsync();
    }

    public async Task LoadForEditAsync(int itemId)
    {
        await LoadLookupsAsync();

        var dto = await _inventoryService.GetItemForEditAsync(itemId);
        if (dto is null) return;

        _id = dto.Id;
        Code = dto.Code;
        Barcode = dto.Barcode;
        BaseUnitBarcode = dto.BaseUnitBarcode;
        UnitsPerPackage = dto.UnitsPerPackage;
        SaleUnits.Clear(); foreach (var unit in dto.SaleUnits) SaleUnits.Add(unit);
        PackageUnitName = dto.PackageUnitName;
        Name = dto.Name;
        GenericName = dto.GenericName;
        Strength = dto.Strength;
        Form = dto.Form;
        CategoryId = dto.CategoryId;
        UnitOfMeasureId = dto.UnitOfMeasureId;
        ManufacturerId = dto.ManufacturerId;
        RequiresPrescription = dto.RequiresPrescription;
        IsControlledSubstance = dto.IsControlledSubstance;
        PurchaseType = dto.PurchaseType;
        DefaultPurchasePrice = dto.DefaultPurchasePrice;
        DefaultSalePrice = dto.DefaultSalePrice ?? SalePricePolicy.FromPurchasePrice(dto.DefaultPurchasePrice, PurchaseType);
        TaxRatePercent = dto.TaxRatePercent;
        ReorderPoint = dto.ReorderPoint;
        MinStockLevel = dto.MinStockLevel;
        MaxStockLevel = dto.MaxStockLevel;
        IsActive = dto.IsActive;

        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(DialogTitle));
    }

    private async Task LoadLookupsAsync()
    {
        var categories = await _inventoryService.GetCategoriesAsync();
        Categories.Clear();
        foreach (var c in categories.Where(c => c.IsActive)) Categories.Add(new LookupOption { Id = c.Id, Name = c.Name });

        var units = await _inventoryService.GetUnitsAsync();
        Units.Clear();
        foreach (var u in units.Where(u => u.IsActive)) Units.Add(new LookupOption { Id = u.Id, Name = u.Name });

        var manufacturers = await _inventoryService.GetManufacturersAsync();
        Manufacturers.Clear();
        Manufacturers.Add(new LookupOption { Id = null, Name = "(بدون تحديد)" });
        foreach (var m in manufacturers.Where(m => m.IsActive)) Manufacturers.Add(new LookupOption { Id = m.Id, Name = m.Name });

        if (CategoryId == 0 && Categories.Count > 0) CategoryId = Categories.First().Id!.Value;
        if (UnitOfMeasureId == 0 && Units.Count > 0) UnitOfMeasureId = Units.First().Id!.Value;
    }

    public async Task SaveAsync()
    {
        if (await SaveCoreAsync()) RequestClose?.Invoke();
    }

    public async Task SaveAndReceiveAsync()
    {
        if (IsEditMode || !CanReceiveStock || !await SaveCoreAsync()) return;
        ReceiveStockAfterSave = true;
        RequestClose?.Invoke();
    }

    public async Task SaveAndAddAnotherAsync()
    {
        if (IsEditMode || !await SaveCoreAsync()) return;
        SuccessMessage = $"تم حفظ الصنف «{SavedItemName}». أدخل الصنف التالي؛ التصنيف والوحدة محفوظان لتسريع الإدخال.";
        ResetForNextItem();
        NewItemReady?.Invoke();
    }

    private void ResetForNextItem()
    {
        _id = null;
        Code = Name = string.Empty; Barcode = BaseUnitBarcode = GenericName = Strength = null;
        ManufacturerId = null; RequiresPrescription = IsControlledSubstance = false;
        UnitsPerPackage = 1; PackageUnitName = "علبة"; SaleUnits.Clear();
        DefaultPurchasePrice = DefaultSalePrice = TaxRatePercent = 0;
        ReorderPoint = MinStockLevel = MaxStockLevel = 0; IsActive = true;
        OnPropertyChanged(nameof(IsEditMode)); OnPropertyChanged(nameof(DialogTitle));
    }

    private async Task<bool> SaveCoreAsync()
    {
        if (IsBusy) return false;
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var dto = new ItemUpsertDto
            {
                Id = _id,
                Code = Code,
                Barcode = Barcode,
                BaseUnitBarcode = BaseUnitBarcode,
                UnitsPerPackage = UnitsPerPackage,
                SaleUnits = SaleUnits.ToList(),
                PackageUnitName = PackageUnitName,
                Name = Name,
                GenericName = GenericName,
                Strength = Strength,
                Form = Form,
                CategoryId = CategoryId,
                UnitOfMeasureId = UnitOfMeasureId,
                ManufacturerId = ManufacturerId,
                RequiresPrescription = RequiresPrescription,
                IsControlledSubstance = IsControlledSubstance,
                DefaultSalePrice = DefaultSalePrice,
                PurchaseType = PurchaseType,
                DefaultPurchasePrice = DefaultPurchasePrice,
                TaxRatePercent = TaxRatePercent,
                ReorderPoint = ReorderPoint,
                MinStockLevel = MinStockLevel,
                MaxStockLevel = MaxStockLevel,
                IsActive = IsActive
            };

            var result = _id.HasValue
                ? await _inventoryService.UpdateItemAsync(dto)
                : await _inventoryService.CreateItemAsync(dto);

            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر حفظ الصنف.";
                return false;
            }

            SavedItemId = result.Value!.Id;
            SavedItemCode = result.Value.Code; SavedItemName = result.Value.Name;
            SavedSuccessfully = true;
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"تعذر حفظ الصنف: {ex.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
