using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using PharmacyERP.Application.Features.Licensing;
using PharmacyERP.Infrastructure.Services;
using Xunit;

namespace PharmacyERP.Application.Tests;

/// <summary>Real asymmetric signatures and the production verifier; no SQL Server or Windows UI required.</summary>
public sealed class LicenseSignatureTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "PharmacyERP-LicenseTests-" + Guid.NewGuid().ToString("N"));
    private readonly RSA _issuer = RSA.Create(2048);
    private DateTime _now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    private LicenseService Service(string? key = null, bool missingKey = false) => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApplicationSettings:LicenseFolder"] = _folder
        }).Build(), () => missingKey ? null : key ?? _issuer.ExportSubjectPublicKeyInfoPem(), () => _now, "PHARMACY-TEST");

    private LicensePayload Payload(string hardware, DateTime? expiry = null) => new()
    {
        LicenseKey = "A1234567890ABCDE",
        CustomerName = "صيدلية النخيل / Test Pharmacy",
        HardwareId = hardware,
        IssuedAtUtc = _now.AddDays(-10),
        ExpiryDate = expiry ?? _now.Date.AddDays(365),
        MaxBranches = 2,
        MaxUsers = 5
    };

    [Fact]
    public async Task RealSignedLicense_ActivatesAndEnforcesBothLimits()
    {
        var service = Service();
        var payload = Payload(service.GetHardwareFingerprint());
        var result = await service.ActivateLicenseAsync(LicenseDocumentCodec.Sign(payload, _issuer));
        result.Succeeded.Should().BeTrue();
        var status = await service.GetLicenseStatusAsync();
        status.IsValid.Should().BeTrue();
        status.IsVerificationConfigured.Should().BeTrue();
        status.License!.CustomerName.Should().Be(payload.CustomerName);
        status.DaysUntilExpiry.Should().Be(365);
        (await service.CanAddBranchAsync(1)).Should().BeTrue();
        (await service.CanAddBranchAsync(2)).Should().BeFalse();
        (await service.CanAddUserAsync(4)).Should().BeTrue();
        (await service.CanAddUserAsync(5)).Should().BeFalse();
    }

    [Fact]
    public void Signature_IsIndependentOfJsonFormattingAndCurrentCulture()
    {
        var payload = Payload("DEVICE-123");
        var signed = LicenseDocumentCodec.Sign(payload, _issuer);
        using var parsed = JsonDocument.Parse(signed);
        var compact = JsonSerializer.Serialize(parsed.RootElement);
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-IQ");
            LicenseDocumentCodec.TryRead(compact, out var document, out _).Should().BeTrue();
            using var publicKey = LicenseDocumentCodec.ReadPublicKey(_issuer.ExportSubjectPublicKeyInfoPem());
            LicenseDocumentCodec.Verify(document!, publicKey).Should().BeTrue();
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Theory]
    [InlineData("MaxUsers", "999")]
    [InlineData("CustomerName", "Changed pharmacy")]
    [InlineData("HardwareId", "OTHER-DEVICE")]
    [InlineData("ExpiryDate", "2099-01-01T00:00:00Z")]
    public async Task ModifyingEntitlementInvalidatesSignature(string field, string replacement)
    {
        var service = Service();
        var json = JsonNode.Parse(LicenseDocumentCodec.Sign(Payload(service.GetHardwareFingerprint()), _issuer))!;
        if (field == "MaxUsers") json["Payload"]![field] = int.Parse(replacement, CultureInfo.InvariantCulture);
        else json["Payload"]![field] = replacement;
        var result = await service.ActivateLicenseAsync(json.ToJsonString());
        result.Succeeded.Should().BeFalse();
        File.Exists(Path.Combine(_folder, "license.lic")).Should().BeFalse();
    }

    [Fact]
    public async Task AnotherIssuerKeyCannotActivateThisRelease()
    {
        var service = Service();
        using var attacker = RSA.Create(2048);
        var result = await service.ActivateLicenseAsync(LicenseDocumentCodec.Sign(Payload(service.GetHardwareFingerprint()), attacker));
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task ValidSignatureForAnotherMachineIsRejected()
    {
        var service = Service();
        var signed = LicenseDocumentCodec.Sign(Payload("OTHER-MACHINE"), _issuer);
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "license.lic"), signed);
        var status = await service.GetLicenseStatusAsync();
        status.IsValid.Should().BeFalse();
        status.IsHardwareMismatch.Should().BeTrue();
    }

    [Fact]
    public async Task MissingVerificationKeyFailsClosedWithoutRequiringASharedSecret()
    {
        var service = Service(missingKey: true);
        var payload = Payload(service.GetHardwareFingerprint());
        var result = await service.ActivateLicenseAsync(LicenseDocumentCodec.Sign(payload, _issuer));
        result.Succeeded.Should().BeFalse();
        var status = await service.GetLicenseStatusAsync();
        status.IsVerificationConfigured.Should().BeFalse();
        status.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ExpiredLicenseKeepsFileAndLicenseInfoForRenewal()
    {
        var service = Service();
        var payload = Payload(service.GetHardwareFingerprint(), _now.Date);
        (await service.ActivateLicenseAsync(LicenseDocumentCodec.Sign(payload, _issuer))).Succeeded.Should().BeTrue();
        _now = _now.AddDays(1);
        var status = await service.GetLicenseStatusAsync();
        status.IsExpired.Should().BeTrue();
        status.IsValid.Should().BeFalse();
        status.License!.LicenseKey.Should().Be(payload.LicenseKey);
        File.Exists(Path.Combine(_folder, "license.lic")).Should().BeTrue();
        (await service.CanAddUserAsync(1)).Should().BeFalse();
    }

    [Fact]
    public async Task NewSignedRenewalRestoresOperationsWithoutChangingDeviceIdentity()
    {
        var service = Service();
        var hardware = service.GetHardwareFingerprint();
        var payload = Payload(hardware, _now.Date);
        (await service.ActivateLicenseAsync(LicenseDocumentCodec.Sign(payload, _issuer))).Succeeded.Should().BeTrue();
        _now = _now.AddDays(5);
        (await service.GetLicenseStatusAsync()).IsExpired.Should().BeTrue();
        payload.IssuedAtUtc = _now;
        payload.ExpiryDate = _now.AddYears(1).Date;
        payload.MaxUsers = 10;
        (await service.ActivateLicenseAsync(LicenseDocumentCodec.Sign(payload, _issuer))).Succeeded.Should().BeTrue();
        (await service.GetLicenseStatusAsync()).IsValid.Should().BeTrue();
        service.GetHardwareFingerprint().Should().Be(hardware);
        (await service.CanAddUserAsync(9)).Should().BeTrue();
    }

    [Fact]
    public async Task BadReplacementNeverOverwritesTheInstalledLicense()
    {
        var service = Service();
        var signed = LicenseDocumentCodec.Sign(Payload(service.GetHardwareFingerprint()), _issuer);
        (await service.ActivateLicenseAsync(signed)).Succeeded.Should().BeTrue();
        (await service.ActivateLicenseAsync("{broken")).Succeeded.Should().BeFalse();
        File.ReadAllText(Path.Combine(_folder, "license.lic")).Should().Be(signed);
        (await service.GetLicenseStatusAsync()).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task PerpetualLicenseHasNoExpiryDate()
    {
        var service = Service();
        var payload = Payload(service.GetHardwareFingerprint());
        payload.ExpiryDate = null;
        (await service.ActivateLicenseAsync(LicenseDocumentCodec.Sign(payload, _issuer))).Succeeded.Should().BeTrue();
        _now = _now.AddYears(3);
        var status = await service.GetLicenseStatusAsync();
        status.IsValid.Should().BeTrue();
        status.DaysUntilExpiry.Should().BeNull();
    }

    [Fact]
    public async Task MovingClockBackSeveralDaysTriggersReadOnlyStatus()
    {
        var service = Service();
        var payload = Payload(service.GetHardwareFingerprint());
        (await service.ActivateLicenseAsync(LicenseDocumentCodec.Sign(payload, _issuer))).Succeeded.Should().BeTrue();
        _now = _now.AddDays(20);
        (await service.GetLicenseStatusAsync()).IsValid.Should().BeTrue();
        _now = _now.AddDays(-10);
        var status = await service.GetLicenseStatusAsync();
        status.IsValid.Should().BeFalse();
        status.IsClockRollbackDetected.Should().BeTrue();
        File.Exists(Path.Combine(_folder, "license.lic")).Should().BeTrue();
    }

    [Fact]
    public async Task OldSharedSecretLicenseRequiresExplicitReissue()
    {
        var legacy = """{"Payload":{"LicenseKey":"LEGACY"},"Signature":"aGVsbG8="}""";
        var result = await Service().ActivateLicenseAsync(legacy);
        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("الإصدار القديم"));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 5)]
    public void InvalidEntitlementCountsCannotBeSigned(int branches, int users)
    {
        var payload = Payload("DEVICE");
        payload.MaxBranches = branches;
        payload.MaxUsers = users;
        Action action = () => LicenseDocumentCodec.Sign(payload, _issuer);
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PrivateKeyIsRejectedAsAnEmbeddedVerificationKey()
    {
        Action action = () => LicenseDocumentCodec.ReadPublicKey(_issuer.ExportPkcs8PrivateKeyPem());
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task MalformedSignatureFailsAsAStatusRatherThanAnException()
    {
        var service = Service();
        var json = JsonNode.Parse(LicenseDocumentCodec.Sign(Payload(service.GetHardwareFingerprint()), _issuer))!;
        json["Signature"] = "this is not base64";
        (await service.ActivateLicenseAsync(json.ToJsonString())).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task FutureDatedLicenseFailsUntilTheDeviceClockIsCorrect()
    {
        var service = Service();
        var payload = Payload(service.GetHardwareFingerprint());
        payload.IssuedAtUtc = _now.AddDays(3);
        var result = await service.ActivateLicenseAsync(LicenseDocumentCodec.Sign(payload, _issuer));
        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("تاريخ"));
    }

    [Fact]
    public async Task CorruptClockCheckpointDoesNotCrashAndDoesNotGrantOperations()
    {
        var service = Service();
        var signed = LicenseDocumentCodec.Sign(Payload(service.GetHardwareFingerprint()), _issuer);
        (await service.ActivateLicenseAsync(signed)).Succeeded.Should().BeTrue();
        File.WriteAllText(Path.Combine(_folder, "license-clock.date"), "invalid");
        var status = await service.GetLicenseStatusAsync();
        status.IsValid.Should().BeFalse();
        File.ReadAllText(Path.Combine(_folder, "license.lic")).Should().Be(signed);
    }

    [Fact]
    public void WeakRsaPublicKeyCannotBeUsedForARelease()
    {
        using var weak = RSA.Create(1024);
        Action action = () => LicenseDocumentCodec.ReadPublicKey(weak.ExportSubjectPublicKeyInfoPem());
        action.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("SchemaVersion", "1")]
    [InlineData("Algorithm", "HMAC-SHA256")]
    [InlineData("Product", "OtherProduct")]
    public void EnvelopeMetadataCannotDowngradeOrCrossProducts(string field, string value)
    {
        var json = JsonNode.Parse(LicenseDocumentCodec.Sign(Payload("DEVICE"), _issuer))!;
        if (field == "SchemaVersion") json[field] = int.Parse(value, CultureInfo.InvariantCulture);
        else json[field] = value;
        LicenseDocumentCodec.TryRead(json.ToJsonString(), out _, out _).Should().BeFalse();
    }

    public void Dispose()
    {
        _issuer.Dispose();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
