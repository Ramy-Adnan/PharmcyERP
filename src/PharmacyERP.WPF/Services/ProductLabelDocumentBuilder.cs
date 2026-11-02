using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;

namespace PharmacyERP.WPF.Services;

/// <summary>Builds the same physical-size label for preview and Windows printing.</summary>
public static class ProductLabelDocumentBuilder
{
    private const double DipsPerMillimetre = 96d / 25.4;
    private static readonly FontFamily LabelFont = new("Tahoma");

    public static FixedPage BuildLabel(ProductLabelRequest request, double dpiX = 203, double dpiY = 203,
        Thickness? imageableMargins = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!double.IsFinite(request.WidthMm) || !double.IsFinite(request.HeightMm)
            || request.WidthMm <= 0 || request.HeightMm <= 0 || request.WidthMm > 300 || request.HeightMm > 300)
            throw new ArgumentException("أدخل مقاس ملصق صالحاً بالمليمتر، بحد أقصى 300 × 300 مم.");
        if (!double.IsFinite(dpiX) || !double.IsFinite(dpiY) || dpiX <= 0 || dpiY <= 0 || dpiX > 2400 || dpiY > 2400)
            throw new ArgumentException("تعذر تحديد دقة الطابعة. اختر دقة طباعة صالحة قبل طباعة الملصق.");
        if (string.IsNullOrWhiteSpace(request.Name) || request.SalePrice <= 0)
            throw new ArgumentException("اسم المنتج وسعر بيع أكبر من صفر مطلوبان لطباعة الملصق.");

        var width = request.WidthMm * DipsPerMillimetre;
        var height = request.HeightMm * DipsPerMillimetre;
        var printerMargins = imageableMargins ?? new Thickness(0);
        if (new[] { printerMargins.Left, printerMargins.Top, printerMargins.Right, printerMargins.Bottom }.Any(v => !double.IsFinite(v)))
            throw new ArgumentException("تعذر قراءة هوامش الطابعة. راجع مقاس الورق وإعدادات الطباعة.");
        var padding = 2 * DipsPerMillimetre;
        var margins = new Thickness(Math.Max(padding, printerMargins.Left), Math.Max(padding, printerMargins.Top),
            Math.Max(padding, printerMargins.Right), Math.Max(padding, printerMargins.Bottom));
        var innerWidth = width - margins.Left - margins.Right;
        var innerHeight = height - margins.Top - margins.Bottom;
        if (innerWidth <= 0 || innerHeight <= 0) throw TooSmall();

        var pattern = ProductBarcodeEncoder.Encode(request.Barcode);
        // Use whole printer dots, never fractional or stretched narrow bars.
        // At 203/300 dpi this yields two/three dots per module respectively.
        var dotWidth = 96d / dpiX;
        var dotHeight = 96d / dpiY;
        var minimumDots = Math.Max(1, (int)Math.Ceiling(0.19 * dpiX / 25.4));
        var availableDotsPerModule = (int)Math.Floor(innerWidth / dotWidth / pattern.Width);
        if (availableDotsPerModule < minimumDots)
            throw new ArgumentException("عرض الملصق لا يكفي لباركود واضح قابل للمسح. كبّر عرض الملصق؛ لا يتم ضغط الأشرطة أو قصها.");
        var moduleDots = Math.Max(minimumDots, Math.Min(availableDotsPerModule, (int)Math.Floor(0.5 * dpiX / 25.4)));
        var moduleWidth = moduleDots * dotWidth;
        var barcodeWidth = pattern.Width * moduleWidth;
        var minLeftDot = Math.Ceiling(margins.Left / dotWidth);
        var maxLeftDot = Math.Floor((width - margins.Right - barcodeWidth) / dotWidth);
        if (minLeftDot > maxLeftDot)
            throw new ArgumentException("عرض الملصق لا يكفي لباركود واضح قابل للمسح. كبّر عرض الملصق؛ لا يتم ضغط الأشرطة أو قصها.");

