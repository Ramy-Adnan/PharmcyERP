using Microsoft.Extensions.DependencyInjection;

namespace PharmacyERP.Application;

/// <summary>Registers all Application-layer services. Called once from the WPF composition root.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // FluentValidation validators and AutoMapper/Mapster profiles for later
        // phases register themselves here via assembly scanning. Kept explicit
        // and minimal in Phase 0 since Auth is the only feature implemented so far.
        return services;
    }
}
