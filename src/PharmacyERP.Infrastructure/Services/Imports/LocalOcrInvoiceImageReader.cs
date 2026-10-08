using System.Security.Cryptography;
using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Purchasing.Imports;

namespace PharmacyERP.Infrastructure.Services.Imports;

public sealed class LocalOcrInvoiceImageReader : IInvoiceImageReader
{
    private readonly ILocalInvoiceOcr _ocr;
    public LocalOcrInvoiceImageReader(ILocalInvoiceOcr ocr) => _ocr = ocr;
    public string ProviderName => "LocalOCR";
    public bool ProcessesLocally => true;
    public async Task<Result<InvoiceImageDocument>> ReadAsync(InvoiceImageInput image, CancellationToken cancellationToken = default)
    {
        var error = InvoiceImageContract.Validate(image);
        if (error is not null) return Result<InvoiceImageDocument>.Failure(error);
        try
        {
            var page = await _ocr.ReadAsync(image.Content, cancellationToken);
            if (string.IsNullOrWhiteSpace(page.Text)) return Result<InvoiceImageDocument>.Failure("لم يقرأ OCR نصاً من الصورة. صوّر الفاتورة كاملة بإضاءة واضحة ومن الأعلى.");
            var document = LocalInvoiceTableParser.Parse(page);
            document.SourceFileName = Path.GetFileName(image.FileName);
            document.SourceHash = Convert.ToHexString(SHA256.HashData(image.Content));
            return Result<InvoiceImageDocument>.Success(document);
        }
        catch (OperationCanceledException) { throw; }
        catch (InvalidOperationException ex) { return Result<InvoiceImageDocument>.Failure(ex.Message); }
        catch (Exception) { return Result<InvoiceImageDocument>.Failure("تعذر تشغيل OCR المحلي. اضغط اختبار الخدمة في الإعدادات وتأكد من تثبيت مكونات التشغيل. لم يُضف مخزون."); }
    }
}
