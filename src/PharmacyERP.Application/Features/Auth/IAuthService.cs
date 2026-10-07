using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Auth.DTOs;

namespace PharmacyERP.Application.Features.Auth;

/// <summary>
/// Orchestrates the full login workflow: credential verification, lockout
/// policy enforcement, branch resolution, permission aggregation and
/// login-history logging. Implemented in Infrastructure since it needs
/// direct data access; the WPF layer only ever talks to this interface.
/// </summary>
public interface IAuthService
{
    Task<Result<LoginResultDto>> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default);
    Task LogoutAsync(int userId, CancellationToken cancellationToken = default);
    Task<Result> ChangePasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default);
}
