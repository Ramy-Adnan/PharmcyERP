using PharmacyERP.Application.Features.Sales.DTOs;
using PharmacyERP.Domain.Enums;

namespace PharmacyERP.WPF.Services;

public sealed record ReceiptText(string Value, bool Bold = false, double FontSize = 11);

public sealed class ReceiptContent
{
    public string? LogoBase64 { get; init; }
    public List<ReceiptText> Header { get; } = new();
    public List<List<ReceiptText>> Products { get; } = new();
    public List<ReceiptText> Totals { get; } = new();
    public List<ReceiptText> Footer { get; } = new();
}

/// <summary>The preview and printer share this content, including the payment amounts at sale.</summary>
public static class ReceiptContentBuilder
{
    public static ReceiptContent Build(SalesInvoiceDetailDto invoice, ReceiptSettings settings)
    {
        var h = invoice.Header;
        var content = new ReceiptContent { LogoBase64 = settings.ShowLogo ? settings.LogoBase64 : null };
        if (settings.ShowPharmacyName)
        {
            var name = string.IsNullOrWhiteSpace(settings.PharmacyName) ? h.BranchName : settings.PharmacyName.Trim();
            content.Header.Add(new(name, true, 17));
            if (!string.IsNullOrWhiteSpace(h.BranchName) && name != h.BranchName)
                content.Header.Add(new(h.BranchName, false, 10));
        }
        if (settings.ShowAddress && !string.IsNullOrWhiteSpace(settings.Address))
            content.Header.Add(new(settings.Address.Trim(), false, 10));
        content.Header.Add(new("فاتورة بيع", true, 13));
        content.Header.Add(new(h.Number, false, 10));
        content.Header.Add(new($"{h.SaleAtUtc.ToLocalTime():yyyy-MM-dd  HH:mm}", false, 10));
        content.Header.Add(new($"الموظف: {h.CashierName}", false, 10));
        content.Header.Add(new($"العميل: {h.CustomerName ?? "عميل نقدي"}", false, 10));
        foreach (var line in invoice.Lines)
        {
            var product = new List<ReceiptText>
            {
                new(line.ItemName, true, 12),
                new($"الكمية: {line.Quantity}  ×  السعر: {line.UnitPrice:N2}"),
                new($"الإجمالي: {line.LineTotal:N2}", true)
            };
            if (line.DiscountAmount > 0) product.Add(new($"خصم الصنف: {line.DiscountAmount:N2}", false, 10));
            content.Products.Add(product);
        }
        content.Totals.Add(new($"المجموع: {h.SubTotal:N2}"));
        content.Totals.Add(new($"الضريبة: {h.TaxAmount:N2}"));
        content.Totals.Add(new($"الخصم: {h.DiscountAmount:N2}"));
        content.Totals.Add(new($"الإجمالي: {h.TotalAmount:N2}", true, 16));
        var method = h.PaymentMethod switch { PaymentMethod.Cash => "نقدي", PaymentMethod.Card => "بطاقة", PaymentMethod.Credit => "آجل", _ => "مختلط" };
        content.Totals.Add(new($"طريقة الدفع: {method}", true));
        if (h.PaymentMethod == PaymentMethod.Credit)
        {
            content.Totals.Add(new($"المستلم عند البيع: {h.InitialPaymentAmount:N2}"));
            content.Totals.Add(new($"المتبقي على العميل عند البيع: {h.DebtAtSale:N2}", true));
        }
        else
        {
            content.Totals.Add(new($"المستلم: {h.AmountTendered:N2}"));
            if (h.PaymentMethod == PaymentMethod.Cash && h.ChangeGiven > 0)
                content.Totals.Add(new($"الباقي: {h.ChangeGiven:N2}"));
        }
        if (settings.ShowFooterMessage && !string.IsNullOrWhiteSpace(settings.FooterMessage))
            content.Footer.Add(new(settings.FooterMessage.Trim(), true, 11));
        return content;
    }
}
