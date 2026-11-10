using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PharmacyERP.Infrastructure.Services;

public sealed class BackupBundleManifest
{
    public int SchemaVersion { get; init; } = 1;
    public string DatabaseName { get; init; } = string.Empty;
    public string? PharmacyName { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public List<BackupBundleEntry> Files { get; init; } = new();
}

public sealed class BackupBundleEntry
{
    public string Path { get; init; } = string.Empty;
    public long Length { get; init; }
    public string Sha256 { get; init; } = string.Empty;
}

/// <summary>Strict portable data format: no application executables, licenses, machine IDs or encrypted credentials.</summary>
public static class BackupBundleArchive
{
    public const long MaxDatabaseBytes = 16L * 1024 * 1024 * 1024;
    public const long MaxBundleBytes = 20L * 1024 * 1024 * 1024;
    public const long MaxImageBytes = 5L * 1024 * 1024;
    public const long MaxReceiptBytes = 4L * 1024 * 1024;
    private const long MaxManifestBytes = 8L * 1024 * 1024;
    private const int MaxEntries = 20_002;
    private static readonly Regex ImageName = new(@"\Aproduct-images/[A-Fa-f0-9]{64}\.(?:png|jpg)\z", RegexOptions.CultureInvariant);
    private static readonly Regex Digest = new(@"\A[A-Fa-f0-9]{64}\z", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new()
        { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public static bool IsProductImageName(string fileName) => ImageName.IsMatch("product-images/" + fileName);

    public static async Task CreateAsync(string destinationZip, string databaseName, string? pharmacyName,
        IReadOnlyDictionary<string, string> sources, CancellationToken token = default)
    {
        BackupSql.Identifier(databaseName);
        if (!sources.ContainsKey("database.bak")) throw new InvalidDataException("الحزمة لا تحتوي نسخة قاعدة البيانات.");
        if (sources.Count > MaxEntries || sources.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != sources.Count)
            throw new InvalidDataException("عدد ملفات الحزمة أكبر من المسموح أو أسماء الملفات مكررة.");
        var destination = System.IO.Path.GetFullPath(destinationZip);
        if (File.Exists(destination)) throw new IOException("ملف التصدير موجود مسبقاً؛ اختر اسماً جديداً للحزمة.");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var entries = new List<BackupBundleEntry>();
            long totalBytes = 0;
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
                foreach (var (relative, source) in sources.OrderBy(s => s.Key, StringComparer.Ordinal))
                {
                    token.ThrowIfCancellationRequested();
                    var limit = MaximumLength(relative);
                    if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("لا يمكن تضمين روابط ملفات في حزمة البيانات.");
                    await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (input.Length <= 0 || input.Length > limit) throw new InvalidDataException($"حجم الملف '{relative}' غير صالح.");
                    totalBytes = checked(totalBytes + input.Length);
                    if (totalBytes > MaxBundleBytes) throw new InvalidDataException("حجم الحزمة أكبر من المسموح.");
                    var entry = archive.CreateEntry(relative, CompressionLevel.Fastest);
                    await using var target = entry.Open();
                    var (length, digest) = await CopyHashedAsync(input, target, limit, token);
                    entries.Add(new BackupBundleEntry { Path = relative, Length = length, Sha256 = digest });
                }
                var manifest = new BackupBundleManifest
                {
                    DatabaseName = databaseName, PharmacyName = pharmacyName, CreatedAtUtc = DateTime.UtcNow, Files = entries
                };
                var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Fastest);
                await using var manifestStream = manifestEntry.Open();
                await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, token);
            }
            File.Move(temporary, destination, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>Reads bounded metadata and validates the entry inventory; full content hashes are checked on extraction.</summary>
    public static async Task<BackupBundleManifest> InspectAsync(string bundlePath, string expectedDatabase, CancellationToken token = default)
    {
        await using var input = new FileStream(bundlePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        return await ReadManifestAsync(archive, expectedDatabase, token);
    }

    /// <summary>Extracts only whitelisted files, checking every byte count and SHA-256 before any database operation.</summary>
    public static async Task<BackupBundleManifest> ExtractAsync(string bundlePath, string expectedDatabase, string stagingDirectory, CancellationToken token = default)
    {
        var staging = System.IO.Path.GetFullPath(stagingDirectory);
        if (Directory.Exists(staging) && Directory.EnumerateFileSystemEntries(staging).Any())
            throw new InvalidDataException("مجلد تجهيز الاستعادة ليس فارغاً.");
        Directory.CreateDirectory(staging);
        await using var input = new FileStream(bundlePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        var manifest = await ReadManifestAsync(archive, expectedDatabase, token);
        var lookup = archive.Entries.ToDictionary(e => e.FullName, StringComparer.Ordinal);
        foreach (var file in manifest.Files)
        {
            token.ThrowIfCancellationRequested();
            var relative = file.Path.Replace('/', System.IO.Path.DirectorySeparatorChar);
            var targetPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(staging, relative));
            if (!targetPath.StartsWith(staging.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("مسار خارج مجلد الاستعادة.");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetPath)!);
            await using var source = lookup[file.Path].Open();
            await using var target = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var (length, digest) = await CopyHashedAsync(source, target, MaximumLength(file.Path), token);
            if (length != file.Length || !string.Equals(digest, file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"فشل فحص سلامة الملف '{file.Path}'. لم تُستبدل قاعدة البيانات.");
        }
        return manifest;
    }

    private static async Task<BackupBundleManifest> ReadManifestAsync(ZipArchive archive, string expectedDatabase, CancellationToken token)
    {
        BackupSql.Identifier(expectedDatabase);
        if (archive.Entries.Count is < 2 or > MaxEntries + 1) throw new InvalidDataException("عدد ملفات الحزمة غير صالح.");
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (!entries.TryAdd(entry.FullName, entry)) throw new InvalidDataException("الحزمة تحتوي أسماء ملفات مكررة.");
            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType == 0xA000) throw new InvalidDataException("الحزمة تحتوي رابط ملفات غير مسموح.");
            if (entry.FullName == "manifest.json")
            {
                if (entry.Length <= 0 || entry.Length > MaxManifestBytes) throw new InvalidDataException("حجم بيان الحزمة غير صالح.");
            }
            else if (entry.Length <= 0 || entry.Length > MaximumLength(entry.FullName))
                throw new InvalidDataException($"حجم الملف '{entry.FullName}' غير صالح.");
        }
        if (!entries.TryGetValue("manifest.json", out var manifestEntry) || manifestEntry.FullName != "manifest.json")
            throw new InvalidDataException("ملف بيان الحزمة غير موجود.");
        await using var stream = manifestEntry.Open();
        using var bounded = new MemoryStream();
        await CopyHashedAsync(stream, bounded, MaxManifestBytes, token);
        bounded.Position = 0;
        var manifest = await JsonSerializer.DeserializeAsync<BackupBundleManifest>(bounded, JsonOptions, token)
            ?? throw new InvalidDataException("بيان الحزمة غير صالح.");
        if (manifest.SchemaVersion != 1 || manifest.Files is null || manifest.Files.Count is < 1 or > MaxEntries)
            throw new InvalidDataException("إصدار الحزمة غير مدعوم أو قائمة الملفات غير صالحة.");
        if (!string.Equals(manifest.DatabaseName, expectedDatabase, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"الحزمة تخص قاعدة '{manifest.DatabaseName}' بينما الاتصال الحالي بقاعدة '{expectedDatabase}'.");
        if (manifest.CreatedAtUtc == default || manifest.PharmacyName?.Length > 120)
            throw new InvalidDataException("بيانات تعريف الحزمة غير صالحة.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var file in manifest.Files)
        {
            if (file is null || !seen.Add(file.Path) || file.Length <= 0 || file.Length > MaximumLength(file.Path) || !Digest.IsMatch(file.Sha256 ?? string.Empty))
                throw new InvalidDataException("قائمة سلامة ملفات الحزمة غير صالحة.");
            if (!entries.TryGetValue(file.Path, out var entry) || entry.FullName != file.Path || entry.Length != file.Length)
                throw new InvalidDataException("ملفات الحزمة لا تطابق بيانها.");
            total = checked(total + file.Length);
            if (total > MaxBundleBytes) throw new InvalidDataException("حجم الحزمة بعد فك الضغط أكبر من المسموح.");
        }
        if (!seen.Contains("database.bak") || entries.Count != seen.Count + 1)
            throw new InvalidDataException("الحزمة ناقصة أو تحتوي ملفات غير معلنة.");
        return manifest;
    }

    private static long MaximumLength(string relative)
    {
        if (relative == "database.bak") return MaxDatabaseBytes;
        if (relative == "receipt-settings.json") return MaxReceiptBytes;
        if (relative is not null && ImageName.IsMatch(relative)) return MaxImageBytes;
        throw new InvalidDataException("تحتوي الحزمة ملفاً أو مساراً غير مسموح. لا تُنقل التراخيص أو إعدادات المفاتيح المشفرة.");
    }

    private static async Task<(long Length, string Digest)> CopyHashedAsync(Stream source, Stream target, long limit, CancellationToken token)
    {
        var buffer = new byte[131072];
        long length = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count;
        while ((count = await source.ReadAsync(buffer, token)) > 0)
        {
            length = checked(length + count);
            if (length > limit) throw new InvalidDataException("تجاوز الملف الحجم المسموح بعد فك الضغط.");
            hash.AppendData(buffer, 0, count);
            await target.WriteAsync(buffer.AsMemory(0, count), token);
        }
        return (length, Convert.ToHexString(hash.GetHashAndReset()));
    }
}
