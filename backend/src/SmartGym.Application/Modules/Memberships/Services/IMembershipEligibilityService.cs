using SmartGym.Application.Modules.Memberships.Dtos;

namespace SmartGym.Application.Modules.Memberships.Services;

public interface IMembershipEligibilityService
{
    Task<MembershipEligibilityDto> EvaluateEligibilityAsync(
        Guid membershipId,
        DateOnly? targetDate = null,
        Guid? activityId = null,
        Guid? classSessionId = null,
        CancellationToken cancellationToken = default);

    Task<MembershipSelectionResultDto> SelectMembershipForAsync(
        Guid studentId,
        Guid? activityId = null,
        DateOnly? targetDate = null,
        Guid? explicitMembershipId = null,
        Guid? classSessionId = null,
        CancellationToken cancellationToken = default);
}
