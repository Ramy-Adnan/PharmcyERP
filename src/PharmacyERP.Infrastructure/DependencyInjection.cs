using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Auth;
using PharmacyERP.Application.Features.Accounting;
using PharmacyERP.Application.Features.Backup;
using PharmacyERP.Application.Features.Branches;
using PharmacyERP.Application.Features.Insurance;
using PharmacyERP.Application.Features.Inventory;
using PharmacyERP.Application.Features.Hr;
using PharmacyERP.Application.Features.Licensing;
using PharmacyERP.Application.Features.Prescriptions;
using PharmacyERP.Application.Features.Purchasing;
using PharmacyERP.Application.Features.Reports;
using PharmacyERP.Application.Features.Sales;
using PharmacyERP.Application.Features.Security;
using PharmacyERP.Application.Features.Updates;
using PharmacyERP.Infrastructure.Identity;
using PharmacyERP.Infrastructure.Persistence;
using PharmacyERP.Infrastructure.Persistence.Interceptors;
using PharmacyERP.Infrastructure.Persistence.Seed;
using PharmacyERP.Infrastructure.Services;

namespace PharmacyERP.Infrastructure;

/// <summary>Registers every Infrastructure-layer service. Called once from the WPF composition root (App.xaml.cs).</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found in appsettings.json.");

        services.AddSingleton<AuditableEntitySaveChangesInterceptor>();

        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                sqlOptions.EnableRetryOnFailure(maxRetryCount: 3);
            });
        });

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        // CurrentUserService holds the live session for the process lifetime.
        services.AddSingleton<ICurrentUserService, CurrentUserService>();
        services.AddSingleton<IDateTime, DateTimeService>();
        services.AddSingleton<IPasswordHasher, PasswordHasherService>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IBranchService, BranchService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IPurchasingService, PurchasingService>();
        services.AddScoped<ISalesService, SalesService>();
        services.AddScoped<IPrescriptionService, PrescriptionService>();
        services.AddScoped<IInsuranceService, InsuranceService>();
        services.AddScoped<IAccountingService, AccountingService>();
        services.AddScoped<IReportingService, ReportingService>();
        services.AddScoped<IHrService, HrService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<ILicenseService, LicenseService>();
        services.AddScoped<ApplicationDbSeeder>();

        return services;
    }
}
