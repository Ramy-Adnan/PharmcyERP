using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Security.DTOs;

namespace PharmacyERP.Application.Features.Security;

/// <summary>Full CRUD for Users, including branch assignment and password administration by managers.</summary>
public interface IUserService
{
    Task<List<UserDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<UserUpsertDto?> GetForEditAsync(int id, CancellationToken cancellationToken = default);
    Task<Result<UserDto>> CreateAsync(UserUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result<UserDto>> UpdateAsync(UserUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result> SetStatusAsync(int userId, bool activate, CancellationToken cancellationToken = default);
    Task<Result> ResetPasswordAsync(int userId, string newPassword, CancellationToken cancellationToken = default);
    Task<Result> UnlockAsync(int userId, CancellationToken cancellationToken = default);
}
