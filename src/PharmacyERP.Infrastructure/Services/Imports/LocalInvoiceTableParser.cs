using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PharmacyERP.Application.Features.Purchasing.Imports;

namespace PharmacyERP.Infrastructure.Services.Imports;

/// <summary>Reads values only under recognized table headers. Unknown layouts stay editable, never guessed.</summary>
public static class LocalInvoiceTableParser
{
    private sealed record Column(string Field, InvoiceOcrWord Header);
    private static string Normalize(string text)
    {
        var result = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            result.Append(c is >= '٠' and <= '٩' ? (char)('0' + c - '٠') : c is >= '۰' and <= '۹' ? (char)('0' + c - '۰') : c switch
            { 'أ' or 'إ' or 'آ' => 'ا', 'ة' => 'ه', 'ى' => 'ي', '٬' => ',', '٫' => '.', _ => char.ToLowerInvariant(c) });
        }
        return result.ToString().Normalize(NormalizationForm.FormC);
    }
    private static string Compact(string text) => Regex.Replace(Normalize(text), @"[^\p{L}\p{N}]", "");
    private static readonly Dictionary<string, string[]> Headers = new()
    {
        ["name"] = new[] { "اسمالماده", "اسمالصنف", "الماده", "العلاج", "الصنف", "description", "item", "medicine", "product" },
        ["quantity"] = new[] { "الكميه", "كميه", "quantity", "qty" },
        ["bonus"] = new[] { "البونص", "بونص", "bonus", "free" },
        ["price"] = new[] { "المفرد", "سعرالمفرد", "unitprice", "price" },
        ["total"] = new[] { "الاجمالي", "السعرالاجمالي", "linetotal", "amount", "total" },
        ["batch"] = new[] { "الطلبيه", "رقمالطلبيه", "الوجبه", "batch", "lot" },
        ["expiry"] = new[] { "الصلاحيه", "تاريخالصلاحيه", "expiry", "exp" }
    };
    private static List<List<InvoiceOcrWord>> Rows(IReadOnlyList<InvoiceOcrWord> words)
    {
        var rows = new List<List<InvoiceOcrWord>>();
        foreach (var word in words.OrderBy(w => w.CenterY))
        {
            var last = rows.LastOrDefault();
            if (last is null || Math.Abs(last.Average(w => w.CenterY) - word.CenterY) > Math.Max(6, word.Height * .65)) rows.Add(new() { word });
            else last.Add(word);
        }
        return rows;
    }
    private static decimal? Money(string text)
    {
        text = Normalize(text).Trim();
        if (!Regex.IsMatch(text, @"\A(?:\d+|\d{1,3}(?:,\d{3})+)(?:\.\d{1,2})?\z")) return null;
        return decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
    private static int? Count(string text) => int.TryParse(Normalize(text).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;
    private static DateTime? Date(string text) => DateTime.TryParseExact(Normalize(text).Trim(), new[] { "dd-MM-yyyy", "d-M-yyyy", "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    private static string ReadWords(IEnumerable<InvoiceOcrWord> words)
    {
        var list = words.ToList();
        var arabic = list.Sum(w => Regex.Matches(w.Text, @"\p{IsArabic}").Count);
        var latin = list.Sum(w => Regex.Matches(w.Text, "[a-zA-Z]").Count);
        return string.Join(" ", (arabic > latin ? list.OrderByDescending(w => w.Left) : list.OrderBy(w => w.Left)).Select(w => w.Text));
    }
    private static bool Footer(string text) => Regex.IsMatch(Normalize(text), @"(?:المجموع|مجموع|الرصيد|اجمالي الفاتوره|توقيع|المستلم|\bgrand\s+total\b|\bbalance\b)");
    public static InvoiceImageDocument Parse(InvoiceOcrPage page)
    {
        var document = new InvoiceImageDocument { RawOcrText = page.Text, Notes = "قراءة OCR محلية؛ راجع كل حقل مع الصورة. الحقول غير الواضحة تبقى فارغة، ولا يُستنتج عدد الأشرطة من عدد الحبات." };
        var rows = Rows(page.Words);
        var columns = new List<Column>(); double headerBottom = 0;
        foreach (var row in rows)
        {
            // Header words may be on two lines. A stable set is collected until the first dated/numeric product row.
            if (columns.Count >= 3 && row.Any(w => Date(w.Text).HasValue)) break;
            foreach (var word in row.Where(w => w.Confidence >= 40))
            {
                foreach (var header in Headers)
                    if (!columns.Any(c => c.Field == header.Key) && header.Value.Contains(Compact(word.Text)))
                    { columns.Add(new(header.Key, word)); headerBottom = Math.Max(headerBottom, word.Top + word.Height); break; }
            }
            if (columns.Count >= 5 && columns.Any(c => c.Field == "name")) break;
        }
        var name = columns.FirstOrDefault(c => c.Field == "name");
        if (name is null || columns.Count < 3)
        { document.Notes += " لم تتضح عناوين أعمدة الجدول؛ راجع النص المقروء وأضف الأسطر يدوياً."; return document; }
        var headerTop = columns.Min(c => c.Header.Top);
        var headerText = string.Join("\n", rows.Where(r => r.All(w => w.Top < headerTop)).Select(ReadWords));
        var number = Regex.Match(Normalize(headerText), @"(?:رقم\s*الفاتوره|invoice\s*(?:no\.?|number))\s*[:#-]?\s*([0-9]+)");
        if (number.Success) document.InvoiceNumber = number.Groups[1].Value;
        var invoiceDate = Regex.Match(Normalize(headerText), @"(?:التاريخ|\bdate)\s*[:#-]?\s*(\d{1,4}[-/]\d{1,2}[-/]\d{1,4})");
        if (invoiceDate.Success) document.InvoiceDate = Date(invoiceDate.Groups[1].Value);
        if (Regex.IsMatch(Normalize(page.Text), @"(?:الدينار\s*العراقي|\biqd\b)")) document.Currency = "IQD";
        var left = columns.Where(c => c.Header.CenterX < name.Header.CenterX).OrderByDescending(c => c.Header.CenterX).FirstOrDefault();
        var right = columns.Where(c => c.Header.CenterX > name.Header.CenterX).OrderBy(c => c.Header.CenterX).FirstOrDefault();
        var nameLeft = left is null ? double.MinValue : left.Header.Left + left.Header.Width + 4;
        var nameRight = right is null ? double.MaxValue : right.Header.Left - 4;
        foreach (var row in rows.Where(r => r.Average(w => w.CenterY) > headerBottom))
        {
            var rowText = ReadWords(row);
            if (Footer(rowText))
            {
                // The supplier balance is never the invoice total.
                if (!Normalize(rowText).Contains("الرصيد") && !Normalize(rowText).Contains("balance") &&
                    Regex.IsMatch(Normalize(rowText), @"(?:المجموع|مجموع|اجمالي الفاتوره|\bgrand\s+total\b)"))
                {
                    var amounts = row.Where(w => w.Confidence >= 45).Select(w => Money(w.Text)).Where(v => v.HasValue).ToList();
                    if (amounts.Count == 1) document.InvoiceTotal = amounts[0];
                }
                break;
            }
            var nameWords = row.Where(w => w.CenterX > nameLeft && w.CenterX < nameRight).ToList();
            var itemName = ReadWords(nameWords).Trim();
            if (Regex.Matches(itemName, @"\p{L}").Count < 3) continue;
            var cells = new Dictionary<string, List<InvoiceOcrWord>>();
            foreach (var word in row.Except(nameWords))
            {
                var column = columns.Where(c => c.Field != "name").OrderBy(c => Math.Abs(c.Header.CenterX - word.CenterX)).First();
                if (!cells.TryGetValue(column.Field, out var cell)) cells[column.Field] = cell = new();
                cell.Add(word);
            }
            string? Cell(string field)
            {
                if (!cells.TryGetValue(field, out var words) || words.Any(w => w.Confidence < 45)) return null;
                return string.Join("", words.OrderBy(w => w.Left).Select(w => w.Text));
            }
            var quantity = Count(Cell("quantity") ?? ""); var price = Money(Cell("price") ?? ""); var expiry = Date(Cell("expiry") ?? "");
            if (!quantity.HasValue && !price.HasValue && !expiry.HasValue) continue;
            var line = new InvoiceImageLine { Name = itemName, Quantity = quantity, BonusQuantity = Count(Cell("bonus") ?? ""), UnitPrice = price,
                LineTotal = Money(Cell("total") ?? ""), BatchNumber = Cell("batch"), ExpiryDate = expiry,
                Notes = "OCR: " + rowText + " — راجع الاسم والكميات والدفعة والصلاحية؛ لا توجد مراجعة مؤكدة تلقائياً." };
            var unit = Regex.Match(Normalize(itemName), @"\b(\d{1,4})\s*(tab(?:lets?)?|caps?(?:ules?)?|amp(?:oules?)?|sachets?)\b");
            if (unit.Success)
            {
                line.DeclaredUnitCount = Count(unit.Groups[1].Value);
                line.DeclaredUnitKind = unit.Groups[2].Value.StartsWith("amp") ? "ampoule" : unit.Groups[2].Value.StartsWith("sachet") ? "sachet" : "tablet";
            }
            if (line.Quantity.HasValue && line.UnitPrice.HasValue && line.LineTotal.HasValue && line.Quantity * line.UnitPrice != line.LineTotal)
                line.Notes += " مجموع السطر المطبوع لا يطابق الكمية × سعر المفرد؛ صحّح القراءة.";
            document.Lines.Add(line);
            if (document.Lines.Count == 500) break;
        }
        if (document.Lines.Count == 0) document.Notes += " لم يتضح جدول المنتجات؛ أضف الأسطر من النص المقروء أو الصورة.";
        return document;
    }
}
