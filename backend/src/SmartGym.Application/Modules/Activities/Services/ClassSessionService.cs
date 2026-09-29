using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Activities.Services;

public class ClassSessionService : IClassSessionService
{
    private readonly ISmartGymDbContext _dbContext;

    public ClassSessionService(ISmartGymDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> GenerateSessionsFromSchedulesAsync(
        DateOnly fromDate,
        DateOnly toDate,
        Guid? recurringScheduleId = null,
        CancellationToken cancellationToken = default)
    {
        if (toDate < fromDate)
        {
            throw new ArgumentException("La fecha final no puede ser anterior a la fecha inicial.");
        }

        if (toDate.DayNumber - fromDate.DayNumber > 90)
        {
            throw new ArgumentException("El rango máximo de generación por lote es de 90 días.");
        }

        var schedulesQuery = _dbContext.RecurringSchedules
            .Include(s => s.Activity)
            .Where(s => s.IsActive);

        if (recurringScheduleId.HasValue)
        {
            schedulesQuery = schedulesQuery.Where(s => s.Id == recurringScheduleId.Value);
        }

        var schedules = await schedulesQuery.ToListAsync(cancellationToken);
        if (!schedules.Any())
        {
            return 0;
        }

        var existingKeys = await _dbContext.ClassSessions
            .Where(cs => cs.Date >= fromDate && cs.Date <= toDate && cs.RecurringScheduleId != null)
            .Select(cs => new { ScheduleId = cs.RecurringScheduleId!.Value, cs.Date })
            .ToListAsync(cancellationToken);

        var existingSet = new HashSet<(Guid ScheduleId, DateOnly Date)>(
            existingKeys.Select(k => (k.ScheduleId, k.Date))
        );

        var newSessions = new List<ClassSession>();

        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
        {
            foreach (var schedule in schedules)
            {
                if (schedule.OccursOn(date))
                {
                    var key = (schedule.Id, date);
                    if (!existingSet.Contains(key))
                    {
                        var session = new ClassSession
                        {
                            Id = Guid.NewGuid(),
                            RecurringScheduleId = schedule.Id,
                            ActivityId = schedule.ActivityId,
                            RoomId = schedule.RoomId,
                            InstructorId = schedule.InstructorId,
                            Date = date,
                            StartTime = schedule.StartTime,
                            EndTime = schedule.EndTime,
                            MaxCapacity = schedule.MaxCapacity ?? schedule.Activity?.MaxCapacity ?? 20,
                            ReservedCount = 0,
                            AttendedCount = 0,
                            Status = ClassSessionStatus.Scheduled,
                            CreatedAtUtc = DateTime.UtcNow,
                            IsActive = true
                        };

                        newSessions.Add(session);
                        existingSet.Add(key);
                    }
                }
            }
        }

        if (newSessions.Any())
        {
            _dbContext.ClassSessions.AddRange(newSessions);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return newSessions.Count;
    }

    public async Task<List<ClassSessionDto>> GetAllAsync(
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        Guid? activityId = null,
        Guid? instructorId = null,
        ClassSessionStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.ClassSessions
            .Include(cs => cs.Activity)
            .Include(cs => cs.Room)
            .Include(cs => cs.Instructor)
            .Include(cs => cs.OriginalInstructor)
            .Include(cs => cs.SubstituteInstructor)
            .AsNoTracking()
            .Where(cs => cs.IsActive);

        if (fromDate.HasValue)
        {
            query = query.Where(cs => cs.Date >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(cs => cs.Date <= toDate.Value);
        }

        if (activityId.HasValue)
        {
            query = query.Where(cs => cs.ActivityId == activityId.Value);
        }

        if (instructorId.HasValue)
        {
            query = query.Where(cs => cs.InstructorId == instructorId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(cs => cs.Status == status.Value);
        }

        var sessions = await query
            .OrderBy(cs => cs.Date)
            .ThenBy(cs => cs.StartTime)
            .ToListAsync(cancellationToken);

        return sessions.Select(MapToDto).ToList();
    }

    public async Task<ClassSessionDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.ClassSessions
            .Include(cs => cs.Activity)
            .Include(cs => cs.Room)
            .Include(cs => cs.Instructor)
            .Include(cs => cs.OriginalInstructor)
            .Include(cs => cs.SubstituteInstructor)
            .AsNoTracking()
            .FirstOrDefaultAsync(cs => cs.Id == id, cancellationToken);

        return session == null ? null : MapToDto(session);
    }

    public async Task<ClassSessionDto> CreateManualAsync(ManualCreateClassSessionRequest request, CancellationToken cancellationToken = default)
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

        var instructor = await _dbContext.People.FirstOrDefaultAsync(p => p.Id == request.InstructorId && p.IsActive, cancellationToken);
        if (instructor == null)
        {
            throw new ArgumentException("El instructor especificado no existe o no está activo.");
        }

        if (request.EndTime <= request.StartTime)
        {
            throw new ArgumentException("La hora de fin debe ser posterior a la hora de inicio.");
        }

        var session = new ClassSession
        {
            Id = Guid.NewGuid(),
            ActivityId = request.ActivityId,
            RoomId = request.RoomId,
            InstructorId = request.InstructorId,
            Date = request.Date,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            MaxCapacity = request.MaxCapacity > 0 ? request.MaxCapacity : activity.MaxCapacity,
            ReservedCount = 0,
            AttendedCount = 0,
            Status = ClassSessionStatus.Scheduled,
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true
        };

        _dbContext.ClassSessions.Add(session);
        await _dbContext.SaveChangesAsync(cancellationToken);

        session.Activity = activity;
        session.Room = room;
        session.Instructor = instructor;

        return MapToDto(session);
    }

    public async Task<ClassSessionDto?> StartSessionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionWithIncludesAsync(id, cancellationToken);
        if (session == null) return null;

        session.Start();
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(session);
    }

    public async Task<ClassSessionDto?> FinishSessionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionWithIncludesAsync(id, cancellationToken);
        if (session == null) return null;

        session.Finish();
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(session);
    }

