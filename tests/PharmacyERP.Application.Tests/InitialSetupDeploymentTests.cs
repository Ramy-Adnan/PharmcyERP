using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Infrastructure.Identity;
using PharmacyERP.Infrastructure.Persistence.Seed;
using PharmacyERP.Infrastructure.Setup;
using PharmacyERP.WPF.ViewModels.SystemManagement;
using Xunit;

namespace PharmacyERP.Application.Tests;

public sealed class InitialSetupDeploymentTests
{
    [Fact]
    public void EnvironmentConnectionCannotBeRedirectedByWizardFieldsOrPasswordEdits()
    {
        const string environmentConnection = "Server=existing-server\\PHARMACY;Database=ExistingPharmacy;User ID=existing-user;Password=existing-password;Encrypt=False";
        var viewModel = new InitialSetupViewModel(environmentConnection, environmentOverride: true);
        viewModel.CanEditConnection.Should().BeFalse();
        viewModel.CanEditSqlCredentials.Should().BeFalse();
        // The view disables these editors, and this verifies the deeper guard
        // even if a caller or a future UI version changes the bound values.
        viewModel.Server = "different-server";
        viewModel.Database = "DifferentPharmacy";
        viewModel.SqlUsername = "different-user";
        viewModel.WindowsAuthentication = true;
        viewModel.TrustServerCertificate = true;
        var actual = new SqlConnectionStringBuilder(viewModel.BuildConnectionString("different-password"));
        actual.DataSource.Should().Be("existing-server\\PHARMACY");
        actual.InitialCatalog.Should().Be("ExistingPharmacy");
        actual.UserID.Should().Be("existing-user");
        actual.Password.Should().Be("existing-password");
        actual.IntegratedSecurity.Should().BeFalse();
        actual.Encrypt.Should().Be(SqlConnectionEncryptOption.Optional);
        viewModel.ForgetInitialSqlPassword();
        viewModel.InitialSqlPassword.Should().BeEmpty();
    }

    [Fact]
    public void EnvironmentTargetMayBeCreatedWithoutChangingTheConnectionUsedByMainHost()
    {
        const string environmentConnection = "Server=.\\SQLEXPRESS;Database=BrandNewPharmacy;Integrated Security=True;TrustServerCertificate=True";
        var viewModel = new InitialSetupViewModel(environmentConnection, environmentOverride: true);
        var before = viewModel.BuildConnectionString(string.Empty);
        viewModel.CreateNew = true;
        viewModel.Mode.Should().Be(DatabaseSetupMode.CreateNew);
        viewModel.UseExisting.Should().BeFalse();
        viewModel.BuildConnectionString("ignored").Should().Be(before);
        viewModel.CanEditConnection.Should().BeFalse();
        viewModel.UseExisting = true;
        viewModel.Mode.Should().Be(DatabaseSetupMode.UseExisting);
        viewModel.BuildConnectionString(string.Empty).Should().Be(before);
    }

    [Fact]
    public void FirstAdministratorPasswordCannotBeBlankShortOrSilentlyTruncatedByBCrypt()
    {
        foreach (var invalid in new[] { "        ", "short", new string('a', 73), new string('س', 37) })
        {
            var validate = () => InitialSetupValidation.ValidateAdministratorPassword(invalid);
            validate.Should().Throw<InvalidOperationException>();
        }
        InitialSetupValidation.ValidateAdministratorPassword(new string('a', 72));
        InitialSetupValidation.ValidateAdministratorPassword(new string('س', 36));
    }

    [Theory]
    [InlineData("master")]
    [InlineData("MODEL")]
    [InlineData("msdb")]
    [InlineData("tempdb")]
    public void SystemDatabasesCannotBeUsedForProvisioning(string database)
    {
        var act = () => SetupDatabaseService.ValidateConnectionString($"Server=.\\SQLEXPRESS;Database={database};Integrated Security=True");
        act.Should().Throw<InvalidOperationException>().WithMessage("*قاعدة بيانات نظام*");
    }

    [Fact]
    public void CreatingOverExistingDatabaseAndUsingForeignDataAreRejected()
    {
        var createOverExisting = () => SetupDatabaseService.ValidateSelection(new(true, true, false, true), DatabaseSetupMode.CreateNew);
        var foreignDatabase = () => SetupDatabaseService.ValidateSelection(new(true, false, false, false), DatabaseSetupMode.UseExisting);
        var missingDatabase = () => SetupDatabaseService.ValidateSelection(new(false, false, false, false), DatabaseSetupMode.UseExisting);
        createOverExisting.Should().Throw<InvalidOperationException>();
        foreignDatabase.Should().Throw<InvalidOperationException>();
        missingDatabase.Should().Throw<InvalidOperationException>();
        SetupDatabaseService.ValidateSelection(new(true, true, false, true), DatabaseSetupMode.UseExisting);
    }

