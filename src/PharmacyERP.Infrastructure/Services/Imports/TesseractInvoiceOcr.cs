using PharmacyERP.Application.Common.Models;
using Tesseract;

namespace PharmacyERP.Infrastructure.Services.Imports;

public sealed record InvoiceOcrWord(string Text, int Left, int Top, int Width, int Height, float Confidence)
{
    public double CenterX => Left + Width / 2d;
    public double CenterY => Top + Height / 2d;
}
public sealed record InvoiceOcrPage(string Text, IReadOnlyList<InvoiceOcrWord> Words);
public interface ILocalInvoiceOcr
{
    Task<InvoiceOcrPage> ReadAsync(byte[] image, CancellationToken cancellationToken);
    Task<Result> CheckAsync(CancellationToken cancellationToken);
}

/// <summary>All processing stays on the device. Native engines are never shared between operations.</summary>
public sealed class TesseractInvoiceOcr : ILocalInvoiceOcr
{
    private readonly string _dataPath;
    public TesseractInvoiceOcr(string? dataPath = null) => _dataPath = dataPath ?? Path.Combine(AppContext.BaseDirectory, "tessdata");
    private TesseractEngine CreateEngine()
    {
        foreach (var language in new[] { "eng", "ara" })
            if (!File.Exists(Path.Combine(_dataPath, language + ".traineddata")))
                throw new InvalidOperationException("ملفات لغة OCR ناقصة؛ أعد بناء البرنامج أو ثبّت النسخة الكاملة التي تحتوي مجلد tessdata.");
        var engine = new TesseractEngine(_dataPath, "eng+ara", EngineMode.LstmOnly);
        engine.SetVariable("user_defined_dpi", 300);
        return engine;
    }
    public Task<InvoiceOcrPage> ReadAsync(byte[] image, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var pix = Pix.LoadFromMemory(image);
        if ((long)pix.Width * pix.Height > 24_000_000)
            throw new InvalidOperationException("دقة الصورة كبيرة جداً؛ اختر صورة حتى 24 مليون بكسل.");
        using var deskewed = pix.Deskew();
        using var engine = CreateEngine();
        // SingleBlock preserves isolated quantity/bonus digits that automatic page segmentation can discard.
        using var page = engine.Process(deskewed, PageSegMode.SingleBlock);
        var text = page.GetText();
        var words = new List<InvoiceOcrWord>();
        using var iterator = page.GetIterator(); iterator.Begin();
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var word = iterator.GetText(PageIteratorLevel.Word)?.Trim();
            if (!string.IsNullOrWhiteSpace(word) && iterator.TryGetBoundingBox(PageIteratorLevel.Word, out var box))
                words.Add(new(word, box.X1, box.Y1, box.Width, box.Height, iterator.GetConfidence(PageIteratorLevel.Word)));
            if (words.Count > 20_000 || text.Length > 1024 * 1024) throw new InvalidOperationException("حلل صفحة واحدة واضحة في كل مرة.");
        } while (iterator.Next(PageIteratorLevel.Word));
        return new InvoiceOcrPage(text, words);
    }, cancellationToken);

    public async Task<Result> CheckAsync(CancellationToken cancellationToken)
    {
        try { await Task.Run(() => { using var engine = CreateEngine(); }, cancellationToken); return Result.Success(); }
        catch (OperationCanceledException) { throw; }
        catch (InvalidOperationException ex) { return Result.Failure(ex.Message); }
        catch (Exception) { return Result.Failure("تعذر تشغيل OCR المحلي. تأكد من وجود ملفات البرنامج وMicrosoft Visual C++ 2015–2022 Runtime المناسب للجهاز."); }
    }
}
