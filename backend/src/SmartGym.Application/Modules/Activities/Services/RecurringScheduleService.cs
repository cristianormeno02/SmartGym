using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Activities.Services;

public class RecurringScheduleService : IRecurringScheduleService
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly IClassSessionScheduleSync _scheduleSync;

    public RecurringScheduleService(
        ISmartGymDbContext dbContext,
        IClassSessionScheduleSync scheduleSync)
    {
        _dbContext = dbContext;
        _scheduleSync = scheduleSync;
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
            .Include(s => s.Secretary)
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

        return list.Select(s => MapToDto(s)).ToList();
    }

    public async Task<RecurringScheduleDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var schedule = await _dbContext.RecurringSchedules
            .Include(s => s.Activity)
            .Include(s => s.Room)
            .Include(s => s.Instructor)
            .Include(s => s.Secretary)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        return schedule == null ? null : MapToDto(schedule);
    }

    public async Task<RecurringScheduleDto> CreateAsync(CreateRecurringScheduleRequest request, CancellationToken cancellationToken = default)
    {
        var entities = await ValidateEntitiesAsync(request.ActivityId, request.RoomId, request.InstructorId, request.SecretaryId, cancellationToken);

        var conflicts = await DetectCollisionsAsync(Guid.Empty, request.RoomId, request.InstructorId, request.DayOfWeek, request.StartTime, request.EndTime, request.ValidFrom, request.ValidTo, cancellationToken);
        if (conflicts.Count > 0)
        {
            var message = conflicts.Any(c => c.Kind == ScheduleConflictKind.Room)
                ? "Ya existe una clase asignada en la misma sala durante ese día y rango horario."
                : "El instructor ya tiene otra clase asignada durante ese día y rango horario.";
            throw new ScheduleConflictException(message, conflicts);
        }

        var schedule = new RecurringSchedule
        {
            Id = Guid.NewGuid(),
            ActivityId = request.ActivityId,
            RoomId = request.RoomId,
            InstructorId = request.InstructorId,
            SecretaryId = request.SecretaryId,
            DayOfWeek = request.DayOfWeek,
            MaxCapacity = entities.Activity.ResolveCapacity(request.MaxCapacity),
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true
        };

        schedule.SetTimeSlot(request.StartTime, request.EndTime);
        schedule.SetValidityPeriod(request.ValidFrom, request.ValidTo);

        _dbContext.RecurringSchedules.Add(schedule);
        await _dbContext.SaveChangesAsync(cancellationToken);

        schedule.Activity = entities.Activity;
        schedule.Room = entities.Room;
        schedule.Instructor = entities.Instructor;
        schedule.Secretary = entities.Secretary;

        // Synchronize and generate sessions
        var divergent = await _scheduleSync.SyncAsync(schedule.Id, cancellationToken: cancellationToken);

        return MapToDto(schedule, divergent);
    }

    public async Task<List<RecurringScheduleDto>> CreateBatchAsync(CreateBatchRecurringScheduleRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Slots == null || request.Slots.Count == 0)
        {
            throw new ArgumentException("Debe especificar al menos un grupo de horarios.");
        }

        if (request.ValidTo.HasValue && request.ValidTo.Value < request.ValidFrom)
        {
            throw new ArgumentException("La fecha de fin de vigencia no puede ser anterior a la de inicio.");
        }

        foreach (var slot in request.Slots)
        {
            if (slot.DaysOfWeek == null || slot.DaysOfWeek.Count == 0)
            {
                throw new ArgumentException("Cada grupo de horarios debe tener al menos un día asignado.");
            }

            if (slot.DaysOfWeek.Distinct().Count() != slot.DaysOfWeek.Count)
            {
                throw new ArgumentException("No se pueden repetir días dentro del mismo grupo de horarios.");
            }

            if (slot.EndTime <= slot.StartTime)
            {
                throw new ArgumentException("La hora de fin debe ser posterior a la de inicio.");
            }
        }

        var entities = await ValidateEntitiesAsync(request.ActivityId, request.RoomId, request.InstructorId, request.SecretaryId, cancellationToken);
        var resolvedCapacity = entities.Activity.ResolveCapacity(request.MaxCapacity);

        var expandedTuples = new List<(DayOfWeek Day, TimeOnly StartTime, TimeOnly EndTime)>();
        foreach (var slot in request.Slots)
        {
            foreach (var day in slot.DaysOfWeek)
            {
                expandedTuples.Add((day, slot.StartTime, slot.EndTime));
            }
        }

        if (expandedTuples.Count > 50)
        {
            throw new ArgumentException("El lote no puede superar el límite máximo de 50 horarios recurrentes.");
        }

        // Sort by day and start time
        expandedTuples = expandedTuples.OrderBy(t => t.Day).ThenBy(t => t.StartTime).ToList();

        var conflicts = new List<ScheduleConflictDto>();

        // 1. Detect collisions within the batch
        for (int i = 0; i < expandedTuples.Count; i++)
        {
            var a = expandedTuples[i];
            for (int j = i + 1; j < expandedTuples.Count; j++)
            {
                var b = expandedTuples[j];
                if (a.Day == b.Day && a.StartTime < b.EndTime && b.StartTime < a.EndTime)
                {
                    conflicts.Add(new ScheduleConflictDto(
                        DayOfWeek: b.Day,
                        StartTime: b.StartTime,
                        EndTime: b.EndTime,
                        Kind: ScheduleConflictKind.WithinBatch,
                        ConflictingScheduleId: null,
                        ConflictingActivityName: entities.Activity.Name,
                        ConflictingStartTime: a.StartTime,
                        ConflictingEndTime: a.EndTime
                    ));
                }
            }
        }

        // 2. Query active schedules from database that could collide
        var relevantDays = expandedTuples.Select(t => t.Day).Distinct().ToList();
        var existingCandidates = await _dbContext.RecurringSchedules
            .Include(s => s.Activity)
            .Where(s => s.IsActive && relevantDays.Contains(s.DayOfWeek) && (s.RoomId == request.RoomId || s.InstructorId == request.InstructorId))
            .ToListAsync(cancellationToken);

        foreach (var t in expandedTuples)
        {
            foreach (var s in existingCandidates.Where(s => s.DayOfWeek == t.Day))
            {
                var datesOverlap = (!request.ValidTo.HasValue || s.ValidFrom <= request.ValidTo.Value) &&
                                   (!s.ValidTo.HasValue || request.ValidFrom <= s.ValidTo.Value);

                if (!datesOverlap) continue;

                var timesOverlap = t.StartTime < s.EndTime && s.StartTime < t.EndTime;
                if (!timesOverlap) continue;

                if (s.RoomId == request.RoomId)
                {
                    conflicts.Add(new ScheduleConflictDto(
                        DayOfWeek: t.Day,
                        StartTime: t.StartTime,
                        EndTime: t.EndTime,
                        Kind: ScheduleConflictKind.Room,
                        ConflictingScheduleId: s.Id,
                        ConflictingActivityName: s.Activity?.Name,
                        ConflictingStartTime: s.StartTime,
                        ConflictingEndTime: s.EndTime
                    ));
                }

                if (s.InstructorId == request.InstructorId && s.RoomId != request.RoomId)
                {
                    conflicts.Add(new ScheduleConflictDto(
                        DayOfWeek: t.Day,
                        StartTime: t.StartTime,
                        EndTime: t.EndTime,
                        Kind: ScheduleConflictKind.Instructor,
                        ConflictingScheduleId: s.Id,
                        ConflictingActivityName: s.Activity?.Name,
                        ConflictingStartTime: s.StartTime,
                        ConflictingEndTime: s.EndTime
                    ));
                }
            }
        }

        if (conflicts.Count > 0)
        {
            throw new ScheduleConflictException("Se detectaron uno o más conflictos en el lote de horarios recurrentes.", conflicts);
        }

        // 3. Persist atomically
        await using var tx = _dbContext.Database.CurrentTransaction == null
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var createdSchedules = new List<RecurringSchedule>();
        var now = DateTime.UtcNow;

        foreach (var t in expandedTuples)
        {
            var schedule = new RecurringSchedule
            {
                Id = Guid.NewGuid(),
                ActivityId = request.ActivityId,
                RoomId = request.RoomId,
                InstructorId = request.InstructorId,
                SecretaryId = request.SecretaryId,
                DayOfWeek = t.Day,
                MaxCapacity = resolvedCapacity,
                CreatedAtUtc = now,
                IsActive = true
            };

            schedule.SetTimeSlot(t.StartTime, t.EndTime);
            schedule.SetValidityPeriod(request.ValidFrom, request.ValidTo);

            schedule.Activity = entities.Activity;
            schedule.Room = entities.Room;
            schedule.Instructor = entities.Instructor;
            schedule.Secretary = entities.Secretary;

            _dbContext.RecurringSchedules.Add(schedule);
            createdSchedules.Add(schedule);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var dtos = new List<RecurringScheduleDto>();
        foreach (var s in createdSchedules)
        {
            var divergent = await _scheduleSync.SyncAsync(s.Id, cancellationToken: cancellationToken);
            dtos.Add(MapToDto(s, divergent));
        }

        if (tx != null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        return dtos;
    }

    public async Task<RecurringScheduleDto?> UpdateAsync(Guid id, UpdateRecurringScheduleRequest request, CancellationToken cancellationToken = default)
    {
        var schedule = await _dbContext.RecurringSchedules
            .Include(s => s.Activity)
            .Include(s => s.Room)
            .Include(s => s.Instructor)
            .Include(s => s.Secretary)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (schedule == null) return null;

        if (request.IsActive && schedule.Activity.Status != ActivityStatus.Active)
        {
            throw new InvalidOperationException("No se puede mantener activo un horario de una actividad que no está activa.");
        }

        var resolvedCapacity = schedule.Activity.ResolveCapacity(request.MaxCapacity);

        var entities = await ValidateEntitiesAsync(schedule.ActivityId, request.RoomId, request.InstructorId, request.SecretaryId, cancellationToken);

        if (request.IsActive)
        {
            var conflicts = await DetectCollisionsAsync(id, request.RoomId, request.InstructorId, request.DayOfWeek, request.StartTime, request.EndTime, request.ValidFrom, request.ValidTo, cancellationToken);
            if (conflicts.Count > 0)
            {
                var message = conflicts.Any(c => c.Kind == ScheduleConflictKind.Room)
                    ? "Ya existe una clase asignada en la misma sala durante ese día y rango horario."
                    : "El instructor ya tiene otra clase asignada durante ese día y rango horario.";
                throw new ScheduleConflictException(message, conflicts);
            }
        }

        schedule.RoomId = request.RoomId;
        schedule.InstructorId = request.InstructorId;
        schedule.SecretaryId = request.SecretaryId;
        schedule.DayOfWeek = request.DayOfWeek;
        schedule.MaxCapacity = resolvedCapacity;
        schedule.IsActive = request.IsActive;
        schedule.UpdatedAtUtc = DateTime.UtcNow;

        schedule.SetTimeSlot(request.StartTime, request.EndTime);
        schedule.SetValidityPeriod(request.ValidFrom, request.ValidTo);

        await _dbContext.SaveChangesAsync(cancellationToken);

        schedule.Room = entities.Room;
        schedule.Instructor = entities.Instructor;
        schedule.Secretary = entities.Secretary;

        // Synchronize sessions within the same transaction/operation
        var divergent = await _scheduleSync.SyncAsync(schedule.Id, cancellationToken: cancellationToken);

        return MapToDto(schedule, divergent);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var schedule = await _dbContext.RecurringSchedules.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (schedule == null) return false;

        schedule.IsActive = false;
        schedule.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Sync to cancel candidates
        await _scheduleSync.SyncAsync(schedule.Id, cancellationToken: cancellationToken);

        return true;
    }

    private record ValidatedScheduleEntities(
        Activity Activity,
        Room Room,
        Person Instructor,
        Person? Secretary
    );

    private async Task<ValidatedScheduleEntities> ValidateEntitiesAsync(
        Guid activityId,
        Guid roomId,
        Guid instructorId,
        Guid? secretaryId,
        CancellationToken cancellationToken)
    {
        var activity = await _dbContext.Activities.FirstOrDefaultAsync(a => a.Id == activityId && a.IsActive, cancellationToken);
        if (activity == null)
        {
            throw new ArgumentException("La actividad especificada no existe o no está activa.");
        }

        var room = await _dbContext.Rooms.FirstOrDefaultAsync(r => r.Id == roomId && r.IsActive, cancellationToken);
        if (room == null)
        {
            throw new ArgumentException("La sala especificada no existe o no está activa.");
        }

        var instructor = await _dbContext.People
            .Include(p => p.PersonRoles)
                .ThenInclude(pr => pr.Role)
            .FirstOrDefaultAsync(p => p.Id == instructorId && p.IsActive, cancellationToken);

        if (instructor == null || !instructor.PersonRoles.Any(pr => pr.IsActive && pr.Role.Name == RoleType.Instructor.ToString()))
        {
            throw new ArgumentException("El instructor especificado no existe o no posee el rol de Instructor asignado.");
        }

        Person? secretary = null;
        if (secretaryId.HasValue)
        {
            secretary = await _dbContext.People
                .Include(p => p.PersonRoles)
                    .ThenInclude(pr => pr.Role)
                .FirstOrDefaultAsync(p => p.Id == secretaryId.Value && p.IsActive, cancellationToken);

            if (secretary == null || !secretary.PersonRoles.Any(pr => pr.IsActive && (pr.Role.Name == RoleType.Secretary.ToString() || pr.Role.Name == RoleType.Administrator.ToString())))
            {
                throw new ArgumentException("El secretario especificado no existe o no posee el rol de Secretario o Administrador asignado.");
            }
        }

        return new ValidatedScheduleEntities(activity, room, instructor, secretary);
    }

    private async Task<List<ScheduleConflictDto>> DetectCollisionsAsync(
        Guid currentId,
        Guid roomId,
        Guid instructorId,
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime,
        DateOnly validFrom,
        DateOnly? validTo,
        CancellationToken cancellationToken)
    {
        var schedules = await _dbContext.RecurringSchedules
            .Include(s => s.Activity)
            .Where(s => s.Id != currentId && s.IsActive && s.DayOfWeek == dayOfWeek && (s.RoomId == roomId || s.InstructorId == instructorId))
            .ToListAsync(cancellationToken);

        var conflicts = new List<ScheduleConflictDto>();

        foreach (var s in schedules)
        {
            var datesOverlap = (!validTo.HasValue || s.ValidFrom <= validTo.Value) &&
                               (!s.ValidTo.HasValue || validFrom <= s.ValidTo.Value);

            if (!datesOverlap) continue;

            var timesOverlap = startTime < s.EndTime && s.StartTime < endTime;
            if (!timesOverlap) continue;

            if (s.RoomId == roomId)
            {
                conflicts.Add(new ScheduleConflictDto(
                    DayOfWeek: dayOfWeek,
                    StartTime: startTime,
                    EndTime: endTime,
                    Kind: ScheduleConflictKind.Room,
                    ConflictingScheduleId: s.Id,
                    ConflictingActivityName: s.Activity?.Name,
                    ConflictingStartTime: s.StartTime,
                    ConflictingEndTime: s.EndTime
                ));
            }

            if (s.InstructorId == instructorId && s.RoomId != roomId)
            {
                conflicts.Add(new ScheduleConflictDto(
                    DayOfWeek: dayOfWeek,
                    StartTime: startTime,
                    EndTime: endTime,
                    Kind: ScheduleConflictKind.Instructor,
                    ConflictingScheduleId: s.Id,
                    ConflictingActivityName: s.Activity?.Name,
                    ConflictingStartTime: s.StartTime,
                    ConflictingEndTime: s.EndTime
                ));
            }
        }

        return conflicts;
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
        var conflicts = await DetectCollisionsAsync(currentId, roomId, Guid.Empty, dayOfWeek, startTime, endTime, validFrom, validTo, cancellationToken);
        return conflicts.Any(c => c.Kind == ScheduleConflictKind.Room);
    }

    private async Task<bool> HasInstructorCollisionAsync(
        Guid currentId,
        Guid instructorId,
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime,
        DateOnly validFrom,
        DateOnly? validTo,
        CancellationToken cancellationToken)
    {
        var conflicts = await DetectCollisionsAsync(currentId, Guid.Empty, instructorId, dayOfWeek, startTime, endTime, validFrom, validTo, cancellationToken);
        return conflicts.Any(c => c.Kind == ScheduleConflictKind.Instructor);
    }

    private static RecurringScheduleDto MapToDto(RecurringSchedule s, IReadOnlyList<Guid>? divergentSessionIds = null) =>
        new(
            s.Id,
            s.ActivityId,
            s.Activity?.Name ?? string.Empty,
            s.RoomId,
            s.Room?.Name ?? string.Empty,
            s.InstructorId,
            s.Instructor?.FullName ?? string.Empty,
            s.SecretaryId,
            s.Secretary?.FullName,
            s.DayOfWeek,
            s.StartTime,
            s.EndTime,
            s.ValidFrom,
            s.ValidTo,
            s.MaxCapacity,
            s.IsActive,
            divergentSessionIds
        );
}