    [Fact]
    public void DatabaseIdentifierEscapesClosingBracketsInsteadOfExecutingExtraStatements()
    {
        SetupDatabaseService.QuoteDatabaseIdentifier("Pharmacy]; DROP DATABASE [Other")
            .Should().Be("[Pharmacy]]; DROP DATABASE [Other]");
        var act = () => SetupDatabaseService.ValidateConnectionString("Server=.;Database=Pharmacy;Integrated Security=True;AttachDbFilename=C:\\not-allowed.mdf");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ProtectedSettingsRoundTripAndFailedReplacementKeepsPreviousWorkingConnection()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PharmacyERP-SetupTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "connection-settings.dat");
            using var protector = new TestProtector();
            var store = new ConnectionSettingsStore(path, protector);
            const string original = "Server=.\\SQLEXPRESS;Database=PharmacyERP;User ID=pharmacy;Password=private-sql-password";
            store.Save(original);
            var before = File.ReadAllBytes(path);
            System.Text.Encoding.UTF8.GetString(before).Should().NotContain("private-sql-password");
            store.Load().Should().Contain("PharmacyERP").And.Contain("private-sql-password");
            protector.FailProtect = true;
            var save = () => store.Save(original.Replace("PharmacyERP", "AnotherPharmacy", StringComparison.Ordinal));
            save.Should().Throw<CryptographicException>();
            File.ReadAllBytes(path).Should().Equal(before);
            Directory.GetFiles(directory, "*.tmp").Should().BeEmpty();
            protector.FailProtect = false;
            before[^1] ^= 1;
            File.WriteAllBytes(path, before);
            var load = () => store.Load();
            load.Should().Throw<InvalidOperationException>().Which.Message.Should().NotContain("private-sql-password");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task MissingFirstPasswordDoesNotPartiallySeedDatabase()
    {
        await using var db = TestDb.CreateContext(new FakeDateTime());
        var seeder = new ApplicationDbSeeder(db, new FakePasswordHasher(), new TestInitialPassword(null));
        var act = () => seeder.SeedAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();
        (await db.Permissions.CountAsync()).Should().Be(0);
        (await db.Branches.CountAsync()).Should().Be(0);
        (await db.Users.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task FirstAdministratorIsHashedAndExistingPasswordIsNeverReset()
    {
        await using var db = TestDb.CreateContext(new FakeDateTime());
        var hasher = new PasswordHasherService();
        var password = new TestInitialPassword("FirstSecurePassword-2026");
        await new ApplicationDbSeeder(db, hasher, password).SeedAsync();
        var original = await db.Users.SingleAsync();
        original.Username.Should().Be("admin");
        original.PasswordHash.Should().NotBe("FirstSecurePassword-2026");
        hasher.Verify("FirstSecurePassword-2026", original.PasswordHash).Should().BeTrue();
        password.Cleared.Should().BeTrue();
        var originalHash = original.PasswordHash;
        var second = new TestInitialPassword(null);
        await new ApplicationDbSeeder(db, hasher, second).SeedAsync();
        (await db.Users.SingleAsync()).PasswordHash.Should().Be(originalHash);
        (await db.Users.CountAsync()).Should().Be(1);
        second.ReadCount.Should().Be(0, "existing installations do not need or replace an initial password");
    }

    private sealed class TestInitialPassword(string? password) : IInitialAdminPasswordProvider
    {
        public bool Cleared { get; private set; }
        public int ReadCount { get; private set; }
        public string? GetPassword() { ReadCount++; return password; }
        public void Clear() { Cleared = true; password = null; }
    }

    private sealed class TestProtector : ISetupSecretProtector, IDisposable
    {
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
        public bool FailProtect { get; set; }
        public byte[] Protect(byte[] data)
        {
            if (FailProtect) throw new CryptographicException("Protection failed.");
            var result = new byte[12 + 16 + data.Length];
            RandomNumberGenerator.Fill(result.AsSpan(0, 12));
            using var cipher = new AesGcm(_key, 16);
            cipher.Encrypt(result.AsSpan(0, 12), data, result.AsSpan(28), result.AsSpan(12, 16));
            return result;
        }
        public byte[] Unprotect(byte[] data)
        {
            var result = new byte[data.Length - 28];
            using var cipher = new AesGcm(_key, 16);
            cipher.Decrypt(data.AsSpan(0, 12), data.AsSpan(28), data.AsSpan(12, 16), result);
            return result;
        }
        public void Dispose() => CryptographicOperations.ZeroMemory(_key);
    }
}
