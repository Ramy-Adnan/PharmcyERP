using System.Data;
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
    private readonly ICurrentUserService _currentUser;

    public SalesService(IApplicationDbContext context, IInventoryService inventoryService, IAccountingService accountingService, IDateTime dateTime, ICurrentUserService currentUser)
    {
        _context = context;
        _inventoryService = inventoryService;
        _accountingService = accountingService;
        _dateTime = dateTime;
        _currentUser = currentUser;
    }

    // ===================== Customers =====================

    public async Task<List<CustomerDto>> GetCustomersAsync(CancellationToken cancellationToken = default)
    {
        var customers = await _context.Customers.OrderBy(c => c.Name).ToListAsync(cancellationToken);
        var invoices = await _context.SalesInvoices.AsNoTracking()
            .Where(i => i.PaymentMethod == PaymentMethod.Credit && i.CustomerId != null && i.Status != SalesInvoiceStatus.Voided)
            .Select(i => new { i.Id, CustomerId = i.CustomerId!.Value, i.TotalAmount }).ToListAsync(cancellationToken);
        var returns = await _context.SalesReturns.AsNoTracking().GroupBy(r => r.SalesInvoiceId)
            .Select(g => new { Id = g.Key, Amount = g.Sum(r => r.TotalAmount) }).ToDictionaryAsync(r => r.Id, r => r.Amount, cancellationToken);
        var payments = await _context.Receipts.AsNoTracking().Where(r => r.ReferenceType == "CustomerCredit" && r.ReferenceId.HasValue)
            .GroupBy(r => r.ReferenceId!.Value).Select(g => new { Id = g.Key, Amount = g.Sum(r => r.Amount) })
            .ToDictionaryAsync(r => r.Id, r => r.Amount, cancellationToken);
        var balances = invoices.GroupBy(i => i.CustomerId).ToDictionary(g => g.Key,
            g => g.Sum(i => Math.Max(0, i.TotalAmount - returns.GetValueOrDefault(i.Id) - payments.GetValueOrDefault(i.Id))));
        return customers.Select(c => { var dto = MapCustomerToDto(c); dto.OutstandingAmount = balances.GetValueOrDefault(c.Id); return dto; }).ToList();
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
        searchText = searchText.Trim();
        if (searchText.Length == 0) return new();
        var today = _dateTime.UtcNow.Date;
        var matches = _context.Items.AsNoTracking().Where(i => i.IsActive &&
            (i.Barcode == searchText || i.BaseUnitBarcode == searchText || i.SaleUnits.Any(u => u.IsActive && u.Barcode == searchText) || i.Code.Contains(searchText) || i.Name.Contains(searchText) ||
             (i.GenericName != null && i.GenericName.Contains(searchText)) ||
             (i.Manufacturer != null && i.Manufacturer.Name.Contains(searchText)) ||
             (i.Barcode != null && i.Barcode.Contains(searchText))));
        var items = await matches.OrderByDescending(i => i.Barcode == searchText || i.BaseUnitBarcode == searchText || i.SaleUnits.Any(u => u.IsActive && u.Barcode == searchText))
            .ThenByDescending(i => i.Code == searchText).ThenBy(i => i.Name)
            .Include(i => i.UnitOfMeasure).Include(i => i.Manufacturer).Include(i => i.SaleUnits).Take(30).ToListAsync(cancellationToken);
        var ids = items.Select(i => i.Id).ToList();
        var batches = await _context.Batches.AsNoTracking()
            .Where(b => ids.Contains(b.ItemId) && b.WarehouseId == warehouseId && b.ExpiryDate >= today && b.QuantityOnHand > 0)
            .OrderBy(b => b.ExpiryDate).ThenBy(b => b.ReceivedAtUtc).ThenBy(b => b.Id)
            .Select(b => new { b.ItemId, b.QuantityOnHand, b.PackageSalePrice, b.ReceivedUnitFactor,
                SalePriceOverride = b.HasConfiguredSalePrice || b.SalePriceOverride > 0 ? b.SalePriceOverride : null })
            .ToListAsync(cancellationToken);
        var stock = batches.GroupBy(b => b.ItemId).ToDictionary(g => g.Key,
            g => new { Quantity = g.Sum(b => b.QuantityOnHand), SalePrice = g.First().SalePriceOverride, PackagePrice = g.First().PackageSalePrice, ReceivedFactor = g.First().ReceivedUnitFactor });
        return items.Select(item => new SaleItemLookupDto
        {
            ItemId = item.Id, Code = item.Code, Barcode = item.Barcode, Name = item.Name,
            BaseUnitBarcode = item.BaseUnitBarcode, UnitsPerPackage = item.UnitsPerPackage,
            PackageUnitName = item.PackageUnitName, ManufacturerName = item.Manufacturer?.Name, Strength = item.Strength,
            PackageSalePrice = stock.GetValueOrDefault(item.Id) is { } priceStock
                ? (priceStock.ReceivedFactor ?? item.UnitsPerPackage) == item.UnitsPerPackage && priceStock.PackagePrice.HasValue
                    ? priceStock.PackagePrice.Value : Math.Round((priceStock.SalePrice ?? item.DefaultSalePrice / item.UnitsPerPackage) * item.UnitsPerPackage, 2, MidpointRounding.AwayFromZero)
                : item.DefaultSalePrice,
            SaleUnits = item.SaleUnits.Where(u => u.IsActive).Select(u => new PharmacyERP.Application.Features.Inventory.DTOs.ItemSaleUnitDto
            {
                Id = u.Id, Name = u.Name, BaseUnitCount = u.BaseUnitCount, Barcode = u.Barcode, IsActive = true,
                SalePrice = stock.GetValueOrDefault(item.Id) is { } unitStock && unitStock.PackagePrice.HasValue && (unitStock.ReceivedFactor ?? item.UnitsPerPackage) == u.BaseUnitCount
                    ? unitStock.PackagePrice.Value : Math.Round((stock.GetValueOrDefault(item.Id)?.SalePrice ?? item.DefaultSalePrice / item.UnitsPerPackage) * u.BaseUnitCount, 2, MidpointRounding.AwayFromZero)
            }).ToList(),
            UnitOfMeasureName = item.UnitOfMeasure.Name,
            DefaultSalePrice = stock.GetValueOrDefault(item.Id)?.SalePrice ?? Math.Round(item.DefaultSalePrice / item.UnitsPerPackage, 2, MidpointRounding.AwayFromZero),
            TaxRatePercent = item.TaxRatePercent, RequiresPrescription = item.RequiresPrescription,
            AvailableQuantity = stock.GetValueOrDefault(item.Id)?.Quantity ?? 0
        }).ToList();
    }

    // ===================== Checkout =====================

    public async Task<SalesInvoiceDto?> FindCheckoutAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var number = "SI-" + requestId.ToString("N")[..24];
        var id = await _context.SalesInvoices.Where(i => i.Number == number).Select(i => (int?)i.Id).FirstOrDefaultAsync(cancellationToken);
        return id.HasValue ? await LoadInvoiceDtoAsync(id.Value, cancellationToken) : null;
    }

    public Task<Result<SalesInvoiceDto>> CheckoutAsync(SalesCheckoutDto dto, int cashierUserId, CancellationToken cancellationToken = default) =>
        InTransactionAsync(() => CheckoutCoreAsync(dto, cashierUserId, cancellationToken), r => r.Succeeded, cancellationToken);

    private async Task<Result<SalesInvoiceDto>> CheckoutCoreAsync(SalesCheckoutDto dto, int cashierUserId, CancellationToken cancellationToken)
    {
        var invoiceNumber = "SI-" + dto.RequestId.ToString("N")[..24];
        var previous = await _context.SalesInvoices.FirstOrDefaultAsync(i => i.Number == invoiceNumber, cancellationToken);
        if (previous is not null)
            return Result<SalesInvoiceDto>.Success((await LoadInvoiceDtoAsync(previous.Id, cancellationToken))!);
        if (!_currentUser.HasPermission("Sales.UsePos")) return Result<SalesInvoiceDto>.Failure("لا تملك صلاحية البيع.");
        if (dto.PaymentMethod is not (PaymentMethod.Cash or PaymentMethod.Card or PaymentMethod.Credit))
            return Result<SalesInvoiceDto>.Failure("اختر نقدي أو بطاقة أو آجل.");
        if (dto.PaymentMethod == PaymentMethod.Credit && dto.CustomerId is null)
            return Result<SalesInvoiceDto>.Failure("البيع الآجل متاح لعميل مسجل فقط.");
        if (dto.CustomerId.HasValue && !await _context.Customers.AnyAsync(c => c.Id == dto.CustomerId && c.IsActive, cancellationToken))
            return Result<SalesInvoiceDto>.Failure("العميل غير موجود أو غير نشط.");
        if (dto.Lines.Any(l => l.UnitPrice < 0 || l.TaxRatePercent < 0 || l.TaxRatePercent > 100 || l.DiscountAmount < 0 ||
            l.DiscountAmount > l.UnitPrice * l.Quantity) || dto.DiscountAmount < 0 || dto.AmountTendered < 0)
            return Result<SalesInvoiceDto>.Failure("راجع السعر والضريبة والخصومات ومبلغ الدفع.");
        if (dto.Lines.GroupBy(l => new { l.ItemId, l.SellAsPackage, l.ItemSaleUnitId }).Any(g => g.Count() > 1))
            return Result<SalesInvoiceDto>.Failure("اجمع الصنف المتكرر بنفس وحدة البيع في سطر واحد.");
        if (dto.BranchId <= 0) return Result<SalesInvoiceDto>.Failure("الرجاء اختيار الفرع.");
        if (dto.WarehouseId <= 0) return Result<SalesInvoiceDto>.Failure("الرجاء اختيار المخزن.");
        if (!dto.Lines.Any()) return Result<SalesInvoiceDto>.Failure("لا يمكن إتمام بيع بدون أصناف.");
        if (dto.Lines.Any(l => l.Quantity <= 0)) return Result<SalesInvoiceDto>.Failure("الكمية يجب أن تكون أكبر من صفر لكل سطر.");

        if (!await _context.Users.AnyAsync(u => u.Id == cashierUserId && u.Status == UserStatus.Active, cancellationToken))
            return Result<SalesInvoiceDto>.Failure("المستخدم غير موجود أو غير نشط.");
        var branchExists = await _context.Branches.AnyAsync(b => b.Id == dto.BranchId, cancellationToken);
        if (!branchExists) return Result<SalesInvoiceDto>.Failure("الفرع غير موجود.");

        var warehouseExists = await _context.Warehouses.AnyAsync(w => w.Id == dto.WarehouseId && w.BranchId == dto.BranchId && w.IsActive, cancellationToken);
        if (!warehouseExists) return Result<SalesInvoiceDto>.Failure("المخزن غير موجود.");

        // Any line whose Item.RequiresPrescription is true must be covered by a linked,
        // still-fillable Prescription belonging to the same customer — enforced here rather
        // than only in the UI, since CheckoutAsync is the single choke point for every sale.
        var lineItemIds = dto.Lines.Select(l => l.ItemId).Distinct().ToList();
        var itemsById = await _context.Items.Where(i => lineItemIds.Contains(i.Id)).Include(i => i.UnitOfMeasure).Include(i => i.Manufacturer).Include(i => i.SaleUnits).ToDictionaryAsync(i => i.Id, cancellationToken);

        if (itemsById.Count != lineItemIds.Count || itemsById.Values.Any(i => !i.IsActive))
            return Result<SalesInvoiceDto>.Failure("أحد الأصناف غير موجود أو غير نشط.");
        if (dto.Lines.Any(l => l.ItemSaleUnitId.HasValue && (l.SellAsPackage || !itemsById[l.ItemId].SaleUnits.Any(u => u.Id == l.ItemSaleUnitId && u.IsActive))))
            return Result<SalesInvoiceDto>.Failure("وحدة البيع لا تعود للصنف أو غير نشطة.");
        int Factor(SaleLineInputDto line) => line.ItemSaleUnitId.HasValue ? itemsById[line.ItemId].SaleUnits.First(u => u.Id == line.ItemSaleUnitId).BaseUnitCount
            : line.SellAsPackage ? itemsById[line.ItemId].UnitsPerPackage : 1;
        if (dto.Lines.Any(l => l.SellAsPackage && itemsById[l.ItemId].UnitsPerPackage == 1))
            return Result<SalesInvoiceDto>.Failure("هذا الصنف لا يحتوي وحدة عبوة منفصلة.");
        if (dto.Lines.Any(l => l.Quantity > int.MaxValue / Factor(l)))
            return Result<SalesInvoiceDto>.Failure("الكمية كبيرة جداً.");
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
                if (dto.Lines.Where(l => l.ItemId == line.ItemId).Sum(l => (long)l.Quantity * Factor(l)) > prescriptionLine.QuantityRemaining)
                    return Result<SalesInvoiceDto>.Failure($"الكمية المطلوبة من '{item.Name}' تتجاوز الكمية المتبقية بالوصفة ({prescriptionLine.QuantityRemaining}).");
            }
        }

        // Pre-check stock availability for every line before committing any change,
        // so a shortfall on line 3 doesn't leave lines 1-2 already deducted.
        foreach (var group in dto.Lines.GroupBy(l => l.ItemId))
        {
            var available = await _inventoryService.GetAvailableQuantityAsync(group.Key, dto.WarehouseId, cancellationToken);
            if (available < group.Sum(l => (long)l.Quantity * Factor(l)))
                return Result<SalesInvoiceDto>.Failure($"الكمية المتوفرة غير كافية لأحد الأصناف. المتوفر: {available}.");
        }

        var subTotal = dto.Lines.Sum(l => l.UnitPrice * l.Quantity);
        var taxAmount = dto.Lines.Sum(l => l.UnitPrice * l.Quantity * l.TaxRatePercent / 100m);
        var lineDiscounts = dto.Lines.Sum(l => l.DiscountAmount);
        var discountAmount = lineDiscounts + dto.DiscountAmount;
        if (discountAmount > subTotal)
            return Result<SalesInvoiceDto>.Failure("الخصم يتجاوز قيمة الأصناف.");
        var totalAmount = Math.Round(subTotal + taxAmount - discountAmount, 2);

        if (dto.PaymentMethod == PaymentMethod.Credit && (dto.AmountTendered > totalAmount || Math.Round(dto.AmountTendered, 2) != dto.AmountTendered))
            return Result<SalesInvoiceDto>.Failure("الدفعة المستلمة الآن يجب أن تكون بين صفر وإجمالي الفاتورة وبمنزلتين عشريتين كحد أقصى.");

        // Cash and card are paid in full; only credit exposes an editable initial payment.
        var amountTendered = dto.PaymentMethod == PaymentMethod.Credit ? dto.AmountTendered : totalAmount;
        var changeGiven = 0m;

        var invoice = new SalesInvoice
        {
            Number = invoiceNumber,
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
                line.ItemId, dto.WarehouseId, line.Quantity * Factor(line),
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
                ItemSaleUnitId = line.ItemSaleUnitId,
                UnitsPerSale = Factor(line),
                UnitName = line.ItemSaleUnitId.HasValue ? itemsById[line.ItemId].SaleUnits.First(u => u.Id == line.ItemSaleUnitId).Name
                    : line.SellAsPackage ? itemsById[line.ItemId].PackageUnitName : itemsById[line.ItemId].UnitOfMeasure.Name,
                ItemDisplayName = string.Join(" · ", new[] { itemsById[line.ItemId].Name, itemsById[line.ItemId].Strength, itemsById[line.ItemId].Manufacturer?.Name }.Where(v => !string.IsNullOrWhiteSpace(v))),
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

                prescriptionLine.QuantityDispensed += line.Quantity * Factor(line);
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
            PaymentMethod = invoice.PaymentMethod,
            SalesInvoiceNumber = invoice.Number,
            SaleDate = invoice.SaleAtUtc.Date,
            NetRevenueAmount = invoice.TotalAmount - invoice.TaxAmount,
            TaxAmount = invoice.TaxAmount,
            TotalAmount = invoice.TotalAmount
        }, cancellationToken);

        if (invoice.PaymentMethod == PaymentMethod.Credit && amountTendered > 0)
        {
            // The POS permission authorizes the initial cash receipt; later repayments require customer-management permission.
            var payment = await RecordCustomerPaymentCoreAsync(new CustomerDebtPaymentDto
            {
                RequestId = dto.RequestId, CustomerId = dto.CustomerId!.Value, InvoiceId = invoice.Id,
                Amount = amountTendered, PaymentMethod = PaymentMethod.Cash, Notes = "دفعة أولى عند البيع الآجل"
            }, cancellationToken, isInitialPosPayment: true);
            if (!payment.Succeeded) return Result<SalesInvoiceDto>.Failure(payment.Errors.FirstOrDefault() ?? "تعذر حفظ الدفعة الأولى.");
        }

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
        var result = invoices.Select(MapInvoiceToDto).ToList();
        var ids = result.Where(i => i.PaymentMethod == PaymentMethod.Credit).Select(i => i.Id).ToList();
        if (ids.Count > 0)
        {
            var receipts = await _context.Receipts.AsNoTracking().Where(r => r.ReferenceType == "CustomerCredit" && r.ReferenceId.HasValue && ids.Contains(r.ReferenceId.Value))
                .Select(r => new { r.Number, r.Amount }).ToDictionaryAsync(r => r.Number, r => r.Amount, cancellationToken);
            foreach (var invoice in result.Where(i => i.PaymentMethod == PaymentMethod.Credit && i.Number.StartsWith("SI-", StringComparison.Ordinal)))
                invoice.InitialPaymentAmount = receipts.GetValueOrDefault("RCT-" + invoice.Number[3..]);
        }
        return result;
    }

    public async Task<SalesInvoiceDetailDto?> GetSalesInvoiceDetailAsync(int id, CancellationToken cancellationToken = default)
    {
        return await LoadInvoiceDetailDtoAsync(id, cancellationToken);
    }

    public Task<Result> CancelSalesInvoiceAsync(int salesInvoiceId, int? performedByUserId, CancellationToken cancellationToken = default) =>
        InTransactionAsync(() => CancelSalesInvoiceCoreAsync(salesInvoiceId, performedByUserId, cancellationToken), r => r.Succeeded, cancellationToken);

    private async Task<Result> CancelSalesInvoiceCoreAsync(int salesInvoiceId, int? performedByUserId, CancellationToken cancellationToken)
    {
        var invoice = await _context.SalesInvoices
            .Include(s => s.Items).ThenInclude(i => i.BatchAllocations)
            .FirstOrDefaultAsync(s => s.Id == salesInvoiceId, cancellationToken);

        if (invoice is null) return Result.Failure("الفاتورة غير موجودة.");
        if (invoice.Status == SalesInvoiceStatus.Voided) return Result.Success();
        if (invoice.Status != SalesInvoiceStatus.Completed)
            return Result.Failure("لا يمكن إلغاء فاتورة تم إرجاع جزء منها بالفعل أو ملغاة مسبقاً. استخدم شاشة المرتجعات بدلاً من ذلك.");

        if (invoice.PaymentMethod == PaymentMethod.Credit && await PaidForInvoiceAsync(invoice.Id, cancellationToken) > 0)
            return Result.Failure("الفاتورة الآجلة لها تسديدات. لا يمكن إلغاؤها دون تسوية المبالغ المقبوضة؛ استخدم إرجاعًا ضمن الرصيد غير المسدد.");
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
                    prescriptionLine.QuantityDispensed = Math.Max(0, prescriptionLine.QuantityDispensed - item.Quantity * item.UnitsPerSale);
                }

                prescription.Status = prescription.Items.Any(i => i.QuantityDispensed > 0)
                    ? PrescriptionStatus.PartiallyFulfilled
                    : PrescriptionStatus.Active;

                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        await ReverseSaleAsync(invoice, invoice.TotalAmount, invoice.TaxAmount, "SalesInvoiceVoid", invoice.Id, cancellationToken);
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

    public Task<Result<SalesReturnDto>> ProcessReturnAsync(SalesReturnRequestDto dto, int processedByUserId, CancellationToken cancellationToken = default) =>
        InTransactionAsync(() => ProcessReturnCoreAsync(dto, processedByUserId, cancellationToken), r => r.Succeeded, cancellationToken);

    private async Task<Result<SalesReturnDto>> ProcessReturnCoreAsync(SalesReturnRequestDto dto, int processedByUserId, CancellationToken cancellationToken)
    {
        var returnNumber = "SR-" + dto.RequestId.ToString("N")[..24];
        var previous = await _context.SalesReturns.Include(r => r.SalesInvoice).Include(r => r.ProcessedByUser).Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Number == returnNumber, cancellationToken);
        if (previous is not null) return Result<SalesReturnDto>.Success(new SalesReturnDto
        {
            Id = previous.Id, Number = previous.Number, SalesInvoiceNumber = previous.SalesInvoice.Number,
            ReturnAtUtc = previous.ReturnAtUtc, Reason = previous.Reason, TotalAmount = previous.TotalAmount,
            ProcessedByUserName = previous.ProcessedByUser.FullName, LineCount = previous.Items.Count
        });
        if (string.IsNullOrWhiteSpace(dto.Reason)) return Result<SalesReturnDto>.Failure("سبب الإرجاع مطلوب.");
        if (!dto.Lines.Any()) return Result<SalesReturnDto>.Failure("يجب اختيار سطر واحد على الأقل للإرجاع.");
        if (dto.Lines.Any(l => l.Quantity <= 0)) return Result<SalesReturnDto>.Failure("كمية الإرجاع يجب أن تكون أكبر من صفر.");

        var invoice = await _context.SalesInvoices
            .Include(s => s.Items).ThenInclude(i => i.BatchAllocations)
            .FirstOrDefaultAsync(s => s.Id == dto.SalesInvoiceId, cancellationToken);

        if (invoice is null) return Result<SalesReturnDto>.Failure("الفاتورة غير موجودة.");
        if (invoice.Status == SalesInvoiceStatus.Voided) return Result<SalesReturnDto>.Failure("لا يمكن إرجاع أصناف من فاتورة ملغاة.");

        if (dto.Lines.GroupBy(l => l.SalesInvoiceItemId).Any(g => g.Count() > 1))
            return Result<SalesReturnDto>.Failure("سطر الإرجاع مكرر.");
        // Validate every requested line before touching any stock.
        foreach (var line in dto.Lines)
        {
            var invoiceItem = invoice.Items.FirstOrDefault(i => i.Id == line.SalesInvoiceItemId);
            if (invoiceItem is null) return Result<SalesReturnDto>.Failure("سطر الفاتورة المحدد غير موجود.");
            if (line.Quantity > invoiceItem.QuantityReturnable)
                return Result<SalesReturnDto>.Failure($"الكمية القابلة للإرجاع لأحد الأصناف هي {invoiceItem.QuantityReturnable} فقط.");
        }

        decimal ReturnAmount(SalesInvoiceItem item, int quantity)
        {
            // Allocate the invoice-level discount and preserve the final rounding remainder.
            var ordered = invoice.Items.OrderBy(i => i.Id).ToList();
            var linesTotal = ordered.Sum(i => i.LineTotal);
            var netLine = linesTotal == 0 ? 0 : item.Id == ordered[^1].Id
                ? invoice.TotalAmount - ordered.Take(ordered.Count - 1).Sum(i => Math.Round(invoice.TotalAmount * i.LineTotal / linesTotal, 2))
                : Math.Round(invoice.TotalAmount * item.LineTotal / linesTotal, 2);
            return Math.Round(netLine * (item.QuantityReturned + quantity) / item.Quantity, 2)
                - Math.Round(netLine * item.QuantityReturned / item.Quantity, 2);
        }
        var expectedReturn = dto.Lines.Sum(l => ReturnAmount(invoice.Items.First(i => i.Id == l.SalesInvoiceItemId), l.Quantity));
        if (invoice.PaymentMethod == PaymentMethod.Credit)
        {
            var returned = await _context.SalesReturns.Where(r => r.SalesInvoiceId == invoice.Id).SumAsync(r => r.TotalAmount, cancellationToken);
            var paid = await PaidForInvoiceAsync(invoice.Id, cancellationToken);
            if (expectedReturn > invoice.TotalAmount - returned - paid)
                return Result<SalesReturnDto>.Failure("قيمة المرتجع تتجاوز الدين المتبقي؛ يلزم تسوية المبلغ المقبوض قبل إرجاعه.");
        }
        if (invoice.PaymentMethod == PaymentMethod.Credit) await ReclassifyLegacyCreditAsync(invoice, cancellationToken);
        var salesReturn = new SalesReturn
        {
            Number = returnNumber,
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
            var remainingToRestock = line.Quantity * invoiceItem.UnitsPerSale;

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

            var lineTotal = ReturnAmount(invoiceItem, line.Quantity);
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
                    prescriptionLine.QuantityDispensed = Math.Max(0, prescriptionLine.QuantityDispensed - line.Quantity * invoiceItem.UnitsPerSale);
                }

                prescription.Status = prescription.Items.All(i => i.QuantityDispensed == 0)
                    ? PrescriptionStatus.Active
                    : PrescriptionStatus.PartiallyFulfilled;

                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        var previousReturns = await _context.SalesReturns.Where(r => r.SalesInvoiceId == invoice.Id && r.Id != salesReturn.Id)
            .SumAsync(r => r.TotalAmount, cancellationToken);
        var returnTax = invoice.TotalAmount == 0 ? 0
            : Math.Round((previousReturns + totalAmount) * invoice.TaxAmount / invoice.TotalAmount, 2)
              - Math.Round(previousReturns * invoice.TaxAmount / invoice.TotalAmount, 2);
        await ReverseSaleAsync(invoice, totalAmount, returnTax, "SalesReturn", salesReturn.Id, cancellationToken);

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

    public async Task<CustomerAccountDto?> GetCustomerAccountAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var customer = await _context.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        if (customer is null) return null;
        var invoices = await _context.SalesInvoices.AsNoTracking().Include(i => i.Branch)
            .Where(i => i.CustomerId == customerId && i.PaymentMethod == PaymentMethod.Credit && i.Status != SalesInvoiceStatus.Voided)
            .OrderByDescending(i => i.SaleAtUtc).ToListAsync(cancellationToken);
        var ids = invoices.Select(i => i.Id).ToList();
        var returns = await _context.SalesReturns.AsNoTracking().Where(r => ids.Contains(r.SalesInvoiceId))
            .GroupBy(r => r.SalesInvoiceId).Select(g => new { Id = g.Key, Amount = g.Sum(r => r.TotalAmount) })
            .ToDictionaryAsync(r => r.Id, r => r.Amount, cancellationToken);
        var payments = await _context.Receipts.AsNoTracking()
            .Where(r => r.ReferenceType == "CustomerCredit" && r.ReferenceId.HasValue && ids.Contains(r.ReferenceId.Value))
            .OrderByDescending(r => r.ReceiptDate).ToListAsync(cancellationToken);
        return new CustomerAccountDto
        {
            CustomerName = customer.Name,
            Invoices = invoices.Select(i => new CustomerCreditInvoiceDto
            {
                InvoiceId = i.Id, Number = i.Number, BranchName = i.Branch.Name, SaleAtUtc = i.SaleAtUtc,
                InvoiceAmount = i.TotalAmount, ReturnedAmount = returns.GetValueOrDefault(i.Id),
                PaidAmount = payments.Where(r => r.ReferenceId == i.Id).Sum(r => r.Amount)
            }).ToList(),
            Payments = payments.Select(r => new CustomerPaymentDto
            {
                Number = r.Number, Date = r.ReceiptDate, Amount = r.Amount,
                InvoiceNumber = invoices.First(i => i.Id == r.ReferenceId).Number,
                Method = r.SourceType == CashSourceType.Cash ? "نقدي" : "بطاقة"
            }).ToList()
        };
    }

    public Task<Result> RecordCustomerPaymentAsync(CustomerDebtPaymentDto dto, CancellationToken cancellationToken = default) =>
        InTransactionAsync(() => RecordCustomerPaymentCoreAsync(dto, cancellationToken), r => r.Succeeded, cancellationToken);

    private async Task<Result> RecordCustomerPaymentCoreAsync(CustomerDebtPaymentDto dto, CancellationToken cancellationToken, bool isInitialPosPayment = false)
    {
        if (!isInitialPosPayment && !_currentUser.HasPermission("Sales.ManageCustomers")) return Result.Failure("لا تملك صلاحية تسديد ديون العملاء.");
        var number = "RCT-" + dto.RequestId.ToString("N")[..24];
        var previousReceipt = await _context.Receipts.FirstOrDefaultAsync(r => r.Number == number, cancellationToken);
        if (previousReceipt is not null)
            return previousReceipt.ReferenceId == dto.InvoiceId && previousReceipt.Amount == dto.Amount
                ? Result.Success() : Result.Failure("حُفظ تسديد سابق بهذا الطلب؛ حدّث الكشف قبل إدخال تسديد جديد.");
        if (dto.Amount <= 0 || decimal.Round(dto.Amount, 2) != dto.Amount) return Result.Failure("أدخل مبلغاً موجباً بمنزلتين عشريتين كحد أقصى.");
        if (dto.PaymentMethod is not (PaymentMethod.Cash or PaymentMethod.Card)) return Result.Failure("تسديد الدين يكون نقداً أو بالبطاقة.");
        var invoice = await _context.SalesInvoices.Include(i => i.Customer)
            .FirstOrDefaultAsync(i => i.Id == dto.InvoiceId && i.CustomerId == dto.CustomerId, cancellationToken);
        if (invoice is null || invoice.PaymentMethod != PaymentMethod.Credit || invoice.Status == SalesInvoiceStatus.Voided)
            return Result.Failure("اختر فاتورة آجلة صالحة لهذا العميل.");
        await ReclassifyLegacyCreditAsync(invoice, cancellationToken);
        var returned = await _context.SalesReturns.Where(r => r.SalesInvoiceId == invoice.Id).SumAsync(r => r.TotalAmount, cancellationToken);
        var outstanding = invoice.TotalAmount - returned - await PaidForInvoiceAsync(invoice.Id, cancellationToken);
        if (dto.Amount > outstanding) return Result.Failure($"المبلغ يتجاوز الدين المتبقي ({outstanding:N2}).");
        var sourceCode = dto.PaymentMethod == PaymentMethod.Cash ? "1110" : "1120";
        var source = await _context.ChartOfAccounts.FirstAsync(a => a.Code == sourceCode, cancellationToken);
        var receivable = await _context.ChartOfAccounts.FirstAsync(a => a.Code == "1160", cancellationToken);
        var entry = new JournalEntry
        {
            Number = "JE-" + Guid.NewGuid().ToString("N")[..24], BranchId = invoice.BranchId,
            EntryDate = _dateTime.UtcNow.Date, Description = $"تسديد دين {invoice.Customer!.Name} - {invoice.Number}",
            ReferenceType = "CustomerCredit", ReferenceId = invoice.Id, IsPosted = true, PostedAtUtc = _dateTime.UtcNow,
            Lines = new List<JournalEntryLine>
            {
                new() { AccountId = source.Id, DebitAmount = dto.Amount },
                new() { AccountId = receivable.Id, CreditAmount = dto.Amount }
            }
        };
        _context.JournalEntries.Add(entry);
        var receipt = new Receipt
        {
            Number = number, BranchId = invoice.BranchId, ReceiptDate = _dateTime.UtcNow,
            Amount = dto.Amount, PayerName = invoice.Customer.Name, Notes = dto.Notes,
            SourceType = dto.PaymentMethod == PaymentMethod.Cash ? CashSourceType.Cash : CashSourceType.Bank,
            ReferenceType = "CustomerCredit", ReferenceId = invoice.Id, JournalEntry = entry
        };
        _context.Receipts.Add(receipt);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private Task<decimal> PaidForInvoiceAsync(int invoiceId, CancellationToken cancellationToken) =>
        _context.Receipts.Where(r => r.ReferenceType == "CustomerCredit" && r.ReferenceId == invoiceId)
            .SumAsync(r => r.Amount, cancellationToken);

    public Task<Result> ReconcileCustomerCreditAsync(int customerId, CancellationToken cancellationToken = default) =>
        InTransactionAsync(async () =>
        {
            if (!_currentUser.HasPermission("Sales.ManageCustomers")) return Result.Failure("لا تملك صلاحية تسوية قيود العملاء.");
            var invoices = await _context.SalesInvoices.Where(i => i.CustomerId == customerId && i.PaymentMethod == PaymentMethod.Credit && i.Status == SalesInvoiceStatus.Completed)
                .ToListAsync(cancellationToken);
            foreach (var invoice in invoices) await ReclassifyLegacyCreditAsync(invoice, cancellationToken);
            return Result.Success();
        }, r => r.Succeeded, cancellationToken);

    private async Task ReclassifyLegacyCreditAsync(SalesInvoice invoice, CancellationToken cancellationToken)
    {
        if (await _context.JournalEntries.AnyAsync(j => j.ReferenceType == "CustomerCreditReclass" && j.ReferenceId == invoice.Id, cancellationToken)) return;
        var original = await _context.JournalEntryLines.Include(l => l.Account).Include(l => l.JournalEntry)
            .Where(l => l.JournalEntry.ReferenceType == "SalesInvoice" && l.JournalEntry.ReferenceId == invoice.Id && l.JournalEntry.IsPosted)
            .ToListAsync(cancellationToken);
        var cashDebit = original.Where(l => l.Account.Code == "1110").Sum(l => l.DebitAmount - l.CreditAmount);
        // Only the known legacy defect: a full credit invoice posted as cash. Never guess custom entries.
        if (cashDebit != invoice.TotalAmount || cashDebit <= 0 || original.Any(l => l.Account.Code == "1160")) return;
        var accounts = await _context.ChartOfAccounts.Where(a => a.Code == "1110" || a.Code == "1160")
            .ToDictionaryAsync(a => a.Code, cancellationToken);
        _context.JournalEntries.Add(new JournalEntry
        {
            Number = "JE-" + Guid.NewGuid().ToString("N")[..24], BranchId = invoice.BranchId, EntryDate = _dateTime.UtcNow.Date,
            Description = $"تصحيح تصنيف بيع آجل قديم - {invoice.Number}", ReferenceType = "CustomerCreditReclass",
            ReferenceId = invoice.Id, IsPosted = true, PostedAtUtc = _dateTime.UtcNow,
            Lines = new List<JournalEntryLine>
            {
                new() { AccountId = accounts["1160"].Id, DebitAmount = cashDebit },
                new() { AccountId = accounts["1110"].Id, CreditAmount = cashDebit }
            }
        });
        await _context.SaveChangesAsync(cancellationToken);
        var legacyReturns = await _context.SalesReturns.Where(r => r.SalesInvoiceId == invoice.Id).ToListAsync(cancellationToken);
        foreach (var returned in legacyReturns)
        {
            if (await _context.JournalEntries.AnyAsync(j => j.ReferenceType == "SalesReturn" && j.ReferenceId == returned.Id, cancellationToken)) continue;
            var tax = invoice.TotalAmount == 0 ? 0 : Math.Round(returned.TotalAmount * invoice.TaxAmount / invoice.TotalAmount, 2);
            await ReverseSaleAsync(invoice, returned.TotalAmount, tax, "SalesReturn", returned.Id, cancellationToken);
        }
    }

    private async Task ReverseSaleAsync(SalesInvoice invoice, decimal amount, decimal tax, string referenceType, int referenceId, CancellationToken cancellationToken)
    {
        if (amount == 0) return;
        // Reverse only invoices with an original posting; retain compatibility with old unposted data.
        if (!await _context.JournalEntries.AnyAsync(j => j.ReferenceType == "SalesInvoice" && j.ReferenceId == invoice.Id && j.IsPosted, cancellationToken)) return;
        if (invoice.PaymentMethod == PaymentMethod.Credit) await ReclassifyLegacyCreditAsync(invoice, cancellationToken);
        var settlement = invoice.PaymentMethod switch { PaymentMethod.Credit => "1160", PaymentMethod.Card => "1120", _ => "1110" };
        var accounts = await _context.ChartOfAccounts.Where(a => a.Code == settlement || a.Code == "4100" || a.Code == "2200")
            .ToDictionaryAsync(a => a.Code, cancellationToken);
        var entry = new JournalEntry
        {
            Number = "JE-" + Guid.NewGuid().ToString("N")[..24], BranchId = invoice.BranchId,
            EntryDate = _dateTime.UtcNow.Date, Description = $"عكس مبيعات - {invoice.Number}", ReferenceType = referenceType,
            ReferenceId = referenceId, IsPosted = true, PostedAtUtc = _dateTime.UtcNow,
            Lines = new List<JournalEntryLine>
            {
                new() { AccountId = accounts["4100"].Id, DebitAmount = amount - tax },
                new() { AccountId = accounts[settlement].Id, CreditAmount = amount }
            }
        };
        if (tax > 0) entry.Lines.Add(new JournalEntryLine { AccountId = accounts["2200"].Id, DebitAmount = tax });
        _context.JournalEntries.Add(entry);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<T> InTransactionAsync<T>(Func<Task<T>> action, Func<T, bool> succeeded, CancellationToken cancellationToken)
    {
        if (_context is not DbContext db || !db.Database.IsRelational()) return await action();
        // SQL Server's configured retry strategy must own the entire transaction, not each SaveChanges.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            try
            {
                var result = await action();
                if (succeeded(result)) await transaction.CommitAsync(cancellationToken);
                else
                {
                    await transaction.RollbackAsync(cancellationToken);
                    db.ChangeTracker.Clear();
                }
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
                throw;
            }
        });
    }

    private async Task<SalesInvoiceDto?> LoadInvoiceDtoAsync(int id, CancellationToken cancellationToken)
    {
        var invoice = await _context.SalesInvoices
            .Include(s => s.Branch)
            .Include(s => s.Customer)
            .Include(s => s.Prescription)
            .Include(s => s.CashierUser)
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (invoice is null) return null;
        var dto = MapInvoiceToDto(invoice);
        await LoadInitialPaymentAsync(dto, cancellationToken);
        return dto;
    }

    private async Task LoadInitialPaymentAsync(SalesInvoiceDto dto, CancellationToken cancellationToken)
    {
        if (dto.PaymentMethod != PaymentMethod.Credit || !dto.Number.StartsWith("SI-", StringComparison.Ordinal)) return;
        var receiptNumber = "RCT-" + dto.Number[3..];
        dto.InitialPaymentAmount = await _context.Receipts.Where(r => r.ReferenceType == "CustomerCredit" && r.ReferenceId == dto.Id && r.Number == receiptNumber)
            .Select(r => (decimal?)r.Amount).FirstOrDefaultAsync(cancellationToken) ?? 0;
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

        var header = MapInvoiceToDto(invoice);
        await LoadInitialPaymentAsync(header, cancellationToken);
        return new SalesInvoiceDetailDto
        {
            Header = header,
            Lines = invoice.Items.Select(i => new SalesInvoiceLineDto
            {
                Id = i.Id,
                ItemId = i.ItemId,
                ItemCode = i.Item.Code,
                ItemName = i.ItemDisplayName ?? i.Item.Name,
                UnitsPerSale = i.UnitsPerSale,
                UnitName = i.UnitName,
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

}
