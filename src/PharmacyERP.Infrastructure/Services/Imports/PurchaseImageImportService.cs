using System.Data;
using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Inventory;
using PharmacyERP.Application.Features.Inventory.DTOs;
using PharmacyERP.Application.Features.Purchasing;
using PharmacyERP.Application.Features.Purchasing.DTOs;
using PharmacyERP.Application.Features.Purchasing.Imports;
using PharmacyERP.Domain.Entities;
using PharmacyERP.Domain.Enums;
using PharmacyERP.Infrastructure.Persistence;

namespace PharmacyERP.Infrastructure.Services.Imports;
public sealed class PurchaseImageImportService : IPurchaseImageImportService
{
    private readonly ApplicationDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly IPurchasingService _purchasing;
    private readonly ICurrentUserService _user;
    private readonly IDateTime _clock;
    public PurchaseImageImportService(ApplicationDbContext db, IInventoryService inventory, IPurchasingService purchasing, ICurrentUserService user, IDateTime clock)
    { _db = db; _inventory = inventory; _purchasing = purchasing; _user = user; _clock = clock; }
    public async Task<List<InvoiceItemMatch>> MatchAsync(int supplierId, IReadOnlyList<InvoiceImageLine> lines, CancellationToken cancellationToken = default)
    {
        if (!_user.HasPermission("Purchasing.ReceiveGoods")) throw new UnauthorizedAccessException("تحتاج صلاحية استلام البضاعة.");
        var keys = lines.Select(l => InvoiceItemMatcher.Normalize(l.Name)).ToList();
        var aliases = await _db.SupplierItemAliases.AsNoTracking().Where(a => a.SupplierId == supplierId && keys.Contains(a.NormalizedName)).ToDictionaryAsync(a => a.NormalizedName, a => a.ItemId, cancellationToken);
        var items = await _db.Items.AsNoTracking().Include(i => i.UnitOfMeasure).Include(i => i.SaleUnits).Where(i => i.IsActive).ToListAsync(cancellationToken);
        return lines.Select(l => InvoiceItemMatcher.Match(l, items, aliases.TryGetValue(InvoiceItemMatcher.Normalize(l.Name), out var id) ? id : null)).ToList();
    }
    public async Task<Result<PurchaseImageImportResult>> SaveReviewedAsync(PurchaseImageImportRequest request, CancellationToken cancellationToken = default)
    {
        if (!_db.Database.IsRelational() || _db.Database.CurrentTransaction is not null) return await SaveCoreAsync(request, cancellationToken);
        return await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            try
            {
                var result = await SaveCoreAsync(request, cancellationToken);
                if (result.Succeeded) await transaction.CommitAsync(cancellationToken);
                else { await transaction.RollbackAsync(cancellationToken); _db.ChangeTracker.Clear(); }
                return result;
            }
            catch { await transaction.RollbackAsync(cancellationToken); _db.ChangeTracker.Clear(); throw; }
        });
    }
    private async Task<Result<PurchaseImageImportResult>> SaveCoreAsync(PurchaseImageImportRequest request, CancellationToken cancellationToken)
    {
        Result<PurchaseImageImportResult> Fail(string error) => Result<PurchaseImageImportResult>.Failure(error);
        if (!_user.HasPermission("Purchasing.ReceiveGoods")) return Fail("تحتاج صلاحية استلام البضاعة.");
        var number = request.SupplierInvoiceNumber.Trim();
        request.SourceHash = request.SourceHash.ToUpperInvariant();
        var key = $"{request.InvoiceDate:yyyy}/{InvoiceItemMatcher.Normalize(number)}";
        var previous = await _db.PurchaseImageImports.FirstOrDefaultAsync(i => i.RequestId == request.RequestId || i.SourceHash == request.SourceHash || (i.SupplierId == request.SupplierId && i.SupplierInvoiceKey == key), cancellationToken);
        if (previous is not null)
        {
            var existing = await _db.GoodsReceiptNotes.IgnoreQueryFilters().FirstAsync(n => n.Id == previous.GoodsReceiptNoteId, cancellationToken);
            if (previous.SupplierId != request.SupplierId || existing.BranchId != request.BranchId || (_user.CurrentBranchId.HasValue && existing.BranchId != _user.CurrentBranchId)) return Fail("الفاتورة مستوردة ضمن مورد أو فرع آخر؛ راجع الاختيار والسجل.");
            return existing.IsDeleted || existing.Status == GoodsReceiptStatus.Cancelled ? Fail("هذه الفاتورة استُوردت وسندها ملغى؛ راجع السجل قبل تكرارها.")
                : Result<PurchaseImageImportResult>.Success(new(existing.Id, true, existing.Number));
        }
        if (string.IsNullOrWhiteSpace(number) || number.Length > 80 || InvoiceItemMatcher.Normalize(number).Length == 0) return Fail("راجع رقم فاتورة المذخر.");
        if (request.SourceHash.Length != 64 || request.SourceHash.Any(c => !Uri.IsHexDigit(c))) return Fail("بصمة صورة الفاتورة غير صالحة.");
        if (request.RequestId == Guid.Empty || !Enum.IsDefined(request.PurchaseType)) return Fail("بيانات الاستيراد غير صالحة.");
        if (request.InvoiceDate.Date > _clock.UtcNow.Date || request.InvoiceDate.Year < 2000) return Fail("راجع تاريخ الفاتورة؛ لا يقبل تاريخاً مستقبلياً.");
        if (request.Lines.Count == 0 || request.Lines.Count > 500 || request.Lines.Any(l => !l.Reviewed)) return Fail("راجع كل سطر وأكّد وحدة التجزئة والدفعة والصلاحية قبل الاستيراد.");
        if (!await _db.Suppliers.AnyAsync(s => s.Id == request.SupplierId && s.IsActive, cancellationToken)) return Fail("اختر مورداً نشطاً.");
        if (!await _db.Warehouses.AnyAsync(w => w.Id == request.WarehouseId && w.BranchId == request.BranchId && w.IsActive, cancellationToken)) return Fail("المخزن لا يعود للفرع المحدد أو غير نشط.");
        if (_user.CurrentBranchId.HasValue && _user.CurrentBranchId != request.BranchId) return Fail("الاستيراد يجب أن يكون لفرع المستخدم الحالي.");
        foreach (var line in request.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.SourceName) || InvoiceItemMatcher.Normalize(line.SourceName).Length == 0 || line.SourceName.Length > 250 || string.IsNullOrWhiteSpace(line.ItemName) || line.ItemName.Length > 250) return Fail("راجع اسم العلاج الأصلي وبطاقته.");
            if (line.Quantity < 0 || line.BonusQuantity < 0 || (long)line.Quantity + line.BonusQuantity <= 0 || line.UnitCost < 0 || line.SalePrice < 0) return Fail("راجع الكمية والبونص والأسعار.");
            if (Math.Round(line.UnitCost, 2) != line.UnitCost || (line.SalePrice.HasValue && Math.Round(line.SalePrice.Value, 2) != line.SalePrice)) return Fail("الأسعار لا تتجاوز منزلتين عشريتين.");
            if (line.BaseUnitsPerReceiveUnit < 1 || line.BaseUnitsPerReceiveUnit > 100000 || ((long)line.Quantity + line.BonusQuantity) * line.BaseUnitsPerReceiveUnit > int.MaxValue) return Fail("عدد أجزاء وحدة الاستلام أو الكمية غير صالح.");
            if (string.IsNullOrWhiteSpace(line.ReceiveUnitName) || line.ReceiveUnitName.Length > 40 || string.IsNullOrWhiteSpace(line.BatchNumber) || line.BatchNumber.Length > 60 || line.ExpiryDate.Date <= _clock.UtcNow.Date) return Fail("راجع وحدة الاستلام ورقم الدفعة وتاريخ الصلاحية المستقبلي.");
            if (!Enum.IsDefined(line.Form)) return Fail("راجع الشكل الدوائي.");
            if (!await _db.UnitsOfMeasure.AnyAsync(u => u.Id == line.BaseUnitOfMeasureId && u.IsActive, cancellationToken)) return Fail("حدّد وحدة المخزون الصغيرة لكل علاج.");
            if (line.ExistingItemId.HasValue)
            {
                var item = await _db.Items.FirstOrDefaultAsync(i => i.Id == line.ExistingItemId && i.IsActive, cancellationToken);
                if (item is null) return Fail("الصنف المطابق غير موجود أو غير نشط.");
                if (item.UnitOfMeasureId != line.BaseUnitOfMeasureId) return Fail("وحدة المخزون الصغيرة للصنف الموجود ثابتة؛ صحّح عدد التجزئة دون تغيير الأرصدة القديمة.");
            }
            else if (!_user.HasPermission("Inventory.ManageItems")) return Fail("إنشاء علاج جديد يحتاج صلاحية إدارة الأصناف.");
            else if (!await _db.ItemCategories.AnyAsync(c => c.Id == line.CategoryId && c.IsActive, cancellationToken)) return Fail("حدد تصنيفاً نشطاً للعلاج الجديد.");
        }
        foreach (var group in request.Lines.GroupBy(l => InvoiceItemMatcher.Normalize(l.SourceName)))
            if (group.Select(l => new { l.ExistingItemId, l.BaseUnitOfMeasureId, l.RequiresPrescription, l.IsControlledSubstance }).Distinct().Count() != 1)
                return Fail("الاسم نفسه في الفاتورة يجب أن يعود إلى بطاقة واحدة وبنفس وحدة المخزون والمتطلبات الدوائية.");
        var total = request.Lines.Sum(l => Math.Round(l.UnitCost * l.Quantity, 2, MidpointRounding.AwayFromZero));
        if (total != request.ReviewedTotal) return Fail($"مجموع الأسطر {total:N2} لا يطابق إجمالي الفاتورة {request.ReviewedTotal:N2}. صحّح القراءة قبل الحفظ.");
        var note = new GoodsReceiptUpsertDto { BranchId = request.BranchId, WarehouseId = request.WarehouseId, SupplierId = request.SupplierId,
            ReceiptDate = request.InvoiceDate.Date, PurchaseType = request.PurchaseType, Notes = $"فاتورة المذخر {number} — استيراد صورة بعد المراجعة" };
        var newItems = new Dictionary<string, int>();
        foreach (var line in request.Lines)
        {
            var source = InvoiceItemMatcher.Normalize(line.SourceName);
            var id = line.ExistingItemId;
            var createdId = 0;
            if (!id.HasValue && !newItems.TryGetValue(source, out createdId))
            {
                var created = await _inventory.CreateItemAsync(new ItemUpsertDto { Code = "IMG-" + Guid.NewGuid().ToString("N")[..20],
                    Name = line.ItemName.Trim(), Barcode = line.Barcode, Strength = line.Strength, CategoryId = line.CategoryId,
                    UnitOfMeasureId = line.BaseUnitOfMeasureId, PackageUnitName = line.ReceiveUnitName.Trim(), UnitsPerPackage = line.BaseUnitsPerReceiveUnit,
                    DefaultPurchasePrice = line.UnitCost, DefaultSalePrice = line.SalePrice, PurchaseType = request.PurchaseType,
                    Form = line.Form, RequiresPrescription = line.RequiresPrescription, IsControlledSubstance = line.IsControlledSubstance });
                if (!created.Succeeded) return Fail(created.Errors.First());
                id = created.Value!.Id; newItems[source] = id.Value;
            }
            else if (!id.HasValue) id = createdId;
            var item = await _db.Items.Include(i => i.SaleUnits).FirstAsync(i => i.Id == id.Value, cancellationToken);
            int? receiveUnitId = null;
            if (line.BaseUnitsPerReceiveUnit != item.UnitsPerPackage || line.ReceiveUnitName.Trim() != (item.UnitsPerPackage > 1 ? item.PackageUnitName : (await _db.UnitsOfMeasure.FirstAsync(u => u.Id == item.UnitOfMeasureId, cancellationToken)).Name))
            {
                var unit = item.SaleUnits.FirstOrDefault(u => u.IsActive && u.BaseUnitCount == line.BaseUnitsPerReceiveUnit && u.Name == line.ReceiveUnitName.Trim())
                    ?? item.SaleUnits.FirstOrDefault(u => u.IsActive && u.BaseUnitCount == line.BaseUnitsPerReceiveUnit);
                if (unit is null)
                {
                    if (!_user.HasPermission("Inventory.ManageItems")) return Fail("تعريف تعبئة جديدة يحتاج صلاحية إدارة الأصناف.");
                    var unitName = line.ReceiveUnitName.Trim();
                    if (unitName == item.PackageUnitName || item.SaleUnits.Any(u => u.Name == unitName)) unitName = $"{unitName} ({line.BaseUnitsPerReceiveUnit})";
                    unit = new ItemSaleUnit { Name = unitName, BaseUnitCount = line.BaseUnitsPerReceiveUnit, ItemId = item.Id };
                    _db.ItemSaleUnits.Add(unit); await _db.SaveChangesAsync(cancellationToken);
                }
                receiveUnitId = unit.Id;
            }
            note.Lines.Add(new() { ItemId = item.Id, ItemSaleUnitId = receiveUnitId, QuantityReceived = line.Quantity, BonusQuantity = line.BonusQuantity,
                UnitCost = line.UnitCost, SalePrice = line.SalePrice, BatchNumber = line.BatchNumber.Trim(), ExpiryDate = line.ExpiryDate.Date });
            var alias = await _db.SupplierItemAliases.FirstOrDefaultAsync(a => a.SupplierId == request.SupplierId && a.NormalizedName == source, cancellationToken);
            if (alias is null) _db.SupplierItemAliases.Add(new() { SupplierId = request.SupplierId, ItemId = item.Id, SourceName = line.SourceName.Trim(), NormalizedName = source });
            else if (alias.ItemId != item.Id) return Fail("تسمية المذخر مرتبطة ببطاقة أخرى؛ صحّح المطابقة دون تغيير الاستيراد السابق.");
            // Persist each learned alias before another same-named row queries it.
            await _db.SaveChangesAsync(cancellationToken);
        }
        var receipt = await _purchasing.CreateGoodsReceiptAsync(note, cancellationToken);
        if (!receipt.Succeeded) return Fail(receipt.Errors.First());
        _db.PurchaseImageImports.Add(new() { RequestId = request.RequestId, SupplierId = request.SupplierId, GoodsReceiptNoteId = receipt.Value!.Id,
            SupplierInvoiceKey = key, SourceHash = request.SourceHash.ToUpperInvariant(), ParsedTotal = request.ParsedTotal,
            ReviewedTotal = request.ReviewedTotal, ImportedAtUtc = _clock.UtcNow, ImportedByUserId = _user.UserId });
        await _db.SaveChangesAsync(cancellationToken);
        return Result<PurchaseImageImportResult>.Success(new(receipt.Value.Id, false, receipt.Value.Number));
    }
}
