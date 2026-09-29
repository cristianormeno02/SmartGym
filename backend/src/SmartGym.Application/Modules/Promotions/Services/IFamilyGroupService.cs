using SmartGym.Application.Modules.Promotions.Dtos;

namespace SmartGym.Application.Modules.Promotions.Services;

public interface IFamilyGroupService
{
    Task<List<FamilyGroupDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<FamilyGroupDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<FamilyGroupDto?> GetByPersonIdAsync(Guid personId, CancellationToken cancellationToken = default);
    Task<FamilyGroupDto> CreateAsync(CreateFamilyGroupRequest request, CancellationToken cancellationToken = default);
    Task<FamilyGroupDto?> AddMemberAsync(Guid groupId, AddFamilyMemberRequest request, CancellationToken cancellationToken = default);
    Task<bool> RemoveMemberAsync(Guid groupId, Guid personId, CancellationToken cancellationToken = default);
}
