using SmartGym.Application.Modules.Memberships.Dtos;

namespace SmartGym.Application.Modules.Memberships.Services;

public interface IMembershipService
{
    Task<MembershipDto> AssignMembershipAsync(AssignMembershipRequest request, CancellationToken cancellationToken = default);
    Task<List<MembershipDto>> GetByStudentIdAsync(Guid studentId, CancellationToken cancellationToken = default);
    Task<MembershipDto?> GetActiveMembershipAsync(Guid studentId, DateOnly? date = null, CancellationToken cancellationToken = default);
    Task<MembershipDto?> CancelMembershipAsync(Guid membershipId, string reason, CancellationToken cancellationToken = default);
}
