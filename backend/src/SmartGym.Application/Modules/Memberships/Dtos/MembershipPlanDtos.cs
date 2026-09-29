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
    bool IsActive
);

public record CreateMembershipPlanRequest(
    string Name,
    string? Description,
    int DurationDays,
    decimal Price,
    int Credits,
    bool IsUnlimited,
    bool AppliesToAllActivities,
    List<Guid>? AllowedActivityIds
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
    bool IsActive
);
