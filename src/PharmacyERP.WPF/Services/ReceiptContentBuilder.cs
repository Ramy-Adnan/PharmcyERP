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
            content.Header.Add(new(name, true, 15));
        }
        if (settings.ShowAddress && !string.IsNullOrWhiteSpace(settings.Address))
            content.Header.Add(new(settings.Address.Trim(), false, 10));
        content.Header.Add(new(h.Number, false, 9));
        content.Header.Add(new($"{h.SaleAtUtc.ToLocalTime():yyyy-MM-dd  HH:mm}", false, 9));
        foreach (var line in invoice.Lines)
        {
            var product = new List<ReceiptText>
            {
                new($"{line.ItemName}  —  الكمية: {line.Quantity}", true, 11)
            };
            content.Products.Add(product);
        }
        var received = h.PaymentMethod == PaymentMethod.Credit ? h.InitialPaymentAmount : h.AmountTendered;
        var remaining = h.PaymentMethod == PaymentMethod.Credit ? h.DebtAtSale
            : h.PaymentMethod == PaymentMethod.Cash ? h.ChangeGiven : 0;
        content.Totals.Add(new($"الإجمالي: {h.TotalAmount:N2}", true, 15));
        content.Totals.Add(new($"المستلم: {received:N2}"));
        content.Totals.Add(new($"الباقي: {remaining:N2}", true));
        if (settings.ShowFooterMessage && !string.IsNullOrWhiteSpace(settings.FooterMessage))
            content.Footer.Add(new(settings.FooterMessage.Trim(), true, 11));
        return content;
    }
}
