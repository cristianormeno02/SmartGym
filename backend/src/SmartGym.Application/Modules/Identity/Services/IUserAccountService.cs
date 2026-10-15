using SmartGym.Application.Modules.Identity.Dtos;

namespace SmartGym.Application.Modules.Identity.Services;

public interface IUserAccountService
{
    Task<UserAccountDto> GetAccountAsync(Guid personId, CancellationToken cancellationToken = default);

    Task<UserAccountDto> ProvisionAsync(Guid personId, ProvisionUserAccountRequest request, CancellationToken cancellationToken = default);

    Task<UserAccountDto> UpdateRolesAsync(Guid personId, UpdateUserRolesRequest request, CancellationToken cancellationToken = default);

    Task<UserAccountDto> UpdateStatusAsync(Guid personId, UpdateUserStatusRequest request, CancellationToken cancellationToken = default);

    Task ResetPasswordAsync(Guid personId, AdminResetPasswordRequest request, CancellationToken cancellationToken = default);
}
