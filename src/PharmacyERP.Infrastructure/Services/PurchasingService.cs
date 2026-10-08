using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Accounting;
using PharmacyERP.Application.Features.Accounting.DTOs;
using PharmacyERP.Application.Features.Inventory;
using PharmacyERP.Application.Features.Inventory.DTOs;
using PharmacyERP.Application.Features.Purchasing;
using PharmacyERP.Application.Features.Purchasing.DTOs;
using PharmacyERP.Domain.Entities;
using PharmacyERP.Domain.Common;
using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Infrastructure.Services;

public class PurchasingService : IPurchasingService
{
    private readonly IApplicationDbContext _context;
    private readonly IInventoryService _inventoryService;
    private readonly IAccountingService _accountingService;
    private readonly IDateTime _dateTime;

    public PurchasingService(IApplicationDbContext context, IInventoryService inventoryService, IAccountingService accountingService, IDateTime dateTime)
    {
        _context = context;
        _inventoryService = inventoryService;
        _accountingService = accountingService;
        _dateTime = dateTime;
    }

    // ===================== Suppliers =====================

    public async Task<List<SupplierDto>> GetSuppliersAsync(CancellationToken cancellationToken = default)
    {
        var suppliers = await _context.Suppliers
            .Include(s => s.PurchaseOrders)
            .Include(s => s.PurchaseInvoices)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

        return suppliers.Select(s => new SupplierDto
        {
            Id = s.Id,
            Code = s.Code,
            Name = s.Name,
            ContactPerson = s.ContactPerson,
            Phone = s.Phone,
            Email = s.Email,
            Address = s.Address,
            TaxRegistrationNumber = s.TaxRegistrationNumber,
            PaymentTermsDays = s.PaymentTermsDays,
            IsActive = s.IsActive,
            OpenPurchaseOrderCount = s.PurchaseOrders.Count(po =>
                po.Status == PurchaseOrderStatus.Submitted || po.Status == PurchaseOrderStatus.PartiallyReceived),
            TotalOutstandingBalance = s.PurchaseInvoices
                .Where(i => i.Status != PurchaseInvoiceStatus.Cancelled)
                .Sum(i => i.TotalAmount - i.AmountPaid)
        }).ToList();
    }

    public async Task<SupplierUpsertDto?> GetSupplierForEditAsync(int id, CancellationToken cancellationToken = default)
    {
        var s = await _context.Suppliers.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (s is null) return null;

        return new SupplierUpsertDto
        {
            Id = s.Id,
            Code = s.Code,
            Name = s.Name,
            ContactPerson = s.ContactPerson,
            Phone = s.Phone,
            Email = s.Email,
            Address = s.Address,
            TaxRegistrationNumber = s.TaxRegistrationNumber,
            PaymentTermsDays = s.PaymentTermsDays,
            IsActive = s.IsActive
        };
    }

    public async Task<Result<SupplierDto>> CreateSupplierAsync(SupplierUpsertDto dto, CancellationToken cancellationToken = default)
    {
        var validation = await ValidateSupplierAsync(dto, cancellationToken);
        if (validation is not null) return Result<SupplierDto>.Failure(validation);

        var supplier = new Supplier
        {
            Code = dto.Code.Trim().ToUpperInvariant(),
            Name = dto.Name.Trim(),
            ContactPerson = dto.ContactPerson,
            Phone = dto.Phone,
            Email = dto.Email,
            Address = dto.Address,
            TaxRegistrationNumber = dto.TaxRegistrationNumber,
            PaymentTermsDays = dto.PaymentTermsDays,
            IsActive = dto.IsActive
        };

        _context.Suppliers.Add(supplier);
        await _context.SaveChangesAsync(cancellationToken);

        return Result<SupplierDto>.Success((await GetSuppliersAsync(cancellationToken)).First(s => s.Id == supplier.Id));
    }

    public async Task<Result<SupplierDto>> UpdateSupplierAsync(SupplierUpsertDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Id is null) return Result<SupplierDto>.Failure("معرّف المورد مطلوب.");

        var supplier = await _context.Suppliers.FirstOrDefaultAsync(s => s.Id == dto.Id, cancellationToken);
        if (supplier is null) return Result<SupplierDto>.Failure("المورد غير موجود.");

        var validation = await ValidateSupplierAsync(dto, cancellationToken);
        if (validation is not null) return Result<SupplierDto>.Failure(validation);

        supplier.Code = dto.Code.Trim().ToUpperInvariant();
        supplier.Name = dto.Name.Trim();
        supplier.ContactPerson = dto.ContactPerson;
        supplier.Phone = dto.Phone;
        supplier.Email = dto.Email;
        supplier.Address = dto.Address;
        supplier.TaxRegistrationNumber = dto.TaxRegistrationNumber;
        supplier.PaymentTermsDays = dto.PaymentTermsDays;
        supplier.IsActive = dto.IsActive;

