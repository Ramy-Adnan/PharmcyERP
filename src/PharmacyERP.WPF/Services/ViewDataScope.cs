using Microsoft.Extensions.DependencyInjection;

namespace PharmacyERP.WPF.Services;

/// <summary>
/// Owns the scoped services of one screen. Concurrent screens must not share
/// a DbContext; services within one screen still share its transaction context.
/// </summary>
public sealed class ViewDataScope : IDisposable
{
    private readonly IServiceScope _scope;

    public ViewDataScope(IServiceScopeFactory scopeFactory) => _scope = scopeFactory.CreateScope();

    public IServiceProvider Services => _scope.ServiceProvider;

    public void Dispose() => _scope.Dispose();
}
