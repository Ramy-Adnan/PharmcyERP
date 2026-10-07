using System.Printing;
using System.Windows.Controls;
using System.Windows.Documents;
using PharmacyERP.Application.Features.Sales.DTOs;

namespace PharmacyERP.WPF.Services;

/// <summary>Render Arabic through the Windows printer driver; no ESC/POS code-page assumptions.</summary>
public sealed class ThermalReceiptPrinter : IReceiptPrinter
{
    private readonly IReceiptSettingsStore _settings;
    public ThermalReceiptPrinter(IReceiptSettingsStore settings) => _settings = settings;

    public void Print(SalesInvoiceDetailDto invoice)
    {
        using var queue = LocalPrintServer.GetDefaultPrintQueue();
        if (queue.IsOffline || queue.IsInError || (queue.QueueStatus & PrintQueueStatus.PaperOut) != 0)
            throw new InvalidOperationException("الطابعة الافتراضية غير جاهزة. تحقق من الاتصال والورق ثم أعد طباعة الفاتورة.");
        var requested = queue.DefaultPrintTicket.Clone();
        const double receiptWidth = ReceiptDocumentBuilder.PaperWidth;
        // Start with the driver's supported length; request a shorter page below when supported.
        requested.PageMediaSize = new PageMediaSize(receiptWidth, requested.PageMediaSize?.Height ?? 297d / 25.4 * 96);
        requested.PageOrientation = PageOrientation.Portrait;
        var ticket = queue.MergeAndValidatePrintTicket(queue.DefaultPrintTicket, requested).ValidatedPrintTicket;
        var dialog = new PrintDialog { PrintQueue = queue, PrintTicket = ticket };
        var width = Math.Min(receiptWidth, dialog.PrintableAreaWidth);
        if (width < 150) throw new InvalidOperationException("اضبط ورق الطابعة الافتراضية على 80mm في إعدادات Windows.");
        var settings = _settings.Load();
        var document = ReceiptDocumentBuilder.Build(invoice, settings, width, dialog.PrintableAreaHeight);
        try
        {
            var requiredHeight = ReceiptDocumentBuilder.MeasureHeight(document);
            var paperHeight = ticket.PageMediaSize?.Height ?? dialog.PrintableAreaHeight;
            var driverMargins = Math.Max(0, paperHeight - dialog.PrintableAreaHeight);
            var shortHeight = Math.Max(40d / 25.4 * 96, requiredHeight + driverMargins);
            if (shortHeight < paperHeight)
            {
                var shortRequest = ticket.Clone();
                shortRequest.PageMediaSize = new PageMediaSize(receiptWidth, shortHeight);
                dialog.PrintTicket = queue.MergeAndValidatePrintTicket(ticket, shortRequest).ValidatedPrintTicket;
                var shortWidth = Math.Min(receiptWidth, dialog.PrintableAreaWidth);
                var shortened = ReceiptDocumentBuilder.Build(invoice, settings, shortWidth, dialog.PrintableAreaHeight);
                if (shortWidth >= 150 && ReceiptDocumentBuilder.MeasureHeight(shortened) <= dialog.PrintableAreaHeight)
                    document = shortened;
                else dialog.PrintTicket = ticket;
            }
        }
        catch
        {
            // Optional measurement/custom sizing must not block printing on fixed-size drivers.
            dialog.PrintTicket = ticket;
        }
        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, $"POS {invoice.Header.Number}");
    }
}
