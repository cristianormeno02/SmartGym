using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Activities.Services;

public class ActivityService : IActivityService
{
    private readonly ISmartGymDbContext _dbContext;

    public ActivityService(ISmartGymDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<ActivityDto>> GetAllAsync(bool onlyEnabled = false, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Activities
            .Include(a => a.DefaultRoom)
            .AsNoTracking();

        if (onlyEnabled)
        {
            query = query.Where(a => a.IsActive && a.Status == ActivityStatus.Enabled);
        }

        var list = await query
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken);

        return list.Select(MapToDto).ToList();
    }

    public async Task<ActivityDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var activity = await _dbContext.Activities
            .Include(a => a.DefaultRoom)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        return activity == null ? null : MapToDto(activity);
    }

    public async Task<ActivityDto> CreateAsync(CreateActivityRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("El nombre de la actividad es requerido.", nameof(request.Name));
        }

        if (request.DefaultRoomId.HasValue)
        {
            var roomExists = await _dbContext.Rooms.AnyAsync(r => r.Id == request.DefaultRoomId.Value, cancellationToken);
            if (!roomExists)
            {
                throw new ArgumentException("La sala predeterminada especificada no existe.");
            }
        }

        var activity = new Activity
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Summary = request.Summary?.Trim(),
            Description = request.Description?.Trim(),
            LogoUrl = request.LogoUrl?.Trim(),
            ImageUrls = request.ImageUrls ?? new List<string>(),
            Status = request.Status,
            MinAge = request.MinAge,
            MaxAge = request.MaxAge,
            DefaultRoomId = request.DefaultRoomId,
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true
        };

        activity.SetCapacities(request.MinCapacity, request.MaxCapacity);

        _dbContext.Activities.Add(activity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (activity.DefaultRoomId.HasValue)
        {
            activity.DefaultRoom = await _dbContext.Rooms.FindAsync(new object[] { activity.DefaultRoomId.Value }, cancellationToken);
        }

        return MapToDto(activity);
    }

    public async Task<ActivityDto?> UpdateAsync(Guid id, UpdateActivityRequest request, CancellationToken cancellationToken = default)
    {
        var activity = await _dbContext.Activities
            .Include(a => a.DefaultRoom)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (activity == null) return null;

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("El nombre de la actividad no puede estar vacío.", nameof(request.Name));
        }

        if (request.DefaultRoomId.HasValue)
        {
            var roomExists = await _dbContext.Rooms.AnyAsync(r => r.Id == request.DefaultRoomId.Value, cancellationToken);
            if (!roomExists)
            {
                throw new ArgumentException("La sala predeterminada especificada no existe.");
            }
        }

        activity.Name = request.Name.Trim();
        activity.Summary = request.Summary?.Trim();
        activity.Description = request.Description?.Trim();
        activity.LogoUrl = request.LogoUrl?.Trim();
        activity.ImageUrls = request.ImageUrls ?? new List<string>();
        activity.Status = request.Status;
        activity.MinAge = request.MinAge;
        activity.MaxAge = request.MaxAge;
        activity.DefaultRoomId = request.DefaultRoomId;
        activity.IsActive = request.IsActive;
        activity.UpdatedAtUtc = DateTime.UtcNow;

        activity.SetCapacities(request.MinCapacity, request.MaxCapacity);

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (activity.DefaultRoomId.HasValue && (activity.DefaultRoom == null || activity.DefaultRoom.Id != activity.DefaultRoomId.Value))
        {
            activity.DefaultRoom = await _dbContext.Rooms.FindAsync(new object[] { activity.DefaultRoomId.Value }, cancellationToken);
        }

        return MapToDto(activity);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var activity = await _dbContext.Activities.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (activity == null) return false;

        activity.IsActive = false;
        activity.Status = ActivityStatus.Archived;
        activity.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static ActivityDto MapToDto(Activity a) =>
        new(
            a.Id,
            a.Name,
            a.Summary,
            a.Description,
            a.MinCapacity,
            a.MaxCapacity,
            a.LogoUrl,
            a.ImageUrls ?? new List<string>(),
            a.Status,
            a.MinAge,
            a.MaxAge,
            a.DefaultRoomId,
            a.DefaultRoom?.Name,
            a.IsActive
        );
}
