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
            // Candidate condition:
            // Status == Scheduled AND ReservedCount == 0 AND !IsDetachedFromSchedule
            bool isCandidate = session.Status == ClassSessionStatus.Scheduled &&
                               session.ReservedCount == 0 &&
                               !session.IsDetachedFromSchedule;

            if (!isCandidate)
            {
                // Non-candidate sessions are left untouched and reported as divergent
                divergentSessionIds.Add(session.Id);
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
                // Cancel candidate with ScheduleEnded
                session.Cancel(
                    ClassSessionCancellationReason.ScheduleEnded,
                    observations: "El horario recurrente fue modificado, desactivado o finalizó su vigencia.",
                    byUserId: null,
                    nowUtc: now);
            }
            else
            {
                // In-place update: recalculate start/end, room, staff, capacity
                var newStartsAtUtc = _gymCalendar.ToUtc(localDate, schedule.StartTime);
                var newEndsAtUtc = _gymCalendar.ToUtc(localDate, schedule.EndTime);
                if (newEndsAtUtc <= newStartsAtUtc)
                {
                    newEndsAtUtc = newEndsAtUtc.AddDays(1);
                }

                var resolvedCapacity = schedule.MaxCapacity ?? schedule.Activity.DefaultCapacity ?? session.Capacity;

                // Check collision if (scheduleId, newStartsAtUtc) already exists on another session
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
}
