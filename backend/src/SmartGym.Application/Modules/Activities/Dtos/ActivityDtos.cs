using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Activities.Dtos;

public record RoomDto(
    Guid Id,
    string Name,
    string? Description,
    int Capacity,
    bool IsActive
);

public record CreateRoomRequest(
    string Name,
    string? Description,
    int Capacity
);

public record UpdateRoomRequest(
    string Name,
    string? Description,
    int Capacity,
    bool IsActive
);

public record ActivityListItemDto(
    Guid Id,
    string Code,
    string Name,
    string? ShortDescription,
    string? ColorHex,
    int? DefaultCapacity,
    int? MinAge,
    int? MaxAge,
    ActivityStatus Status,
    string? LogoUrl,
    string? PrimaryImageUrl,
    DateTime CreatedAtUtc
);

public record ActivityDetailDto(
    Guid Id,
    string Code,
    string Name,
    string? ShortDescription,
    string? Description,
    string? EquipmentNotes,
    string? ColorHex,
    int? DefaultCapacity,
    int? MinAge,
    int? MaxAge,
    ActivityStatus Status,
    uint Version,
    List<ActivityMediaDto> Media,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc
);

public record PublicActivityDto(
    string Code,
    string Name,
    string? ShortDescription,
    string? Description,
    string? EquipmentNotes,
    string? ColorHex,
    int? MinAge,
    int? MaxAge,
    string? LogoUrl,
    string? PrimaryImageUrl,
    List<string> GalleryImageUrls
);

public record ActivityMediaDto(
    Guid Id,
    ActivityMediaType Type,
    string Url,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    int Width,
    int Height,
    int SortOrder,
    bool IsPrimary,
    DateTime CreatedAtUtc
);

public record CreateActivityRequest(
    string Code,
    string Name,
    string? ShortDescription,
    string? Description,
    string? EquipmentNotes,
    string? ColorHex,
    int? DefaultCapacity,
    int? MinAge,
    int? MaxAge
);

public record UpdateActivityRequest(
    string Name,
    string? ShortDescription,
    string? Description,
    string? EquipmentNotes,
    string? ColorHex,
    int? DefaultCapacity,
    int? MinAge,
    int? MaxAge,
    uint Version
);

public record ChangeActivityStatusRequest(
    ActivityStatus Status,
    uint Version
);

public record SortActivityMediaRequest(
    List<Guid> MediaIds
);

public record ActivityDependenciesDto(
    int ActiveSchedules,
    int FutureSessions,
    int TotalSchedules,
    int TotalSessions,
    int MembershipPlans
);
