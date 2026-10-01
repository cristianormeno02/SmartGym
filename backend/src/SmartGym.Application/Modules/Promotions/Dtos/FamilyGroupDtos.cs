using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Promotions.Dtos;

public record FamilyGroupMemberDto(
    Guid PersonId,
    string FullName,
    string Dni,
    string Relationship,
    DateTime JoinedAtUtc,
    DocumentType? DocumentType = null,
    string? DocumentNumber = null
);

public record FamilyGroupDto(
    Guid Id,
    string Name,
    Guid PrimaryContactId,
    string PrimaryContactName,
    string PrimaryContactEmail,
    List<FamilyGroupMemberDto> Members,
    bool IsActive
);

public record CreateFamilyGroupRequest(
    string Name,
    Guid PrimaryContactId,
    string? PrimaryContactRelationship
);

public record AddFamilyMemberRequest(
    Guid PersonId,
    string Relationship
);
