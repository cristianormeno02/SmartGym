using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Domain.Entities.Activities;

namespace SmartGym.Application.Modules.Activities.Services;

public class RoomService : IRoomService
{
    private readonly ISmartGymDbContext _dbContext;

    public RoomService(ISmartGymDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<RoomDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Rooms
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new RoomDto(
                r.Id,
                r.Name,
                r.Description,
                r.Capacity,
                r.IsActive
            ))
            .ToListAsync(cancellationToken);
    }

    public async Task<RoomDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var room = await _dbContext.Rooms
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (room == null) return null;

        return new RoomDto(
            room.Id,
            room.Name,
            room.Description,
            room.Capacity,
            room.IsActive
        );
    }

    public async Task<RoomDto> CreateAsync(CreateRoomRequest request, CancellationToken cancellationToken = default)
    {
        var room = new Room
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            Capacity = request.Capacity,
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true
        };

        _dbContext.Rooms.Add(room);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new RoomDto(
            room.Id,
            room.Name,
            room.Description,
            room.Capacity,
            room.IsActive
        );
    }

    public async Task<RoomDto?> UpdateAsync(Guid id, UpdateRoomRequest request, CancellationToken cancellationToken = default)
    {
        var room = await _dbContext.Rooms.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (room == null) return null;

        room.Name = request.Name.Trim();
        room.Description = request.Description?.Trim();
        room.Capacity = request.Capacity;
        room.IsActive = request.IsActive;
        room.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new RoomDto(
            room.Id,
            room.Name,
            room.Description,
            room.Capacity,
            room.IsActive
        );
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var room = await _dbContext.Rooms.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (room == null) return false;

        room.IsActive = false;
        room.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }
}
