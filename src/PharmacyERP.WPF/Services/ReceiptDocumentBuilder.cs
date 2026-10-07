using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using PharmacyERP.Application.Features.Sales.DTOs;

namespace PharmacyERP.WPF.Services;

public static class ReceiptDocumentBuilder
{
    public const double PaperWidth = 80d / 25.4 * 96;

    /// <summary>Measure the same blocks that are printed, with room for driver/font rounding.</summary>
    public static double MeasureHeight(FlowDocument document)
    {
        var textWidth = document.PageWidth - document.PagePadding.Left - document.PagePadding.Right;
        double BlockHeight(Block block, double width)
        {
            var innerWidth = Math.Max(1, width - block.Margin.Left - block.Margin.Right);
            var margins = block.Margin.Top + block.Margin.Bottom;
            if (block is Section section)
                return margins + section.Blocks.Cast<Block>().Sum(child => BlockHeight(child, innerWidth));
            if (block is not Paragraph paragraph) return margins;
            var image = paragraph.Inlines.OfType<InlineUIContainer>().Select(i => i.Child).OfType<Image>().FirstOrDefault();
            if (image is not null) return margins + image.Height + image.Margin.Top + image.Margin.Bottom + 6;
            var text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.TrimEnd('\r', '\n');
            var formatted = new FormattedText(string.IsNullOrEmpty(text) ? " " : text,
                CultureInfo.CurrentCulture, document.FlowDirection,
                new Typeface(paragraph.FontFamily, paragraph.FontStyle, paragraph.FontWeight, paragraph.FontStretch),
                paragraph.FontSize, Brushes.Black, 1)
            {
                MaxTextWidth = innerWidth
            };
            return margins + formatted.Height + paragraph.BorderThickness.Top + paragraph.BorderThickness.Bottom;
        }
        var height = document.Blocks.Cast<Block>().Sum(block => BlockHeight(block, textWidth))
            + document.PagePadding.Top + document.PagePadding.Bottom;
        return Math.Ceiling(height * 1.08 + 10);
    }

    public static FlowDocument Build(SalesInvoiceDetailDto invoice, ReceiptSettings settings,
        double width = PaperWidth, double height = double.PositiveInfinity)
    {
        var content = ReceiptContentBuilder.Build(invoice, settings);
        var document = new FlowDocument
        {
            PageWidth = width, ColumnWidth = width, PagePadding = new Thickness(9, 6, 9, 6),
            FlowDirection = FlowDirection.RightToLeft, TextAlignment = TextAlignment.Center,
            FontFamily = new FontFamily("Tahoma"), FontSize = 11, Foreground = Brushes.Black,
            Background = Brushes.White, IsHyphenationEnabled = false
        };
        if (double.IsFinite(height) && height > 0) document.PageHeight = height;
        if (content.LogoBase64 is not null)
        {
            var logo = ReceiptLogo.Load(content.LogoBase64);
            var scale = Math.Min(1, Math.Min(110d / logo.PixelWidth, 54d / logo.PixelHeight));
            var image = new Image
            {
                Source = logo, Width = logo.PixelWidth * scale, Height = logo.PixelHeight * scale,
                Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 3)
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
        var section = new Section { Margin = new Thickness(0, 1, 0, 1), Padding = new Thickness(0), TextAlignment = TextAlignment.Center };
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            section.Blocks.Add(new Paragraph(new Run(line.Value))
            {
                TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 1, 0, 1), Padding = new Thickness(0),
                FontWeight = line.Bold ? FontWeights.Bold : FontWeights.Normal,
                FontSize = line.FontSize, KeepTogether = true, KeepWithNext = index < lines.Count - 1
            });
        }
        document.Blocks.Add(section);
    }

    private static void AddDivider(FlowDocument document) => document.Blocks.Add(new Paragraph
    {
        Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(0), FontSize = 1,
        BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 0, 0, 0.7),
        TextAlignment = TextAlignment.Center
    });
}
