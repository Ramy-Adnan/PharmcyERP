using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Extensions.Configuration;
using PharmacyERP.Application.Features.Sales.DTOs;
using PharmacyERP.Domain.Enums;

namespace PharmacyERP.WPF.Services;

/// <summary>Render Arabic through the Windows printer driver; no ESC/POS code-page assumptions.</summary>
public sealed class ThermalReceiptPrinter : IReceiptPrinter
{
    private readonly IConfiguration _configuration;
    public ThermalReceiptPrinter(IConfiguration configuration) => _configuration = configuration;

    public void Print(SalesInvoiceDetailDto invoice)
    {
        using var queue = LocalPrintServer.GetDefaultPrintQueue();
        if (queue.IsOffline || queue.IsInError || (queue.QueueStatus & PrintQueueStatus.PaperOut) != 0)
            throw new InvalidOperationException("الطابعة الافتراضية غير جاهزة. تحقق من الاتصال والورق ثم أعد طباعة الفاتورة.");
        var requested = queue.DefaultPrintTicket.Clone();
        const double receiptWidth = 80d / 25.4 * 96;
        // The driver owns cutting and supported paper lengths. Keep its configured roll length.
        requested.PageMediaSize = new PageMediaSize(receiptWidth, requested.PageMediaSize?.Height ?? 297d / 25.4 * 96);
        requested.PageOrientation = PageOrientation.Portrait;
        var ticket = queue.MergeAndValidatePrintTicket(queue.DefaultPrintTicket, requested).ValidatedPrintTicket;
        var dialog = new PrintDialog { PrintQueue = queue, PrintTicket = ticket };
        var width = Math.Min(receiptWidth, dialog.PrintableAreaWidth);
        if (width < 150) throw new InvalidOperationException("اضبط ورق الطابعة الافتراضية على 80mm في إعدادات Windows.");
        var document = new FlowDocument
        {
            PageWidth = width, PageHeight = dialog.PrintableAreaHeight, ColumnWidth = width,
            PagePadding = new Thickness(7), FlowDirection = FlowDirection.RightToLeft,
            FontFamily = new FontFamily("Tahoma"), FontSize = 11
        };
        void Text(string value, bool bold = false, bool center = false)
        {
            document.Blocks.Add(new Paragraph(new Run(value))
            {
                Margin = new Thickness(0, 2, 0, 2), FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                TextAlignment = center ? TextAlignment.Center : TextAlignment.Right
            });
        }
        var h = invoice.Header;
        Text(_configuration["ApplicationSettings:CompanyName"] ?? "Pharmacy ERP", true, true);
        Text(h.BranchName, false, true);
        Text("فاتورة بيع", true, true);
        Text(h.Number, false, true);
        Text($"{h.SaleAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}");
        Text($"الموظف: {h.CashierName}");
        Text($"العميل: {h.CustomerName ?? "عميل نقدي"}");
        Text("────────────────────", false, true);
        foreach (var line in invoice.Lines)
        {
            Text(line.ItemName, true);
            Text($"{line.Quantity} × {line.UnitPrice:N2}    = {line.LineTotal:N2}");
            if (line.DiscountAmount > 0) Text($"خصم الصنف: {line.DiscountAmount:N2}");
        }
        Text("────────────────────", false, true);
        Text($"المجموع: {h.SubTotal:N2}");
        Text($"الضريبة: {h.TaxAmount:N2}");
        Text($"الخصم: {h.DiscountAmount:N2}");
        Text($"الإجمالي: {h.TotalAmount:N2}", true);
        var method = h.PaymentMethod switch { PaymentMethod.Cash => "نقدي", PaymentMethod.Card => "بطاقة", PaymentMethod.Credit => "آجل", _ => "مختلط" };
        Text($"طريقة الدفع: {method}");
        if (h.PaymentMethod == PaymentMethod.Credit) Text($"دين على العميل عند البيع: {h.TotalAmount:N2}", true);
        else
        {
            Text($"المستلم: {h.AmountTendered:N2}");
            if (h.PaymentMethod == PaymentMethod.Cash) Text($"الباقي: {h.ChangeGiven:N2}");
        }
        Text("شكراً لزيارتكم", false, true);
        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, $"POS {h.Number}");
    }
}
