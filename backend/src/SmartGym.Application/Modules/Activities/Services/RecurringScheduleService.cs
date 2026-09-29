using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Activities.Services;

public class RecurringScheduleService : IRecurringScheduleService
{
    private readonly ISmartGymDbContext _dbContext;

    public RecurringScheduleService(ISmartGymDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<RecurringScheduleDto>> GetAllAsync(
        Guid? activityId = null,
        Guid? instructorId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.RecurringSchedules
            .Include(s => s.Activity)
            .Include(s => s.Room)
            .Include(s => s.Instructor)
            .AsNoTracking()
            .Where(s => s.IsActive);

        if (activityId.HasValue)
        {
            query = query.Where(s => s.ActivityId == activityId.Value);
        }

        if (instructorId.HasValue)
        {
            query = query.Where(s => s.InstructorId == instructorId.Value);
        }

        var list = await query
            .OrderBy(s => s.DayOfWeek)
            .ThenBy(s => s.StartTime)
            .ToListAsync(cancellationToken);

        return list.Select(MapToDto).ToList();
    }

    public async Task<RecurringScheduleDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var schedule = await _dbContext.RecurringSchedules
            .Include(s => s.Activity)
            .Include(s => s.Room)
            .Include(s => s.Instructor)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        return schedule == null ? null : MapToDto(schedule);
    }

    public async Task<RecurringScheduleDto> CreateAsync(CreateRecurringScheduleRequest request, CancellationToken cancellationToken = default)
    {
        var activity = await _dbContext.Activities.FirstOrDefaultAsync(a => a.Id == request.ActivityId && a.IsActive, cancellationToken);
        if (activity == null)
        {
            throw new ArgumentException("La actividad especificada no existe o no está activa.");
        }

        var room = await _dbContext.Rooms.FirstOrDefaultAsync(r => r.Id == request.RoomId && r.IsActive, cancellationToken);
        if (room == null)
        {
            throw new ArgumentException("La sala especificada no existe o no está activa.");
        }

        var instructor = await _dbContext.People
            .Include(p => p.PersonRoles)
                .ThenInclude(pr => pr.Role)
            .FirstOrDefaultAsync(p => p.Id == request.InstructorId && p.IsActive, cancellationToken);

        if (instructor == null || !instructor.PersonRoles.Any(pr => pr.IsActive && pr.Role.Name == RoleType.Instructor.ToString()))
        {
            throw new ArgumentException("El instructor especificado no existe o no posee el rol de Instructor asignado.");
        }

        // Validación de superposición de horarios en la misma sala
        var roomConflict = await HasRoomCollisionAsync(Guid.Empty, request.RoomId, request.DayOfWeek, request.StartTime, request.EndTime, request.ValidFrom, request.ValidTo, cancellationToken);
        if (roomConflict)
        {
            throw new InvalidOperationException("Ya existe una clase asignada en la misma sala durante ese día y rango horario.");
        }

        var schedule = new RecurringSchedule
        {
            Id = Guid.NewGuid(),
            ActivityId = request.ActivityId,
            RoomId = request.RoomId,
            InstructorId = request.InstructorId,
            DayOfWeek = request.DayOfWeek,
            MaxCapacity = request.MaxCapacity ?? activity.MaxCapacity,
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true
        };

        schedule.SetTimeSlot(request.StartTime, request.EndTime);
        schedule.SetValidityPeriod(request.ValidFrom, request.ValidTo);

        _dbContext.RecurringSchedules.Add(schedule);
        await _dbContext.SaveChangesAsync(cancellationToken);

        schedule.Activity = activity;
        schedule.Room = room;
        schedule.Instructor = instructor;

        return MapToDto(schedule);
    }

    public async Task<RecurringScheduleDto?> UpdateAsync(Guid id, UpdateRecurringScheduleRequest request, CancellationToken cancellationToken = default)
    {
        var schedule = await _dbContext.RecurringSchedules
            .Include(s => s.Activity)
            .Include(s => s.Room)
            .Include(s => s.Instructor)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (schedule == null) return null;

        var room = await _dbContext.Rooms.FirstOrDefaultAsync(r => r.Id == request.RoomId && r.IsActive, cancellationToken);
        if (room == null)
        {
            throw new ArgumentException("La sala especificada no existe o no está activa.");
        }

        var instructor = await _dbContext.People
            .Include(p => p.PersonRoles)
                .ThenInclude(pr => pr.Role)
            .FirstOrDefaultAsync(p => p.Id == request.InstructorId && p.IsActive, cancellationToken);

        if (instructor == null || !instructor.PersonRoles.Any(pr => pr.IsActive && pr.Role.Name == RoleType.Instructor.ToString()))
        {
            throw new ArgumentException("El instructor especificado no existe o no posee el rol de Instructor asignado.");
        }

        if (request.IsActive)
        {
            var roomConflict = await HasRoomCollisionAsync(id, request.RoomId, request.DayOfWeek, request.StartTime, request.EndTime, request.ValidFrom, request.ValidTo, cancellationToken);
            if (roomConflict)
            {
                throw new InvalidOperationException("Ya existe una clase asignada en la misma sala durante ese día y rango horario.");
            }
        }

        schedule.RoomId = request.RoomId;
        schedule.InstructorId = request.InstructorId;
        schedule.DayOfWeek = request.DayOfWeek;
        schedule.MaxCapacity = request.MaxCapacity ?? schedule.Activity.MaxCapacity;
        schedule.IsActive = request.IsActive;
        schedule.UpdatedAtUtc = DateTime.UtcNow;

        schedule.SetTimeSlot(request.StartTime, request.EndTime);
        schedule.SetValidityPeriod(request.ValidFrom, request.ValidTo);

        await _dbContext.SaveChangesAsync(cancellationToken);

        schedule.Room = room;
        schedule.Instructor = instructor;

        return MapToDto(schedule);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var schedule = await _dbContext.RecurringSchedules.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (schedule == null) return false;

        schedule.IsActive = false;
        schedule.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<bool> HasRoomCollisionAsync(
        Guid currentId,
        Guid roomId,
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime,
        DateOnly validFrom,
        DateOnly? validTo,
        CancellationToken cancellationToken)
    {
        var schedules = await _dbContext.RecurringSchedules
            .Where(s => s.Id != currentId && s.IsActive && s.RoomId == roomId && s.DayOfWeek == dayOfWeek)
            .ToListAsync(cancellationToken);

        foreach (var s in schedules)
        {
            // Verificación de solapamiento de fechas
            var datesOverlap = (!validTo.HasValue || s.ValidFrom <= validTo.Value) &&
                               (!s.ValidTo.HasValue || validFrom <= s.ValidTo.Value);

            if (datesOverlap)
            {
                // Verificación de solapamiento de horarios
                var timesOverlap = startTime < s.EndTime && s.StartTime < endTime;
                if (timesOverlap)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static RecurringScheduleDto MapToDto(RecurringSchedule s) =>
        new(
            s.Id,
            s.ActivityId,
            s.Activity?.Name ?? string.Empty,
            s.RoomId,
            s.Room?.Name ?? string.Empty,
            s.InstructorId,
            s.Instructor?.FullName ?? string.Empty,
            s.DayOfWeek,
            s.StartTime,
            s.EndTime,
            s.ValidFrom,
            s.ValidTo,
            s.MaxCapacity,
            s.IsActive
        );
}
