using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Memberships.Dtos;

public record MembershipDto(
    Guid Id,
    Guid StudentId,
    string StudentName,
    string StudentDni,
    Guid MembershipPlanId,
    string MembershipPlanName,
    DateOnly StartDate,
    DateOnly EndDate,
    int TotalCredits,
    int AvailableCredits,
    MembershipStatus Status,
    bool IsValidToday
);

public record AssignMembershipRequest(
    Guid StudentId,
    Guid MembershipPlanId,
    DateOnly? StartDate
);

public record CancelMembershipRequest(
    string Reason
);
