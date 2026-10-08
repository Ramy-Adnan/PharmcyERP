using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Purchasing.Imports;
using PharmacyERP.Infrastructure.Identity;
using PharmacyERP.Infrastructure.Services.Imports;
using PharmacyERP.WPF.ViewModels.SystemManagement;
using Xunit;

namespace PharmacyERP.Application.Tests;

public sealed class InvoiceAiSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pharmacy-ai-settings-" + Guid.NewGuid().ToString("N"));
    // Production uses DPAPI on Windows. Exercise storage with real authenticated encryption on Linux.
    private sealed class TestProtector : IInvoiceAiSecretProtector
    {
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
        public bool FailProtection { get; set; }
        public byte[] Protect(byte[] data)
        {
            if (FailProtection) throw new CryptographicException();
            var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16]; var encrypted = new byte[data.Length];
            using var aes = new AesGcm(_key, 16); aes.Encrypt(nonce, data, encrypted, tag);
            return nonce.Concat(tag).Concat(encrypted).ToArray();
        }
        public byte[] Unprotect(byte[] data)
        {
            if (data.Length < 28) throw new CryptographicException();
            var plain = new byte[data.Length - 28]; using var aes = new AesGcm(_key, 16);
            aes.Decrypt(data.AsSpan(0, 12), data.AsSpan(28), data.AsSpan(12, 16), plain); return plain;
        }
    }
    private readonly TestProtector _protector = new();
    private string FilePath => Path.Combine(_directory, "invoice-ai-settings.dat");
    private EncryptedInvoiceAiSettingsStore Store => new(_protector, FilePath);
    private static InvoiceAiSettings Settings(string key = "customer-test-key") => new() { ApiKey = key };
    private sealed record Sent(HttpMethod Method, Uri Uri, string? Key, string? Auth, string? Body);
    private sealed class Handler : HttpMessageHandler
    {
        public List<Sent> Requests { get; } = new();
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add(new(request.Method, request.RequestUri!, request.Headers.TryGetValues("x-goog-api-key", out var keys) ? keys.Single() : null,
                request.Headers.Authorization?.ToString(), request.Content is null ? null : await request.Content.ReadAsStringAsync(token)));
            const string document = "{\"lines\":[{\"name\":\"Paracetamol\"}]}";
            var response = request.RequestUri!.Host == "api.openai.com"
                ? JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = "stop", message = new { content = document } } } })
                : JsonSerializer.Serialize(new { candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text = document } } } } } });
            return new(Status) { Content = new StringContent(response) };
        }
    }
    private sealed class Tester : IInvoiceAiConnectionTester
    {
        public int Calls { get; private set; }
        public InvoiceAiSettings? Received { get; private set; }
        public async Task<Result> TestAsync(InvoiceAiSettings settings, CancellationToken token = default)
        { Calls++; Received = settings; await Task.Yield(); return Result.Success(); }
    }
    private ConfiguredInvoiceImageReader Reader(Handler handler, IInvoiceAiSettingsStore? store = null) => new(new HttpClient(handler),
        new InvoiceVisionOptions { ApiKey = () => "environment-openai-key", Model = () => "gpt-4.1-mini" },
        new GeminiVisionOptions { ApiKey = () => "environment-gemini-key", Model = () => "gemini-2.5-flash" },
        () => "OpenAI", store ?? Store);
    private static InvoiceImageInput Image() => new("invoice.jpg", "image/jpeg", new byte[] { 255, 216, 255, 1 });
    private InvoiceAiSettingsViewModel Editor(Tester? tester = null, params string[] permissions)
    {
        var user = new CurrentUserService(); user.SetSession(1, "manager", 1, permissions.Length == 0 ? new[] { "Branches.Manage" } : permissions);
        var vm = new InvoiceAiSettingsViewModel(Store, tester ?? new Tester(), user); vm.Initialize(); return vm;
    }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    [Fact]
    public void SavedKey_IsEncrypted_AndSurvivesCreatingANewStore()
    {
        Store.Load().Should().BeNull(); Store.Save(Settings());
        Encoding.UTF8.GetString(File.ReadAllBytes(FilePath)).Should().NotContain("customer-test-key").And.NotContain("ApiKey");
        Store.Load()!.ApiKey.Should().Be("customer-test-key"); Directory.GetFiles(_directory, "*.tmp").Should().BeEmpty();
    }
    [Fact]
    public void EncryptionFailure_PreservesPreviousSettings()
    {
        Store.Save(Settings("previous-key")); _protector.FailProtection = true;
        Action save = () => Store.Save(Settings("replacement-key")); save.Should().Throw<CryptographicException>();
        Store.Load()!.ApiKey.Should().Be("previous-key");
    }
    [Theory]
    [InlineData("provider")] [InlineData("model-url")] [InlineData("key-newline")]
    public void InvalidSettings_DoNotOverwriteExistingKey(string problem)
    {
        Store.Save(Settings()); var bad = Settings();
        if (problem == "provider") bad.Provider = "unknown";
        if (problem == "model-url") bad.Model = "gemini-test/../../other?key=secret";
        if (problem == "key-newline") bad.ApiKey = "secret\r\nextra";
        Action save = () => Store.Save(bad); save.Should().Throw<InvalidOperationException>(); Store.Load()!.ApiKey.Should().Be("customer-test-key");
    }
    [Fact]
    public void CopiedCiphertext_CannotBeOpenedByAnotherProtector_AndCanBeReconfigured()
    {
        Store.Save(Settings()); var other = new EncryptedInvoiceAiSettingsStore(new TestProtector(), FilePath);
        Action read = () => other.Load(); read.Should().Throw<InvalidOperationException>().WithMessage("*إعدادات النظام*");
        other.Save(Settings("new-customer-key")); other.Load()!.ApiKey.Should().Be("new-customer-key");
    }
    [Fact]
    public async Task SavedSettings_OverrideEnvironment_AndChangesApplyWithoutRestartingReader()
    {
        Store.Save(Settings()); var handler = new Handler(); using var reader = Reader(handler);
        reader.ProviderName.Should().Be("Gemini"); (await reader.ReadAsync(Image())).Succeeded.Should().BeTrue();
        handler.Requests[0].Key.Should().Be("customer-test-key"); handler.Requests[0].Auth.Should().BeNull();
        Store.Save(new() { Provider = "OpenAI", Model = "gpt-4.1-mini", ApiKey = "new-openai-key" });
        reader.ProviderName.Should().Be("OpenAI"); (await reader.ReadAsync(Image())).Succeeded.Should().BeTrue();
        handler.Requests[1].Auth.Should().Be("Bearer new-openai-key"); handler.Requests[1].Key.Should().BeNull();
    }
    [Fact]
    public async Task DeletedKey_DoesNotFallBackToEnvironmentKeys()
    {
        Store.Save(Settings(string.Empty)); var handler = new Handler(); using var reader = Reader(handler);
        (await reader.ReadAsync(Image())).Succeeded.Should().BeFalse(); handler.Requests.Should().BeEmpty();
    }
    [Fact]
    public async Task CorruptSettings_DoNotCrashProviderDisplayOrSendAnEnvironmentKey()
    {
        Store.Save(Settings()); File.WriteAllText(FilePath, "corrupted"); var handler = new Handler(); using var reader = Reader(handler);
        Action name = () => _ = reader.ProviderName; name.Should().NotThrow();
        (await reader.ReadAsync(Image())).Errors.Single().Should().Contain("إعدادات النظام"); handler.Requests.Should().BeEmpty();
    }
    [Fact]
    public void Editor_DoesNotExposeSavedKey_AndBlankFieldPreservesIt()
    {
        Store.Save(Settings()); var vm = Editor(); vm.HasSavedKey.Should().BeTrue(); vm.NewApiKey.Should().BeEmpty();
        var confirmations = new List<ReceiptSettingsSaveResult>(); vm.SaveCompleted += (_, result) => confirmations.Add(result);
        vm.Save(); Store.Load()!.ApiKey.Should().Be("customer-test-key"); confirmations.Single().Succeeded.Should().BeTrue();
        vm.StatusMessage.Should().NotContain("customer-test-key");
    }
    [Fact]
    public void ReplacingKey_ClearsPasswordInput_AfterConfirmedSave()
    {
        Store.Save(Settings()); var vm = Editor(); var cleared = 0; vm.ClearPasswordRequested += (_, _) => cleared++;
        vm.NewApiKey = "new-key"; vm.Save(); Store.Load()!.ApiKey.Should().Be("new-key"); vm.NewApiKey.Should().BeEmpty(); cleared.Should().Be(1);
    }
    [Fact]
    public void SwitchingProvider_NeverReusesTheOtherProvidersSavedKey()
    {
        Store.Save(Settings()); var vm = Editor(); vm.Provider = "OpenAI";
        vm.HasSavedKey.Should().BeFalse(); vm.Save(); Store.Load()!.Provider.Should().Be("Gemini");
        vm.NewApiKey = "customer-openai-key"; vm.Save(); Store.Load()!.Provider.Should().Be("OpenAI");
        Store.Load()!.ApiKey.Should().Be("customer-openai-key");
    }
    [Fact]
    public void RemoveKey_DisablesConfiguredReading_AndCannotBeUndoneByABlankSave()
    {
        Store.Save(Settings()); var vm = Editor(); vm.RemoveKey(); vm.HasSavedKey.Should().BeFalse(); Store.Load()!.ApiKey.Should().BeEmpty();
        vm.Save(); Store.Load()!.ApiKey.Should().BeEmpty();
    }
    [Fact]
    public async Task CashierCannotReadChangeDeleteOrTestSavedKey()
    {
        Store.Save(Settings()); var tester = new Tester(); var vm = Editor(tester, "Sales.UsePos");
        vm.CanManage.Should().BeFalse(); vm.HasSavedKey.Should().BeFalse();
        vm.NewApiKey = "new-key"; vm.Save(); vm.RemoveKey(); await vm.TestAsync();
        Store.Load()!.ApiKey.Should().Be("customer-test-key"); tester.Calls.Should().Be(0);
    }
    [Fact]
    public async Task TestingAnUnsavedDraft_DoesNotPersistIt()
    {
        Store.Save(Settings()); var tester = new Tester(); var vm = Editor(tester); vm.NewApiKey = "unsaved-test-key";
        await vm.TestAsync(); tester.Received!.ApiKey.Should().Be("unsaved-test-key"); Store.Load()!.ApiKey.Should().Be("customer-test-key");
        vm.IsBusy.Should().BeFalse(); vm.StatusMessage.Should().Contain("نجح الاتصال").And.NotContain("unsaved-test-key");
    }
    [Theory]
    [InlineData("Gemini", "gemini-2.5-flash", "generativelanguage.googleapis.com")]
    [InlineData("OpenAI", "gpt-4.1-mini", "api.openai.com")]
    public async Task ConnectionTest_UsesModelMetadata_WithoutUploadingAnInvoice(string provider, string model, string host)
    {
        var handler = new Handler(); using var tester = new InvoiceAiConnectionTester(new HttpClient(handler));
        var result = await tester.TestAsync(new() { Provider = provider, Model = model, ApiKey = "metadata-test-key" });
        result.Succeeded.Should().BeTrue(); var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Get); request.Uri.Host.Should().Be(host); request.Uri.Query.Should().BeEmpty(); request.Body.Should().BeNull();
        if (provider == "Gemini") request.Key.Should().Be("metadata-test-key"); else request.Auth.Should().Be("Bearer metadata-test-key");
    }
    [Theory]
    [InlineData(400)] [InlineData(403)] [InlineData(404)] [InlineData(429)]
    public async Task FailedConnectionTest_ShowsAnErrorWithoutExposingTheKey(int status)
    {
        using var tester = new InvoiceAiConnectionTester(new HttpClient(new Handler { Status = (HttpStatusCode)status }));
        var result = await tester.TestAsync(Settings()); result.Succeeded.Should().BeFalse(); result.Errors.Single().Should().NotContain("customer-test-key");
    }
}
