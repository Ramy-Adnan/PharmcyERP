using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Infrastructure.Persistence.Interceptors;

namespace PharmacyERP.Infrastructure.Persistence;

/// <summary>
/// Lets the EF Core tooling (Add-Migration / Update-Database / dotnet ef) construct an
/// <see cref="ApplicationDbContext"/> at design time.
///
/// This is required because the startup project is a WPF app: EF's tooling normally
/// discovers the context by locating a generic host builder (the ASP.NET Core
/// Program.CreateHostBuilder convention), but this application builds its host inside
/// App.OnStartup, which the tooling cannot invoke. Without this factory, every
/// Add-Migration call fails with "Unable to create an object of type 'ApplicationDbContext'".
///
/// Only used by tooling — it is never part of the running application's DI graph.
/// </summary>
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = ResolveConnectionString();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .Options;

        // The audit interceptor requires a session and a clock. At design time there is no
        // logged-in user, so these stubs simply satisfy the constructor — no audit rows are
        // ever written during migration scaffolding.
        var interceptor = new AuditableEntitySaveChangesInterceptor(
            new DesignTimeCurrentUserService(), new DesignTimeDateTime());

        return new ApplicationDbContext(options, interceptor);
    }

    /// <summary>
    /// Reads the same connection string the application uses, by walking up from the
    /// tooling's working directory to find the WPF project's appsettings.json. Falls back to
    /// a local default so that `Add-Migration` (which never opens a connection) still works
    /// even if the file cannot be located.
    /// </summary>
    private static string ResolveConnectionString()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "PharmacyERP.WPF", "appsettings.json");
            if (File.Exists(candidate))
            {
                var configuration = new ConfigurationBuilder()
                    .AddJsonFile(candidate, optional: false)
                    .Build();

                var fromFile = configuration.GetConnectionString("DefaultConnection");
                if (!string.IsNullOrWhiteSpace(fromFile)) return fromFile;
            }

            directory = directory.Parent;
        }

        return "Server=.\\RAMY;Database=PharmacyERP;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";
    }

    private class DesignTimeDateTime : IDateTime
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }

    private class DesignTimeCurrentUserService : ICurrentUserService
    {
        public int? UserId => null;
        public string? UserName => null;
        public int? CurrentBranchId => null;
        public IReadOnlyCollection<string> Permissions => Array.Empty<string>();

        public bool HasPermission(string permissionCode) => false;
        public void SetSession(int userId, string userName, int branchId, IEnumerable<string> permissions) { }
        public void ClearSession() { }
    }
}
