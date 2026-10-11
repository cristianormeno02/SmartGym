using SmartGym.Application.Common.Models;
using SmartGym.Application.Modules.Memberships.Dtos;

namespace SmartGym.Application.Modules.Memberships.Services;

public interface IMembershipService
{
    Task<PagedResult<MembershipListItemDto>> GetPagedAsync(
        MembershipListFilter filter,
        CancellationToken cancellationToken = default);

    Task<MembershipDetailDto> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MembershipSummaryDto>> GetByStudentIdAsync(
        Guid studentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MembershipSummaryDto>> GetCurrentByStudentIdAsync(
        Guid studentId,
        CancellationToken cancellationToken = default);

    Task<MembershipSummaryDto?> GetActiveMembershipAsync(
        Guid studentId,
        DateOnly? date = null,
        CancellationToken cancellationToken = default);

    Task<MembershipDetailDto> CreateAsync(
        CreateMembershipRequest request,
        Guid? currentUserId = null,
        bool isCallerAdministrator = false,
        CancellationToken cancellationToken = default);

    Task<MembershipDetailDto> RenewAsync(
        Guid membershipId,
        RenewMembershipRequest request,
        Guid? currentUserId = null,
        bool isCallerAdministrator = false,
        CancellationToken cancellationToken = default);

    Task<MembershipDetailDto> ActivateAsync(
        Guid membershipId,
        ActivateMembershipRequest request,
        Guid? currentUserId = null,
        bool isCallerAdministrator = false,
        CancellationToken cancellationToken = default);

    Task<MembershipDetailDto> SuspendAsync(
        Guid membershipId,
        SuspendMembershipRequest request,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<MembershipDetailDto> ReactivateAsync(
        Guid membershipId,
        ReactivateMembershipRequest request,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<MembershipDetailDto> CancelAsync(
        Guid membershipId,
        CancelMembershipRequest request,
        Guid? currentUserId = null,
        CancellationToken cancellationToken = default);

    Task<MembershipDetailDto> UpdateNotesAsync(
        Guid membershipId,
        UpdateMembershipNotesRequest request,
        Guid? currentUserId = null,
        CancellationToken cancellationToken = default);

    Task<MembershipExpirationBatchResultDto> ProcessExpirationsAndWarningsAsync(
        CancellationToken cancellationToken = default);
}
