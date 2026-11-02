using PharmacyERP.Application.Features.Sales.DTOs;

namespace PharmacyERP.WPF.Services;

/// <summary>A label always belongs to a specific sale unit with its registered scanner barcode.</summary>
public sealed record ProductLabelUnit(string Key, string Name, int Factor, string Barcode);

public sealed record ProductLabelRequest(string Name, string Barcode, string UnitName, decimal SalePrice,
    int Copies, double WidthMm, double HeightMm);

public static class ProductLabelSelection
{
    public static IReadOnlyList<ProductLabelUnit> UnitsFor(SaleItemLookupDto item)
    {
        var units = new List<ProductLabelUnit>();
        if (item.UnitsPerPackage > 1)
            units.Add(new("package", item.PackageUnitName, item.UnitsPerPackage, item.Barcode?.Trim() ?? ""));
        units.Add(new("base", item.UnitOfMeasureName, 1,
            (item.UnitsPerPackage == 1 && string.IsNullOrWhiteSpace(item.BaseUnitBarcode) ? item.Barcode : item.BaseUnitBarcode)?.Trim() ?? ""));
        units.AddRange(item.SaleUnits.Where(u => u.IsActive && u.BaseUnitCount > 0)
            .Select(u => new ProductLabelUnit("unit:" + u.Id, u.Name, u.BaseUnitCount, u.Barcode?.Trim() ?? "")));
        return units;
    }

    public static string? BarcodeError(ProductLabelUnit? unit, IReadOnlyList<ProductLabelUnit> units)
    {
        if (unit is null) return "اختر وحدة البيع المطلوبة.";
        if (string.IsNullOrWhiteSpace(unit.Barcode))
            return "لا يوجد باركود مسجل لهذه الوحدة. أضف باركوداً خاصاً بها من بطاقة الصنف قبل طباعة الملصق.";
        if (units.Any(u => u.Key != unit.Key && u.Factor != unit.Factor &&
            string.Equals(u.Barcode, unit.Barcode, StringComparison.OrdinalIgnoreCase)))
            return "الباركود نفسه مستخدم لوحدتين بمعاملين مختلفين. صحح باركود الوحدات من بطاقة الصنف أولاً.";
        try { ProductBarcodeEncoder.Encode(unit.Barcode); }
        catch (ArgumentException ex) { return ex.Message; }
        return null;
    }

    public static string? LayoutError(int copies, double widthMm, double heightMm)
    {
        if (copies is < 1 or > 500) return "عدد الملصقات يجب أن يكون بين 1 و500.";
        if (!double.IsFinite(widthMm) || widthMm is < 20 or > 100 || !double.IsFinite(heightMm) || heightMm is < 15 or > 100)
            return "عرض الملصق بين 20 و100 مم وارتفاعه بين 15 و100 مم.";
        return null;
    }

    public static decimal? PriceForBatch(SaleItemLookupDto? item, int batchId, int factor) =>
        item?.BatchPrices.FirstOrDefault(b => b.BatchId == batchId)?.UnitPrices.GetValueOrDefault(factor);
}
