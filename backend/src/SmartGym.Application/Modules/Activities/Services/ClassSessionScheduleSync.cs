using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Activities.Services;

public class ClassSessionScheduleSync : IClassSessionScheduleSync
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly IGymCalendar _gymCalendar;
    private readonly IClassSessionGenerator _generator;

    public ClassSessionScheduleSync(
        ISmartGymDbContext dbContext,
        IGymCalendar gymCalendar,
        IClassSessionGenerator generator)
    {
        _dbContext = dbContext;
        _gymCalendar = gymCalendar;
        _generator = generator;
    }

    public async Task<List<Guid>> SyncAsync(
        Guid scheduleId,
        DateTime? nowUtc = null,
        CancellationToken cancellationToken = default)
    {
        var now = nowUtc ?? DateTime.UtcNow;

        var schedule = await _dbContext.RecurringSchedules
            .Include(s => s.Activity)
            .FirstOrDefaultAsync(s => s.Id == scheduleId, cancellationToken);

        if (schedule == null)
        {
            return new List<Guid>();
        }

        // Fetch all future sessions of this schedule
        var futureSessions = await _dbContext.ClassSessions
            .Where(cs => cs.RecurringScheduleId == scheduleId && cs.StartsAtUtc > now)
            .ToListAsync(cancellationToken);

        var divergentSessionIds = new List<Guid>();

        foreach (var session in futureSessions)
        {
            // Las sesiones modificadas puntualmente a mano deben respetarse como divergentes
            if (session.IsDetachedFromSchedule)
            {
                divergentSessionIds.Add(session.Id);
                continue;
            }

            // Solo sincronizamos ocurrencias vivas en estado Scheduled u Open
            if (session.Status != ClassSessionStatus.Scheduled && session.Status != ClassSessionStatus.Open)
            {
                continue;
            }

            // Derive local date for this session
            var localStarts = _gymCalendar.ToLocal(session.StartsAtUtc);
            var localDate = DateOnly.FromDateTime(localStarts.DateTime);

            // Check if the session date still matches the schedule
            bool matchesSchedule = schedule.IsActive &&
                                   schedule.Activity.Status == ActivityStatus.Active &&
                                   schedule.OccursOn(localDate);

            if (!matchesSchedule)
            {
                if (session.ReservedCount == 0)
                {
                    // Cancel candidate with ScheduleEnded
                    session.Cancel(
                        ClassSessionCancellationReason.ScheduleEnded,
                        observations: "El horario recurrente fue modificado, desactivado o finalizó su vigencia.",
                        byUserId: null,
                        nowUtc: now);
                }
                else
                {
                    divergentSessionIds.Add(session.Id);
                }
                continue;
            }

            // In-place update: recalculate start/end, room, staff, capacity
            var newStartsAtUtc = _gymCalendar.ToUtc(localDate, schedule.StartTime);
            var newEndsAtUtc = _gymCalendar.ToUtc(localDate, schedule.EndTime);
            if (newEndsAtUtc <= newStartsAtUtc)
            {
                newEndsAtUtc = newEndsAtUtc.AddDays(1);
            }

            var resolvedCapacity = schedule.MaxCapacity ?? schedule.Activity.DefaultCapacity ?? session.Capacity;
            bool timeSlotChanged = session.StartsAtUtc != newStartsAtUtc || session.EndsAtUtc != newEndsAtUtc;

            if (session.ReservedCount == 0)
            {
                // Sin reservas: actualización total in-place
                bool collisionWithExisting = await _dbContext.ClassSessions
                    .AnyAsync(cs => cs.RecurringScheduleId == scheduleId &&
                                    cs.StartsAtUtc == newStartsAtUtc &&
                                    cs.Id != session.Id, cancellationToken);

                if (collisionWithExisting)
                {
                    session.Cancel(
                        ClassSessionCancellationReason.ScheduleEnded,
                        observations: "Colisión con una sesión existente tras cambio de horario.",
                        byUserId: null,
                        nowUtc: now);
                }
                else
                {
                    session.SyncFromSchedule(
                        newStartsAtUtc,
                        newEndsAtUtc,
                        schedule.RoomId,
                        resolvedCapacity,
                        schedule.InstructorId,
                        schedule.SecretaryId);
                }
            }
            else
            {
                // Con reservas previas
                if (timeSlotChanged)
                {
                    // No se puede mover el horario a espaldas de los alumnos ya inscriptos
                    divergentSessionIds.Add(session.Id);
                }
                else
                {
                    // Misma fecha y franja horaria: se sincroniza el personal asignado
                    session.SyncStaffFromSchedule(schedule.InstructorId, schedule.SecretaryId);
                }
            }
        }

        // Save modifications to candidates before calling generator to ensure consistent state
        await _dbContext.SaveChangesAsync(cancellationToken);

        // If the schedule is still active, generate occurrences in the forward window
        if (schedule.IsActive && schedule.Activity.Status == ActivityStatus.Active)
        {
            var today = _gymCalendar.Today;
            var windowEnd = today.AddDays(60);
            await _generator.GenerateAsync(today, windowEnd, scheduleId, cancellationToken);
        }

        return divergentSessionIds;
    }

    public async Task<int> SyncAllActiveAsync(
        DateTime? nowUtc = null,
        CancellationToken cancellationToken = default)
    {
        var activeScheduleIds = await _dbContext.RecurringSchedules
            .AsNoTracking()
            .Where(s => s.IsActive && s.Activity.IsActive && s.Activity.Status == ActivityStatus.Active)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        int count = 0;
        foreach (var scheduleId in activeScheduleIds)
        {
            await SyncAsync(scheduleId, nowUtc, cancellationToken);
            count++;
        }

        return count;
    }
}
