using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Backup;
using PharmacyERP.Infrastructure.Services;
using Xunit;

namespace PharmacyERP.Application.Tests;

public sealed class BackupDeploymentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PharmacyERP-backup-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly string ImageName = new string('A', 64) + ".png";
    private static readonly byte[] Png = { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0 };

    public BackupDeploymentTests() => Directory.CreateDirectory(_root);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    [Fact]
    public void Backup_QuotesIdentifiersAndDoesNotRequireExpressCompression()
    {
        var sql = BackupSql.BackupCommand("Pharmacy]ERP");
        sql.Should().Contain("[Pharmacy]]ERP]").And.Contain("@path").And.Contain("COPY_ONLY").And.Contain("CHECKSUM").And.Contain("NO_COMPRESSION");
        sql.Should().NotContain("FORMAT");
    }

    [Theory]
    [InlineData("")]
    [InlineData("master")]
    [InlineData("MSDB")]
    [InlineData("Pharmacy\nERP")]
    public void SystemOrMalformedDatabaseIdentifiersAreRejected(string name) =>
        FluentActions.Invoking(() => BackupSql.Identifier(name)).Should().Throw<ArgumentException>();

    [Fact]
    public void RestoreUsesDestinationFileParametersAndChecksSourceBeforeReplacement()
    {
        BackupSql.RestoreCommand("PharmacyERP", 2, 3).Should().Contain("FILE = 2").And.Contain("MOVE @logical0 TO @target0").And.Contain("MOVE @logical2 TO @target2");
        FluentActions.Invoking(() => BackupSql.ValidateBackupSource("PharmacyERP", "OtherPharmacy", 1, false)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => BackupSql.ValidateBackupSource("PharmacyERP", "PharmacyERP", 2, false)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => BackupSql.ValidateBackupSource("PharmacyERP", "PharmacyERP", 1, true)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RestorePathsUseServerSeparatorsRatherThanTheApplicationOperatingSystem()
    {
        BackupSql.CombineServerPath(@"D:\SqlData", "Pharmacy.mdf").Should().Be(@"D:\SqlData\Pharmacy.mdf");
        BackupSql.CombineServerPath("/var/opt/mssql/data/", "Pharmacy.mdf").Should().Be("/var/opt/mssql/data/Pharmacy.mdf");
        BackupSql.SafeFileStem("Pharmacy/..:ERP").Should().NotContain("/").And.NotContain(":").And.NotContain("..");
    }

    [Fact]
    public async Task BundleRoundTripPreservesDatabaseBrandingAndPhotosButNotMachineSecrets()
    {
        var backup = CreateBackupFake();
        var local = Path.Combine(_root, "local");
        Directory.CreateDirectory(Path.Combine(local, "product-images"));
        await File.WriteAllTextAsync(Path.Combine(local, "receipt-settings.json"), "{\"PharmacyName\":\"صيدلية الاختبار\"}");
        await File.WriteAllBytesAsync(Path.Combine(local, "product-images", ImageName), Png);
        await File.WriteAllTextAsync(Path.Combine(local, "invoice-ai-settings.dat"), "encrypted secret");
        await File.WriteAllTextAsync(Path.Combine(local, "license.lic"), "customer license");
        await File.WriteAllTextAsync(Path.Combine(local, "machine.id"), "device id");
        var service = new BackupDataTransferService(backup, "PharmacyERP", local);
        var bundle = Path.Combine(_root, "pharmacy.zip");

        var exported = await service.ExportBundleAsync(bundle);
        exported.Succeeded.Should().BeTrue();
        var preview = await service.InspectBundleAsync(bundle);
        preview.Succeeded.Should().BeTrue();
        preview.Value!.PharmacyName.Should().Be("صيدلية الاختبار");
        preview.Value.ProductImageCount.Should().Be(1);
        using (var archive = ZipFile.OpenRead(bundle))
            archive.Entries.Select(e => e.FullName).Should().BeEquivalentTo("manifest.json", "database.bak", "receipt-settings.json", "product-images/" + ImageName);

        var extracted = Path.Combine(_root, "extracted");
        await BackupBundleArchive.ExtractAsync(bundle, "PharmacyERP", extracted);
        (await File.ReadAllBytesAsync(Path.Combine(extracted, "product-images", ImageName))).Should().Equal(Png);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("product-images/../../outside.txt")]
    [InlineData("C:/outside.txt")]
    [InlineData("license.lic")]
    [InlineData("invoice-ai-settings.dat")]
    [InlineData("product-images/unknown.exe")]
    public async Task ZipSlipSecretsAndExecutablesAreRejectedBeforeExtraction(string badPath)
    {
        var bundle = Path.Combine(_root, "bad.zip");
        await MakeArchiveAsync(bundle, new Dictionary<string, byte[]>
            { ["database.bak"] = Encoding.UTF8.GetBytes("database"), [badPath] = Encoding.UTF8.GetBytes("bad") });
        var extraction = Path.Combine(_root, "stage");
        var action = () => BackupBundleArchive.ExtractAsync(bundle, "PharmacyERP", extraction);
        await action.Should().ThrowAsync<InvalidDataException>();
        File.Exists(Path.Combine(_root, "outside.txt")).Should().BeFalse();
        if (Directory.Exists(extraction)) Directory.EnumerateFiles(extraction, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task TamperedPayloadFailsHashValidation()
    {
        var bundle = Path.Combine(_root, "tampered.zip");
        var original = Encoding.UTF8.GetBytes("original");
        await MakeArchiveAsync(bundle, new Dictionary<string, byte[]> { ["database.bak"] = original });
        using (var archive = ZipFile.Open(bundle, ZipArchiveMode.Update))
        {
            archive.GetEntry("database.bak")!.Delete();
            await using var stream = archive.CreateEntry("database.bak").Open();
            await stream.WriteAsync(Encoding.UTF8.GetBytes("modified"));
        }
        var action = () => BackupBundleArchive.ExtractAsync(bundle, "PharmacyERP", Path.Combine(_root, "stage"));
        await action.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task BundleForAnotherDatabaseIsRejectedBeforeAnyBackupOrRestore()
    {
        var bundle = Path.Combine(_root, "another.zip");
        await MakeArchiveAsync(bundle, new Dictionary<string, byte[]> { ["database.bak"] = Encoding.UTF8.GetBytes("database") }, "OtherPharmacy");
        var backup = CreateBackupFake();
        var service = new BackupDataTransferService(backup, "PharmacyERP", Path.Combine(_root, "local"));
        var imported = await service.ImportBundleAsync(bundle);
        imported.Succeeded.Should().BeFalse();
        backup.Created.Should().Be(0);
        backup.Restored.Should().BeEmpty();
    }

    [Fact]
    public async Task CaseCollidingArchiveNamesAreRejected()
    {
        var bundle = Path.Combine(_root, "duplicate.zip");
        await MakeArchiveAsync(bundle, new Dictionary<string, byte[]>
            { ["database.bak"] = Encoding.UTF8.GetBytes("database"), ["DATABASE.BAK"] = Encoding.UTF8.GetBytes("another") });
        var action = () => BackupBundleArchive.InspectAsync(bundle, "PharmacyERP");
        await action.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task ImportStopsIfSafetyBackupCannotBeCreated()
    {
        var bundle = Path.Combine(_root, "valid.zip");
        await MakeArchiveAsync(bundle, new Dictionary<string, byte[]> { ["database.bak"] = Encoding.UTF8.GetBytes("database") });
        var backup = CreateBackupFake();
        backup.FailBackup = true;
        var service = new BackupDataTransferService(backup, "PharmacyERP", Path.Combine(_root, "local"));
        var imported = await service.ImportBundleAsync(bundle);
        imported.Succeeded.Should().BeFalse();
        backup.Restored.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportPreservesDeviceSecretsAndKeepsPreviousBrandingForRecovery()
    {
        var bundle = Path.Combine(_root, "valid.zip");
        await MakeArchiveAsync(bundle, new Dictionary<string, byte[]>
        {
            ["database.bak"] = Encoding.UTF8.GetBytes("database"),
            ["receipt-settings.json"] = Encoding.UTF8.GetBytes("{\"PharmacyName\":\"New pharmacy\"}"),
            ["product-images/" + ImageName] = Png
        });
        var local = Path.Combine(_root, "local");
        Directory.CreateDirectory(Path.Combine(local, "product-images"));
        await File.WriteAllTextAsync(Path.Combine(local, "receipt-settings.json"), "{\"PharmacyName\":\"Old pharmacy\"}");
        await File.WriteAllTextAsync(Path.Combine(local, "license.lic"), "local license");
        await File.WriteAllTextAsync(Path.Combine(local, "invoice-ai-settings.dat"), "local encrypted key");
        var backup = CreateBackupFake();
        var service = new BackupDataTransferService(backup, "PharmacyERP", local);

        var imported = await service.ImportBundleAsync(bundle);
        imported.Succeeded.Should().BeTrue();
        backup.Created.Should().Be(1);
        backup.Restored.Should().ContainSingle();
        (await File.ReadAllTextAsync(Path.Combine(local, "receipt-settings.json"))).Should().Contain("New pharmacy");
        (await File.ReadAllTextAsync(Path.Combine(local, "license.lic"))).Should().Be("local license");
        (await File.ReadAllTextAsync(Path.Combine(local, "invoice-ai-settings.dat"))).Should().Be("local encrypted key");
        var previous = Directory.EnumerateDirectories(local, ".transfer-before-*").Single();
        (await File.ReadAllTextAsync(Path.Combine(previous, "receipt-settings.json"))).Should().Contain("Old pharmacy");
        Directory.EnumerateDirectories(backup.BackupFolder, "transfer-staging-*").Should().BeEmpty();
    }

    [Fact]
    public async Task FailedDatabaseRestoreAttemptsSafetyRollbackAndLeavesLocalBrandingUntouched()
    {
        var bundle = Path.Combine(_root, "valid.zip");
        await MakeArchiveAsync(bundle, new Dictionary<string, byte[]> { ["database.bak"] = Encoding.UTF8.GetBytes("database") });
        var local = Path.Combine(_root, "local");
        Directory.CreateDirectory(local);
        await File.WriteAllTextAsync(Path.Combine(local, "receipt-settings.json"), "{\"PharmacyName\":\"Old pharmacy\"}");
        var backup = CreateBackupFake();
        backup.FailFirstRestore = true;
        var service = new BackupDataTransferService(backup, "PharmacyERP", local);

        var imported = await service.ImportBundleAsync(bundle);
        imported.Succeeded.Should().BeFalse();
        backup.Restored.Should().HaveCount(2);
        backup.Restored[1].Should().EndWith("safety.bak");
        (await File.ReadAllTextAsync(Path.Combine(local, "receipt-settings.json"))).Should().Contain("Old pharmacy");
        imported.Errors.Should().Contain(e => e.Contains("استُعيدت نسخة الأمان"));
    }

    [Fact]
    public async Task MalformedReceiptCannotBeInstalledOrTriggerDatabaseReplacement()
    {
        var bundle = Path.Combine(_root, "receipt-bad.zip");
        await MakeArchiveAsync(bundle, new Dictionary<string, byte[]>
        {
            ["database.bak"] = Encoding.UTF8.GetBytes("database"),
            ["receipt-settings.json"] = Encoding.UTF8.GetBytes("{\"PharmacyName\":null}")
        });
        var backup = CreateBackupFake();
        var service = new BackupDataTransferService(backup, "PharmacyERP", Path.Combine(_root, "local"));
        (await service.ImportBundleAsync(bundle)).Succeeded.Should().BeFalse();
        backup.Created.Should().Be(0);
        backup.Restored.Should().BeEmpty();
    }

    private FakeBackup CreateBackupFake()
    {
        var folder = Path.Combine(_root, "backups");
        Directory.CreateDirectory(folder);
        return new FakeBackup(folder);
    }

    private static async Task MakeArchiveAsync(string path, Dictionary<string, byte[]> payloads, string databaseName = "PharmacyERP")
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, bytes) in payloads)
        {
            await using var stream = archive.CreateEntry(name).Open();
            await stream.WriteAsync(bytes);
        }
        var manifest = new BackupBundleManifest
        {
            DatabaseName = databaseName, PharmacyName = "Test pharmacy", CreatedAtUtc = DateTime.UtcNow,
            Files = payloads.Select(p => new BackupBundleEntry
                { Path = p.Key, Length = p.Value.Length, Sha256 = Convert.ToHexString(SHA256.HashData(p.Value)) }).ToList()
        };
        await using var manifestStream = archive.CreateEntry("manifest.json").Open();
        await JsonSerializer.SerializeAsync(manifestStream, manifest);
    }

    private sealed class FakeBackup(string folder) : IBackupService
    {
        public string BackupFolder => folder;
        public int Created { get; private set; }
        public bool FailBackup { get; set; }
        public bool FailFirstRestore { get; set; }
        public List<string> Restored { get; } = new();
        public Task<List<BackupFileDto>> GetBackupHistoryAsync(CancellationToken cancellationToken = default) => Task.FromResult(new List<BackupFileDto>());
        public async Task<Result<BackupFileDto>> CreateBackupAsync(CancellationToken cancellationToken = default)
        {
            Created++;
            if (FailBackup) return Result<BackupFileDto>.Failure("SQL permission denied");
            var path = Path.Combine(folder, "safety.bak");
            await File.WriteAllTextAsync(path, "safety", cancellationToken);
            return Result<BackupFileDto>.Success(new BackupFileDto { FullPath = path, FileName = "safety.bak" });
        }
        public Task<Result> RestoreBackupAsync(string path, CancellationToken cancellationToken = default)
        {
            Restored.Add(path);
            return Task.FromResult(FailFirstRestore && Restored.Count == 1 ? Result.Failure("SQL restore failed") : Result.Success());
        }
        public Task<Result> DeleteBackupAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
    }
}
