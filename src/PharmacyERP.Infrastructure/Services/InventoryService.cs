using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Inventory;
using PharmacyERP.Application.Features.Inventory.DTOs;
using PharmacyERP.Domain.Entities;
using PharmacyERP.Domain.Common;
using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Infrastructure.Services;

public class InventoryService : IInventoryService
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTime _dateTime;

    public InventoryService(IApplicationDbContext context, IDateTime dateTime)
    {
        _context = context;
        _dateTime = dateTime;
    }

    // ===================== Categories =====================

    public async Task<List<ItemCategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var categories = await _context.ItemCategories.Include(c => c.Items).OrderBy(c => c.Name).ToListAsync(cancellationToken);
        return categories.Select(c => new ItemCategoryDto { Id = c.Id, Code = c.Code, Name = c.Name, IsActive = c.IsActive, ItemCount = c.Items.Count }).ToList();
    }

    public async Task<Result<ItemCategoryDto>> UpsertCategoryAsync(int? id, string code, string name, bool isActive, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return Result<ItemCategoryDto>.Failure("رمز التصنيف مطلوب.");
        if (string.IsNullOrWhiteSpace(name)) return Result<ItemCategoryDto>.Failure("اسم التصنيف مطلوب.");

        var codeTaken = await _context.ItemCategories.AnyAsync(c => c.Code == code.Trim() && c.Id != id, cancellationToken);
        if (codeTaken) return Result<ItemCategoryDto>.Failure("رمز التصنيف مستخدم مسبقاً.");

        ItemCategory entity;
        if (id.HasValue)
        {
            entity = await _context.ItemCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("التصنيف غير موجود.");
        }
        else
        {
            entity = new ItemCategory();
            _context.ItemCategories.Add(entity);
        }

        entity.Code = code.Trim();
        entity.Name = name.Trim();
        entity.IsActive = isActive;

        await _context.SaveChangesAsync(cancellationToken);
        return Result<ItemCategoryDto>.Success(new ItemCategoryDto { Id = entity.Id, Code = entity.Code, Name = entity.Name, IsActive = entity.IsActive });
    }

    public async Task<Result> DeleteCategoryAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.ItemCategories.Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (entity is null) return Result.Failure("التصنيف غير موجود.");
        if (entity.Items.Any()) return Result.Failure("لا يمكن حذف تصنيف مرتبط بأصناف. عطّله بدلاً من حذفه.");

        _context.ItemCategories.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // ===================== Units of Measure =====================

    public async Task<List<UnitOfMeasureDto>> GetUnitsAsync(CancellationToken cancellationToken = default)
    {
        var units = await _context.UnitsOfMeasure.Include(u => u.Items).OrderBy(u => u.Name).ToListAsync(cancellationToken);
        return units.Select(u => new UnitOfMeasureDto { Id = u.Id, Code = u.Code, Name = u.Name, IsActive = u.IsActive, ItemCount = u.Items.Count }).ToList();
    }

    public async Task<Result<UnitOfMeasureDto>> UpsertUnitAsync(int? id, string code, string name, bool isActive, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return Result<UnitOfMeasureDto>.Failure("رمز الوحدة مطلوب.");
        if (string.IsNullOrWhiteSpace(name)) return Result<UnitOfMeasureDto>.Failure("اسم الوحدة مطلوب.");

        var codeTaken = await _context.UnitsOfMeasure.AnyAsync(u => u.Code == code.Trim() && u.Id != id, cancellationToken);
        if (codeTaken) return Result<UnitOfMeasureDto>.Failure("رمز الوحدة مستخدم مسبقاً.");

        UnitOfMeasure entity;
        if (id.HasValue)
        {
            entity = await _context.UnitsOfMeasure.FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("الوحدة غير موجودة.");
        }
        else
        {
            entity = new UnitOfMeasure();
            _context.UnitsOfMeasure.Add(entity);
        }

        entity.Code = code.Trim();
        entity.Name = name.Trim();
        entity.IsActive = isActive;

        await _context.SaveChangesAsync(cancellationToken);
        return Result<UnitOfMeasureDto>.Success(new UnitOfMeasureDto { Id = entity.Id, Code = entity.Code, Name = entity.Name, IsActive = entity.IsActive });
    }

    public async Task<Result> DeleteUnitAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.UnitsOfMeasure.Include(u => u.Items).FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (entity is null) return Result.Failure("الوحدة غير موجودة.");
        if (entity.Items.Any()) return Result.Failure("لا يمكن حذف وحدة مرتبطة بأصناف. عطّلها بدلاً من حذفها.");

        _context.UnitsOfMeasure.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // ===================== Manufacturers =====================

    public async Task<List<ManufacturerDto>> GetManufacturersAsync(CancellationToken cancellationToken = default)
    {
        var manufacturers = await _context.Manufacturers.Include(m => m.Items).OrderBy(m => m.Name).ToListAsync(cancellationToken);
        return manufacturers.Select(m => new ManufacturerDto { Id = m.Id, Name = m.Name, Country = m.Country, IsActive = m.IsActive, ItemCount = m.Items.Count }).ToList();
    }

    public async Task<Result<ManufacturerDto>> UpsertManufacturerAsync(int? id, string name, string? country, bool isActive, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return Result<ManufacturerDto>.Failure("اسم الشركة المصنّعة مطلوب.");

        var nameTaken = await _context.Manufacturers.AnyAsync(m => m.Name == name.Trim() && m.Id != id, cancellationToken);
        if (nameTaken) return Result<ManufacturerDto>.Failure("اسم الشركة مستخدم مسبقاً.");

        Manufacturer entity;
        if (id.HasValue)
        {
            entity = await _context.Manufacturers.FirstOrDefaultAsync(m => m.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("الشركة غير موجودة.");
        }
        else
        {
            entity = new Manufacturer();
            _context.Manufacturers.Add(entity);
        }

        entity.Name = name.Trim();
        entity.Country = country;
        entity.IsActive = isActive;

        await _context.SaveChangesAsync(cancellationToken);
        return Result<ManufacturerDto>.Success(new ManufacturerDto { Id = entity.Id, Name = entity.Name, Country = entity.Country, IsActive = entity.IsActive });
    }

    public async Task<Result> DeleteManufacturerAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Manufacturers.Include(m => m.Items).FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (entity is null) return Result.Failure("الشركة غير موجودة.");
        if (entity.Items.Any()) return Result.Failure("لا يمكن حذف شركة مرتبطة بأصناف. عطّلها بدلاً من حذفها.");

        _context.Manufacturers.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // ===================== Items =====================

    public async Task<List<ItemDto>> GetItemsAsync(string? searchText = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Items
            .Include(i => i.Category)
            .Include(i => i.UnitOfMeasure)
            .Include(i => i.Manufacturer)
            .Include(i => i.Batches)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var term = searchText.Trim();
            query = query.Where(i =>
                i.Name.Contains(term) ||
                i.Code.Contains(term) ||
                (i.Manufacturer != null && i.Manufacturer.Name.Contains(term)) ||
                i.BaseUnitBarcode == term ||
                (i.Barcode != null && i.Barcode.Contains(term)) ||
                (i.GenericName != null && i.GenericName.Contains(term)));
        }

        var items = await query.OrderBy(i => i.Name).ToListAsync(cancellationToken);

        return items.Select(i => new ItemDto
        {
            Id = i.Id,
            Code = i.Code,
            Barcode = i.Barcode?.Trim(),
            BaseUnitBarcode = i.BaseUnitBarcode?.Trim(),
            UnitsPerPackage = i.UnitsPerPackage,
            PackageUnitName = i.PackageUnitName.Trim(),
            Name = i.Name,
            GenericName = i.GenericName,
            Strength = i.Strength,
            Form = i.Form,
            CategoryName = i.Category.Name,
            UnitOfMeasureName = i.UnitOfMeasure.Name,
            ManufacturerName = i.Manufacturer?.Name,
            RequiresPrescription = i.RequiresPrescription,
            IsControlledSubstance = i.IsControlledSubstance,
            DefaultSalePrice = i.DefaultSalePrice,
            PurchaseType = i.PurchaseType,
            DefaultPurchasePrice = i.DefaultPurchasePrice,
            TaxRatePercent = i.TaxRatePercent,
            ReorderPoint = i.ReorderPoint,
            MinStockLevel = i.MinStockLevel,
            MaxStockLevel = i.MaxStockLevel,
            IsActive = i.IsActive,
            TotalQuantityOnHand = i.Batches.Sum(b => b.QuantityOnHand)
        }).ToList();
    }

    public async Task<ItemUpsertDto?> GetItemForEditAsync(int id, CancellationToken cancellationToken = default)
    {
        var item = await _context.Items.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null) return null;

        return new ItemUpsertDto
        {
            Id = item.Id,
            Code = item.Code,
            Barcode = item.Barcode?.Trim(),
            BaseUnitBarcode = item.BaseUnitBarcode?.Trim(),
            UnitsPerPackage = item.UnitsPerPackage,
            PackageUnitName = item.PackageUnitName.Trim(),
            Name = item.Name,
            GenericName = item.GenericName,
            Strength = item.Strength,
            Form = item.Form,
            CategoryId = item.CategoryId,
            UnitOfMeasureId = item.UnitOfMeasureId,
            ManufacturerId = item.ManufacturerId,
            RequiresPrescription = item.RequiresPrescription,
            IsControlledSubstance = item.IsControlledSubstance,
            DefaultSalePrice = item.DefaultSalePrice,
            PurchaseType = item.PurchaseType,
            DefaultPurchasePrice = item.DefaultPurchasePrice,
            TaxRatePercent = item.TaxRatePercent,
            ReorderPoint = item.ReorderPoint,
            MinStockLevel = item.MinStockLevel,
            MaxStockLevel = item.MaxStockLevel,
            IsActive = item.IsActive
        };
    }

    public async Task<Result<ItemDto>> CreateItemAsync(ItemUpsertDto dto, CancellationToken cancellationToken = default)
    {
        var validation = await ValidateItemAsync(dto, cancellationToken);
        if (validation is not null) return Result<ItemDto>.Failure(validation);

        var item = new Item
        {
            Code = dto.Code.Trim().ToUpperInvariant(),
            Barcode = dto.Barcode?.Trim(),
            BaseUnitBarcode = dto.BaseUnitBarcode?.Trim(),
            UnitsPerPackage = dto.UnitsPerPackage,
            PackageUnitName = dto.PackageUnitName.Trim(),
            Name = dto.Name.Trim(),
            GenericName = dto.GenericName,
            Strength = dto.Strength,
            Form = dto.Form,
            CategoryId = dto.CategoryId,
            UnitOfMeasureId = dto.UnitOfMeasureId,
            ManufacturerId = dto.ManufacturerId,
            RequiresPrescription = dto.RequiresPrescription,
            IsControlledSubstance = dto.IsControlledSubstance,
            DefaultSalePrice = dto.DefaultSalePrice ?? SalePricePolicy.FromPurchasePrice(dto.DefaultPurchasePrice, dto.PurchaseType),
            PurchaseType = dto.PurchaseType,
            DefaultPurchasePrice = dto.DefaultPurchasePrice,
            TaxRatePercent = dto.TaxRatePercent,
            ReorderPoint = dto.ReorderPoint,
            MinStockLevel = dto.MinStockLevel,
            MaxStockLevel = dto.MaxStockLevel,
            IsActive = dto.IsActive
        };

        _context.Items.Add(item);
        await _context.SaveChangesAsync(cancellationToken);

        return Result<ItemDto>.Success((await GetItemsAsync(cancellationToken: cancellationToken)).First(i => i.Id == item.Id));
    }

    public async Task<Result<ItemDto>> UpdateItemAsync(ItemUpsertDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Id is null) return Result<ItemDto>.Failure("معرّف الصنف مطلوب.");

        var item = await _context.Items.FirstOrDefaultAsync(i => i.Id == dto.Id, cancellationToken);
        if (item is null) return Result<ItemDto>.Failure("الصنف غير موجود.");

        var validation = await ValidateItemAsync(dto, cancellationToken);
        if (validation is not null) return Result<ItemDto>.Failure(validation);

        if (item.UnitsPerPackage != dto.UnitsPerPackage || item.UnitOfMeasureId != dto.UnitOfMeasureId || item.PackageUnitName != dto.PackageUnitName.Trim())
        {
            var used = await _context.Batches.AnyAsync(b => b.ItemId == item.Id, cancellationToken)
                || await _context.StockTransactions.AnyAsync(t => t.ItemId == item.Id, cancellationToken)
                || await _context.PurchaseOrderItems.IgnoreQueryFilters().AnyAsync(l => l.ItemId == item.Id, cancellationToken)
                || await _context.GoodsReceiptItems.IgnoreQueryFilters().AnyAsync(l => l.ItemId == item.Id, cancellationToken)
                || await _context.PurchaseInvoiceItems.IgnoreQueryFilters().AnyAsync(l => l.ItemId == item.Id, cancellationToken)
                || await _context.SalesInvoiceItems.IgnoreQueryFilters().AnyAsync(l => l.ItemId == item.Id, cancellationToken)
                || await _context.PrescriptionItems.IgnoreQueryFilters().AnyAsync(l => l.ItemId == item.Id, cancellationToken);
            if (used) return Result<ItemDto>.Failure("لا يمكن تغيير وحدات صنف له حركات سابقة. أنشئ بطاقة جديدة بتعبئة صحيحة، وسوِّ رصيد البطاقة القديمة بوحدتها الأصلية.");
        }

        item.Code = dto.Code.Trim().ToUpperInvariant();
        item.Barcode = dto.Barcode?.Trim();
        item.BaseUnitBarcode = dto.BaseUnitBarcode?.Trim();
        item.UnitsPerPackage = dto.UnitsPerPackage;
        item.PackageUnitName = dto.PackageUnitName.Trim();
        item.Name = dto.Name.Trim();
        item.GenericName = dto.GenericName;
        item.Strength = dto.Strength;
        item.Form = dto.Form;
        item.CategoryId = dto.CategoryId;
        item.UnitOfMeasureId = dto.UnitOfMeasureId;
        item.ManufacturerId = dto.ManufacturerId;
        item.RequiresPrescription = dto.RequiresPrescription;
        item.IsControlledSubstance = dto.IsControlledSubstance;
        item.DefaultSalePrice = dto.DefaultSalePrice ?? SalePricePolicy.FromPurchasePrice(dto.DefaultPurchasePrice, dto.PurchaseType);
        item.PurchaseType = dto.PurchaseType;
        item.DefaultPurchasePrice = dto.DefaultPurchasePrice;
        item.TaxRatePercent = dto.TaxRatePercent;
        item.ReorderPoint = dto.ReorderPoint;
        item.MinStockLevel = dto.MinStockLevel;
        item.MaxStockLevel = dto.MaxStockLevel;
        item.IsActive = dto.IsActive;

        await _context.SaveChangesAsync(cancellationToken);
        return Result<ItemDto>.Success((await GetItemsAsync(cancellationToken: cancellationToken)).First(i => i.Id == item.Id));
    }

    public async Task<Result> SetItemActiveStatusAsync(int itemId, bool isActive, CancellationToken cancellationToken = default)
    {
        var item = await _context.Items.FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);
        if (item is null) return Result.Failure("الصنف غير موجود.");

        item.IsActive = isActive;
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<string?> ValidateItemAsync(ItemUpsertDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.Code)) return "رمز الصنف مطلوب.";
        if (string.IsNullOrWhiteSpace(dto.Name)) return "اسم الصنف مطلوب.";
        if (dto.CategoryId <= 0) return "الرجاء اختيار تصنيف.";
        if (dto.UnitOfMeasureId <= 0) return "الرجاء اختيار وحدة قياس.";
        if (dto.DefaultSalePrice < 0) return "سعر البيع لا يمكن أن يكون سالباً.";
        if (!Enum.IsDefined(dto.PurchaseType)) return "نوع الشراء غير صالح.";
        if (dto.DefaultPurchasePrice < 0) return "سعر الشراء لا يمكن أن يكون سالباً.";

        if (dto.UnitsPerPackage < 1 || dto.UnitsPerPackage > 100000) return "عدد الوحدات في العلبة يجب أن يكون بين 1 و100000.";
        if (string.IsNullOrWhiteSpace(dto.PackageUnitName) || dto.PackageUnitName.Length > 50) return "اسم وحدة العبوة مطلوب (حتى 50 حرفاً).";
        if (dto.Barcode?.Trim().Length > 50 || dto.BaseUnitBarcode?.Trim().Length > 50) return "الباركود لا يتجاوز 50 حرفاً.";
        if (!string.IsNullOrWhiteSpace(dto.BaseUnitBarcode) && dto.BaseUnitBarcode.Trim() == dto.Barcode?.Trim()) return "باركود الشريط يجب أن يختلف عن باركود العلبة.";
        if (!string.IsNullOrWhiteSpace(dto.BaseUnitBarcode) && dto.UnitsPerPackage == 1) return "باركود الوحدة الصغيرة يحتاج تعريف عبوة متعددة الوحدات.";
        foreach (var barcode in new[] { dto.Barcode?.Trim(), dto.BaseUnitBarcode?.Trim() }.Where(b => !string.IsNullOrWhiteSpace(b)))
            if (await _context.Items.AnyAsync(i => i.Id != dto.Id && (i.Barcode == barcode || i.BaseUnitBarcode == barcode), cancellationToken))
                return "الباركود مستخدم لصنف آخر؛ لكل شركة/عبوة باركود مستقل.";

        var codeTaken = await _context.Items.AnyAsync(i => i.Code == dto.Code.Trim().ToUpper() && i.Id != dto.Id, cancellationToken);
        if (codeTaken) return "رمز الصنف مستخدم مسبقاً.";

        var categoryExists = await _context.ItemCategories.AnyAsync(c => c.Id == dto.CategoryId, cancellationToken);
        if (!categoryExists) return "التصنيف المحدد غير موجود.";

        var unitExists = await _context.UnitsOfMeasure.AnyAsync(u => u.Id == dto.UnitOfMeasureId, cancellationToken);
        if (!unitExists) return "الوحدة المحددة غير موجودة.";

        if (dto.ManufacturerId.HasValue)
        {
            var manufacturerExists = await _context.Manufacturers.AnyAsync(m => m.Id == dto.ManufacturerId, cancellationToken);
            if (!manufacturerExists) return "الشركة المصنّعة المحددة غير موجودة.";
        }

        return null;
    }

    // ===================== Batches / Stock movement =====================

    public async Task<List<BatchDto>> GetBatchesForItemAsync(int itemId, int? warehouseId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Batches
            .Include(b => b.Item).ThenInclude(i => i.UnitOfMeasure)
            .Include(b => b.Warehouse)
            .Where(b => b.ItemId == itemId);

        if (warehouseId.HasValue)
            query = query.Where(b => b.WarehouseId == warehouseId);

        var batches = await query.OrderBy(b => b.ExpiryDate).ToListAsync(cancellationToken);
        var now = _dateTime.UtcNow;

        return batches.Select(b => MapBatchToDto(b, now)).ToList();
    }

    public async Task<Result<BatchDto>> ReceiveBatchAsync(ReceiveBatchDto dto, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(dto.PurchaseType)) return Result<BatchDto>.Failure("نوع الشراء غير صالح.");
        if (dto.Quantity <= 0) return Result<BatchDto>.Failure("الكمية المستلمة يجب أن تكون أكبر من صفر.");
        if (dto.PurchasePrice < 0 || dto.SalePriceOverride < 0)
            return Result<BatchDto>.Failure("سعر الشراء والبيع لا يمكن أن يكونا سالبين.");
        if (string.IsNullOrWhiteSpace(dto.BatchNumber)) return Result<BatchDto>.Failure("رقم الدفعة (Batch Number) مطلوب.");
        if (dto.ExpiryDate.Date <= _dateTime.UtcNow.Date) return Result<BatchDto>.Failure("تاريخ الانتهاء يجب أن يكون في المستقبل.");

        var item = await _context.Items.FirstOrDefaultAsync(i => i.Id == dto.ItemId, cancellationToken);
        if (item is null || !item.IsActive) return Result<BatchDto>.Failure("الصنف غير موجود أو غير نشط.");
        if (dto.Quantity > int.MaxValue / item.UnitsPerPackage) return Result<BatchDto>.Failure("الكمية كبيرة جداً.");
        var stockQuantity = dto.Quantity * item.UnitsPerPackage;
        var packageSalePrice = dto.SalePriceOverride ?? SalePricePolicy.FromPurchasePrice(dto.PurchasePrice, dto.PurchaseType);

        var warehouseExists = await _context.Warehouses.AnyAsync(w => w.Id == dto.WarehouseId, cancellationToken);
        if (!warehouseExists) return Result<BatchDto>.Failure("المخزن غير موجود.");

        var batch = new Batch
        {
            ItemId = dto.ItemId,
            WarehouseId = dto.WarehouseId,
            BatchNumber = dto.BatchNumber.Trim(),
            ManufactureDate = dto.ManufactureDate,
            ExpiryDate = dto.ExpiryDate,
            QuantityOnHand = stockQuantity,
            PurchaseType = dto.PurchaseType,
            PurchasePrice = Math.Round(dto.PurchasePrice / item.UnitsPerPackage, 6, MidpointRounding.AwayFromZero),
            PackageSalePrice = packageSalePrice,
            SalePriceOverride = Math.Round(packageSalePrice / item.UnitsPerPackage, 2, MidpointRounding.AwayFromZero),
            HasConfiguredSalePrice = true,
            ReceivedAtUtc = _dateTime.UtcNow,
            SupplierReference = dto.SupplierReference
        };

        _context.Batches.Add(batch);
        await _context.SaveChangesAsync(cancellationToken);

        await RecordStockTransactionAsync(batch, StockTransactionType.PurchaseReceipt, stockQuantity,
            referenceType: "ManualReceipt", referenceId: null, notes: "استلام دفعة جديدة (إدخال يدوي)",
            performedByUserId, cancellationToken);

        return Result<BatchDto>.Success(MapBatchToDto(await ReloadBatchAsync(batch.Id, cancellationToken), _dateTime.UtcNow));
    }

    public async Task<Result> AdjustStockAsync(StockAdjustmentDto dto, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        if (dto.QuantityDelta == 0) return Result.Failure("قيمة التعديل يجب ألا تساوي صفراً.");
        if (string.IsNullOrWhiteSpace(dto.Reason)) return Result.Failure("سبب التعديل مطلوب.");

        var batch = await _context.Batches.FirstOrDefaultAsync(b => b.Id == dto.BatchId, cancellationToken);
        if (batch is null) return Result.Failure("الدفعة غير موجودة.");

        var newQuantity = batch.QuantityOnHand + dto.QuantityDelta;
        if (newQuantity < 0) return Result.Failure($"لا يمكن إنقاص الكمية بهذا المقدار. الكمية الحالية {batch.QuantityOnHand} فقط.");

        batch.QuantityOnHand = newQuantity;
        await _context.SaveChangesAsync(cancellationToken);

        var type = dto.QuantityDelta > 0 ? StockTransactionType.AdjustmentIncrease : StockTransactionType.AdjustmentDecrease;
        await RecordStockTransactionAsync(batch, type, dto.QuantityDelta,
            referenceType: "ManualAdjustment", referenceId: null, notes: dto.Reason,
            performedByUserId, cancellationToken);

        return Result.Success();
    }

    public async Task<List<StockTransactionDto>> GetTransactionHistoryAsync(int batchId, CancellationToken cancellationToken = default)
    {
        var transactions = await _context.StockTransactions
            .Where(t => t.BatchId == batchId)
            .OrderByDescending(t => t.TransactionAtUtc)
            .ToListAsync(cancellationToken);

        var userIds = transactions.Where(t => t.PerformedByUserId.HasValue).Select(t => t.PerformedByUserId!.Value).Distinct().ToList();
        var userNames = userIds.Count == 0
            ? new Dictionary<int, string>()
            : await _context.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        return transactions.Select(t => new StockTransactionDto
        {
            TransactionAtUtc = t.TransactionAtUtc,
            TypeDisplay = TranslateTransactionType(t.Type),
            QuantityChange = t.QuantityChange,
            ResultingQuantityOnHand = t.ResultingQuantityOnHand,
            Notes = t.Notes,
            PerformedByUserName = t.PerformedByUserId.HasValue && userNames.TryGetValue(t.PerformedByUserId.Value, out var name) ? name : null
        }).ToList();
    }

    // ===================== Reporting =====================

    public async Task<List<ItemStockSummaryDto>> GetStockOverviewAsync(int warehouseId, bool lowStockOnly = false, CancellationToken cancellationToken = default)
    {
        var batches = await _context.Batches
            .Include(b => b.Item).ThenInclude(i => i.UnitOfMeasure)
            .Include(b => b.Warehouse)
            .Where(b => b.WarehouseId == warehouseId && b.QuantityOnHand > 0)
            .ToListAsync(cancellationToken);

        var summaries = batches
            .GroupBy(b => b.ItemId)
            .Select(g =>
            {
                var first = g.First();
                var totalQty = g.Sum(b => b.QuantityOnHand);
                return new ItemStockSummaryDto
                {
                    ItemId = first.ItemId,
                    ItemCode = first.Item.Code,
                    ItemName = first.Item.Name,
                    UnitOfMeasureName = first.Item.UnitOfMeasure.Name,
                    WarehouseId = warehouseId,
                    WarehouseName = first.Warehouse.Name,
                    TotalQuantityOnHand = totalQty,
                    ReorderPoint = first.Item.ReorderPoint,
                    IsBelowReorderPoint = totalQty <= first.Item.ReorderPoint,
                    NearestExpiryDate = g.Min(b => b.ExpiryDate)
                };
            })
            .OrderBy(s => s.ItemName)
            .ToList();

        return lowStockOnly ? summaries.Where(s => s.IsBelowReorderPoint).ToList() : summaries;
    }

    public async Task<List<ExpiringBatchDto>> GetExpiringBatchesAsync(int warehouseId, int withinDays = 90, CancellationToken cancellationToken = default)
    {
        var now = _dateTime.UtcNow;
        var threshold = now.Date.AddDays(withinDays);

        var batches = await _context.Batches
            .Include(b => b.Item).ThenInclude(i => i.UnitOfMeasure)
            .Include(b => b.Warehouse)
            .Where(b => b.WarehouseId == warehouseId && b.QuantityOnHand > 0 && b.ExpiryDate.Date <= threshold)
            .OrderBy(b => b.ExpiryDate)
            .ToListAsync(cancellationToken);

        return batches.Select(b => new ExpiringBatchDto
        {
            BatchId = b.Id,
            ItemCode = b.Item.Code,
            ItemName = b.Item.Name,
            UnitOfMeasureName = b.Item.UnitOfMeasure.Name,
            WarehouseName = b.Warehouse.Name,
            BatchNumber = b.BatchNumber,
            ExpiryDate = b.ExpiryDate,
            DaysUntilExpiry = b.DaysUntilExpiry(now),
            QuantityOnHand = b.QuantityOnHand,
            IsExpired = b.IsExpired(now)
        }).ToList();
    }

    public async Task<int> GetAvailableQuantityAsync(int itemId, int warehouseId, CancellationToken cancellationToken = default)
    {
        return await _context.Batches
            .Where(b => b.ItemId == itemId && b.WarehouseId == warehouseId && b.ExpiryDate >= _dateTime.UtcNow.Date)
            .SumAsync(b => b.QuantityOnHand, cancellationToken);
    }

    public async Task<Result<List<BatchAllocationResultDto>>> IssueStockFefoAsync(
        int itemId, int warehouseId, int quantity, string referenceType, int? referenceId,
        int? performedByUserId, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0) return Result<List<BatchAllocationResultDto>>.Failure("الكمية المطلوبة يجب أن تكون أكبر من صفر.");

        var candidateBatches = await _context.Batches
            .Where(b => b.ItemId == itemId && b.WarehouseId == warehouseId && b.QuantityOnHand > 0 && b.ExpiryDate >= _dateTime.UtcNow.Date)
            .OrderBy(b => b.ExpiryDate).ThenBy(b => b.ReceivedAtUtc).ThenBy(b => b.Id)
            .ToListAsync(cancellationToken);

        var totalAvailable = candidateBatches.Sum(b => b.QuantityOnHand);
        if (totalAvailable < quantity)
            return Result<List<BatchAllocationResultDto>>.Failure($"الكمية المتوفرة غير كافية. المتوفر حالياً: {totalAvailable} فقط.");

        var remaining = quantity;
        var allocations = new List<BatchAllocationResultDto>();

        foreach (var batch in candidateBatches)
        {
            if (remaining <= 0) break;

            var takeFromThisBatch = Math.Min(remaining, batch.QuantityOnHand);
            batch.QuantityOnHand -= takeFromThisBatch;
            remaining -= takeFromThisBatch;

            await RecordStockTransactionAsync(batch, StockTransactionType.SaleIssue, -takeFromThisBatch,
                referenceType, referenceId, notes: null, performedByUserId, cancellationToken);

            allocations.Add(new BatchAllocationResultDto
            {
                BatchId = batch.Id,
                BatchNumber = batch.BatchNumber,
                QuantityTaken = takeFromThisBatch,
                UnitCost = batch.PurchasePrice,
                ExpiryDate = batch.ExpiryDate
            });
        }

        return Result<List<BatchAllocationResultDto>>.Success(allocations);
    }

    public async Task<Result> RestockBatchAsync(
        int batchId, int quantity, string referenceType, int? referenceId,
        int? performedByUserId, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0) return Result.Failure("كمية إعادة التخزين يجب أن تكون أكبر من صفر.");

        var batch = await _context.Batches.FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken);
        if (batch is null) return Result.Failure("الدفعة غير موجودة.");

        batch.QuantityOnHand += quantity;
        await _context.SaveChangesAsync(cancellationToken);

        await RecordStockTransactionAsync(batch, StockTransactionType.ReturnFromCustomer, quantity,
            referenceType, referenceId, notes: null, performedByUserId, cancellationToken);

        return Result.Success();
    }

    // ===================== Internal helpers =====================

    private async Task RecordStockTransactionAsync(Batch batch, StockTransactionType type, int quantityChange,
        string? referenceType, int? referenceId, string? notes, int? performedByUserId, CancellationToken cancellationToken)
    {
        _context.StockTransactions.Add(new StockTransaction
        {
            ItemId = batch.ItemId,
            BatchId = batch.Id,
            WarehouseId = batch.WarehouseId,
            Type = type,
            QuantityChange = quantityChange,
            ResultingQuantityOnHand = batch.QuantityOnHand,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Notes = notes,
            TransactionAtUtc = _dateTime.UtcNow,
            PerformedByUserId = performedByUserId
        });

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<Batch> ReloadBatchAsync(int batchId, CancellationToken cancellationToken) =>
        await _context.Batches.Include(b => b.Item).ThenInclude(i => i.UnitOfMeasure).Include(b => b.Warehouse).FirstAsync(b => b.Id == batchId, cancellationToken);

    private static BatchDto MapBatchToDto(Batch b, DateTime now) => new()
    {
        Id = b.Id,
        ItemId = b.ItemId,
        ItemName = b.Item.Name,
        UnitOfMeasureName = b.Item.UnitOfMeasure.Name,
        WarehouseId = b.WarehouseId,
        WarehouseName = b.Warehouse.Name,
        BatchNumber = b.BatchNumber,
        ManufactureDate = b.ManufactureDate,
        ExpiryDate = b.ExpiryDate,
        QuantityOnHand = b.QuantityOnHand,
        PurchaseType = b.PurchaseType,
        PurchasePrice = b.PurchasePrice,
        SalePriceOverride = b.SalePriceOverride,
        ReceivedAtUtc = b.ReceivedAtUtc,
        SupplierReference = b.SupplierReference,
        DaysUntilExpiry = b.DaysUntilExpiry(now),
        IsExpired = b.IsExpired(now)
    };

    private static string TranslateTransactionType(StockTransactionType type) => type switch
    {
        StockTransactionType.PurchaseReceipt => "استلام مشتريات",
        StockTransactionType.SaleIssue => "صرف بيع",
        StockTransactionType.AdjustmentIncrease => "تعديل زيادة",
        StockTransactionType.AdjustmentDecrease => "تعديل نقصان",
        StockTransactionType.TransferOut => "تحويل صادر",
        StockTransactionType.TransferIn => "تحويل وارد",
        StockTransactionType.ExpiredWriteOff => "إتلاف - منتهي الصلاحية",
        StockTransactionType.DamagedWriteOff => "إتلاف - تالف",
        StockTransactionType.ReturnFromCustomer => "مرتجع من عميل",
        StockTransactionType.ReturnToSupplier => "مرتجع لمورد",
        StockTransactionType.OpeningBalance => "رصيد افتتاحي",
        _ => type.ToString()
    };
}
