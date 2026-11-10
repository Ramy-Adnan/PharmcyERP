using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using PharmacyERP.Infrastructure.Services;
using Xunit;

namespace PharmacyERP.Application.Tests;

public sealed class UpdatePackageTests
{
    [Theory]
    [InlineData("http://publisher.example/setup.exe")]
    [InlineData("file:///tmp/setup.exe")]
    [InlineData("https://user:password@publisher.example/setup.exe")]
    public async Task NonHttpsOrCredentialUrls_AreRejectedBeforeARequest(string url)
    {
        using var fixture = new Fixture(_ => Response(Installer()));
        var action = () => fixture.Service.DownloadAndLaunchInstallerAsync(url, expectedSha256: Hash(Installer()));
        await action.Should().ThrowAsync<InvalidOperationException>();
        fixture.Handler.Requests.Should().Be(0);
        fixture.Launched.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-hash")]
    public async Task MissingOrInvalidHash_AreRejectedBeforeDownloading(string? hash)
    {
        using var fixture = new Fixture(_ => Response(Installer()));
        var action = () => fixture.Service.DownloadAndLaunchInstallerAsync(Fixture.DownloadUrl, expectedSha256: hash);
        await action.Should().ThrowAsync<InvalidOperationException>();
        fixture.Handler.Requests.Should().Be(0);
    }

    [Fact]
    public async Task HashMismatch_NeverLaunchesAndRemovesTheDownload()
    {
        using var fixture = new Fixture(_ => Response(Installer()));
        var action = () => fixture.Service.DownloadAndLaunchInstallerAsync(Fixture.DownloadUrl, expectedSha256: new string('0', 64));
        await action.Should().ThrowAsync<InvalidDataException>();
        fixture.Launched.Should().BeNull();
        Directory.GetFiles(fixture.Directory).Should().BeEmpty();
    }

    [Fact]
    public async Task VerifiedDownload_LaunchesInteractiveSetupWithNoGuessedSilentArguments()
    {
        var installer = Installer();
        using var fixture = new Fixture(_ => Response(installer));
        await fixture.Service.DownloadAndLaunchInstallerAsync(Fixture.DownloadUrl, expectedSha256: Hash(installer));
        fixture.Launched.Should().NotBeNull();
        fixture.Launched!.Arguments.Should().BeEmpty();
        fixture.Launched.UseShellExecute.Should().BeTrue();
        File.ReadAllBytes(fixture.Launched.FileName).Should().Equal(installer);
    }

    [Fact]
    public async Task HashMatchingNonExecutable_NeverLaunches()
    {
        var content = Encoding.UTF8.GetBytes("<html>This is not the installer.</html>");
        using var fixture = new Fixture(_ => Response(content));
        var action = () => fixture.Service.DownloadAndLaunchInstallerAsync(Fixture.DownloadUrl, expectedSha256: Hash(content));
        await action.Should().ThrowAsync<InvalidDataException>();
        fixture.Launched.Should().BeNull();
        Directory.GetFiles(fixture.Directory).Should().BeEmpty();
    }

    [Fact]
    public async Task RedirectToHttp_IsRejectedEvenWhenTheHashMatches()
    {
        var content = Installer();
        using var fixture = new Fixture(_ =>
        {
            var response = Response(content);
            response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://publisher.example/setup.exe");
            return response;
        });
        var action = () => fixture.Service.DownloadAndLaunchInstallerAsync(Fixture.DownloadUrl, expectedSha256: Hash(content));
        await action.Should().ThrowAsync<InvalidOperationException>();
        fixture.Launched.Should().BeNull();
    }

    [Fact]
    public async Task OversizedPackage_IsRejectedBeforeWriting()
    {
        using var fixture = new Fixture(_ =>
        {
            var response = Response(Installer());
            response.Content.Headers.ContentLength = 1024L * 1024 * 1024 + 1;
            return response;
        });
        var action = () => fixture.Service.DownloadAndLaunchInstallerAsync(Fixture.DownloadUrl, expectedSha256: Hash(Installer()));
        await action.Should().ThrowAsync<InvalidDataException>();
        fixture.Launched.Should().BeNull();
        Directory.GetFiles(fixture.Directory).Should().BeEmpty();
    }

    [Fact]
    public async Task LauncherFailure_KeepsTheApplicationRunningAndRemovesTemporaryPackage()
    {
        using var fixture = new Fixture(_ => Response(Installer()), launchError: new InvalidOperationException("UAC cancelled"));
        var action = () => fixture.Service.DownloadAndLaunchInstallerAsync(Fixture.DownloadUrl, expectedSha256: Hash(Installer()));
        await action.Should().ThrowAsync<InvalidOperationException>();
        Directory.GetFiles(fixture.Directory).Should().BeEmpty();
    }

    [Fact]
    public async Task ValidNewManifest_RequiresHttpsAndHashBeforeOfferingTheUpdate()
    {
        var hash = Hash(Installer());
        using var fixture = new Fixture(_ => Manifest(hash));
        var result = await fixture.Service.CheckForUpdateAsync();
        result.IsUpdateAvailable.Should().BeTrue();
        result.Sha256.Should().Be(hash);
        result.DownloadUrl.Should().Be(Fixture.DownloadUrl);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task ManifestMissingHash_IsReportedAsAnErrorRatherThanUpToDate()
    {
        using var fixture = new Fixture(_ => Manifest(null));
        var result = await fixture.Service.CheckForUpdateAsync();
        result.IsUpdateAvailable.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task OfflineCheck_DoesNotThrowOrClaimItSuccessfullyCheckedTheLatestVersion()
    {
        using var fixture = new Fixture(_ => throw new HttpRequestException("offline"));
        var result = await fixture.Service.CheckForUpdateAsync();
        result.IsUpdateAvailable.Should().BeFalse();
        result.CurrentVersion.Should().Be("1.0.0");
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CheckCancellation_IsHonored()
    {
        using var fixture = new Fixture(_ => Manifest(Hash(Installer())));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var action = () => fixture.Service.CheckForUpdateAsync(cancellation.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private static HttpResponseMessage Manifest(string? hash) => Response(JsonSerializer.SerializeToUtf8Bytes(new
    {
        Version = "1.1.0", DownloadUrl = Fixture.DownloadUrl, Sha256 = hash, ReleaseNotes = "A tested release."
    }));

    private static byte[] Installer()
    {
        var bytes = new byte[128];
        bytes[0] = (byte)'M'; bytes[1] = (byte)'Z';
        BitConverter.GetBytes(64).CopyTo(bytes, 0x3c);
        BitConverter.GetBytes(0x00004550).CopyTo(bytes, 64);
        return bytes;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static HttpResponseMessage Response(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private sealed class Fixture : IDisposable
    {
        public const string DownloadUrl = "https://publisher.example/setup.exe";
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "pharmacy-update-test-" + Guid.NewGuid().ToString("N"));
        public FakeHandler Handler { get; }
        public ProcessStartInfo? Launched { get; private set; }
        public UpdateService Service { get; }
        private readonly HttpClient _client;

        public Fixture(Func<HttpRequestMessage, HttpResponseMessage> response, Exception? launchError = null)
        {
            System.IO.Directory.CreateDirectory(Directory);
            Handler = new FakeHandler(response);
            _client = new HttpClient(Handler);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApplicationSettings:UpdateFeedUrl"] = "https://publisher.example/latest.json"
            }).Build();
            Service = new UpdateService(configuration, _client, info =>
            {
                if (launchError is not null) throw launchError;
                Launched = info;
            }, Directory, "1.0.0");
        }

        public void Dispose()
        {
            Service.Dispose();
            _client.Dispose();
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests++;
            var result = response(request);
            result.RequestMessage ??= request;
            return Task.FromResult(result);
        }
    }
}
