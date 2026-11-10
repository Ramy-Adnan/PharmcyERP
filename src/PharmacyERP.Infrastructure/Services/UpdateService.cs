using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PharmacyERP.Application.Features.Updates;

namespace PharmacyERP.Infrastructure.Services;

/// <summary>HTTPS release metadata and hash-verified interactive installer download.</summary>
public sealed class UpdateService : IUpdateService, IDisposable
{
    private const int MaxManifestBytes = 256 * 1024;
    private const long MaxInstallerBytes = 1024L * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly string _updateFeedUrl;
    private readonly Action<ProcessStartInfo> _launchInstaller;
    private readonly string _temporaryDirectory;
    private readonly string _currentVersion;

    public UpdateService(IConfiguration configuration)
        : this(configuration, new HttpClient { Timeout = TimeSpan.FromMinutes(15) }, LaunchInstaller)
    {
        _ownsClient = true;
    }

    /// <summary>Inject transport/launcher for integration tests without starting an executable.</summary>
    public UpdateService(IConfiguration configuration, HttpClient httpClient,
        Action<ProcessStartInfo> launchInstaller, string? temporaryDirectory = null, string? currentVersion = null)
    {
        _httpClient = httpClient;
        _launchInstaller = launchInstaller;
        _temporaryDirectory = temporaryDirectory ?? Path.GetTempPath();
        _currentVersion = currentVersion ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
        _updateFeedUrl = configuration["ApplicationSettings:UpdateFeedUrl"]
            ?? "https://api.alliba.uk/updates/pharmacy-erp/latest.json";
    }

    public async Task<UpdateCheckResultDto> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        var result = new UpdateCheckResultDto { CurrentVersion = _currentVersion };
        try
        {
            var uri = RequireHttps(_updateFeedUrl);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            RequireHttpsResponse(response);
            if (response.Content.Headers.ContentLength > MaxManifestBytes)
                throw new InvalidDataException("ملف معلومات التحديث أكبر من الحد المسموح.");

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
            {
                if (buffer.Length + read > MaxManifestBytes)
                    throw new InvalidDataException("ملف معلومات التحديث أكبر من الحد المسموح.");
                buffer.Write(chunk, 0, read);
            }
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(buffer.ToArray(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest is null || !Version.TryParse(manifest.Version, out var latest) ||
                !Version.TryParse(_currentVersion, out var current))
                throw new InvalidDataException("رقم إصدار التحديث غير صالح.");

            result.LatestVersion = manifest.Version;
            result.ReleaseNotes = manifest.ReleaseNotes;
            result.IsMandatory = manifest.Mandatory;
            if (latest <= current) return result;

            RequireHttps(manifest.DownloadUrl);
            RequireHash(manifest.Sha256);
            result.DownloadUrl = manifest.DownloadUrl;
            result.Sha256 = manifest.Sha256!.Trim().ToLowerInvariant();
            result.IsUpdateAvailable = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or
                                   InvalidOperationException or ArgumentException or OperationCanceledException)
        {
            // Do not misreport a network failure as proof that the app is up to date.
            // Avoid exposing transport errors which may include query-string credentials.
            result.ErrorMessage = "تعذر التحقق من التحديث. تأكد من الاتصال ومن إعداد ملف إصدار HTTPS مع رابط صالح وبصمة SHA-256.";
        }
        return result;
    }

    public async Task DownloadAndLaunchInstallerAsync(string downloadUrl, IProgress<int>? progress = null,
        CancellationToken cancellationToken = default, string? expectedSha256 = null)
    {
        var uri = RequireHttps(downloadUrl);
        var expected = RequireHash(expectedSha256);
        Directory.CreateDirectory(_temporaryDirectory);
        var tempPath = Path.Combine(_temporaryDirectory, $"PharmacyERP-Update-{Guid.NewGuid():N}.exe");
        var launched = false;
        try
        {
            using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            RequireHttpsResponse(response);
            var totalBytes = response.Content.Headers.ContentLength;
            if (totalBytes > MaxInstallerBytes)
                throw new InvalidDataException("حجم مثبت التحديث أكبر من الحد المسموح (1 GB).");

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 81920, useAsync: true))
            {
                var bytes = new byte[81920];
                long totalRead = 0;
                int read;
                while ((read = await input.ReadAsync(bytes, cancellationToken)) > 0)
                {
                    totalRead += read;
                    if (totalRead > MaxInstallerBytes)
                        throw new InvalidDataException("حجم مثبت التحديث أكبر من الحد المسموح (1 GB).");
                    hash.AppendData(bytes, 0, read);
                    await output.WriteAsync(bytes.AsMemory(0, read), cancellationToken);
                    if (totalBytes is > 0)
                        progress?.Report((int)Math.Min(99, totalRead * 100 / totalBytes.Value));
                }
                await output.FlushAsync(cancellationToken);
            }

            if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), expected))
                throw new InvalidDataException("بصمة التحديث لا تطابق الإصدار المنشور. لم يتم تشغيل المثبت.");
            RequireWindowsExecutable(tempPath);
            cancellationToken.ThrowIfCancellationRequested();
            // EXE/MSI options differ by package. Keep the normal interactive
            // setup flow instead of guessing Advanced Installer's silent switch.
            _launchInstaller(new ProcessStartInfo { FileName = tempPath, Arguments = string.Empty, UseShellExecute = true });
            launched = true;
            progress?.Report(100);
        }
        finally
        {
            if (!launched && File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static Uri RequireHttps(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("رابط التحديث يجب أن يستخدم HTTPS دون بيانات دخول ضمن الرابط.");
        return uri;
    }

    private static void RequireHttpsResponse(HttpResponseMessage response)
    {
        if (response.RequestMessage?.RequestUri is { } finalUri) RequireHttps(finalUri.AbsoluteUri);
    }

    private static byte[] RequireHash(string? value)
    {
        var hash = value?.Trim();
        if (hash is null || hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            throw new InvalidOperationException("يجب توفير بصمة SHA-256 صالحة للمثبت قبل تنزيله.");
        return Convert.FromHexString(hash);
    }

    private static void RequireWindowsExecutable(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length < 64 || stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
            throw new InvalidDataException("الملف الذي تم تنزيله ليس مثبت Windows صالحاً.");
        stream.Position = 0x3c;
        using var reader = new BinaryReader(stream);
        var offset = reader.ReadInt32();
        if (offset < 64 || offset > stream.Length - 4)
            throw new InvalidDataException("الملف الذي تم تنزيله ليس مثبت Windows صالحاً.");
        stream.Position = offset;
        if (reader.ReadUInt32() != 0x00004550)
            throw new InvalidDataException("الملف الذي تم تنزيله ليس مثبت Windows صالحاً.");
    }

    private static void LaunchInstaller(ProcessStartInfo info)
    {
        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("تعذر بدء مثبت التحديث.");
    }

    public void Dispose()
    {
        if (_ownsClient) _httpClient.Dispose();
    }

    private sealed class UpdateManifest
    {
        public string Version { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string? Sha256 { get; set; }
        public string? ReleaseNotes { get; set; }
        public bool Mandatory { get; set; }
    }
}
