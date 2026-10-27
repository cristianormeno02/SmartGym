namespace SmartGym.Application.Modules.Memberships.Dtos;

public record MembershipPlanDto(
    Guid Id,
    string Name,
    string? Description,
    int DurationDays,
    decimal Price,
    int Credits,
    bool IsUnlimited,
    bool AppliesToAllActivities,
    List<Guid> AllowedActivityIds,
    List<string> AllowedActivityNames,
    bool IsActive,
    bool IsSingleClassPass = false
);

public record CreateMembershipPlanRequest(
    string Name,
    string? Description,
    int DurationDays,
    decimal Price,
    int Credits,
    bool IsUnlimited,
    bool AppliesToAllActivities,
    List<Guid>? AllowedActivityIds,
    bool IsSingleClassPass = false
);

public record UpdateMembershipPlanRequest(
    string Name,
    string? Description,
    int DurationDays,
    decimal Price,
    int Credits,
    bool IsUnlimited,
    bool AppliesToAllActivities,
    List<Guid>? AllowedActivityIds,
    bool IsActive,
    bool IsSingleClassPass = false
);
