using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Backup;

namespace PharmacyERP.Infrastructure.Services;

/// <summary>Database + per-user logo/photos. Never imports licenses, machine IDs or DPAPI secrets.</summary>
public sealed class BackupDataTransferService : IDataTransferService
{
    private readonly IBackupService _backup;
    private readonly string _databaseName;
    private readonly string _localData;
    private readonly SemaphoreSlim _operation = new(1, 1);

    public BackupDataTransferService(IBackupService backup, IConfiguration configuration)
        : this(backup, new SqlConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection") ?? "").InitialCatalog,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmacyERP")) { }

    public BackupDataTransferService(IBackupService backup, string databaseName, string localDataDirectory)
    {
        _backup = backup;
        BackupSql.Identifier(databaseName);
        _databaseName = databaseName;
        _localData = Path.GetFullPath(localDataDirectory);
    }

    public async Task<Result<BackupFileDto>> ExportBundleAsync(string destinationZip, CancellationToken cancellationToken = default)
    {
        await _operation.WaitAsync(cancellationToken);
        try
        {
            if (!string.Equals(Path.GetExtension(destinationZip), ".zip", StringComparison.OrdinalIgnoreCase) || File.Exists(destinationZip))
                return Result<BackupFileDto>.Failure("اختر اسماً جديداً لحزمة النقل بامتداد .zip؛ لن يستبدل البرنامج ملفاً موجوداً.");
            var backup = await _backup.CreateBackupAsync(cancellationToken);
            if (!backup.Succeeded || backup.Value is null)
                return Result<BackupFileDto>.Failure(backup.Errors);
            var sources = new Dictionary<string, string>(StringComparer.Ordinal) { ["database.bak"] = backup.Value.FullPath };
            var receipt = Path.Combine(_localData, "receipt-settings.json");
            string? pharmacyName = null;
            if (File.Exists(receipt))
            {
                pharmacyName = await ValidateReceiptAsync(receipt, cancellationToken);
                sources["receipt-settings.json"] = receipt;
            }
            var images = Path.Combine(_localData, "product-images");
            if (Directory.Exists(images))
            {
                EnsureNotLink(images);
                foreach (var file in Directory.EnumerateFiles(images))
                    if (BackupBundleArchive.IsProductImageName(Path.GetFileName(file)))
                        sources["product-images/" + Path.GetFileName(file)] = file;
            }
            await BackupBundleArchive.CreateAsync(destinationZip, _databaseName, pharmacyName, sources, cancellationToken);
            var info = new FileInfo(destinationZip);
            return Result<BackupFileDto>.Success(new BackupFileDto
                { FileName = info.Name, FullPath = info.FullName, SizeBytes = info.Length, CreatedAtUtc = info.CreationTimeUtc });
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return Result<BackupFileDto>.Failure("تعذر تصدير حزمة البيانات: " + ex.Message); }
        finally { _operation.Release(); }
    }

    public async Task<Result<DataTransferPreviewDto>> InspectBundleAsync(string bundlePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var manifest = await BackupBundleArchive.InspectAsync(bundlePath, _databaseName, cancellationToken);
            return Result<DataTransferPreviewDto>.Success(new DataTransferPreviewDto
            {
                DatabaseName = manifest.DatabaseName, PharmacyName = manifest.PharmacyName, CreatedAtUtc = manifest.CreatedAtUtc,
                ProductImageCount = manifest.Files.Count(f => f.Path.StartsWith("product-images/", StringComparison.Ordinal)),
                HasReceiptSettings = manifest.Files.Any(f => f.Path == "receipt-settings.json")
            });
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return Result<DataTransferPreviewDto>.Failure("تعذر قراءة حزمة البيانات: " + ex.Message); }
    }

    public async Task<Result> ImportBundleAsync(string bundlePath, CancellationToken cancellationToken = default)
    {
        await _operation.WaitAsync(cancellationToken);
        var id = Guid.NewGuid().ToString("N");
        // SQL Server must read the extracted .bak too, so stage under the configured shared backup folder.
        var stage = Path.Combine(_backup.BackupFolder, "transfer-staging-" + id);
        var incoming = Path.Combine(_localData, ".transfer-incoming-" + id);
        var before = Path.Combine(_localData, ".transfer-before-" + id);
        var images = Path.Combine(_localData, "product-images");
        var receipt = Path.Combine(_localData, "receipt-settings.json");
        BackupFileDto? safety = null;
        var restoreAttempted = false;
        var imagesMoved = false;
        var receiptMoved = false;
        var newImagesInstalled = false;
        var newReceiptInstalled = false;
        try
        {
            EnsureNotLink(_localData);
            var manifest = await BackupBundleArchive.ExtractAsync(bundlePath, _databaseName, stage, cancellationToken);
            var stagedReceipt = Path.Combine(stage, "receipt-settings.json");
            if (File.Exists(stagedReceipt)) await ValidateReceiptAsync(stagedReceipt, cancellationToken);
            Directory.CreateDirectory(incoming);
            Directory.CreateDirectory(Path.Combine(incoming, "product-images"));
            foreach (var entry in manifest.Files.Where(f => f.Path.StartsWith("product-images/", StringComparison.Ordinal)))
            {
                var source = Path.Combine(stage, "product-images", Path.GetFileName(entry.Path));
                await ValidateImageAsync(source, cancellationToken);
                File.Copy(source, Path.Combine(incoming, "product-images", Path.GetFileName(entry.Path)));
            }
            if (File.Exists(stagedReceipt)) File.Copy(stagedReceipt, Path.Combine(incoming, "receipt-settings.json"));
            cancellationToken.ThrowIfCancellationRequested();
            var safetyResult = await _backup.CreateBackupAsync(cancellationToken);
            if (!safetyResult.Succeeded || safetyResult.Value is null)
                return Result.Failure(new[] { "لم تبدأ الاستعادة لأن إنشاء نسخة أمان للوضع الحالي فشل." }.Concat(safetyResult.Errors));
            safety = safetyResult.Value;
            cancellationToken.ThrowIfCancellationRequested();
            // No cancellation once destructive work starts. A rollback must not inherit a cancelled token.
            restoreAttempted = true;
            var restored = await _backup.RestoreBackupAsync(Path.Combine(stage, "database.bak"), CancellationToken.None);
            if (!restored.Succeeded) throw new InvalidOperationException(string.Join(Environment.NewLine, restored.Errors));
            Directory.CreateDirectory(before);
            if (Directory.Exists(images))
            {
                EnsureNotLink(images);
                Directory.Move(images, Path.Combine(before, "product-images"));
                imagesMoved = true;
            }
            if (File.Exists(receipt))
            {
                EnsureNotLink(receipt);
                File.Move(receipt, Path.Combine(before, "receipt-settings.json"));
                receiptMoved = true;
            }
            Directory.Move(Path.Combine(incoming, "product-images"), images);
            newImagesInstalled = true;
            var incomingReceipt = Path.Combine(incoming, "receipt-settings.json");
            if (File.Exists(incomingReceipt))
            {
                File.Move(incomingReceipt, receipt);
                newReceiptInstalled = true;
            }
            await File.WriteAllTextAsync(Path.Combine(before, "restore-info.json"), JsonSerializer.Serialize(new
                { SafetyBackupPath = safety.FullPath, RestoredAtUtc = DateTime.UtcNow, SourcePharmacy = manifest.PharmacyName }), CancellationToken.None);
            return Result.Success();
        }
        catch (Exception ex)
        {
            var errors = new List<string> { "فشل استيراد حزمة البيانات: " + ex.Message };
            if (restoreAttempted && safety is not null)
            {
                try
                {
                    if (newImagesInstalled && Directory.Exists(images)) Directory.Delete(images, recursive: true);
                    if (imagesMoved) Directory.Move(Path.Combine(before, "product-images"), images);
                    if (newReceiptInstalled && File.Exists(receipt)) File.Delete(receipt);
                    if (receiptMoved) File.Move(Path.Combine(before, "receipt-settings.json"), receipt);
                }
                catch (Exception rollbackEx) { errors.Add("تعذر استرجاع بعض ملفات الهوية والصور. احتُفظ بالملفات السابقة في: " + before + ". " + rollbackEx.Message); }
                try
                {
                    var rollback = await _backup.RestoreBackupAsync(safety.FullPath, CancellationToken.None);
                    errors.Add(rollback.Succeeded
                        ? "استُعيدت نسخة الأمان لقاعدة البيانات. أغلق البرنامج وأعد تشغيله قبل المتابعة."
                        : "تعذر استرجاع قاعدة البيانات تلقائياً. لا تتابع العمل؛ استعد نسخة الأمان من: " + safety.FullPath + ". " + string.Join(" ", rollback.Errors));
                }
                catch (Exception rollbackEx)
                {
                    errors.Add("تعذر استرجاع نسخة الأمان تلقائياً. لا تتابع العمل؛ احتُفظ بالنسخة في: " + safety.FullPath + ". " + rollbackEx.Message);
                }
            }
            return Result.Failure(errors);
        }
        finally
        {
            TryDeleteDirectory(stage);
            TryDeleteDirectory(incoming);
            _operation.Release();
        }
    }

    private static async Task<string?> ValidateReceiptAsync(string path, CancellationToken token)
    {
        EnsureNotLink(path);
        if (new FileInfo(path).Length > BackupBundleArchive.MaxReceiptBytes) throw new InvalidDataException("ملف إعدادات الوصل كبير جداً.");
        await using var input = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(input, new JsonDocumentOptions { MaxDepth = 8 }, token);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("ملف إعدادات الوصل غير صالح.");
        foreach (var (name, max) in new[] { ("PharmacyName", 120), ("Address", 250), ("FooterMessage", 400) })
            if (root.TryGetProperty(name, out var value) && (value.ValueKind != JsonValueKind.String || value.GetString()!.Length > max))
                throw new InvalidDataException("بيانات الوصل غير صالحة: " + name);
        foreach (var name in new[] { "ShowLogo", "ShowPharmacyName", "ShowAddress", "ShowFooterMessage" })
            if (root.TryGetProperty(name, out var value) && value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException("خيارات الوصل غير صالحة.");
        if (root.TryGetProperty("LogoBase64", out var logo) && logo.ValueKind != JsonValueKind.Null)
        {
            if (logo.ValueKind != JsonValueKind.String || logo.GetString()!.Length > 3_000_000) throw new InvalidDataException("شعار الوصل غير صالح.");
            byte[] data;
            try { data = Convert.FromBase64String(logo.GetString()!); }
            catch (FormatException) { throw new InvalidDataException("شعار الوصل غير صالح."); }
            var png = data.Length >= 24 && data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            var jpeg = data.Length >= 4 && data[0] == 255 && data[1] == 216 && data[2] == 255 && data[^2] == 255 && data[^1] == 217;
            if (data.Length is 0 or > 2 * 1024 * 1024 || (!png && !jpeg)) throw new InvalidDataException("شعار الوصل غير صالح.");
        }
        return root.TryGetProperty("PharmacyName", out var pharmacy) ? pharmacy.GetString() : null;
    }

    private static async Task ValidateImageAsync(string path, CancellationToken token)
    {
        await using var input = File.OpenRead(path);
        var signature = new byte[8];
        var read = await input.ReadAsync(signature, token);
        if (read < 8 || !HasImageSignature(signature)) throw new InvalidDataException("صورة منتج غير صالحة داخل الحزمة.");
    }

    private static bool HasImageSignature(ReadOnlySpan<byte> data) =>
        data.Length >= 8 && (data[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) || data[..3].SequenceEqual(new byte[] { 255, 216, 255 }));

    private static void EnsureNotLink(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("لا يمكن نقل البيانات عبر رابط ملفات أو مجلدات.");
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
