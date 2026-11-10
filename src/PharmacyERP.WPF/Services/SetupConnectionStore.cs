using System.IO;
using PharmacyERP.Infrastructure.Setup;

namespace PharmacyERP.WPF.Services;

public sealed class SetupConnectionStore : ConnectionSettingsStore
{
    public SetupConnectionStore() : base(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PharmacyERP", "connection-settings.dat"), new WindowsSetupSecretProtector()) { }
}
