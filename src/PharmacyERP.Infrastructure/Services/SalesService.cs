using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Accounting;
using PharmacyERP.Application.Features.Accounting.DTOs;
using PharmacyERP.Application.Features.Inventory;
using PharmacyERP.Application.Features.Sales;
using PharmacyERP.Application.Features.Sales.DTOs;
using PharmacyERP.Domain.Entities;
using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Infrastructure.Services;

/// <summary>
/// Implements the POS checkout, sales history, and customer returns workflow.
/// Every quantity change is delegated to IInventoryService (FEFO issuance on
/// sale, targeted batch restock on return) so the StockTransaction ledger
/// remains the single source of truth for all stock movement regardless of
/// which module triggered it — the same integration pattern Purchasing uses.
/// Every completed sale is also posted to Accounting via IAccountingService,
/// so the financial books stay in sync with POS activity automatically.
/// </summary>
public class SalesService : ISalesService
{
    private readonly IApplicationDbContext _context;
    private readonly IInventoryService _inventoryService;
    private readonly IAccountingService _accountingService;
    private readonly IDateTime _dateTime;

    public SalesService(IApplicationDbContext context, IInventoryService inventoryService, IAccountingService accountingService, IDateTime dateTime)
    {
        _context = context;
        _inventoryService = inventoryService;
        _accountingService = accountingService;
        _dateTime = dateTime;
    }

    // ===================== Customers =====================

    public async Task<List<CustomerDto>> GetCustomersAsync(CancellationToken cancellationToken = default)
    {
        var customers = await _context.Customers.OrderBy(c => c.Name).ToListAsync(cancellationToken);
        return customers.Select(MapCustomerToDto).ToList();
    }

    public async Task<CustomerUpsertDto?> GetCustomerForEditAsync(int id, CancellationToken cancellationToken = default)
    {
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (customer is null) return null;

        return new CustomerUpsertDto
        {
            Id = customer.Id,
            Code = customer.Code,
            Name = customer.Name,
            Phone = customer.Phone,
            Address = customer.Address,
            IsActive = customer.IsActive
        };
    }

    public async Task<Result<CustomerDto>> CreateCustomerAsync(CustomerUpsertDto dto, CancellationToken cancellationToken = default)
    {
        var validation = await ValidateCustomerAsync(dto, cancellationToken);
        if (validation is not null) return Result<CustomerDto>.Failure(validation);

        var customer = new Customer
        {
            Code = dto.Code.Trim().ToUpperInvariant(),
            Name = dto.Name.Trim(),
            Phone = dto.Phone,
            Address = dto.Address,
            IsActive = dto.IsActive
        };

        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(cancellationToken);

        return Result<CustomerDto>.Success(MapCustomerToDto(customer));
    }