        var title = string.IsNullOrWhiteSpace(request.UnitName) ? request.Name.Trim()
            : request.Name.Trim() + " · " + request.UnitName.Trim();
        var name = Text(title, 11, FlowDirection.RightToLeft);
        name.TextWrapping = TextWrapping.Wrap; name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.LineHeight = 14; name.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; name.MaxHeight = 28;
        name.Measure(new Size(innerWidth, double.PositiveInfinity));
        var nameHeight = name.DesiredSize.Height;

        var digits = Text(pattern.Text, 9, FlowDirection.LeftToRight);
        digits.Language = XmlLanguage.GetLanguage("en-US");
        var price = Text(request.SalePrice.ToString("#,##0.##", CultureInfo.InvariantCulture) + " د.ع", 16, FlowDirection.RightToLeft);
        price.FontWeight = FontWeights.Bold;
        foreach (var text in new[] { digits, price })
        {
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (text.DesiredSize.Width > innerWidth)
                throw new ArgumentException("عرض الملصق لا يكفي لإظهار الباركود والسعر كاملين. كبّر عرض الملصق.");
        }

        var gap = 0.6 * DipsPerMillimetre;
        var priceTop = height - margins.Bottom - price.DesiredSize.Height;
        var digitsTop = priceTop - gap - digits.DesiredSize.Height;
        var barcodeRegionTop = margins.Top + nameHeight + gap;
        var barcodeRegionHeight = digitsTop - gap - barcodeRegionTop;
        var barcodeHeightDots = (int)Math.Floor(Math.Min(12 * DipsPerMillimetre, barcodeRegionHeight) / dotHeight);
        if (barcodeHeightDots < Math.Ceiling(5 * DipsPerMillimetre / dotHeight)) throw TooSmall();
        var barcodeHeight = barcodeHeightDots * dotHeight;
        var barcodeTop = Math.Ceiling((barcodeRegionTop + (barcodeRegionHeight - barcodeHeight) / 2) / dotHeight) * dotHeight;
        // Leave enough space after rounding to a complete printer row.
        if (barcodeTop + barcodeHeight > digitsTop - gap + 0.001) throw TooSmall();

        var page = new FixedPage
        {
            Width = width, Height = height, Background = Brushes.White,
            FlowDirection = FlowDirection.LeftToRight
        };
        Add(page, name, margins.Left, margins.Top, innerWidth, nameHeight);

        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, barcodeWidth, barcodeHeight));
            for (var x = 0; x < pattern.Width;)
            {
                if (!pattern.Modules[x, 0]) { x++; continue; }
                var start = x;
                while (x < pattern.Width && pattern.Modules[x, 0]) x++;
                dc.DrawRectangle(Brushes.Black, null, new Rect(start * moduleWidth, 0, (x - start) * moduleWidth, barcodeHeight));
            }
        }
        drawing.Freeze();
        var barcode = new Image { Source = new DrawingImage(drawing), Stretch = Stretch.None };
        RenderOptions.SetEdgeMode(barcode, EdgeMode.Aliased);
        var barcodeLeft = Math.Round((minLeftDot + maxLeftDot) / 2, MidpointRounding.AwayFromZero) * dotWidth;
        Add(page, barcode, barcodeLeft, barcodeTop, barcodeWidth, barcodeHeight);
        Add(page, digits, margins.Left, digitsTop, innerWidth, digits.DesiredSize.Height);
        Add(page, price, margins.Left, priceTop, innerWidth, price.DesiredSize.Height);
        page.Measure(new Size(width, height)); page.Arrange(new Rect(0, 0, width, height)); page.UpdateLayout();
        return page;
    }

    private static TextBlock Text(string value, double size, FlowDirection direction) => new()
    {
        Text = value, FontFamily = LabelFont, FontSize = size, Foreground = Brushes.Black,
        FlowDirection = direction, TextAlignment = TextAlignment.Center, Padding = new Thickness(0), Margin = new Thickness(0)
    };

    private static void Add(FixedPage page, FrameworkElement child, double left, double top, double width, double height)
    {
        child.Width = width; child.Height = height;
        FixedPage.SetLeft(child, left); FixedPage.SetTop(child, top); page.Children.Add(child);
    }

    private static ArgumentException TooSmall() => new("ارتفاع الملصق أو مساحته القابلة للطباعة لا يكفيان للاسم والباركود والسعر. كبّر المقاس أو راجع هوامش الطابعة.");
}
