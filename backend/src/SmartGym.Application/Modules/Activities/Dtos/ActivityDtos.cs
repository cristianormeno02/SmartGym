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

public record ActivityDto(
    Guid Id,
    string Name,
    string? Summary,
    string? Description,
    int MinCapacity,
    int MaxCapacity,
    string? LogoUrl,
    List<string> ImageUrls,
    ActivityStatus Status,
    int? MinAge,
    int? MaxAge,
    Guid? DefaultRoomId,
    string? DefaultRoomName,
    bool IsActive
);

public record CreateActivityRequest(
    string Name,
    string? Summary,
    string? Description,
    int MinCapacity,
    int MaxCapacity,
    string? LogoUrl,
    List<string>? ImageUrls,
    ActivityStatus Status,
    int? MinAge,
    int? MaxAge,
    Guid? DefaultRoomId
);

public record UpdateActivityRequest(
    string Name,
    string? Summary,
    string? Description,
    int MinCapacity,
    int MaxCapacity,
    string? LogoUrl,
    List<string>? ImageUrls,
    ActivityStatus Status,
    int? MinAge,
    int? MaxAge,
    Guid? DefaultRoomId,
    bool IsActive
);
