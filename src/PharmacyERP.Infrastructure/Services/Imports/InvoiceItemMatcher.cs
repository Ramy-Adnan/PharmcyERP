using System.Text;
using System.Text.RegularExpressions;
using PharmacyERP.Application.Features.Purchasing.Imports;
using PharmacyERP.Domain.Entities;
namespace PharmacyERP.Infrastructure.Services.Imports;
public static class InvoiceItemMatcher
{
    public static string Normalize(string? value) => Regex.Replace((value ?? "").Normalize(NormalizationForm.FormKC).ToLowerInvariant(), @"[^\p{L}\p{N}]", "");
    public static InvoiceItemMatch Match(InvoiceImageLine line, IReadOnlyList<Item> items, int? aliasItemId)
    {
        var active = items.Where(i => i.IsActive).ToList();
        var source = Normalize(line.Name);
        if (aliasItemId.HasValue && active.Any(i => i.Id == aliasItemId))
        {
            var aliased = active.Single(i => i.Id == aliasItemId);
            return new() { ItemId = aliased.Id, Reason = "مطابقة تسمية المذخر المحفوظة", Candidates = new() { new(aliased.Id, aliased.Name, aliased.UnitOfMeasure.Name, aliased.UnitsPerPackage, 1m) } };
        }
        var exact = active.Where(i => (aliasItemId.HasValue && i.Id == aliasItemId)
            || (!string.IsNullOrWhiteSpace(line.Barcode) && (i.Barcode == line.Barcode || i.BaseUnitBarcode == line.Barcode || i.SaleUnits.Any(u => u.IsActive && u.Barcode == line.Barcode)))
            || (source.Length > 0 && source == Normalize(i.Name))).ToList();
        InvoiceItemCandidate Candidate(Item i, decimal score) => new(i.Id, i.Name + (i.Strength is null ? "" : " · " + i.Strength), i.UnitOfMeasure.Name, i.UnitsPerPackage, score);
        if (exact.Count == 1) return new() { ItemId = exact[0].Id, Reason = aliasItemId == exact[0].Id ? "مطابقة تسمية المذخر المحفوظة" : "مطابقة اسم أو باركود", Candidates = new() { Candidate(exact[0], 1m) } };
        if (exact.Count > 1) return new() { Reason = "أكثر من بطاقة مطابقة؛ اختر الصنف الصحيح", Candidates = exact.Select(i => Candidate(i, 1m)).ToList() };
        // Partial/generic names are suggestions only, especially when strength/packaging differs.
        var words = Regex.Matches(line.Name.ToLowerInvariant(), @"[\p{L}\p{N}]+").Select(m => m.Value).Where(w => w.Length > 2).Distinct().ToHashSet();
        var candidates = active.Select(i =>
        {
            var itemWords = Regex.Matches($"{i.Name} {i.GenericName} {i.Strength}".ToLowerInvariant(), @"[\p{L}\p{N}]+").Select(m => m.Value).Where(w => w.Length > 2).Distinct().ToHashSet();
            var score = words.Count == 0 || itemWords.Count == 0 ? 0 : (decimal)words.Intersect(itemWords).Count() / Math.Max(words.Count, itemWords.Count);
            return Candidate(i, score);
        }).Where(c => c.Score > 0).OrderByDescending(c => c.Score).Take(8).ToList();
        return new() { Reason = candidates.Count == 0 ? "صنف جديد مقترح" : "مرشح موجود؛ راجع الاسم والتركيز ووحدة المخزون", Candidates = candidates };
    }
}
