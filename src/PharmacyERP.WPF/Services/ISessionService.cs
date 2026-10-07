using PharmacyERP.Application.Features.Auth.DTOs;

namespace PharmacyERP.WPF.Services;

/// <summary>
/// Holds the logged-in user's data for the WPF UI layer (display name, role,
/// current branch, menu visibility) — distinct from ICurrentUserService which
/// is the Application-layer's view of the session used for audit stamping.
/// </summary>
public interface ISessionService
{
    bool IsAuthenticated { get; }
    LoginResultDto? CurrentSession { get; }

    void Start(LoginResultDto session);
    void End();
}