    public async Task<Result<CustomerDto>> UpdateCustomerAsync(CustomerUpsertDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Id is null) return Result<CustomerDto>.Failure("معرّف العميل مطلوب.");

        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == dto.Id, cancellationToken);
        if (customer is null) return Result<CustomerDto>.Failure("العميل غير موجود.");

        var validation = await ValidateCustomerAsync(dto, cancellationToken);
        if (validation is not null) return Result<CustomerDto>.Failure(validation);

        customer.Code = dto.Code.Trim().ToUpperInvariant();
        customer.Name = dto.Name.Trim();
        customer.Phone = dto.Phone;
        customer.Address = dto.Address;
        customer.IsActive = dto.IsActive;

        await _context.SaveChangesAsync(cancellationToken);
        return Result<CustomerDto>.Success(MapCustomerToDto(customer));
    }

    public async Task<Result> SetCustomerActiveStatusAsync(int customerId, bool isActive, CancellationToken cancellationToken = default)
    {
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        if (customer is null) return Result.Failure("العميل غير موجود.");

        customer.IsActive = isActive;
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<string?> ValidateCustomerAsync(CustomerUpsertDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.Code)) return "رمز العميل مطلوب.";
        if (string.IsNullOrWhiteSpace(dto.Name)) return "اسم العميل مطلوب.";

        var codeTaken = await _context.Customers.AnyAsync(c => c.Code == dto.Code.Trim().ToUpper() && c.Id != dto.Id, cancellationToken);
        if (codeTaken) return "رمز العميل مستخدم مسبقاً.";

        return null;
    }

    // ===================== POS item lookup =====================

    public async Task<List<SaleItemLookupDto>> SearchSaleItemsAsync(string searchText, int warehouseId, CancellationToken cancellationToken = default)
    {
        var items = await _inventoryService.GetItemsAsync(searchText, cancellationToken);
        var results = new List<SaleItemLookupDto>();

        foreach (var item in items.Where(i => i.IsActive).Take(30))
        {
            var availableQty = await _inventoryService.GetAvailableQuantityAsync(item.Id, warehouseId, cancellationToken);
            results.Add(new SaleItemLookupDto
            {
                ItemId = item.Id,
                Code = item.Code,
                Barcode = item.Barcode,
                Name = item.Name,
                UnitOfMeasureName = item.UnitOfMeasureName,
                DefaultSalePrice = item.DefaultSalePrice,
                TaxRatePercent = item.TaxRatePercent,
                RequiresPrescription = item.RequiresPrescription,
                AvailableQuantity = availableQty
            });
        }

        return results;
    }

    // ===================== Checkout =====================

    public async Task<Result<SalesInvoiceDto>> CheckoutAsync(SalesCheckoutDto dto, int cashierUserId, CancellationToken cancellationToken = default)
    {
        if (dto.BranchId <= 0) return Result<SalesInvoiceDto>.Failure("الرجاء اختيار الفرع.");
        if (dto.WarehouseId <= 0) return Result<SalesInvoiceDto>.Failure("الرجاء اختيار المخزن.");
        if (!dto.Lines.Any()) return Result<SalesInvoiceDto>.Failure("لا يمكن إتمام بيع بدون أصناف.");
        if (dto.Lines.Any(l => l.Quantity <= 0)) return Result<SalesInvoiceDto>.Failure("الكمية يجب أن تكون أكبر من صفر لكل سطر.");

        var branchExists = await _context.Branches.AnyAsync(b => b.Id == dto.BranchId, cancellationToken);
        if (!branchExists) return Result<SalesInvoiceDto>.Failure("الفرع غير موجود.");

        var warehouseExists = await _context.Warehouses.AnyAsync(w => w.Id == dto.WarehouseId, cancellationToken);
        if (!warehouseExists) return Result<SalesInvoiceDto>.Failure("المخزن غير موجود.");

        // Any line whose Item.RequiresPrescription is true must be covered by a linked,
        // still-fillable Prescription belonging to the same customer — enforced here rather
        // than only in the UI, since CheckoutAsync is the single choke point for every sale.
        var lineItemIds = dto.Lines.Select(l => l.ItemId).Distinct().ToList();
        var itemsById = await _context.Items.Where(i => lineItemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);

        var requiresPrescription = lineItemIds.Any(id => itemsById.TryGetValue(id, out var item) && item.RequiresPrescription);

        Prescription? prescription = null;
        if (requiresPrescription)
        {
            if (dto.PrescriptionId is null)
                return Result<SalesInvoiceDto>.Failure("أحد الأصناف يتطلب وصفة طبية. الرجاء ربط الفاتورة بوصفة طبية أولاً.");

            prescription = await _context.Prescriptions
                .Include(p => p.Items)
                .FirstOrDefaultAsync(p => p.Id == dto.PrescriptionId.Value, cancellationToken);

            if (prescription is null) return Result<SalesInvoiceDto>.Failure("الوصفة الطبية المحددة غير موجودة.");
            if (prescription.CustomerId != dto.CustomerId) return Result<SalesInvoiceDto>.Failure("الوصفة الطبية المحددة لا تعود لهذا العميل.");
            if (prescription.Status is PrescriptionStatus.Fulfilled or PrescriptionStatus.Cancelled or PrescriptionStatus.Expired)
                return Result<SalesInvoiceDto>.Failure("هذه الوصفة الطبية لم تعد قابلة للصرف.");

            foreach (var line in dto.Lines)
            {
                if (!itemsById.TryGetValue(line.ItemId, out var item) || !item.RequiresPrescription) continue;

                var prescriptionLine = prescription.Items.FirstOrDefault(i => i.ItemId == line.ItemId);
                if (prescriptionLine is null)
                    return Result<SalesInvoiceDto>.Failure($"الصنف '{item.Name}' غير مذكور في الوصفة الطبية المحددة.");
                if (line.Quantity > prescriptionLine.QuantityRemaining)
                    return Result<SalesInvoiceDto>.Failure($"الكمية المطلوبة من '{item.Name}' تتجاوز الكمية المتبقية بالوصفة ({prescriptionLine.QuantityRemaining}).");
            }
        }

        // Pre-check stock availability for every line before committing any change,
        // so a shortfall on line 3 doesn't leave lines 1-2 already deducted.
        foreach (var line in dto.Lines)
        {
            var available = await _inventoryService.GetAvailableQuantityAsync(line.ItemId, dto.WarehouseId, cancellationToken);
            if (available < line.Quantity)
                return Result<SalesInvoiceDto>.Failure($"الكمية المتوفرة غير كافية لأحد الأصناف. المتوفر: {available}.");
        }

        var subTotal = dto.Lines.Sum(l => l.UnitPrice * l.Quantity);
        var taxAmount = dto.Lines.Sum(l => l.UnitPrice * l.Quantity * l.TaxRatePercent / 100m);
        var lineDiscounts = dto.Lines.Sum(l => l.DiscountAmount);
        var discountAmount = lineDiscounts + dto.DiscountAmount;
        var totalAmount = Math.Max(0, Math.Round(subTotal + taxAmount - discountAmount, 2));

        if (dto.PaymentMethod == PaymentMethod.Cash && dto.AmountTendered < totalAmount)
            return Result<SalesInvoiceDto>.Failure("المبلغ المستلم من العميل أقل من إجمالي الفاتورة.");

        var amountTendered = dto.PaymentMethod == PaymentMethod.Cash ? dto.AmountTendered : totalAmount;
        var changeGiven = Math.Max(0, amountTendered - totalAmount);

        var invoice = new SalesInvoice
        {
            Number = await GenerateNumberAsync("SI", () => _context.SalesInvoices.CountAsync(cancellationToken)),
            BranchId = dto.BranchId,
            WarehouseId = dto.WarehouseId,
            CustomerId = dto.CustomerId,
            PrescriptionId = dto.PrescriptionId,
            CashierUserId = cashierUserId,
            SaleAtUtc = _dateTime.UtcNow,
            SubTotal = Math.Round(subTotal, 2),
            TaxAmount = Math.Round(taxAmount, 2),
            DiscountAmount = Math.Round(discountAmount, 2),
            TotalAmount = totalAmount,
            PaymentMethod = dto.PaymentMethod,
            AmountTendered = amountTendered,
            ChangeGiven = changeGiven,
            Status = SalesInvoiceStatus.Completed,
            Notes = dto.Notes
        };

        _context.SalesInvoices.Add(invoice);
        await _context.SaveChangesAsync(cancellationToken);

        foreach (var line in dto.Lines)
        {
            var issueResult = await _inventoryService.IssueStockFefoAsync(
                line.ItemId, dto.WarehouseId, line.Quantity,
                referenceType: "SalesInvoice", referenceId: invoice.Id,
                performedByUserId: cashierUserId, cancellationToken: cancellationToken);

            if (!issueResult.Succeeded)
            {
                // Extremely unlikely given the pre-check above (only a concurrent sale
                // in the same instant could cause this) — surface the failure rather
                // than leaving a half-built invoice.
                return Result<SalesInvoiceDto>.Failure(issueResult.Errors.FirstOrDefault() ?? "تعذر صرف الكمية من المخزون.");
            }

            var lineTotal = Math.Round(line.UnitPrice * line.Quantity * (1 + line.TaxRatePercent / 100m) - line.DiscountAmount, 2);

            var invoiceItem = new SalesInvoiceItem
            {
                SalesInvoiceId = invoice.Id,
                ItemId = line.ItemId,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                TaxRatePercent = line.TaxRatePercent,
                DiscountAmount = line.DiscountAmount,
                LineTotal = lineTotal
            };

            _context.SalesInvoiceItems.Add(invoiceItem);
            await _context.SaveChangesAsync(cancellationToken);

            foreach (var allocation in issueResult.Value!)
            {
                _context.SalesInvoiceItemBatches.Add(new SalesInvoiceItemBatch
                {
                    SalesInvoiceItemId = invoiceItem.Id,
                    BatchId = allocation.BatchId,
                    QuantityTaken = allocation.QuantityTaken,
                    UnitCost = allocation.UnitCost
                });
            }

            await _context.SaveChangesAsync(cancellationToken);
        }

        if (prescription is not null)
        {
            foreach (var line in dto.Lines)
            {
                var prescriptionLine = prescription.Items.FirstOrDefault(i => i.ItemId == line.ItemId);
                if (prescriptionLine is null) continue;

                prescriptionLine.QuantityDispensed += line.Quantity;
            }

            prescription.Status = prescription.Items.All(i => i.QuantityRemaining == 0)
                ? PrescriptionStatus.Fulfilled
                : PrescriptionStatus.PartiallyFulfilled;

            await _context.SaveChangesAsync(cancellationToken);
        }

        await _accountingService.PostSalesInvoiceAsync(new SalesInvoicePostingRequest
        {
            BranchId = invoice.BranchId,
            SalesInvoiceId = invoice.Id,
            SalesInvoiceNumber = invoice.Number,
            SaleDate = invoice.SaleAtUtc.Date,
            NetRevenueAmount = invoice.TotalAmount - invoice.TaxAmount,
            TaxAmount = invoice.TaxAmount,
            TotalAmount = invoice.TotalAmount
        }, cancellationToken);

        return Result<SalesInvoiceDto>.Success((await LoadInvoiceDtoAsync(invoice.Id, cancellationToken))!);
    }

    // ===================== Sales history =====================

    public async Task<List<SalesInvoiceDto>> GetSalesInvoicesAsync(DateTime? fromUtc = null, DateTime? toUtc = null, CancellationToken cancellationToken = default)
    {
        var query = _context.SalesInvoices
            .Include(s => s.Branch)
            .Include(s => s.Customer)
            .Include(s => s.Prescription)
            .Include(s => s.CashierUser)
            .Include(s => s.Items)
            .AsQueryable();

        if (fromUtc.HasValue) query = query.Where(s => s.SaleAtUtc >= fromUtc.Value);
        if (toUtc.HasValue) query = query.Where(s => s.SaleAtUtc <= toUtc.Value);

        var invoices = await query.OrderByDescending(s => s.SaleAtUtc).Take(500).ToListAsync(cancellationToken);
        return invoices.Select(MapInvoiceToDto).ToList();
    }

    public async Task<SalesInvoiceDetailDto?> GetSalesInvoiceDetailAsync(int id, CancellationToken cancellationToken = default)
    {
        return await LoadInvoiceDetailDtoAsync(id, cancellationToken);
    }

    public async Task<Result> CancelSalesInvoiceAsync(int salesInvoiceId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        var invoice = await _context.SalesInvoices
            .Include(s => s.Items).ThenInclude(i => i.BatchAllocations)
            .FirstOrDefaultAsync(s => s.Id == salesInvoiceId, cancellationToken);

        if (invoice is null) return Result.Failure("الفاتورة غير موجودة.");
        if (invoice.Status != SalesInvoiceStatus.Completed)
            return Result.Failure("لا يمكن إلغاء فاتورة تم إرجاع جزء منها بالفعل أو ملغاة مسبقاً. استخدم شاشة المرتجعات بدلاً من ذلك.");

        foreach (var item in invoice.Items)
        {
            foreach (var allocation in item.BatchAllocations)
            {
                var restockQty = allocation.QuantityTaken - allocation.RestockedQuantity;
                if (restockQty <= 0) continue;

                var restockResult = await _inventoryService.RestockBatchAsync(
                    allocation.BatchId, restockQty, referenceType: "SalesInvoiceVoid", referenceId: invoice.Id,
                    performedByUserId, cancellationToken);

                if (!restockResult.Succeeded)
                    return Result.Failure(restockResult.Errors.FirstOrDefault() ?? "تعذر إرجاع الكمية إلى المخزون.");

                allocation.RestockedQuantity = allocation.QuantityTaken;
            }

            item.QuantityReturned = item.Quantity;
        }

        invoice.Status = SalesInvoiceStatus.Voided;
        await _context.SaveChangesAsync(cancellationToken);

        if (invoice.PrescriptionId.HasValue)
        {
            var prescription = await _context.Prescriptions
                .Include(p => p.Items)
                .FirstOrDefaultAsync(p => p.Id == invoice.PrescriptionId.Value, cancellationToken);

            if (prescription is not null)
            {
                foreach (var item in invoice.Items)
                {
                    var prescriptionLine = prescription.Items.FirstOrDefault(i => i.ItemId == item.ItemId);
                    if (prescriptionLine is null) continue;
                    prescriptionLine.QuantityDispensed = Math.Max(0, prescriptionLine.QuantityDispensed - item.Quantity);
                }

                prescription.Status = prescription.Items.Any(i => i.QuantityDispensed > 0)
                    ? PrescriptionStatus.PartiallyFulfilled
                    : PrescriptionStatus.Active;

                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        return Result.Success();
    }

    // ===================== Returns =====================

    public async Task<List<SalesReturnDto>> GetSalesReturnsAsync(CancellationToken cancellationToken = default)
    {
        var returns = await _context.SalesReturns
            .Include(r => r.SalesInvoice)
            .Include(r => r.ProcessedByUser)
            .Include(r => r.Items)
            .OrderByDescending(r => r.ReturnAtUtc)
            .Take(500)
            .ToListAsync(cancellationToken);

        return returns.Select(r => new SalesReturnDto
        {
            Id = r.Id,
            Number = r.Number,
            SalesInvoiceNumber = r.SalesInvoice.Number,
            ReturnAtUtc = r.ReturnAtUtc,
            Reason = r.Reason,
            TotalAmount = r.TotalAmount,
            ProcessedByUserName = r.ProcessedByUser.FullName,
            LineCount = r.Items.Count
        }).ToList();
    }

    public async Task<Result<SalesReturnDto>> ProcessReturnAsync(SalesReturnRequestDto dto, int processedByUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Reason)) return Result<SalesReturnDto>.Failure("سبب الإرجاع مطلوب.");
        if (!dto.Lines.Any()) return Result<SalesReturnDto>.Failure("يجب اختيار سطر واحد على الأقل للإرجاع.");
        if (dto.Lines.Any(l => l.Quantity <= 0)) return Result<SalesReturnDto>.Failure("كمية الإرجاع يجب أن تكون أكبر من صفر.");

        var invoice = await _context.SalesInvoices
            .Include(s => s.Items).ThenInclude(i => i.BatchAllocations)
            .FirstOrDefaultAsync(s => s.Id == dto.SalesInvoiceId, cancellationToken);

        if (invoice is null) return Result<SalesReturnDto>.Failure("الفاتورة غير موجودة.");
        if (invoice.Status == SalesInvoiceStatus.Voided) return Result<SalesReturnDto>.Failure("لا يمكن إرجاع أصناف من فاتورة ملغاة.");

        // Validate every requested line before touching any stock.
        foreach (var line in dto.Lines)
        {
            var invoiceItem = invoice.Items.FirstOrDefault(i => i.Id == line.SalesInvoiceItemId);
            if (invoiceItem is null) return Result<SalesReturnDto>.Failure("سطر الفاتورة المحدد غير موجود.");
            if (line.Quantity > invoiceItem.QuantityReturnable)
                return Result<SalesReturnDto>.Failure($"الكمية القابلة للإرجاع لأحد الأصناف هي {invoiceItem.QuantityReturnable} فقط.");
        }

        var salesReturn = new SalesReturn
        {
            Number = await GenerateNumberAsync("SR", () => _context.SalesReturns.CountAsync(cancellationToken)),
            SalesInvoiceId = invoice.Id,
            BranchId = invoice.BranchId,
            ReturnAtUtc = _dateTime.UtcNow,
            Reason = dto.Reason.Trim(),
            ProcessedByUserId = processedByUserId,
            TotalAmount = 0
        };

        _context.SalesReturns.Add(salesReturn);
        await _context.SaveChangesAsync(cancellationToken);

        decimal totalAmount = 0;

        foreach (var line in dto.Lines)
        {
            var invoiceItem = invoice.Items.First(i => i.Id == line.SalesInvoiceItemId);
            var remainingToRestock = line.Quantity;

            // Restock from the exact batches this line was originally taken from, in
            // the same order, respecting how much of each allocation was already
            // restocked by any earlier partial return of this same line.
            foreach (var allocation in invoiceItem.BatchAllocations.OrderBy(a => a.BatchId))
            {
                if (remainingToRestock <= 0) break;

                var takeFromThisAllocation = Math.Min(remainingToRestock, allocation.QuantityAvailableToReturn);
                if (takeFromThisAllocation <= 0) continue;

                var restockResult = await _inventoryService.RestockBatchAsync(
                    allocation.BatchId, takeFromThisAllocation, referenceType: "SalesReturn", referenceId: salesReturn.Id,
                    processedByUserId, cancellationToken);

                if (!restockResult.Succeeded)
                    return Result<SalesReturnDto>.Failure(restockResult.Errors.FirstOrDefault() ?? "تعذر إرجاع الكمية إلى المخزون.");

                allocation.RestockedQuantity += takeFromThisAllocation;
                remainingToRestock -= takeFromThisAllocation;
            }

            var lineTotal = Math.Round(invoiceItem.UnitPrice * line.Quantity, 2);
            totalAmount += lineTotal;

            invoiceItem.QuantityReturned += line.Quantity;

            _context.SalesReturnItems.Add(new SalesReturnItem
            {
                SalesReturnId = salesReturn.Id,
                SalesInvoiceItemId = invoiceItem.Id,
                ItemId = invoiceItem.ItemId,
                Quantity = line.Quantity,
                UnitPrice = invoiceItem.UnitPrice,
                LineTotal = lineTotal
            });
        }

        salesReturn.TotalAmount = totalAmount;

        var allReturned = invoice.Items.All(i => i.QuantityReturned >= i.Quantity);
        invoice.Status = allReturned ? SalesInvoiceStatus.FullyReturned : SalesInvoiceStatus.PartiallyReturned;

        await _context.SaveChangesAsync(cancellationToken);

        if (invoice.PrescriptionId.HasValue)
        {
            var prescription = await _context.Prescriptions
                .Include(p => p.Items)
                .FirstOrDefaultAsync(p => p.Id == invoice.PrescriptionId.Value, cancellationToken);

            if (prescription is not null)
            {
                foreach (var line in dto.Lines)
                {
                    var invoiceItem = invoice.Items.First(i => i.Id == line.SalesInvoiceItemId);
                    var prescriptionLine = prescription.Items.FirstOrDefault(i => i.ItemId == invoiceItem.ItemId);
                    if (prescriptionLine is null) continue;
                    prescriptionLine.QuantityDispensed = Math.Max(0, prescriptionLine.QuantityDispensed - line.Quantity);
                }

                prescription.Status = prescription.Items.All(i => i.QuantityDispensed == 0)
                    ? PrescriptionStatus.Active
                    : PrescriptionStatus.PartiallyFulfilled;

                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        var reloaded = await _context.SalesReturns
            .Include(r => r.SalesInvoice)
            .Include(r => r.ProcessedByUser)
            .Include(r => r.Items)
            .FirstAsync(r => r.Id == salesReturn.Id, cancellationToken);

        return Result<SalesReturnDto>.Success(new SalesReturnDto
        {
            Id = reloaded.Id,
            Number = reloaded.Number,
            SalesInvoiceNumber = reloaded.SalesInvoice.Number,
            ReturnAtUtc = reloaded.ReturnAtUtc,
            Reason = reloaded.Reason,
            TotalAmount = reloaded.TotalAmount,
            ProcessedByUserName = reloaded.ProcessedByUser.FullName,
            LineCount = reloaded.Items.Count
        });
    }

    // ===================== Internal helpers =====================

    private async Task<SalesInvoiceDto?> LoadInvoiceDtoAsync(int id, CancellationToken cancellationToken)
    {
        var invoice = await _context.SalesInvoices
            .Include(s => s.Branch)
            .Include(s => s.Customer)
            .Include(s => s.Prescription)
            .Include(s => s.CashierUser)
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        return invoice is null ? null : MapInvoiceToDto(invoice);
    }

    private async Task<SalesInvoiceDetailDto?> LoadInvoiceDetailDtoAsync(int id, CancellationToken cancellationToken)
    {
        var invoice = await _context.SalesInvoices
            .Include(s => s.Branch)
            .Include(s => s.Customer)
            .Include(s => s.Prescription)
            .Include(s => s.CashierUser)
            .Include(s => s.Items).ThenInclude(i => i.Item)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (invoice is null) return null;

        return new SalesInvoiceDetailDto
        {
            Header = MapInvoiceToDto(invoice),
            Lines = invoice.Items.Select(i => new SalesInvoiceLineDto
            {
                Id = i.Id,
                ItemId = i.ItemId,
                ItemCode = i.Item.Code,
                ItemName = i.Item.Name,
                Quantity = i.Quantity,
                QuantityReturned = i.QuantityReturned,
                QuantityReturnable = i.QuantityReturnable,
                UnitPrice = i.UnitPrice,
                TaxRatePercent = i.TaxRatePercent,
                DiscountAmount = i.DiscountAmount,
                LineTotal = i.LineTotal
            }).ToList()
        };
    }

    private static SalesInvoiceDto MapInvoiceToDto(SalesInvoice s) => new()
    {
        Id = s.Id,
        Number = s.Number,
        BranchName = s.Branch.Name,
        CustomerId = s.CustomerId,
        CustomerName = s.Customer?.Name,
        PrescriptionNumber = s.Prescription?.Number,
        CashierName = s.CashierUser.FullName,
        SaleAtUtc = s.SaleAtUtc,
        SubTotal = s.SubTotal,
        TaxAmount = s.TaxAmount,
        DiscountAmount = s.DiscountAmount,
        TotalAmount = s.TotalAmount,
        PaymentMethod = s.PaymentMethod,
        AmountTendered = s.AmountTendered,
        ChangeGiven = s.ChangeGiven,
        Status = s.Status,
        LineCount = s.Items.Count
    };

    private static CustomerDto MapCustomerToDto(Customer c) => new()
    {
        Id = c.Id,
        Code = c.Code,
        Name = c.Name,
        Phone = c.Phone,
        Address = c.Address,
        IsActive = c.IsActive
    };

    private static async Task<string> GenerateNumberAsync(string prefix, Func<Task<int>> countAsync)
    {
        var count = await countAsync();
        return $"{prefix}-{(count + 1):D6}";
    }
}
