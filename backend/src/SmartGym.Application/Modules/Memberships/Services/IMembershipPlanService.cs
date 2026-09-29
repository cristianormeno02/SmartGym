using SmartGym.Application.Modules.Memberships.Dtos;

namespace SmartGym.Application.Modules.Memberships.Services;

public interface IMembershipPlanService
{
    Task<List<MembershipPlanDto>> GetAllAsync(bool onlyActive = false, CancellationToken cancellationToken = default);
    Task<MembershipPlanDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<MembershipPlanDto> CreateAsync(CreateMembershipPlanRequest request, CancellationToken cancellationToken = default);
    Task<MembershipPlanDto?> UpdateAsync(Guid id, UpdateMembershipPlanRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
