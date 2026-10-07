using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using PharmacyERP.Application.Features.Sales.DTOs;

namespace PharmacyERP.WPF.Services;

public static class ReceiptDocumentBuilder
{
    public const double PaperWidth = 80d / 25.4 * 96;

    public static FlowDocument Build(SalesInvoiceDetailDto invoice, ReceiptSettings settings,
        double width = PaperWidth, double height = double.PositiveInfinity)
    {
        var content = ReceiptContentBuilder.Build(invoice, settings);
        var document = new FlowDocument
        {
            PageWidth = width, ColumnWidth = width, PagePadding = new Thickness(9, 10, 9, 10),
            FlowDirection = FlowDirection.RightToLeft, TextAlignment = TextAlignment.Center,
            FontFamily = new FontFamily("Tahoma"), FontSize = 11, Foreground = Brushes.Black,
            Background = Brushes.White, IsHyphenationEnabled = false
        };
        if (double.IsFinite(height) && height > 0) document.PageHeight = height;
        if (content.LogoBase64 is not null)
        {
            var logo = ReceiptLogo.Load(content.LogoBase64);
            var scale = Math.Min(1, Math.Min(135d / logo.PixelWidth, 85d / logo.PixelHeight));
            var image = new Image
            {
                Source = logo, Width = logo.PixelWidth * scale, Height = logo.PixelHeight * scale,
                Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            };
            var paragraph = new Paragraph(new InlineUIContainer(image))
            {
                TextAlignment = TextAlignment.Center, Margin = new Thickness(0), KeepWithNext = true
            };
            document.Blocks.Add(paragraph);
        }
        AddSection(document, content.Header);
        AddDivider(document);
        foreach (var product in content.Products)
        {
            AddSection(document, product);
            AddDivider(document);
        }
        AddSection(document, content.Totals);
        if (content.Footer.Count > 0)
        {
            AddDivider(document);
            AddSection(document, content.Footer);
        }
        return document;
    }

    private static void AddSection(FlowDocument document, List<ReceiptText> lines)
    {
        var section = new Section { Margin = new Thickness(0, 3, 0, 3), TextAlignment = TextAlignment.Center };
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            section.Blocks.Add(new Paragraph(new Run(line.Value))
            {
                TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 3, 0, 3),
                FontWeight = line.Bold ? FontWeights.Bold : FontWeights.Normal,
                FontSize = line.FontSize, KeepTogether = true, KeepWithNext = index < lines.Count - 1
            });
        }
        document.Blocks.Add(section);
    }

    private static void AddDivider(FlowDocument document) => document.Blocks.Add(new Paragraph
    {
        Margin = new Thickness(0, 4, 0, 4), Padding = new Thickness(0), FontSize = 1,
        BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 0, 0, 0.7),
        TextAlignment = TextAlignment.Center
    });
}
