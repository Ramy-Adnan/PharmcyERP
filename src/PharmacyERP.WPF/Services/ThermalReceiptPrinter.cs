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
        // The driver owns cutting and supported paper lengths. Keep its configured roll length.
        requested.PageMediaSize = new PageMediaSize(receiptWidth, requested.PageMediaSize?.Height ?? 297d / 25.4 * 96);
        requested.PageOrientation = PageOrientation.Portrait;
        var ticket = queue.MergeAndValidatePrintTicket(queue.DefaultPrintTicket, requested).ValidatedPrintTicket;
        var dialog = new PrintDialog { PrintQueue = queue, PrintTicket = ticket };
        var width = Math.Min(receiptWidth, dialog.PrintableAreaWidth);
        if (width < 150) throw new InvalidOperationException("اضبط ورق الطابعة الافتراضية على 80mm في إعدادات Windows.");
        var document = ReceiptDocumentBuilder.Build(invoice, _settings.Load(), width, dialog.PrintableAreaHeight);
        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, $"POS {invoice.Header.Number}");
    }
}
