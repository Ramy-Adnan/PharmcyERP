using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace PharmacyERP.WPF.Services;

/// <summary>One physical label per page, through the Windows label-printer driver.</summary>
public sealed class ProductLabelPrinter
{
    public bool Print(ProductLabelRequest request)
    {
        if (ProductLabelSelection.LayoutError(request.Copies, request.WidthMm, request.HeightMm) is { } error)
            throw new ArgumentException(error);
        var width = request.WidthMm / 25.4 * 96;
        var height = request.HeightMm / 25.4 * 96;
        var dialog = new PrintDialog { UserPageRangeEnabled = false, SelectedPagesEnabled = false, CurrentPageEnabled = false };
        if (dialog.PrintTicket is { } initial)
        {
            initial.PageMediaSize = new PageMediaSize(width, height);
            initial.PageOrientation = PageOrientation.Portrait;
            initial.CopyCount = 1;
        }
        if (dialog.ShowDialog() != true) return false;
        var queue = dialog.PrintQueue ?? throw new InvalidOperationException("اختر طابعة الملصقات أولاً.");
        queue.Refresh();
        if (queue.IsOffline || queue.IsInError || (queue.QueueStatus & PrintQueueStatus.PaperOut) != 0)
            throw new InvalidOperationException("الطابعة غير جاهزة. تحقق من الاتصال وورق الملصقات.");
        var proposed = (dialog.PrintTicket ?? queue.DefaultPrintTicket).Clone();
        proposed.PageMediaSize = new PageMediaSize(width, height);
        proposed.PageOrientation = PageOrientation.Portrait;
        proposed.CopyCount = 1; // The document itself contains the requested number of labels.
        proposed.PageScalingFactor = 100;
        proposed.PagesPerSheet = 1;
        proposed.Duplexing = Duplexing.OneSided;
        var ticket = queue.MergeAndValidatePrintTicket(queue.DefaultPrintTicket, proposed).ValidatedPrintTicket;
        if (ticket.PageScalingFactor is int scale && scale != 100 || ticket.PagesPerSheet is int perSheet && perSheet != 1
            || ticket.Duplexing is Duplexing duplex && duplex != Duplexing.OneSided)
            throw new InvalidOperationException("اضبط الطابعة على حجم فعلي 100%، وصفحة واحدة لكل ورقة، وطباعة وجه واحد حتى تبقى أبعاد الباركود صحيحة.");
        var capabilities = queue.GetPrintCapabilities(ticket);
        var actualWidth = capabilities.OrientedPageMediaWidth ?? ticket.PageMediaSize?.Width ?? width;
        var actualHeight = capabilities.OrientedPageMediaHeight ?? ticket.PageMediaSize?.Height ?? height;
        if (Math.Abs(actualWidth - width) > 2 || Math.Abs(actualHeight - height) > 2)
            throw new InvalidOperationException($"تعريف الطابعة لم يقبل مقاس {request.WidthMm:0.##} × {request.HeightMm:0.##} مم. " +
                "عرّف مقاس الملصق نفسه في خصائص طابعة Windows ثم أعد المحاولة؛ لن يصغّر البرنامج الباركود تلقائياً.");
        var margins = capabilities.PageImageableArea is { } area
            ? new Thickness(Math.Max(0, area.OriginWidth), Math.Max(0, area.OriginHeight),
                Math.Max(0, width - area.OriginWidth - area.ExtentWidth), Math.Max(0, height - area.OriginHeight - area.ExtentHeight))
            : new Thickness(0);
        var dpiX = ticket.PageResolution?.X ?? 203;
        var dpiY = ticket.PageResolution?.Y ?? dpiX;
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new Size(width, height);
        for (var index = 0; index < request.Copies; index++)
        {
            var page = ProductLabelDocumentBuilder.BuildLabel(request, dpiX, dpiY, margins);
            page.Measure(new Size(width, height)); page.Arrange(new Rect(0, 0, width, height)); page.UpdateLayout();
            document.Pages.Add(new PageContent { Child = page });
        }
        dialog.PrintTicket = ticket;
        dialog.PrintDocument(document.DocumentPaginator, $"ملصقات {request.Name}");
        return true;
    }
}
