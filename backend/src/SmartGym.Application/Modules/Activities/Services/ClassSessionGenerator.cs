using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Activities.Services;

public class ClassSessionGenerator : IClassSessionGenerator
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly IGymCalendar _gymCalendar;
    private readonly IOptions<ClassSessionsOptions> _options;
    private readonly IAuditService _auditService;
    private readonly ILogger<ClassSessionGenerator> _logger;

    public ClassSessionGenerator(
        ISmartGymDbContext dbContext,
        IGymCalendar gymCalendar,
        IOptions<ClassSessionsOptions> options,
        IAuditService auditService,
        ILogger<ClassSessionGenerator> logger)
    {
        _dbContext = dbContext;
        _gymCalendar = gymCalendar;
        _options = options;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<GenerationResultDto> GenerateAsync(DateOnly from, DateOnly to, Guid? scheduleId = null, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("La fecha final no puede ser anterior a la inicial.");
        }

        if (to.DayNumber - from.DayNumber > 92)
        {
            throw new ArgumentException("El rango máximo de generación por lote es de 92 días.");
        }

        var schedulesQuery = _dbContext.RecurringSchedules
            .Include(s => s.Activity)
            .Where(s => s.IsActive && s.Activity.Status == ActivityStatus.Active);

        if (scheduleId.HasValue)
        {
            schedulesQuery = schedulesQuery.Where(s => s.Id == scheduleId.Value);
        }

        var schedules = await schedulesQuery.ToListAsync(cancellationToken);
        int created = 0;
        int skipped = 0;
        var conflicts = new List<GenerationConflictDto>();

        foreach (var schedule in schedules)
        {
            for (var d = from; d <= to; d = d.AddDays(1))
            {
                if (!schedule.OccursOn(d)) continue;

                var startsAtUtc = _gymCalendar.ToUtc(d, schedule.StartTime);
                var endsAtUtc = _gymCalendar.ToUtc(d, schedule.EndTime);
                if (endsAtUtc <= startsAtUtc) endsAtUtc = endsAtUtc.AddDays(1);

                // Si ya existe en cualquier estado, se omite
                var exists = await _dbContext.ClassSessions
                    .AnyAsync(cs => cs.RecurringScheduleId == schedule.Id && cs.StartsAtUtc == startsAtUtc, cancellationToken);

                if (exists)
                {
                    skipped++;
                    continue;
                }

                // Resolver cupo
                var capacity = schedule.MaxCapacity ?? schedule.Activity.DefaultCapacity ?? 0;
                if (capacity <= 0)
                {
                    conflicts.Add(new GenerationConflictDto(schedule.Id, d, "NO_CAPACITY"));
                    continue;
                }

                // Solapamiento de sala (tanto en base de datos como en las entidades recién añadidas en el mismo lote)
                var roomConflictDb = await _dbContext.ClassSessions
                    .AnyAsync(cs => cs.RoomId == schedule.RoomId &&
                                    cs.IsActive &&
                                    (cs.Status == ClassSessionStatus.Scheduled || cs.Status == ClassSessionStatus.Open) &&
                                    cs.StartsAtUtc < endsAtUtc && cs.EndsAtUtc > startsAtUtc, cancellationToken);
                var roomConflictLocal = _dbContext.ClassSessions.Local
                    .Any(cs => cs.RoomId == schedule.RoomId &&
                               cs.IsActive &&
                               (cs.Status == ClassSessionStatus.Scheduled || cs.Status == ClassSessionStatus.Open) &&
                               cs.StartsAtUtc < endsAtUtc && cs.EndsAtUtc > startsAtUtc);

                if (roomConflictDb || roomConflictLocal)
                {
                    conflicts.Add(new GenerationConflictDto(schedule.Id, d, "ROOM_OVERLAP"));
                    continue;
                }

                // Solapamiento de instructor
                var instructorConflictDb = await _dbContext.ClassSessions
                    .AnyAsync(cs => cs.EffectiveInstructorId == schedule.InstructorId &&
                                    cs.IsActive &&
                                    (cs.Status == ClassSessionStatus.Scheduled || cs.Status == ClassSessionStatus.Open) &&
                                    cs.StartsAtUtc < endsAtUtc && cs.EndsAtUtc > startsAtUtc, cancellationToken);
                var instructorConflictLocal = _dbContext.ClassSessions.Local
                    .Any(cs => cs.EffectiveInstructorId == schedule.InstructorId &&
                               cs.IsActive &&
                               (cs.Status == ClassSessionStatus.Scheduled || cs.Status == ClassSessionStatus.Open) &&
                               cs.StartsAtUtc < endsAtUtc && cs.EndsAtUtc > startsAtUtc);

                if (instructorConflictDb || instructorConflictLocal)
                {
                    conflicts.Add(new GenerationConflictDto(schedule.Id, d, "INSTRUCTOR_OVERLAP"));
                    continue;
                }

                var session = ClassSession.CreateFromSchedule(
                    schedule.Id,
                    schedule.ActivityId,
                    schedule.RoomId,
                    startsAtUtc,
                    endsAtUtc,
                    capacity,
                    schedule.InstructorId,
                    schedule.SecretaryId);

                _dbContext.ClassSessions.Add(session);
                created++;
            }
        }

        if (created > 0)
        {
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogWarning(ex, "Posible carrera de concurrencia al persistir sesiones generadas. Reintentando...");
                // Descartar cambios pendientes y reintentar una sola vez si el DbContext subyacente lo permite
                if (_dbContext is DbContext efDbContext)
                {
                    foreach (var entry in efDbContext.ChangeTracker.Entries().ToList())
                    {
                        entry.State = EntityState.Detached;
                    }
                }

                // Si era generación manual puntual o lote, reintentamos
                return await RetryGenerateOnceAsync(from, to, scheduleId, cancellationToken);
            }

            await _auditService.LogAsync(
                action: "CLASS_SESSIONS_GENERATED",
                entityName: "ClassSession",
                entityId: "Batch",
                oldValues: null,
                newValues: System.Text.Json.JsonSerializer.Serialize(new { Created = created, Skipped = skipped, ConflictsCount = conflicts.Count }),
                userId: null,
                userEmail: null,
                ipAddress: null,
                cancellationToken: cancellationToken);
        }

        return new GenerationResultDto(created, skipped, conflicts);
    }

    private async Task<GenerationResultDto> RetryGenerateOnceAsync(DateOnly from, DateOnly to, Guid? scheduleId, CancellationToken cancellationToken)
    {
        var schedulesQuery = _dbContext.RecurringSchedules
            .Include(s => s.Activity)
            .Where(s => s.IsActive && s.Activity.Status == ActivityStatus.Active);

        if (scheduleId.HasValue)
        {
            schedulesQuery = schedulesQuery.Where(s => s.Id == scheduleId.Value);
        }

        var schedules = await schedulesQuery.ToListAsync(cancellationToken);
        int created = 0;
        int skipped = 0;
        var conflicts = new List<GenerationConflictDto>();

        foreach (var schedule in schedules)
        {
            for (var d = from; d <= to; d = d.AddDays(1))
            {
                if (!schedule.OccursOn(d)) continue;

                var startsAtUtc = _gymCalendar.ToUtc(d, schedule.StartTime);
                var endsAtUtc = _gymCalendar.ToUtc(d, schedule.EndTime);
                if (endsAtUtc <= startsAtUtc) endsAtUtc = endsAtUtc.AddDays(1);

                var exists = await _dbContext.ClassSessions
                    .AnyAsync(cs => cs.RecurringScheduleId == schedule.Id && cs.StartsAtUtc == startsAtUtc, cancellationToken);

                if (exists)
                {
                    skipped++;
                    continue;
                }

                var capacity = schedule.MaxCapacity ?? schedule.Activity.DefaultCapacity ?? 0;
                if (capacity <= 0)
                {
                    conflicts.Add(new GenerationConflictDto(schedule.Id, d, "NO_CAPACITY"));
                    continue;
                }

                var roomConflictDb = await _dbContext.ClassSessions
                    .AnyAsync(cs => cs.RoomId == schedule.RoomId &&
                                    cs.IsActive &&
                                    (cs.Status == ClassSessionStatus.Scheduled || cs.Status == ClassSessionStatus.Open) &&
                                    cs.StartsAtUtc < endsAtUtc && cs.EndsAtUtc > startsAtUtc, cancellationToken);
                var roomConflictLocal = _dbContext.ClassSessions.Local
                    .Any(cs => cs.RoomId == schedule.RoomId &&
                               cs.IsActive &&
                               (cs.Status == ClassSessionStatus.Scheduled || cs.Status == ClassSessionStatus.Open) &&
                               cs.StartsAtUtc < endsAtUtc && cs.EndsAtUtc > startsAtUtc);

                if (roomConflictDb || roomConflictLocal)
                {
                    conflicts.Add(new GenerationConflictDto(schedule.Id, d, "ROOM_OVERLAP"));
                    continue;
                }

                var instructorConflictDb = await _dbContext.ClassSessions
                    .AnyAsync(cs => cs.EffectiveInstructorId == schedule.InstructorId &&
                                    cs.IsActive &&
                                    (cs.Status == ClassSessionStatus.Scheduled || cs.Status == ClassSessionStatus.Open) &&
                                    cs.StartsAtUtc < endsAtUtc && cs.EndsAtUtc > startsAtUtc, cancellationToken);
                var instructorConflictLocal = _dbContext.ClassSessions.Local
                    .Any(cs => cs.EffectiveInstructorId == schedule.InstructorId &&
                               cs.IsActive &&
                               (cs.Status == ClassSessionStatus.Scheduled || cs.Status == ClassSessionStatus.Open) &&
                               cs.StartsAtUtc < endsAtUtc && cs.EndsAtUtc > startsAtUtc);

                if (instructorConflictDb || instructorConflictLocal)
                {
                    conflicts.Add(new GenerationConflictDto(schedule.Id, d, "INSTRUCTOR_OVERLAP"));
                    continue;
                }

                var session = ClassSession.CreateFromSchedule(
                    schedule.Id,
                    schedule.ActivityId,
                    schedule.RoomId,
                    startsAtUtc,
                    endsAtUtc,
                    capacity,
                    schedule.InstructorId,
                    schedule.SecretaryId);

                _dbContext.ClassSessions.Add(session);
                created++;
            }
        }

        if (created > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.LogAsync(
                action: "CLASS_SESSIONS_GENERATED",
                entityName: "ClassSession",
                entityId: "Batch",
                oldValues: null,
                newValues: System.Text.Json.JsonSerializer.Serialize(new { Created = created, Skipped = skipped, ConflictsCount = conflicts.Count }),
                userId: null,
                userEmail: null,
                ipAddress: null,
                cancellationToken: cancellationToken);
        }

        return new GenerationResultDto(created, skipped, conflicts);
    }

    public async Task<int> AutoOpenAsync(DateTime? nowUtc = null, CancellationToken cancellationToken = default)
    {
        var leadTime = _options.Value.AutoOpenLeadTime;
        if (!leadTime.HasValue) return 0;

        var now = nowUtc ?? DateTime.UtcNow;
        var threshold = now.Add(leadTime.Value);

        var sessionsToOpen = await _dbContext.ClassSessions
            .Where(cs => cs.Status == ClassSessionStatus.Scheduled &&
                         cs.IsActive &&
                         cs.StartsAtUtc > now &&
                         cs.StartsAtUtc <= threshold)
            .ToListAsync(cancellationToken);

        int count = 0;
        foreach (var session in sessionsToOpen)
        {
            session.Open(now);
            count++;
        }

        if (count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return count;
    }
}
