namespace PharmacyERP.WPF.Services;

/// <summary>
/// Lets ViewModels switch the application's top-level window/content
/// (Login -> Main Shell, and later between Shell modules) without holding a
/// direct reference to Window objects, keeping the MVVM separation intact.
/// </summary>
public interface INavigationService
{
    void ShowLogin();
    void ShowMainShell();
}
