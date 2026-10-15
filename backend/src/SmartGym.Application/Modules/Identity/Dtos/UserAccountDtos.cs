namespace SmartGym.Application.Modules.Identity.Dtos;

public record UserAccountDto(
    bool HasAccount,
    Guid? UserId,
    string? Username,
    bool? IsActive,
    DateTime? LastLoginUtc,
    List<string> Roles
);

public record ProvisionUserAccountRequest(
    string Password,
    List<int>? RoleIds
);

public record UpdateUserRolesRequest(
    List<int>? RoleIds
);

public record UpdateUserStatusRequest(
    bool IsActive
);

public record AdminResetPasswordRequest(
    string NewPassword
);