        await _context.SaveChangesAsync(cancellationToken);
        return Result<SupplierDto>.Success((await GetSuppliersAsync(cancellationToken)).First(s => s.Id == supplier.Id));
    }

    public async Task<Result> SetSupplierActiveStatusAsync(int supplierId, bool isActive, CancellationToken cancellationToken = default)
    {
        var supplier = await _context.Suppliers.FirstOrDefaultAsync(s => s.Id == supplierId, cancellationToken);
        if (supplier is null) return Result.Failure("المورد غير موجود.");

        supplier.IsActive = isActive;
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<string?> ValidateSupplierAsync(SupplierUpsertDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.Code)) return "رمز المورد مطلوب.";
        if (string.IsNullOrWhiteSpace(dto.Name)) return "اسم المورد مطلوب.";

        var codeTaken = await _context.Suppliers.AnyAsync(s => s.Code == dto.Code.Trim().ToUpper() && s.Id != dto.Id, cancellationToken);
        if (codeTaken) return "رمز المورد مستخدم مسبقاً.";

        return null;
    }

    // ===================== Purchase Orders =====================

    public async Task<List<PurchaseOrderDto>> GetPurchaseOrdersAsync(CancellationToken cancellationToken = default)
    {
        var orders = await _context.PurchaseOrders
            .Include(p => p.Supplier)
            .Include(p => p.Branch)
            .Include(p => p.Warehouse)
            .Include(p => p.Items)
            .OrderByDescending(p => p.OrderDate).ThenByDescending(p => p.Id)
            .ToListAsync(cancellationToken);

        return orders.Select(MapPurchaseOrderToDto).ToList();
    }

    public async Task<PurchaseOrderUpsertDto?> GetPurchaseOrderForEditAsync(int id, CancellationToken cancellationToken = default)
    {
        var order = await _context.PurchaseOrders
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (order is null) return null;

        return new PurchaseOrderUpsertDto
        {
            Id = order.Id,
            SupplierId = order.SupplierId,
            BranchId = order.BranchId,
            WarehouseId = order.WarehouseId,
            OrderDate = order.OrderDate,
            ExpectedDeliveryDate = order.ExpectedDeliveryDate,
            PurchaseType = order.PurchaseType,
            Notes = order.Notes,
            Lines = order.Items.Select(i => new PurchaseOrderLineUpsertDto
            {
                Id = i.Id,
                ItemId = i.ItemId,
                QuantityOrdered = i.QuantityOrdered,
                UnitCost = i.UnitCost,
                SalePrice = i.SalePrice,
                TaxRatePercent = i.TaxRatePercent
            }).ToList()
        };
    }

    public async Task<List<PurchaseOrderLineDto>> GetPurchaseOrderLinesAsync(int purchaseOrderId, CancellationToken cancellationToken = default)
    {
        var lines = await _context.PurchaseOrderItems
            .Include(i => i.PurchaseOrder)
            .Include(i => i.Item).ThenInclude(item => item.UnitOfMeasure)
            .Where(i => i.PurchaseOrderId == purchaseOrderId)
            .ToListAsync(cancellationToken);

        return lines.Select(MapPurchaseOrderLineToDto).ToList();
    }

    public async Task<Result<PurchaseOrderDto>> CreatePurchaseOrderAsync(PurchaseOrderUpsertDto dto, CancellationToken cancellationToken = default)
    {
        var validation = await ValidatePurchaseOrderAsync(dto, cancellationToken);
        if (validation is not null) return Result<PurchaseOrderDto>.Failure(validation);

        var order = new PurchaseOrder
        {
            Number = await GenerateNumberAsync("PO", () => _context.PurchaseOrders.CountAsync(cancellationToken)),
            SupplierId = dto.SupplierId,
            BranchId = dto.BranchId,
            WarehouseId = dto.WarehouseId,
            OrderDate = dto.OrderDate,
            ExpectedDeliveryDate = dto.ExpectedDeliveryDate,
            PurchaseType = dto.PurchaseType,
            Notes = dto.Notes,
            Status = PurchaseOrderStatus.Draft
        };

        foreach (var line in dto.Lines)
        {
            order.Items.Add(new PurchaseOrderItem
            {
                ItemId = line.ItemId,
                QuantityOrdered = line.QuantityOrdered,
                UnitCost = line.UnitCost,
                SalePrice = line.SalePrice ?? SalePricePolicy.FromPurchasePrice(line.UnitCost, dto.PurchaseType),
                TaxRatePercent = line.TaxRatePercent
            });
        }

        _context.PurchaseOrders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);

        return Result<PurchaseOrderDto>.Success((await GetPurchaseOrdersAsync(cancellationToken)).First(p => p.Id == order.Id));
    }

    public async Task<Result<PurchaseOrderDto>> UpdatePurchaseOrderAsync(PurchaseOrderUpsertDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Id is null) return Result<PurchaseOrderDto>.Failure("معرّف أمر الشراء مطلوب.");

        var order = await _context.PurchaseOrders
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == dto.Id, cancellationToken);

        if (order is null) return Result<PurchaseOrderDto>.Failure("أمر الشراء غير موجود.");
        if (order.Status != PurchaseOrderStatus.Draft)
            return Result<PurchaseOrderDto>.Failure("لا يمكن تعديل أمر شراء بعد اعتماده. أنشئ أمراً جديداً أو ألغِ هذا الأمر.");

        var validation = await ValidatePurchaseOrderAsync(dto, cancellationToken);
        if (validation is not null) return Result<PurchaseOrderDto>.Failure(validation);

        order.SupplierId = dto.SupplierId;
        order.BranchId = dto.BranchId;
        order.WarehouseId = dto.WarehouseId;
        order.OrderDate = dto.OrderDate;
        order.ExpectedDeliveryDate = dto.ExpectedDeliveryDate;
        order.PurchaseType = dto.PurchaseType;
        order.Notes = dto.Notes;

        // Replace all lines wholesale — the PO is still Draft so no receipts can reference old line IDs yet.
        foreach (var existingLine in order.Items.ToList())
            _context.PurchaseOrderItems.Remove(existingLine);

        foreach (var line in dto.Lines)
        {
            order.Items.Add(new PurchaseOrderItem
            {
                ItemId = line.ItemId,
                QuantityOrdered = line.QuantityOrdered,
                UnitCost = line.UnitCost,
                SalePrice = line.SalePrice ?? SalePricePolicy.FromPurchasePrice(line.UnitCost, dto.PurchaseType),
                TaxRatePercent = line.TaxRatePercent
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Result<PurchaseOrderDto>.Success((await GetPurchaseOrdersAsync(cancellationToken)).First(p => p.Id == order.Id));
    }

    public async Task<Result> SubmitPurchaseOrderAsync(int purchaseOrderId, CancellationToken cancellationToken = default)
    {
        var order = await _context.PurchaseOrders.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == purchaseOrderId, cancellationToken);
        if (order is null) return Result.Failure("أمر الشراء غير موجود.");
        if (order.Status != PurchaseOrderStatus.Draft) return Result.Failure("لا يمكن اعتماد أمر شراء تم اعتماده مسبقاً.");
        if (!order.Items.Any()) return Result.Failure("لا يمكن اعتماد أمر شراء لا يحتوي على أي أصناف.");

        order.Status = PurchaseOrderStatus.Submitted;
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> CancelPurchaseOrderAsync(int purchaseOrderId, CancellationToken cancellationToken = default)
    {
        var order = await _context.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == purchaseOrderId, cancellationToken);
        if (order is null) return Result.Failure("أمر الشراء غير موجود.");
        if (order.Status == PurchaseOrderStatus.Received) return Result.Failure("لا يمكن إلغاء أمر شراء مكتمل الاستلام.");

        order.Status = PurchaseOrderStatus.Cancelled;
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> DeletePurchaseOrderAsync(int purchaseOrderId, CancellationToken cancellationToken = default)
    {
        var order = await _context.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == purchaseOrderId, cancellationToken);
        if (order is null) return Result.Failure("أمر الشراء غير موجود.");
        if (order.Status != PurchaseOrderStatus.Draft) return Result.Failure("لا يمكن حذف أمر شراء بعد اعتماده — يمكن إلغاؤه بدلاً من ذلك.");

        _context.PurchaseOrders.Remove(order);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<List<PurchaseOrderLineDto>> GetOutstandingLinesForReceiptAsync(int purchaseOrderId, CancellationToken cancellationToken = default)
    {
        var lines = await _context.PurchaseOrderItems
            .Include(i => i.PurchaseOrder)
            .Include(i => i.Item).ThenInclude(item => item.UnitOfMeasure)
            .Where(i => i.PurchaseOrderId == purchaseOrderId && i.QuantityReceived < i.QuantityOrdered)
            .ToListAsync(cancellationToken);

        return lines.Select(MapPurchaseOrderLineToDto).ToList();
    }

    // ===================== Goods Receipt =====================

    public async Task<List<GoodsReceiptNoteDto>> GetGoodsReceiptNotesAsync(CancellationToken cancellationToken = default)
    {
        var notes = await _context.GoodsReceiptNotes
            .Include(n => n.Supplier)
            .Include(n => n.Branch)
            .Include(n => n.Warehouse)
            .Include(n => n.PurchaseOrder)
            .Include(n => n.Items)
            .OrderByDescending(n => n.ReceiptDate).ThenByDescending(n => n.Id)
            .ToListAsync(cancellationToken);

        return notes.Select(MapGoodsReceiptToDto).ToList();
    }

    public async Task<GoodsReceiptUpsertDto?> GetGoodsReceiptForEditAsync(int id, CancellationToken cancellationToken = default)
    {
        var note = await _context.GoodsReceiptNotes
            .Include(n => n.Items)
            .FirstOrDefaultAsync(n => n.Id == id, cancellationToken);

        if (note is null) return null;

        return new GoodsReceiptUpsertDto
        {
            Id = note.Id,
            PurchaseOrderId = note.PurchaseOrderId,
            SupplierId = note.SupplierId,
            BranchId = note.BranchId,
            WarehouseId = note.WarehouseId,
            ReceiptDate = note.ReceiptDate,
            PurchaseType = note.PurchaseType,
            Notes = note.Notes,
            Lines = note.Items.Select(i => new GoodsReceiptLineUpsertDto
            {
                Id = i.Id,
                PurchaseOrderItemId = i.PurchaseOrderItemId,
                ItemId = i.ItemId,
                BatchNumber = i.BatchNumber,
                ManufactureDate = i.ManufactureDate,
                ExpiryDate = i.ExpiryDate,
                QuantityReceived = i.QuantityReceived,
                UnitCost = i.UnitCost,
                SalePrice = i.SalePrice
            }).ToList()
        };
    }

    public async Task<List<GoodsReceiptLineDto>> GetGoodsReceiptLinesAsync(int goodsReceiptNoteId, CancellationToken cancellationToken = default)
    {
        var lines = await _context.GoodsReceiptItems
            .Include(i => i.Item)
            .Where(i => i.GoodsReceiptNoteId == goodsReceiptNoteId)
            .ToListAsync(cancellationToken);

        return lines.Select(MapGoodsReceiptLineToDto).ToList();
    }

    public async Task<Result<GoodsReceiptNoteDto>> CreateGoodsReceiptAsync(GoodsReceiptUpsertDto dto, CancellationToken cancellationToken = default)
    {
        var validation = await ValidateGoodsReceiptAsync(dto, cancellationToken);
        if (validation is not null) return Result<GoodsReceiptNoteDto>.Failure(validation);

        var number = await GenerateNumberAsync("GRN", () => _context.GoodsReceiptNotes.CountAsync(cancellationToken));

        var note = new GoodsReceiptNote
        {
            Number = number,
            PurchaseOrderId = dto.PurchaseOrderId,
            SupplierId = dto.SupplierId,
            BranchId = dto.BranchId,
            WarehouseId = dto.WarehouseId,
            ReceiptDate = dto.ReceiptDate,
            PurchaseType = dto.PurchaseType,
            Notes = dto.Notes,
            Status = GoodsReceiptStatus.Draft
        };

        foreach (var line in dto.Lines)
        {
            note.Items.Add(new GoodsReceiptItem
            {
                PurchaseOrderItemId = line.PurchaseOrderItemId,
                ItemId = line.ItemId,
                BatchNumber = line.BatchNumber,
                ManufactureDate = line.ManufactureDate,
                ExpiryDate = line.ExpiryDate,
                QuantityReceived = line.QuantityReceived,
                UnitCost = line.UnitCost,
                SalePrice = line.SalePrice ?? SalePricePolicy.FromPurchasePrice(line.UnitCost, dto.PurchaseType)
            });
        }

        _context.GoodsReceiptNotes.Add(note);
        await _context.SaveChangesAsync(cancellationToken);

        return Result<GoodsReceiptNoteDto>.Success((await GetGoodsReceiptNotesAsync(cancellationToken)).First(n => n.Id == note.Id));
    }

    public async Task<Result<GoodsReceiptNoteDto>> UpdateGoodsReceiptAsync(GoodsReceiptUpsertDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Id is null) return Result<GoodsReceiptNoteDto>.Failure("معرّف سند الاستلام مطلوب.");

        var note = await _context.GoodsReceiptNotes.Include(n => n.Items).FirstOrDefaultAsync(n => n.Id == dto.Id, cancellationToken);
        if (note is null) return Result<GoodsReceiptNoteDto>.Failure("سند الاستلام غير موجود.");
        if (note.Status != GoodsReceiptStatus.Draft) return Result<GoodsReceiptNoteDto>.Failure("لا يمكن تعديل سند استلام تم ترحيله أو إلغاؤه.");

        var validation = await ValidateGoodsReceiptAsync(dto, cancellationToken);
        if (validation is not null) return Result<GoodsReceiptNoteDto>.Failure(validation);

        note.PurchaseOrderId = dto.PurchaseOrderId;
        note.SupplierId = dto.SupplierId;
        note.BranchId = dto.BranchId;
        note.WarehouseId = dto.WarehouseId;
        note.ReceiptDate = dto.ReceiptDate;
        note.PurchaseType = dto.PurchaseType;
        note.Notes = dto.Notes;

        foreach (var existingLine in note.Items.ToList())
            _context.GoodsReceiptItems.Remove(existingLine);

        foreach (var line in dto.Lines)
        {
            note.Items.Add(new GoodsReceiptItem
            {
                PurchaseOrderItemId = line.PurchaseOrderItemId,
                ItemId = line.ItemId,
                BatchNumber = line.BatchNumber,
                ManufactureDate = line.ManufactureDate,
                ExpiryDate = line.ExpiryDate,
                QuantityReceived = line.QuantityReceived,
                UnitCost = line.UnitCost,
                SalePrice = line.SalePrice ?? SalePricePolicy.FromPurchasePrice(line.UnitCost, dto.PurchaseType)
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Result<GoodsReceiptNoteDto>.Success((await GetGoodsReceiptNotesAsync(cancellationToken)).First(n => n.Id == note.Id));
    }

    /// <summary>
    /// Commits a Draft receipt to Inventory. This is the sole integration
    /// point between Purchasing and Inventory: every line calls
    /// IInventoryService.ReceiveBatchAsync so Batches/StockTransactions stay
    /// authoritative, then reconciles the linked PurchaseOrder's received
    /// quantities and rolls its status forward (PartiallyReceived/Received).
    /// </summary>
    public async Task<Result> PostGoodsReceiptAsync(int goodsReceiptNoteId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        var note = await _context.GoodsReceiptNotes
            .Include(n => n.Items)
            .FirstOrDefaultAsync(n => n.Id == goodsReceiptNoteId, cancellationToken);

        if (note is null) return Result.Failure("سند الاستلام غير موجود.");
        if (note.Status != GoodsReceiptStatus.Draft) return Result.Failure("سند الاستلام مُرحّل بالفعل أو مُلغى.");
        if (!note.Items.Any()) return Result.Failure("لا يمكن ترحيل سند استلام لا يحتوي على أصناف.");

        foreach (var line in note.Items)
        {
            var receiveResult = await _inventoryService.ReceiveBatchAsync(new ReceiveBatchDto
            {
                ItemId = line.ItemId,
                WarehouseId = note.WarehouseId,
                BatchNumber = line.BatchNumber,
                ManufactureDate = line.ManufactureDate,
                ExpiryDate = line.ExpiryDate,
                Quantity = line.QuantityReceived,
                PurchaseType = note.PurchaseType,
                PurchasePrice = line.UnitCost,
                SalePriceOverride = line.SalePrice,
                SupplierReference = note.Number
            }, performedByUserId, cancellationToken);

            if (!receiveResult.Succeeded)
                return Result.Failure(receiveResult.Errors.FirstOrDefault() ?? $"تعذر استلام الصنف رقم {line.ItemId}.");

            if (line.PurchaseOrderItemId.HasValue)
            {
                var poItem = await _context.PurchaseOrderItems.FirstOrDefaultAsync(i => i.Id == line.PurchaseOrderItemId.Value, cancellationToken);
                if (poItem is not null)
                    poItem.QuantityReceived += line.QuantityReceived;
            }
        }

        note.Status = GoodsReceiptStatus.Posted;

        if (note.PurchaseOrderId.HasValue)
        {
            var order = await _context.PurchaseOrders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == note.PurchaseOrderId.Value, cancellationToken);
            if (order is not null)
            {
                var allReceived = order.Items.All(i => i.QuantityReceived >= i.QuantityOrdered);
                var anyReceived = order.Items.Any(i => i.QuantityReceived > 0);
                order.Status = allReceived ? PurchaseOrderStatus.Received
                    : anyReceived ? PurchaseOrderStatus.PartiallyReceived
                    : order.Status;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> DeleteGoodsReceiptAsync(int goodsReceiptNoteId, CancellationToken cancellationToken = default)
    {
        var note = await _context.GoodsReceiptNotes.FirstOrDefaultAsync(n => n.Id == goodsReceiptNoteId, cancellationToken);
        if (note is null) return Result.Failure("سند الاستلام غير موجود.");
        if (note.Status != GoodsReceiptStatus.Draft) return Result.Failure("لا يمكن حذف سند استلام تم ترحيله — أرصدة المخزون أصبحت معتمدة عليه.");

        _context.GoodsReceiptNotes.Remove(note);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<string?> ValidateGoodsReceiptAsync(GoodsReceiptUpsertDto dto, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(dto.PurchaseType)) return "نوع الشراء غير صالح.";
        if (dto.SupplierId <= 0) return "الرجاء اختيار المورد.";
        if (dto.BranchId <= 0) return "الرجاء اختيار الفرع.";
        if (dto.WarehouseId <= 0) return "الرجاء اختيار المخزن.";
        if (!dto.Lines.Any()) return "يجب إضافة صنف واحد على الأقل.";

        foreach (var line in dto.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.BatchNumber)) return "رقم الدفعة مطلوب لكل صنف.";
            if (line.QuantityReceived <= 0) return "الكمية المستلمة يجب أن تكون أكبر من صفر لكل صنف.";
            if (line.ExpiryDate.Date <= dto.ReceiptDate.Date) return $"تاريخ انتهاء الصلاحية للدفعة '{line.BatchNumber}' يجب أن يكون بعد تاريخ الاستلام.";
            if (line.UnitCost < 0 || line.SalePrice < 0) return "الأسعار لا يمكن أن تكون سالبة.";
        }

        var supplierExists = await _context.Suppliers.AnyAsync(s => s.Id == dto.SupplierId, cancellationToken);
        if (!supplierExists) return "المورد المحدد غير موجود.";

        return null;
    }

    private static GoodsReceiptNoteDto MapGoodsReceiptToDto(GoodsReceiptNote n) => new()
    {
        Id = n.Id,
        Number = n.Number,
        PurchaseOrderNumber = n.PurchaseOrder?.Number,
        SupplierName = n.Supplier.Name,
        BranchName = n.Branch.Name,
        WarehouseName = n.Warehouse.Name,
        ReceiptDate = n.ReceiptDate,
        Status = n.Status,
        PurchaseType = n.PurchaseType,
        Notes = n.Notes,
        TotalCost = n.Items.Sum(i => i.UnitCost * i.QuantityReceived),
        LineCount = n.Items.Count
    };

    private static GoodsReceiptLineDto MapGoodsReceiptLineToDto(GoodsReceiptItem i) => new()
    {
        Id = i.Id,
        ItemId = i.ItemId,
        ItemCode = i.Item.Code,
        ItemName = i.Item.Name,
        BatchNumber = i.BatchNumber,
        ManufactureDate = i.ManufactureDate,
        ExpiryDate = i.ExpiryDate,
        QuantityReceived = i.QuantityReceived,
        UnitCost = i.UnitCost,
        SalePrice = i.SalePrice,
        LineTotal = i.UnitCost * i.QuantityReceived
    };

    // ===================== Purchase Invoices =====================

    public async Task<List<PurchaseInvoiceDto>> GetPurchaseInvoicesAsync(CancellationToken cancellationToken = default)
    {
        var invoices = await _context.PurchaseInvoices
            .Include(i => i.Supplier)
            .Include(i => i.Branch)
            .Include(i => i.GoodsReceiptNote)
            .OrderByDescending(i => i.InvoiceDate).ThenByDescending(i => i.Id)
            .ToListAsync(cancellationToken);

        return invoices.Select(MapPurchaseInvoiceToDto).ToList();
    }

    public async Task<List<PurchaseInvoiceLineDto>> GetPurchaseInvoiceLinesAsync(int purchaseInvoiceId, CancellationToken cancellationToken = default)
    {
        var lines = await _context.PurchaseInvoiceItems
            .Include(i => i.Item)
            .Where(i => i.PurchaseInvoiceId == purchaseInvoiceId)
            .ToListAsync(cancellationToken);

        return lines.Select(MapPurchaseInvoiceLineToDto).ToList();
    }

    public async Task<Result<PurchaseInvoiceDto>> CreatePurchaseInvoiceAsync(PurchaseInvoiceUpsertDto dto, CancellationToken cancellationToken = default)
    {
        var validation = await ValidatePurchaseInvoiceAsync(dto, cancellationToken);
        if (validation is not null) return Result<PurchaseInvoiceDto>.Failure(validation);

        if (dto.GoodsReceiptNoteId.HasValue)
        {
            var receipt = await _context.GoodsReceiptNotes.FirstOrDefaultAsync(n => n.Id == dto.GoodsReceiptNoteId.Value, cancellationToken);
            if (receipt is null || receipt.Status != GoodsReceiptStatus.Posted)
                return Result<PurchaseInvoiceDto>.Failure("يجب اختيار سند استلام مُرحّل.");
            dto.PurchaseType = receipt.PurchaseType;
        }

        var number = await GenerateNumberAsync("PINV", () => _context.PurchaseInvoices.CountAsync(cancellationToken));

        var invoice = new PurchaseInvoice
        {
            Number = number,
            SupplierId = dto.SupplierId,
            GoodsReceiptNoteId = dto.GoodsReceiptNoteId,
            BranchId = dto.BranchId,
            InvoiceDate = dto.InvoiceDate,
            DueDate = dto.DueDate,
            DiscountAmount = dto.DiscountAmount,
            PurchaseType = dto.PurchaseType,
            Notes = dto.Notes,
            Status = PurchaseInvoiceStatus.Unpaid
        };

        foreach (var line in dto.Lines)
        {
            var lineTotal = Math.Round(line.UnitCost * line.Quantity * (1 + line.TaxRatePercent / 100m) - line.DiscountAmount, 2);
            invoice.Items.Add(new PurchaseInvoiceItem
            {
                ItemId = line.ItemId,
                Quantity = line.Quantity,
                UnitCost = line.UnitCost,
                TaxRatePercent = line.TaxRatePercent,
                DiscountAmount = line.DiscountAmount,
                LineTotal = lineTotal
            });
        }

        invoice.SubTotal = invoice.Items.Sum(i => i.UnitCost * i.Quantity);
        invoice.TaxAmount = invoice.Items.Sum(i => i.UnitCost * i.Quantity * i.TaxRatePercent / 100m);
        invoice.TotalAmount = Math.Round(invoice.SubTotal + invoice.TaxAmount - invoice.DiscountAmount, 2);

        _context.PurchaseInvoices.Add(invoice);
        await _context.SaveChangesAsync(cancellationToken);

        await _accountingService.PostPurchaseInvoiceAsync(new PurchaseInvoicePostingRequest
        {
            BranchId = invoice.BranchId,
            PurchaseInvoiceId = invoice.Id,
            PurchaseInvoiceNumber = invoice.Number,
            InvoiceDate = invoice.InvoiceDate,
            NetInventoryAmount = invoice.TotalAmount - invoice.TaxAmount,
            TaxAmount = invoice.TaxAmount,
            TotalAmount = invoice.TotalAmount
        }, cancellationToken);

        return Result<PurchaseInvoiceDto>.Success((await GetPurchaseInvoicesAsync(cancellationToken)).First(i => i.Id == invoice.Id));
    }

    public async Task<Result> RecordPaymentAsync(RecordPaymentDto dto, CancellationToken cancellationToken = default)
    {
        var invoice = await _context.PurchaseInvoices.FirstOrDefaultAsync(i => i.Id == dto.PurchaseInvoiceId, cancellationToken);
        if (invoice is null) return Result.Failure("فاتورة الشراء غير موجودة.");
        if (invoice.Status == PurchaseInvoiceStatus.Cancelled) return Result.Failure("لا يمكن تسجيل دفعة على فاتورة ملغاة.");
        if (dto.Amount <= 0) return Result.Failure("مبلغ الدفعة يجب أن يكون أكبر من صفر.");
        if (dto.Amount > invoice.AmountDue) return Result.Failure($"مبلغ الدفعة أكبر من المبلغ المستحق ({invoice.AmountDue:N2}).");

        invoice.AmountPaid += dto.Amount;
        invoice.Status = invoice.AmountDue <= 0 ? PurchaseInvoiceStatus.Paid : PurchaseInvoiceStatus.PartiallyPaid;

        await _context.SaveChangesAsync(cancellationToken);

        await _accountingService.PostPurchaseInvoicePaymentAsync(new PurchaseInvoicePaymentPostingRequest
        {
            BranchId = invoice.BranchId,
            PurchaseInvoiceId = invoice.Id,
            PurchaseInvoiceNumber = invoice.Number,
            PaymentDate = _dateTime.UtcNow,
            Amount = dto.Amount
        }, cancellationToken);

        return Result.Success();
    }

    public async Task<Result> CancelPurchaseInvoiceAsync(int purchaseInvoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await _context.PurchaseInvoices.FirstOrDefaultAsync(i => i.Id == purchaseInvoiceId, cancellationToken);
        if (invoice is null) return Result.Failure("فاتورة الشراء غير موجودة.");
        if (invoice.AmountPaid > 0) return Result.Failure("لا يمكن إلغاء فاتورة تم تسجيل دفعات عليها.");

        invoice.Status = PurchaseInvoiceStatus.Cancelled;
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<PurchaseInvoiceUpsertDto?> PrefillInvoiceFromGoodsReceiptAsync(int goodsReceiptNoteId, CancellationToken cancellationToken = default)
    {
        var note = await _context.GoodsReceiptNotes
            .Include(n => n.Items)
            .FirstOrDefaultAsync(n => n.Id == goodsReceiptNoteId, cancellationToken);

        if (note is null) return null;

        return new PurchaseInvoiceUpsertDto
        {
            SupplierId = note.SupplierId,
            PurchaseType = note.PurchaseType,
            GoodsReceiptNoteId = note.Id,
            BranchId = note.BranchId,
            InvoiceDate = DateTime.Today,
            Lines = note.Items.Select(i => new PurchaseInvoiceLineUpsertDto
            {
                ItemId = i.ItemId,
                Quantity = i.QuantityReceived,
                UnitCost = i.UnitCost,
                TaxRatePercent = 0,
                DiscountAmount = 0
            }).ToList()
        };
    }

    private async Task<string?> ValidatePurchaseInvoiceAsync(PurchaseInvoiceUpsertDto dto, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(dto.PurchaseType)) return "نوع الشراء غير صالح.";
        if (dto.SupplierId <= 0) return "الرجاء اختيار المورد.";
        if (dto.BranchId <= 0) return "الرجاء اختيار الفرع.";
        if (!dto.Lines.Any()) return "يجب إضافة صنف واحد على الأقل.";
        if (dto.Lines.Any(l => l.Quantity <= 0)) return "الكمية يجب أن تكون أكبر من صفر لكل صنف.";
        if (dto.Lines.Any(l => l.UnitCost < 0)) return "سعر الشراء لا يمكن أن يكون سالباً.";
        if (dto.DiscountAmount < 0) return "قيمة الخصم لا يمكن أن تكون سالبة.";

        var supplierExists = await _context.Suppliers.AnyAsync(s => s.Id == dto.SupplierId, cancellationToken);
        if (!supplierExists) return "المورد المحدد غير موجود.";

        return null;
    }

    private static PurchaseInvoiceDto MapPurchaseInvoiceToDto(PurchaseInvoice i) => new()
    {
        Id = i.Id,
        Number = i.Number,
        SupplierName = i.Supplier.Name,
        GoodsReceiptNumber = i.GoodsReceiptNote?.Number,
        BranchName = i.Branch.Name,
        InvoiceDate = i.InvoiceDate,
        DueDate = i.DueDate,
        SubTotal = i.SubTotal,
        TaxAmount = i.TaxAmount,
        DiscountAmount = i.DiscountAmount,
        TotalAmount = i.TotalAmount,
        AmountPaid = i.AmountPaid,
        AmountDue = i.AmountDue,
        Status = i.Status,
        PurchaseType = i.PurchaseType,
        Notes = i.Notes
    };

    private static PurchaseInvoiceLineDto MapPurchaseInvoiceLineToDto(PurchaseInvoiceItem i) => new()
    {
        Id = i.Id,
        ItemCode = i.Item.Code,
        ItemName = i.Item.Name,
        Quantity = i.Quantity,
        UnitCost = i.UnitCost,
        TaxRatePercent = i.TaxRatePercent,
        DiscountAmount = i.DiscountAmount,
        LineTotal = i.LineTotal
    };

    private async Task<string?> ValidatePurchaseOrderAsync(PurchaseOrderUpsertDto dto, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(dto.PurchaseType)) return "نوع الشراء غير صالح.";
        if (dto.SupplierId <= 0) return "الرجاء اختيار المورد.";
        if (dto.BranchId <= 0) return "الرجاء اختيار الفرع.";
        if (dto.WarehouseId <= 0) return "الرجاء اختيار المخزن.";
        if (!dto.Lines.Any()) return "يجب إضافة صنف واحد على الأقل.";
        if (dto.Lines.Any(l => l.QuantityOrdered <= 0)) return "الكمية المطلوبة يجب أن تكون أكبر من صفر لكل صنف.";
        if (dto.Lines.Any(l => l.UnitCost < 0)) return "سعر الشراء لا يمكن أن يكون سالباً.";
        if (dto.Lines.Any(l => l.SalePrice < 0)) return "سعر البيع لا يمكن أن يكون سالباً.";

        var supplierExists = await _context.Suppliers.AnyAsync(s => s.Id == dto.SupplierId, cancellationToken);
        if (!supplierExists) return "المورد المحدد غير موجود.";

        return null;
    }

    private static PurchaseOrderDto MapPurchaseOrderToDto(PurchaseOrder p) => new()
    {
        Id = p.Id,
        Number = p.Number,
        SupplierId = p.SupplierId,
        SupplierName = p.Supplier.Name,
        BranchName = p.Branch.Name,
        WarehouseName = p.Warehouse.Name,
        OrderDate = p.OrderDate,
        ExpectedDeliveryDate = p.ExpectedDeliveryDate,
        Status = p.Status,
        PurchaseType = p.PurchaseType,
        Notes = p.Notes,
        TotalAmount = p.Items.Sum(i => i.LineTotal),
        LineCount = p.Items.Count,
        HasOutstandingLines = p.Items.Any(i => i.QuantityReceived < i.QuantityOrdered)
    };

    private static PurchaseOrderLineDto MapPurchaseOrderLineToDto(PurchaseOrderItem i) => new()
    {
        Id = i.Id,
        ItemId = i.ItemId,
        ItemCode = i.Item.Code,
        ItemName = i.Item.Name,
        UnitOfMeasureName = i.Item.UnitOfMeasure.Name,
        QuantityOrdered = i.QuantityOrdered,
        QuantityReceived = i.QuantityReceived,
        QuantityOutstanding = i.QuantityOutstanding,
        UnitCost = i.UnitCost,
        SalePrice = i.SalePrice ?? SalePricePolicy.FromPurchasePrice(i.UnitCost, i.PurchaseOrder.PurchaseType),
        TaxRatePercent = i.TaxRatePercent,
        LineTotal = i.LineTotal
    };

    /// <summary>
    /// Sequential document numbering (PO-000001, GRN-000001, ...) based on the
    /// current row count. This is simple and sufficient for a single-process
    /// desktop application; a dedicated Counters table with row-level locking
    /// (planned for the Core/Settings module) would be required if the system
    /// is later split across multiple concurrent application servers.
    /// </summary>
    private static async Task<string> GenerateNumberAsync(string prefix, Func<Task<int>> countAsync)
    {
        var count = await countAsync();
        return $"{prefix}-{(count + 1):D6}";
    }
}
