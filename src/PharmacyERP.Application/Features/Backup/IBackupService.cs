using PharmacyERP.Application.Common.Models;

namespace PharmacyERP.Application.Features.Backup;

/// <summary>
/// Full database backup/restore surface. Runs native SQL Server BACKUP
/// DATABASE / RESTORE DATABASE statements directly against the configured
/// connection string — no third-party backup library needed, and the
/// resulting .bak files are restorable with any standard SQL Server tooling
/// even outside this application, which matters for disaster recovery.
/// </summary>
public interface IBackupService
{
    /// <summary>Full path to the folder backups are written to and listed from (configurable in appsettings.json, defaults under ProgramData).</summary>
    string BackupFolder { get; }

    Task<List<BackupFileDto>> GetBackupHistoryAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs BACKUP DATABASE to a new timestamped .bak file in <see cref="BackupFolder"/> and returns its path.</summary>
    Task<Result<BackupFileDto>> CreateBackupAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores the database from the given .bak file. This requires exclusive access to the
    /// database (SINGLE_USER mode) — the caller must ensure the application is about to restart
    /// immediately afterward, since every other open connection (including this one) will be
    /// forcibly closed by SQL Server as part of the restore.
    /// </summary>
    Task<Result> RestoreBackupAsync(string backupFilePath, CancellationToken cancellationToken = default);

    Task<Result> DeleteBackupAsync(string backupFilePath, CancellationToken cancellationToken = default);
}
