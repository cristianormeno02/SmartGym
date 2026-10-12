using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Models;
using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Activities.Services;

public class ClassSessionService : IClassSessionService
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly IGymCalendar _gymCalendar;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuditService _auditService;

    public ClassSessionService(
        ISmartGymDbContext dbContext,
        IGymCalendar gymCalendar,
        ICurrentUserService currentUserService,
        IAuditService auditService)
    {
        _dbContext = dbContext;
        _gymCalendar = gymCalendar;
        _currentUserService = currentUserService;
        _auditService = auditService;
    }

    public async Task<GenerationResultDto> GenerateSessionsAsync(GenerateSessionsRequest request, CancellationToken cancellationToken = default)
    {
        if (request.To < request.From)
        {
            throw new ArgumentException("La fecha final no puede ser anterior a la inicial.");
        }

        if (request.To.DayNumber - request.From.DayNumber > 92)
        {
            throw new ArgumentException("El rango máximo de generación por lote es de 92 días.");
        }

        var schedulesQuery = _dbContext.RecurringSchedules
            .Include(s => s.Activity)
            .Where(s => s.IsActive && s.Activity.Status == ActivityStatus.Active);

        if (request.RecurringScheduleId.HasValue)
        {
            schedulesQuery = schedulesQuery.Where(s => s.Id == request.RecurringScheduleId.Value);
        }

        var schedules = await schedulesQuery.ToListAsync(cancellationToken);
        int created = 0;
        int skipped = 0;
        var conflicts = new List<GenerationConflictDto>();

        foreach (var schedule in schedules)
        {
            for (var d = request.From; d <= request.To; d = d.AddDays(1))
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

                // Solapamiento de sala
                var roomConflict = await _dbContext.ClassSessions
                    .AnyAsync(cs => cs.RoomId == schedule.RoomId &&
                                    cs.IsActive &&
                                    (cs.Status == ClassSessionStatus.Scheduled || cs.Status == ClassSessionStatus.Open) &&
                                    cs.StartsAtUtc < endsAtUtc && cs.EndsAtUtc > startsAtUtc, cancellationToken);
                if (roomConflict)
                {
                    conflicts.Add(new GenerationConflictDto(schedule.Id, d, "ROOM_OVERLAP"));
                    continue;
                }

                // Solapamiento de instructor
                var instructorConflict = await _dbContext.ClassSessions
                    .AnyAsync(cs => cs.EffectiveInstructorId == schedule.InstructorId &&
                                    cs.IsActive &&
                                    (cs.Status == ClassSessionStatus.Scheduled || cs.Status == ClassSessionStatus.Open) &&
                                    cs.StartsAtUtc < endsAtUtc && cs.EndsAtUtc > startsAtUtc, cancellationToken);
                if (instructorConflict)
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
            await SaveChangesTranslatingConflictsAsync(cancellationToken);

            await _auditService.LogAsync(
                action: "CLASS_SESSIONS_GENERATED",
                entityName: "ClassSession",
                entityId: "Batch",
                oldValues: null,
                newValues: System.Text.Json.JsonSerializer.Serialize(new { Created = created, Skipped = skipped, Conflicts = conflicts.Count }),
                userId: _currentUserService.UserId,
                userEmail: _currentUserService.Email,
                ipAddress: null,
                cancellationToken: cancellationToken);
        }

        return new GenerationResultDto(created, skipped, conflicts);
    }

    public async Task<PagedResult<ClassSessionListItemDto>> GetPagedAsync(
        DateOnly? from = null,
        DateOnly? to = null,
        Guid? activityId = null,
        Guid? roomId = null,
        Guid? instructorId = null,
        Guid? secretaryId = null,
        ClassSessionDisplayStatus? status = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = _gymCalendar.UtcNow;
        var query = _dbContext.ClassSessions
            .Include(cs => cs.Activity)
            .Include(cs => cs.Room)
            .Include(cs => cs.ScheduledInstructor)
            .Include(cs => cs.EffectiveInstructor)
            .Include(cs => cs.ScheduledSecretary)
            .Include(cs => cs.EffectiveSecretary)
            .AsNoTracking()
            .Where(cs => cs.IsActive);

        // Si el usuario es Instructor y no Staff, solo ve sus propias sesiones
        if (_currentUserService.IsInRole(RoleType.Instructor.ToString()) &&
            !_currentUserService.IsInRole(RoleType.Administrator.ToString()) &&
            !_currentUserService.IsInRole(RoleType.Secretary.ToString()))
        {
            var myPersonId = _currentUserService.PersonId;
            if (myPersonId.HasValue)
            {
                query = query.Where(cs => cs.EffectiveInstructorId == myPersonId.Value || cs.ScheduledInstructorId == myPersonId.Value);
            }
        }

        if (from.HasValue && to.HasValue)
        {
            var (startUtc, endUtc) = _gymCalendar.LocalDayRange(from.Value, to.Value);
            query = query.Where(cs => cs.StartsAtUtc >= startUtc && cs.StartsAtUtc < endUtc);
        }

        if (activityId.HasValue) query = query.Where(cs => cs.ActivityId == activityId.Value);
        if (roomId.HasValue) query = query.Where(cs => cs.RoomId == roomId.Value);
        if (instructorId.HasValue) query = query.Where(cs => cs.EffectiveInstructorId == instructorId.Value || cs.ScheduledInstructorId == instructorId.Value);
        if (secretaryId.HasValue) query = query.Where(cs => cs.EffectiveSecretaryId == secretaryId.Value || cs.ScheduledSecretaryId == secretaryId.Value);

        var list = await query.OrderBy(cs => cs.StartsAtUtc).ToListAsync(cancellationToken);

        // Filtrar por DisplayStatus si se especificó
        if (status.HasValue)
        {
            list = list.Where(cs => cs.GetDisplayStatus(nowUtc) == status.Value).ToList();
        }

        var total = list.Count;
        var items = list.Skip((page - 1) * pageSize).Take(pageSize).Select(cs => MapToListItemDto(cs, nowUtc)).ToList();

        return new PagedResult<ClassSessionListItemDto>(items, total, page, pageSize);
    }

    public async Task<ClassSessionDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var nowUtc = _gymCalendar.UtcNow;
        var session = await LoadSessionDetailQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(cs => cs.Id == id && cs.IsActive, cancellationToken);

        if (session == null) return null;

        // Autorización contextual para instructor
        if (_currentUserService.IsInRole(RoleType.Instructor.ToString()) &&
            !_currentUserService.IsInRole(RoleType.Administrator.ToString()) &&
            !_currentUserService.IsInRole(RoleType.Secretary.ToString()))
        {
            var myPersonId = _currentUserService.PersonId;
            if (myPersonId.HasValue && session.EffectiveInstructorId != myPersonId.Value && session.ScheduledInstructorId != myPersonId.Value)
            {
                return null;
            }
        }

        return MapToDetailDto(session, nowUtc);
    }

    public async Task<List<ClassSessionAvailabilityDto>> GetAvailableAsync(
        DateOnly? from = null,
        DateOnly? to = null,
        Guid? activityId = null,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = _gymCalendar.UtcNow;
        var query = _dbContext.ClassSessions
            .Include(cs => cs.Activity)
            .Include(cs => cs.Room)
            .Include(cs => cs.EffectiveInstructor)
            .AsNoTracking()
            .Where(cs => cs.IsActive && cs.Status == ClassSessionStatus.Open && cs.StartsAtUtc > nowUtc && cs.Activity.Status == ActivityStatus.Active);

        if (from.HasValue && to.HasValue)
        {
            var (startUtc, endUtc) = _gymCalendar.LocalDayRange(from.Value, to.Value);
            query = query.Where(cs => cs.StartsAtUtc >= startUtc && cs.StartsAtUtc < endUtc);
        }

        if (activityId.HasValue) query = query.Where(cs => cs.ActivityId == activityId.Value);

        var list = await query.OrderBy(cs => cs.StartsAtUtc).ToListAsync(cancellationToken);

        return list.Select(cs => new ClassSessionAvailabilityDto(
            cs.Id,
            cs.ActivityId,
            cs.Activity.Name,
            cs.RoomId,
            cs.Room.Name,
            cs.EffectiveInstructor.FullName,
            cs.StartsAtUtc,
            _gymCalendar.ToLocal(cs.StartsAtUtc),
            _gymCalendar.ToLocal(cs.EndsAtUtc),
            cs.Capacity,
            cs.ReservedCount,
            cs.AvailableSpots,
            !cs.HasAvailableSpots()
        )).ToList();
    }

    public async Task<ClassSessionDetailDto> CreateExtraordinaryAsync(CreateClassSessionRequest request, CancellationToken cancellationToken = default)
    {
        var nowUtc = _gymCalendar.UtcNow;
        var startsUtc = request.StartsAt.UtcDateTime;
        var endsUtc = request.EndsAt.UtcDateTime;

        if (startsUtc <= nowUtc)
        {
            throw new ArgumentException("El inicio de la sesión debe ser en el futuro.");
        }

        var activity = await _dbContext.Activities.FirstOrDefaultAsync(a => a.Id == request.ActivityId && a.IsActive, cancellationToken);
        if (activity == null || activity.Status != ActivityStatus.Active)
        {
            throw new ArgumentException("La actividad especificada no existe o no se encuentra activa.");
        }

        var capacity = request.Capacity ?? activity.DefaultCapacity ?? 0;
        if (capacity <= 0)
        {
            throw new ArgumentException("El cupo de la clase debe ser mayor a 0.");
        }

        await EnsureNoConflictsAsync(null, request.RoomId, request.InstructorId, startsUtc, endsUtc, cancellationToken);

        var session = ClassSession.CreateExtraordinary(
            request.ActivityId,
            request.RoomId,
            startsUtc,
            endsUtc,
            capacity,
            request.InstructorId,
            request.SecretaryId,
            request.Notes);

        _dbContext.ClassSessions.Add(session);
        await SaveChangesTranslatingConflictsAsync(cancellationToken);

        await _auditService.LogAsync(
            action: "CLASS_SESSION_CREATED",
            entityName: "ClassSession",
            entityId: session.Id.ToString(),
            oldValues: null,
            newValues: System.Text.Json.JsonSerializer.Serialize(new { session.StartsAtUtc, session.EndsAtUtc, session.Capacity, session.RoomId }),
            userId: _currentUserService.UserId,
            userEmail: _currentUserService.Email,
            ipAddress: null,
            cancellationToken: cancellationToken);

        var loaded = await LoadSessionDetailQuery().FirstAsync(cs => cs.Id == session.Id, cancellationToken);
        return MapToDetailDto(loaded, nowUtc);
    }

    public async Task<ClassSessionDetailDto> UpdateAsync(Guid id, UpdateClassSessionRequest request, CancellationToken cancellationToken = default)
    {
        var nowUtc = _gymCalendar.UtcNow;
        var session = await LoadSessionDetailQuery().FirstOrDefaultAsync(cs => cs.Id == id && cs.IsActive, cancellationToken);
        if (session == null) throw new NotFoundException("Clase no encontrada.");

        if (session.Version != request.Version)
        {
            throw new ConflictException("La clase ha sido modificada concurrentemente por otro usuario.");
        }

        // Las notas se pueden editar siempre; el cupo solo se valida si efectivamente cambia.
        if (request.Capacity != session.Capacity)
        {
            session.UpdateCapacity(request.Capacity, nowUtc);
        }
        session.UpdateNotes(request.Notes, nowUtc);

        await SaveChangesTranslatingConflictsAsync(cancellationToken);

        await _auditService.LogAsync(
            action: "CLASS_SESSION_UPDATED",
            entityName: "ClassSession",
            entityId: session.Id.ToString(),
            oldValues: null,
            newValues: System.Text.Json.JsonSerializer.Serialize(new { request.Capacity, request.Notes }),
            userId: _currentUserService.UserId,
            userEmail: _currentUserService.Email,
            ipAddress: null,
            cancellationToken: cancellationToken);

        return MapToDetailDto(session, nowUtc);
    }

    public async Task<ClassSessionDetailDto> OpenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var nowUtc = _gymCalendar.UtcNow;
        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await _dbContext.LockClassSessionForUpdateAsync(id, cancellationToken);

        var session = await LoadSessionDetailQuery().FirstOrDefaultAsync(cs => cs.Id == id && cs.IsActive, cancellationToken);
        if (session == null) throw new NotFoundException("Clase no encontrada.");

        session.Open(nowUtc);
        await SaveChangesTranslatingConflictsAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        await _auditService.LogAsync(
            action: "CLASS_SESSION_OPENED",
            entityName: "ClassSession",
            entityId: session.Id.ToString(),
            oldValues: null,
            newValues: System.Text.Json.JsonSerializer.Serialize(new { session.Status }),
            userId: _currentUserService.UserId,
            userEmail: _currentUserService.Email,
            ipAddress: null,
            cancellationToken: cancellationToken);

        return MapToDetailDto(session, nowUtc);
    }

    public async Task<ClassSessionDetailDto> SuspendAsync(Guid id, SuspendClassSessionRequest request, CancellationToken cancellationToken = default)
    {
        var nowUtc = _gymCalendar.UtcNow;
        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await _dbContext.LockClassSessionForUpdateAsync(id, cancellationToken);

        var session = await LoadSessionDetailQuery().FirstOrDefaultAsync(cs => cs.Id == id && cs.IsActive, cancellationToken);
        if (session == null) throw new NotFoundException("Clase no encontrada.");

        // Autorización contextual: un instructor solo suspende sus clases
        if (_currentUserService.IsInRole(RoleType.Instructor.ToString()) &&
            !_currentUserService.IsInRole(RoleType.Administrator.ToString()) &&
            !_currentUserService.IsInRole(RoleType.Secretary.ToString()))
        {
            var myPersonId = _currentUserService.PersonId;
            if (myPersonId.HasValue && session.EffectiveInstructorId != myPersonId.Value && session.ScheduledInstructorId != myPersonId.Value)
            {
                throw new NotFoundException("Clase no encontrada.");
            }
        }

        session.Suspend(request.Reason, request.Observations, _currentUserService.UserId, nowUtc);
        await SaveChangesTranslatingConflictsAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        await _auditService.LogAsync(
            action: "CLASS_SESSION_SUSPENDED",
            entityName: "ClassSession",
            entityId: session.Id.ToString(),
            oldValues: null,
            newValues: System.Text.Json.JsonSerializer.Serialize(new { request.Reason, request.Observations }),
            userId: _currentUserService.UserId,
            userEmail: _currentUserService.Email,
            ipAddress: null,
            cancellationToken: cancellationToken);

        return MapToDetailDto(session, nowUtc);
    }

    public async Task<ClassSessionDetailDto> CancelAsync(Guid id, CancelClassSessionRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Reason == ClassSessionCancellationReason.ScheduleEnded)
        {
            throw new ArgumentException("El motivo ScheduleEnded está reservado al sistema.");
        }

        var nowUtc = _gymCalendar.UtcNow;
        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await _dbContext.LockClassSessionForUpdateAsync(id, cancellationToken);

        var session = await LoadSessionDetailQuery().FirstOrDefaultAsync(cs => cs.Id == id && cs.IsActive, cancellationToken);
        if (session == null) throw new NotFoundException("Clase no encontrada.");

        session.Cancel(request.Reason, request.Observations, _currentUserService.UserId, nowUtc);
        await SaveChangesTranslatingConflictsAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        await _auditService.LogAsync(
            action: "CLASS_SESSION_CANCELLED",
            entityName: "ClassSession",
            entityId: session.Id.ToString(),
            oldValues: null,
            newValues: System.Text.Json.JsonSerializer.Serialize(new { request.Reason, request.Observations }),
            userId: _currentUserService.UserId,
            userEmail: _currentUserService.Email,
            ipAddress: null,
            cancellationToken: cancellationToken);

        return MapToDetailDto(session, nowUtc);
    }

    public async Task<ClassSessionDetailDto> RescheduleAsync(Guid id, RescheduleClassSessionRequest request, CancellationToken cancellationToken = default)
    {
        var nowUtc = _gymCalendar.UtcNow;
        var newStartsUtc = request.StartsAt.UtcDateTime;
        var newEndsUtc = request.EndsAt.UtcDateTime;

        if (newStartsUtc <= nowUtc)
        {
            throw new ArgumentException("El nuevo horario de la clase debe ser en el futuro.");
        }

        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await _dbContext.LockClassSessionForUpdateAsync(id, cancellationToken);

        var original = await LoadSessionDetailQuery().FirstOrDefaultAsync(cs => cs.Id == id && cs.IsActive, cancellationToken);
        if (original == null) throw new NotFoundException("Clase no encontrada.");

        var roomId = request.RoomId ?? original.RoomId;
        // La original deja de estar activa en este mismo guardado, así que no cuenta como conflicto.
        await EnsureNoConflictsAsync(original.Id, roomId, original.EffectiveInstructorId, newStartsUtc, newEndsUtc, cancellationToken);

        var newSession = ClassSession.CreateExtraordinary(
            original.ActivityId,
            roomId,
            newStartsUtc,
            newEndsUtc,
            original.Capacity,
            original.EffectiveInstructorId,
            original.EffectiveSecretaryId,
            original.Notes,
            rescheduledFromSessionId: original.Id);

        _dbContext.ClassSessions.Add(newSession);

        original.MarkRescheduled(newSession.Id, newStartsUtc, newEndsUtc, _currentUserService.UserId, nowUtc);

        // EF inserta la nueva antes de actualizar la original (FK RescheduledToSessionId), por lo que las
        // exclusiones verían a ambas activas si el nuevo horario se superpone con el original.
        await _dbContext.DeferClassSessionOverlapConstraintsAsync(cancellationToken);
        await SaveChangesTranslatingConflictsAsync(cancellationToken);
        await TranslatingConflictsAsync(() => _dbContext.EnforceDeferredConstraintsAsync(cancellationToken));
        await tx.CommitAsync(cancellationToken);

        await _auditService.LogAsync(
            action: "CLASS_SESSION_RESCHEDULED",
            entityName: "ClassSession",
            entityId: original.Id.ToString(),
            oldValues: null,
            newValues: System.Text.Json.JsonSerializer.Serialize(new { NewSessionId = newSession.Id, newStartsUtc, newEndsUtc, request.Reason }),
            userId: _currentUserService.UserId,
            userEmail: _currentUserService.Email,
            ipAddress: null,
            cancellationToken: cancellationToken);

        var loaded = await LoadSessionDetailQuery().FirstAsync(cs => cs.Id == newSession.Id, cancellationToken);
        return MapToDetailDto(loaded, nowUtc);
    }

    public async Task<ClassSessionDetailDto> ReplaceInstructorAsync(Guid id, ReplaceStaffRequest request, CancellationToken cancellationToken = default)
    {
        var nowUtc = _gymCalendar.UtcNow;
        if (!request.PersonId.HasValue) throw new ArgumentException("El nuevo instructor es obligatorio.");

        var session = await _dbContext.ClassSessions
            .Include(cs => cs.StaffChanges)
            .FirstOrDefaultAsync(cs => cs.Id == id && cs.IsActive, cancellationToken);
        if (session == null) throw new NotFoundException("Clase no encontrada.");

        if (session.Version != request.Version) throw new ConflictException("La clase ha sido modificada concurrentemente.");

        var newPerson = await _dbContext.People
            .Include(p => p.PersonRoles).ThenInclude(pr => pr.Role)
            .FirstOrDefaultAsync(p => p.Id == request.PersonId.Value && p.IsActive, cancellationToken);

        if (newPerson == null || !newPerson.PersonRoles.Any(pr => pr.IsActive && pr.Role.Name == RoleType.Instructor.ToString()))
        {
            throw new ArgumentException("La persona especificada no existe o no tiene el rol Instructor activo.");
        }

        // Verificar solapamiento para el instructor
        await EnsureNoConflictsAsync(session.Id, null, newPerson.Id, session.StartsAtUtc, session.EndsAtUtc, cancellationToken);

        var change = session.ReplaceInstructor(newPerson.Id, request.Reason, _currentUserService.UserId, nowUtc);
        _dbContext.ClassSessionStaffChanges.Add(change);
        await SaveChangesTranslatingConflictsAsync(cancellationToken);

        await _auditService.LogAsync(
            action: "CLASS_SESSION_INSTRUCTOR_REPLACED",
            entityName: "ClassSession",
            entityId: session.Id.ToString(),
            oldValues: null,
            newValues: System.Text.Json.JsonSerializer.Serialize(new { NewInstructorId = newPerson.Id, request.Reason }),
            userId: _currentUserService.UserId,
            userEmail: _currentUserService.Email,
            ipAddress: null,
            cancellationToken: cancellationToken);

        var loaded = await LoadSessionDetailQuery().FirstAsync(cs => cs.Id == session.Id, cancellationToken);
        return MapToDetailDto(loaded, nowUtc);
    }

    public async Task<ClassSessionDetailDto> ReplaceSecretaryAsync(Guid id, ReplaceStaffRequest request, CancellationToken cancellationToken = default)
    {
        var nowUtc = _gymCalendar.UtcNow;
        var session = await _dbContext.ClassSessions
            .Include(cs => cs.StaffChanges)
            .FirstOrDefaultAsync(cs => cs.Id == id && cs.IsActive, cancellationToken);
        if (session == null) throw new NotFoundException("Clase no encontrada.");

        if (session.Version != request.Version) throw new ConflictException("La clase ha sido modificada concurrentemente.");

        if (request.PersonId.HasValue)
        {
            var newPerson = await _dbContext.People
                .Include(p => p.PersonRoles).ThenInclude(pr => pr.Role)
                .FirstOrDefaultAsync(p => p.Id == request.PersonId.Value && p.IsActive, cancellationToken);

            if (newPerson == null || !newPerson.PersonRoles.Any(pr => pr.IsActive && (pr.Role.Name == RoleType.Secretary.ToString() || pr.Role.Name == RoleType.Administrator.ToString())))
            {
                throw new ArgumentException("La persona especificada no posee rol de Secretario o Administrador.");
            }
        }

        var change = session.ReplaceSecretary(request.PersonId, request.Reason, _currentUserService.UserId, nowUtc);
        _dbContext.ClassSessionStaffChanges.Add(change);
        await SaveChangesTranslatingConflictsAsync(cancellationToken);

        await _auditService.LogAsync(
            action: "CLASS_SESSION_SECRETARY_REPLACED",
            entityName: "ClassSession",
            entityId: session.Id.ToString(),
            oldValues: null,
            newValues: System.Text.Json.JsonSerializer.Serialize(new { NewSecretaryId = request.PersonId, request.Reason }),
            userId: _currentUserService.UserId,
            userEmail: _currentUserService.Email,
            ipAddress: null,
            cancellationToken: cancellationToken);

        var loaded = await LoadSessionDetailQuery().FirstAsync(cs => cs.Id == session.Id, cancellationToken);
        return MapToDetailDto(loaded, nowUtc);
    }

    public async Task<ClassSessionDetailDto> ChangeRoomAsync(Guid id, ChangeRoomRequest request, CancellationToken cancellationToken = default)
    {
        var nowUtc = _gymCalendar.UtcNow;
        var session = await LoadSessionDetailQuery().FirstOrDefaultAsync(cs => cs.Id == id && cs.IsActive, cancellationToken);
        if (session == null) throw new NotFoundException("Clase no encontrada.");

        if (session.Version != request.Version) throw new ConflictException("La clase ha sido modificada concurrentemente.");

        var room = await _dbContext.Rooms.FirstOrDefaultAsync(r => r.Id == request.RoomId && r.IsActive, cancellationToken);
        if (room == null) throw new ArgumentException("La sala especificada no existe o no está activa.");

        await EnsureNoConflictsAsync(session.Id, room.Id, null, session.StartsAtUtc, session.EndsAtUtc, cancellationToken);

        session.ChangeRoom(room.Id, _currentUserService.UserId, nowUtc);
        await SaveChangesTranslatingConflictsAsync(cancellationToken);

        await _auditService.LogAsync(
            action: "CLASS_SESSION_ROOM_CHANGED",
            entityName: "ClassSession",
            entityId: session.Id.ToString(),
            oldValues: null,
            newValues: System.Text.Json.JsonSerializer.Serialize(new { NewRoomId = room.Id }),
            userId: _currentUserService.UserId,
            userEmail: _currentUserService.Email,
            ipAddress: null,
            cancellationToken: cancellationToken);

        return MapToDetailDto(session, nowUtc);
    }

    private async Task EnsureNoConflictsAsync(Guid? currentId, Guid? roomId, Guid? instructorId, DateTime startsUtc, DateTime endsUtc, CancellationToken ct)
    {
        if (roomId.HasValue)
        {
            var conflict = await _dbContext.ClassSessions
                .Where(cs => cs.Id != currentId && cs.RoomId == roomId.Value && cs.IsActive &&
                             (cs.Status == ClassSessionStatus.Scheduled || cs.Status == ClassSessionStatus.Open) &&
                             cs.StartsAtUtc < endsUtc && cs.EndsAtUtc > startsUtc)
                .Select(cs => cs.Id)
                .ToListAsync(ct);

            if (conflict.Any())
            {
                throw new ConflictException("La sala especificada ya se encuentra ocupada en ese horario.", new { conflictingSessionIds = conflict });
            }
        }

        if (instructorId.HasValue)
        {
            var conflict = await _dbContext.ClassSessions
                .Where(cs => cs.Id != currentId && cs.EffectiveInstructorId == instructorId.Value && cs.IsActive &&
                             (cs.Status == ClassSessionStatus.Scheduled || cs.Status == ClassSessionStatus.Open) &&
                             cs.StartsAtUtc < endsUtc && cs.EndsAtUtc > startsUtc)
                .Select(cs => cs.Id)
                .ToListAsync(ct);

            if (conflict.Any())
            {
                throw new ConflictException("El instructor ya tiene otra clase asignada en ese horario.", new { conflictingSessionIds = conflict });
            }
        }
    }

    private async Task SaveChangesTranslatingConflictsAsync(CancellationToken ct)
    {
        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConflictException("Conflicto de concurrencia: el registro fue modificado por otra transacción.", ex);
        }
        catch (Exception ex) when (IsPostgresExclusionViolation(ex))
        {
            throw new ConflictException("Conflicto de solapamiento detectado en base de datos.", ex);
        }
    }

    private static async Task TranslatingConflictsAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (IsPostgresExclusionViolation(ex))
        {
            throw new ConflictException("Conflicto de solapamiento detectado en base de datos.", ex);
        }
    }

    private static bool IsPostgresExclusionViolation(Exception ex)
    {
        // ExecuteSql lanza la PostgresException directamente; SaveChanges la envuelve en DbUpdateException.
        return IsExclusionSqlState(ex) || (ex.InnerException != null && IsExclusionSqlState(ex.InnerException));
    }

    private static bool IsExclusionSqlState(Exception ex)
    {
        if (ex.GetType().Name != "PostgresException")
        {
            return false;
        }

        var prop = ex.GetType().GetProperty("SqlState");
        return prop != null && (string?)prop.GetValue(ex) == "23P01";
    }

    private IQueryable<ClassSession> LoadSessionDetailQuery()
    {
        return _dbContext.ClassSessions
            .Include(cs => cs.Activity)
            .Include(cs => cs.Room)
            .Include(cs => cs.ScheduledInstructor)
            .Include(cs => cs.EffectiveInstructor)
            .Include(cs => cs.ScheduledSecretary)
            .Include(cs => cs.EffectiveSecretary)
            .Include(cs => cs.StaffChanges).ThenInclude(sc => sc.PreviousPerson)
            .Include(cs => cs.StaffChanges).ThenInclude(sc => sc.NewPerson);
    }

    private ClassSessionListItemDto MapToListItemDto(ClassSession cs, DateTime nowUtc) =>
        new(
            cs.Id,
            cs.RecurringScheduleId,
            cs.ActivityId,
            cs.Activity?.Name ?? string.Empty,
            cs.RoomId,
            cs.Room?.Name ?? string.Empty,
            cs.ScheduledInstructorId,
            cs.ScheduledInstructor?.FullName ?? string.Empty,
            cs.EffectiveInstructorId,
            cs.EffectiveInstructor?.FullName ?? string.Empty,
            cs.ScheduledSecretaryId,
            cs.ScheduledSecretary?.FullName,
            cs.EffectiveSecretaryId,
            cs.EffectiveSecretary?.FullName,
            cs.StartsAtUtc,
            _gymCalendar.ToLocal(cs.StartsAtUtc),
            _gymCalendar.ToLocal(cs.EndsAtUtc),
            cs.Capacity,
            cs.ReservedCount,
            cs.AttendedCount,
            cs.AvailableSpots,
            cs.Status,
            cs.GetDisplayStatus(nowUtc),
            cs.IsDetachedFromSchedule,
            cs.Version
        );

    private ClassSessionDetailDto MapToDetailDto(ClassSession cs, DateTime nowUtc) =>
        new(
            cs.Id,
            cs.RecurringScheduleId,
            cs.ActivityId,
            cs.Activity?.Name ?? string.Empty,
            cs.RoomId,
            cs.Room?.Name ?? string.Empty,
            cs.ScheduledInstructorId,
            cs.ScheduledInstructor?.FullName ?? string.Empty,
            cs.EffectiveInstructorId,
            cs.EffectiveInstructor?.FullName ?? string.Empty,
            cs.ScheduledSecretaryId,
            cs.ScheduledSecretary?.FullName,
            cs.EffectiveSecretaryId,
            cs.EffectiveSecretary?.FullName,
            cs.StartsAtUtc,
            cs.EndsAtUtc,
            _gymCalendar.ToLocal(cs.StartsAtUtc),
            _gymCalendar.ToLocal(cs.EndsAtUtc),
            cs.Capacity,
            cs.ReservedCount,
            cs.AttendedCount,
            cs.AvailableSpots,
            cs.Status,
            cs.GetDisplayStatus(nowUtc),
            cs.IsDetachedFromSchedule,
            cs.Notes,
            cs.SuspendedAtUtc,
            cs.SuspendedByUserId,
            cs.SuspensionReason,
            cs.SuspensionObservations,
            cs.CancelledAtUtc,
            cs.CancelledByUserId,
            cs.CancellationReason,
            cs.CancellationObservations,
            cs.RescheduledFromSessionId,
            cs.RescheduledToSessionId,
            cs.StaffChanges.Select(sc => new ClassSessionStaffChangeDto(
                sc.Id,
                sc.Role,
                sc.PreviousPersonId,
                sc.PreviousPerson?.FullName,
                sc.NewPersonId,
                sc.NewPerson?.FullName,
                sc.Reason,
                sc.ChangedByUserId,
                sc.ChangedAtUtc
            )).ToList(),
            cs.Version
        );
}
