namespace PharmacyERP.Application.Features.Licensing;

/// <summary>
/// A narrow bootstrap scope for migrations/seeding. Normal application flows are
/// enforced by default; bootstrap permission does not leak into another async flow.
/// </summary>
public sealed class LicenseRuntimeState
{
    private readonly AsyncLocal<int> _bootstrapDepth = new();
    public bool IsBootstrapping => _bootstrapDepth.Value > 0;

    public IDisposable BeginBootstrap()
    {
        _bootstrapDepth.Value++;
        return new BootstrapScope(this);
    }

    private sealed class BootstrapScope : IDisposable
    {
        private LicenseRuntimeState? _owner;
        public BootstrapScope(LicenseRuntimeState owner) => _owner = owner;
        public void Dispose()
        {
            if (_owner is null) return;
            _owner._bootstrapDepth.Value = Math.Max(0, _owner._bootstrapDepth.Value - 1);
            _owner = null;
        }
    }
}
