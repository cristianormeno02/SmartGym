using SmartGym.Application.Modules.Activities.Dtos;

namespace SmartGym.Application.Modules.Activities.Services;

public interface IRoomService
{
    Task<List<RoomDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<RoomDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<RoomDto> CreateAsync(CreateRoomRequest request, CancellationToken cancellationToken = default);
    Task<RoomDto?> UpdateAsync(Guid id, UpdateRoomRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
