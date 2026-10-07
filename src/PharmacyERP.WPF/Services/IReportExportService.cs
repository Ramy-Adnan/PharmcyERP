namespace PharmacyERP.WPF.Services;

/// <summary>
/// Export/print surface shared by every report screen. CSV export is used
/// for "Excel export" — it opens natively in Excel without requiring an
/// Office interop or a third-party spreadsheet library dependency. Printing
/// goes through WPF's native FlowDocument + PrintDialog pipeline, so
/// "export to PDF" is achieved by the user selecting the "Microsoft Print to
/// PDF" virtual printer already built into Windows — no PDF library
/// dependency needed either.
/// </summary>
public interface IReportExportService
{
    /// <summary>Writes rows to a CSV file (UTF-8 with BOM so Excel renders Arabic text correctly) and returns the saved path, or null if the user cancelled the save dialog.</summary>
    string? ExportToCsv(string suggestedFileName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows);

    /// <summary>Shows the Windows print dialog and, if the user confirms, prints the given title/rows as a simple tabular FlowDocument (works with any installed printer, including "Microsoft Print to PDF").</summary>
    void PrintTable(string documentTitle, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows, string? footerText = null);
}
