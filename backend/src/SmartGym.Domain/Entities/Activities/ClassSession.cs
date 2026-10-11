using System;
using System.Collections.Generic;
using SmartGym.Domain.Common;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Activities;

namespace SmartGym.Domain.Entities.Activities;

public class ClassSession : BaseEntity, IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = new();
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();
    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public Guid? RecurringScheduleId { get; private set; }
    public RecurringSchedule? RecurringSchedule { get; private set; }

    public Guid ActivityId { get; private set; }
    public Activity Activity { get; private set; } = null!;

    public Guid RoomId { get; private set; }
    public Room Room { get; private set; } = null!;

    public DateTime StartsAtUtc { get; private set; }
    public DateTime EndsAtUtc { get; private set; }
    public int Capacity { get; private set; }

    public int ReservedCount { get; private set; } = 0;
    public int AttendedCount { get; private set; } = 0;

    public ClassSessionStatus Status { get; private set; } = ClassSessionStatus.Scheduled;

    public Guid ScheduledInstructorId { get; private set; }
    public Person ScheduledInstructor { get; private set; } = null!;

    public Guid EffectiveInstructorId { get; private set; }
    public Person EffectiveInstructor { get; private set; } = null!;

    public Guid? ScheduledSecretaryId { get; private set; }
    public Person? ScheduledSecretary { get; private set; }

    public Guid? EffectiveSecretaryId { get; private set; }
    public Person? EffectiveSecretary { get; private set; }

    public bool IsDetachedFromSchedule { get; private set; } = false;
    public string? Notes { get; private set; }

    public DateTime? SuspendedAtUtc { get; private set; }
    public Guid? SuspendedByUserId { get; private set; }
    public User? SuspendedByUser { get; private set; }
    public ClassSessionSuspensionReason? SuspensionReason { get; private set; }
    public string? SuspensionObservations { get; private set; }

    public DateTime? CancelledAtUtc { get; private set; }
    public Guid? CancelledByUserId { get; private set; }
    public User? CancelledByUser { get; private set; }
    public ClassSessionCancellationReason? CancellationReason { get; private set; }
    public string? CancellationObservations { get; private set; }

    public Guid? RescheduledFromSessionId { get; private set; }
    public ClassSession? RescheduledFromSession { get; private set; }

    public Guid? RescheduledToSessionId { get; private set; }
    public ClassSession? RescheduledToSession { get; private set; }

    public uint Version { get; set; }

    private readonly List<ClassSessionStaffChange> _staffChanges = new();
    public IReadOnlyCollection<ClassSessionStaffChange> StaffChanges => _staffChanges.AsReadOnly();

    public int AvailableSpots => Math.Max(0, Capacity - ReservedCount);
    public bool HasAvailableSpots() => AvailableSpots > 0;

    private ClassSession() { }

    public static ClassSession CreateFromSchedule(
        Guid recurringScheduleId,
        Guid activityId,
        Guid roomId,
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        int capacity,
        Guid instructorId,
        Guid? secretaryId,
        string? notes = null)
    {
        ValidateCreation(startsAtUtc, endsAtUtc, capacity);

        return new ClassSession
        {
            Id = Guid.NewGuid(),
            RecurringScheduleId = recurringScheduleId,
            ActivityId = activityId,
            RoomId = roomId,
            StartsAtUtc = startsAtUtc,
            EndsAtUtc = endsAtUtc,
            Capacity = capacity,
            ScheduledInstructorId = instructorId,
            EffectiveInstructorId = instructorId,
            ScheduledSecretaryId = secretaryId,
            EffectiveSecretaryId = secretaryId,
            Notes = notes,
            Status = ClassSessionStatus.Scheduled,
            IsDetachedFromSchedule = false,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            IsActive = true
        };
    }

    public static ClassSession CreateExtraordinary(
        Guid activityId,
        Guid roomId,
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        int capacity,
        Guid instructorId,
        Guid? secretaryId,
        string? notes = null,
        Guid? rescheduledFromSessionId = null)
    {
        ValidateCreation(startsAtUtc, endsAtUtc, capacity);

        return new ClassSession
        {
            Id = Guid.NewGuid(),
            RecurringScheduleId = null,
            ActivityId = activityId,
            RoomId = roomId,
            StartsAtUtc = startsAtUtc,
            EndsAtUtc = endsAtUtc,
            Capacity = capacity,
            ScheduledInstructorId = instructorId,
            EffectiveInstructorId = instructorId,
            ScheduledSecretaryId = secretaryId,
            EffectiveSecretaryId = secretaryId,
            Notes = notes,
            RescheduledFromSessionId = rescheduledFromSessionId,
            Status = ClassSessionStatus.Scheduled,
            IsDetachedFromSchedule = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            IsActive = true
        };
    }

    private static void ValidateCreation(DateTime startsAtUtc, DateTime endsAtUtc, int capacity)
    {
        if (endsAtUtc <= startsAtUtc)
        {
            throw new ArgumentException("La fecha/hora de finalización debe ser posterior a la de inicio.", nameof(endsAtUtc));
        }

        if (endsAtUtc - startsAtUtc > TimeSpan.FromHours(24))
        {
            throw new ArgumentException("La duración de la sesión no puede exceder las 24 horas.", nameof(endsAtUtc));
        }

        if (capacity <= 0)
        {
            throw new ArgumentException("El cupo debe ser mayor a 0.", nameof(capacity));
        }
    }

    public bool IsClosed(DateTime nowUtc)
    {
        return nowUtc >= EndsAtUtc ||
               Status == ClassSessionStatus.Suspended ||
               Status == ClassSessionStatus.Cancelled ||
               Status == ClassSessionStatus.Rescheduled;
    }

    public ClassSessionDisplayStatus GetDisplayStatus(DateTime nowUtc)
    {
        if (Status == ClassSessionStatus.Suspended) return ClassSessionDisplayStatus.Suspended;
        if (Status == ClassSessionStatus.Cancelled) return ClassSessionDisplayStatus.Cancelled;
        if (Status == ClassSessionStatus.Rescheduled) return ClassSessionDisplayStatus.Rescheduled;

        if (nowUtc >= EndsAtUtc)
        {
            return ClassSessionDisplayStatus.Finished;
        }

        if (nowUtc >= StartsAtUtc)
        {
            return ClassSessionDisplayStatus.InProgress;
        }

        if (Status == ClassSessionStatus.Open && ReservedCount >= Capacity)
        {
            return ClassSessionDisplayStatus.Full;
        }

        return Status == ClassSessionStatus.Open ? ClassSessionDisplayStatus.Open : ClassSessionDisplayStatus.Scheduled;
    }

    public void Open(DateTime nowUtc)
    {
        if (Status != ClassSessionStatus.Scheduled)
        {
            throw new InvalidOperationException($"No se puede abrir una clase en estado {Status}.");
        }

        if (nowUtc >= StartsAtUtc)
        {
            throw new InvalidOperationException("No se puede abrir una clase cuya fecha/hora de inicio ya ha comenzado o pasado.");
        }

        Status = ClassSessionStatus.Open;
        UpdatedAtUtc = nowUtc;
    }

    public void Suspend(
        ClassSessionSuspensionReason reason,
        string? observations,
        Guid? byUserId,
        DateTime nowUtc)
    {
        if (Status != ClassSessionStatus.Scheduled && Status != ClassSessionStatus.Open)
        {
            throw new InvalidOperationException($"No se puede suspender una clase en estado {Status}.");
        }

        if (nowUtc >= EndsAtUtc)
        {
            throw new InvalidOperationException("No se puede suspender una clase que ya ha finalizado.");
        }

        if (reason == ClassSessionSuspensionReason.Other && string.IsNullOrWhiteSpace(observations))
        {
            throw new ArgumentException("Las observaciones son obligatorias cuando el motivo de suspensión es 'Other'.", nameof(observations));
        }

        Status = ClassSessionStatus.Suspended;
        SuspendedAtUtc = nowUtc;
        SuspendedByUserId = byUserId;
        SuspensionReason = reason;
        SuspensionObservations = observations?.Trim();
        UpdatedAtUtc = nowUtc;

        AddDomainEvent(new ClassSessionSuspended(Id, reason, SuspensionObservations, byUserId, nowUtc));
    }

    public void Cancel(
        ClassSessionCancellationReason reason,
        string? observations,
        Guid? byUserId,
        DateTime nowUtc)
    {
        if (Status != ClassSessionStatus.Scheduled && Status != ClassSessionStatus.Open)
        {
            throw new InvalidOperationException($"No se puede cancelar una clase en estado {Status}.");
        }

        if (nowUtc >= StartsAtUtc)
        {
            throw new InvalidOperationException("No se puede cancelar una clase que ya ha iniciado.");
        }

        if (reason == ClassSessionCancellationReason.Other && string.IsNullOrWhiteSpace(observations))
        {
            throw new ArgumentException("Las observaciones son obligatorias cuando el motivo de cancelación es 'Other'.", nameof(observations));
        }

        Status = ClassSessionStatus.Cancelled;
        CancelledAtUtc = nowUtc;
        CancelledByUserId = byUserId;
        CancellationReason = reason;
        CancellationObservations = observations?.Trim();
        UpdatedAtUtc = nowUtc;

        AddDomainEvent(new ClassSessionCancelled(Id, reason, CancellationObservations, byUserId, nowUtc));
    }

    public void MarkRescheduled(
        Guid rescheduledToSessionId,
        DateTime newStartsAtUtc,
        DateTime newEndsAtUtc,
        Guid? byUserId,
        DateTime nowUtc)
    {
        if (Status != ClassSessionStatus.Scheduled && Status != ClassSessionStatus.Open)
        {
            throw new InvalidOperationException($"No se puede reprogramar una clase en estado {Status}.");
        }

        if (nowUtc >= StartsAtUtc)
        {
            throw new InvalidOperationException("No se puede reprogramar una clase que ya ha iniciado.");
        }

        Status = ClassSessionStatus.Rescheduled;
        RescheduledToSessionId = rescheduledToSessionId;
        UpdatedAtUtc = nowUtc;

        AddDomainEvent(new ClassSessionRescheduled(Id, rescheduledToSessionId, newStartsAtUtc, newEndsAtUtc, byUserId));
    }

    public ClassSessionStaffChange ReplaceInstructor(
        Guid newInstructorId,
        string reason,
        Guid? byUserId,
        DateTime nowUtc)
    {
        if (IsClosed(nowUtc))
        {
            throw new InvalidOperationException("No se puede cambiar el instructor en una sesión cerrada o finalizada.");
        }

        if (newInstructorId == EffectiveInstructorId)
        {
            throw new InvalidOperationException("El nuevo instructor efectivo debe ser diferente al actual.");
        }

        var previousId = EffectiveInstructorId;
        EffectiveInstructorId = newInstructorId;
        IsDetachedFromSchedule = true;
        UpdatedAtUtc = nowUtc;

        var change = ClassSessionStaffChange.Create(Id, "Instructor", previousId, newInstructorId, reason, byUserId, nowUtc);
        _staffChanges.Add(change);
        return change;
    }

    public ClassSessionStaffChange ReplaceSecretary(
        Guid? newSecretaryId,
        string reason,
        Guid? byUserId,
        DateTime nowUtc)
    {
        if (IsClosed(nowUtc))
        {
            throw new InvalidOperationException("No se puede cambiar el secretario en una sesión cerrada o finalizada.");
        }

        if (newSecretaryId == EffectiveSecretaryId)
        {
            throw new InvalidOperationException("El nuevo secretario efectivo debe ser diferente al actual.");
        }

        var previousId = EffectiveSecretaryId;
        EffectiveSecretaryId = newSecretaryId;
        IsDetachedFromSchedule = true;
        UpdatedAtUtc = nowUtc;

        var change = ClassSessionStaffChange.Create(Id, "Secretary", previousId, newSecretaryId, reason, byUserId, nowUtc);
        _staffChanges.Add(change);
        return change;
    }

    public void ChangeRoom(Guid newRoomId, Guid? byUserId, DateTime nowUtc)
    {
        if (IsClosed(nowUtc))
        {
            throw new InvalidOperationException("No se puede cambiar la sala en una sesión cerrada o finalizada.");
        }

        if (newRoomId == RoomId)
        {
            return;
        }

        var previousRoomId = RoomId;
        RoomId = newRoomId;
        IsDetachedFromSchedule = true;
        UpdatedAtUtc = nowUtc;

        AddDomainEvent(new ClassSessionRoomChanged(Id, previousRoomId, newRoomId, byUserId));
    }

    public void UpdateNotes(string? notes, DateTime nowUtc)
    {
        Notes = notes?.Trim();
        IsDetachedFromSchedule = true;
        UpdatedAtUtc = nowUtc;
    }

    public void UpdateCapacity(int capacity, DateTime nowUtc)
    {
        if (IsClosed(nowUtc) || nowUtc >= StartsAtUtc)
        {
            throw new InvalidOperationException("No se puede modificar el cupo de una clase que ya ha iniciado o está cerrada.");
        }

        if (capacity <= 0)
        {
            throw new ArgumentException("El cupo debe ser mayor a 0.", nameof(capacity));
        }

        if (capacity < ReservedCount)
        {
            throw new InvalidOperationException($"El cupo no puede ser menor a la cantidad de reservas existentes ({ReservedCount}).");
        }

        Capacity = capacity;
        IsDetachedFromSchedule = true;
        UpdatedAtUtc = nowUtc;
    }

    public void SyncFromSchedule(
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        Guid roomId,
        int capacity,
        Guid scheduledInstructorId,
        Guid? scheduledSecretaryId)
    {
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        RoomId = roomId;
        Capacity = capacity;
        ScheduledInstructorId = scheduledInstructorId;
        EffectiveInstructorId = scheduledInstructorId;
        ScheduledSecretaryId = scheduledSecretaryId;
        EffectiveSecretaryId = scheduledSecretaryId;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void IncrementReservedCount()
    {
        if (ReservedCount >= Capacity)
        {
            throw new InvalidOperationException("No hay cupos disponibles.");
        }
        ReservedCount++;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void DecrementReservedCount()
    {
        if (ReservedCount > 0)
        {
            ReservedCount--;
            UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    public void ResetReservedCount()
    {
        ReservedCount = 0;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void IncrementAttendedCount()
    {
        AttendedCount++;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void DecrementAttendedCount()
    {
        if (AttendedCount > 0)
        {
            AttendedCount--;
            UpdatedAtUtc = DateTime.UtcNow;
        }
    }
}