    public async Task<ClassSessionDto?> SuspendSessionAsync(Guid id, string reason, CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionWithIncludesAsync(id, cancellationToken);
        if (session == null) return null;

        session.Suspend(reason);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(session);
    }

    public async Task<ClassSessionDto?> CancelSessionAsync(Guid id, string reason, CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionWithIncludesAsync(id, cancellationToken);
        if (session == null) return null;

        session.Cancel(reason);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(session);
    }

    public async Task<ClassSessionDto?> AssignSubstituteAsync(Guid id, Guid substituteInstructorId, CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionWithIncludesAsync(id, cancellationToken);
        if (session == null) return null;

        var substitute = await _dbContext.People
            .Include(p => p.PersonRoles)
                .ThenInclude(pr => pr.Role)
            .FirstOrDefaultAsync(p => p.Id == substituteInstructorId && p.IsActive, cancellationToken);

        if (substitute == null || !substitute.PersonRoles.Any(pr => pr.IsActive && pr.Role.Name == RoleType.Instructor.ToString()))
        {
            throw new ArgumentException("El instructor suplente no existe o no tiene el rol de Instructor asignado.");
        }

        session.AssignSubstituteInstructor(substituteInstructorId);
        await _dbContext.SaveChangesAsync(cancellationToken);

        session.SubstituteInstructor = substitute;
        return MapToDto(session);
    }

    private async Task<ClassSession?> LoadSessionWithIncludesAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.ClassSessions
            .Include(cs => cs.Activity)
            .Include(cs => cs.Room)
            .Include(cs => cs.Instructor)
            .Include(cs => cs.OriginalInstructor)
            .Include(cs => cs.SubstituteInstructor)
            .FirstOrDefaultAsync(cs => cs.Id == id, cancellationToken);
    }

    private static ClassSessionDto MapToDto(ClassSession cs) =>
        new(
            cs.Id,
            cs.RecurringScheduleId,
            cs.ActivityId,
            cs.Activity?.Name ?? string.Empty,
            cs.RoomId,
            cs.Room?.Name ?? string.Empty,
            cs.InstructorId,
            cs.Instructor?.FullName ?? string.Empty,
            cs.OriginalInstructorId,
            cs.OriginalInstructor?.FullName,
            cs.SubstituteInstructorId,
            cs.SubstituteInstructor?.FullName,
            cs.Date,
            cs.StartTime,
            cs.EndTime,
            cs.MaxCapacity,
            cs.ReservedCount,
            cs.AttendedCount,
            Math.Max(0, cs.MaxCapacity - cs.ReservedCount),
            cs.Status,
            cs.CancellationReason,
            cs.SuspendedAtUtc
        );
}
