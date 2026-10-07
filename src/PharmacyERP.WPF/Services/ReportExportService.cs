using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;

namespace PharmacyERP.WPF.Services;

public class ReportExportService : IReportExportService
{
    public string? ExportToCsv(string suggestedFileName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var dialog = new SaveFileDialog
        {
            FileName = suggestedFileName,
            DefaultExt = ".csv",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true) return null;

        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", headers.Select(EscapeCsvField)));
        foreach (var row in rows)
            builder.AppendLine(string.Join(",", row.Select(EscapeCsvField)));

        // UTF-8 BOM ensures Excel detects the encoding correctly and renders Arabic text properly
        // instead of showing garbled characters, which plain UTF-8 without a BOM often triggers.
        File.WriteAllText(dialog.FileName, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return dialog.FileName;
    }

    public void PrintTable(string documentTitle, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows, string? footerText = null)
    {
        var printDialog = new PrintDialog();
        if (printDialog.ShowDialog() != true) return;

        var flowDocument = BuildFlowDocument(documentTitle, headers, rows, footerText);
        flowDocument.PageWidth = printDialog.PrintableAreaWidth;
        flowDocument.PageHeight = printDialog.PrintableAreaHeight;
        flowDocument.PagePadding = new Thickness(40);
        flowDocument.ColumnWidth = printDialog.PrintableAreaWidth;
        flowDocument.FlowDirection = FlowDirection.RightToLeft;

        var paginator = ((IDocumentPaginatorSource)flowDocument).DocumentPaginator;
        printDialog.PrintDocument(paginator, documentTitle);
    }

    private static FlowDocument BuildFlowDocument(string title, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows, string? footerText)
    {
        var doc = new FlowDocument { FontFamily = new FontFamily("Segoe UI"), FontSize = 12 };

        doc.Blocks.Add(new Paragraph(new Run(title)) { FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
        doc.Blocks.Add(new Paragraph(new Run($"تاريخ الطباعة: {DateTime.Now:yyyy-MM-dd HH:mm}"))
        { FontSize = 10, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 16) });

        var table = new Table { CellSpacing = 0 };
        for (var i = 0; i < headers.Count; i++)
            table.Columns.Add(new TableColumn());

        var headerGroup = new TableRowGroup();
        var headerRow = new TableRow { Background = Brushes.LightGray };
        foreach (var header in headers)
        {
            headerRow.Cells.Add(new TableCell(new Paragraph(new Run(header)) { FontWeight = FontWeights.Bold })
            { Padding = new Thickness(6), BorderBrush = Brushes.Black, BorderThickness = new Thickness(0.5) });
        }
        headerGroup.Rows.Add(headerRow);
        table.RowGroups.Add(headerGroup);

        var bodyGroup = new TableRowGroup();
        foreach (var row in rows)
        {
            var tableRow = new TableRow();
            foreach (var cell in row)
            {
                tableRow.Cells.Add(new TableCell(new Paragraph(new Run(cell)))
                { Padding = new Thickness(6), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5) });
            }
            bodyGroup.Rows.Add(tableRow);
        }
        table.RowGroups.Add(bodyGroup);

        doc.Blocks.Add(table);

        if (!string.IsNullOrWhiteSpace(footerText))
            doc.Blocks.Add(new Paragraph(new Run(footerText)) { FontWeight = FontWeights.Bold, Margin = new Thickness(0, 16, 0, 0) });

        return doc;
    }

    private static string EscapeCsvField(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        return field;
    }
}
