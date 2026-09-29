using SmartGym.Domain.Common;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;

namespace SmartGym.Domain.Entities.Activities;

public class ClassSession : BaseEntity
{
    public Guid? RecurringScheduleId { get; set; }
    public RecurringSchedule? RecurringSchedule { get; set; }

    public Guid ActivityId { get; set; }
    public Activity Activity { get; set; } = null!;

    public Guid RoomId { get; set; }
    public Room Room { get; set; } = null!;

    public Guid InstructorId { get; set; }
    public Person Instructor { get; set; } = null!;

    public Guid? OriginalInstructorId { get; set; }
    public Person? OriginalInstructor { get; set; }

    public Guid? SubstituteInstructorId { get; set; }
    public Person? SubstituteInstructor { get; set; }

    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    public int MaxCapacity { get; set; }
    public int ReservedCount { get; set; } = 0;
    public int AttendedCount { get; set; } = 0;

    public ClassSessionStatus Status { get; set; } = ClassSessionStatus.Scheduled;
    public string? CancellationReason { get; set; }
    public DateTime? SuspendedAtUtc { get; set; }

    public bool HasAvailableSpots() => ReservedCount < MaxCapacity;

    public void Start()
    {
        if (Status != ClassSessionStatus.Scheduled)
        {
            throw new InvalidOperationException($"No se puede iniciar una clase en estado {Status}.");
        }

        Status = ClassSessionStatus.InProgress;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Finish()
    {
        if (Status != ClassSessionStatus.InProgress && Status != ClassSessionStatus.Scheduled)
        {
            throw new InvalidOperationException($"No se puede finalizar una clase en estado {Status}.");
        }

        Status = ClassSessionStatus.Finished;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Suspend(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Debe especificar un motivo para suspender la clase.", nameof(reason));
        }

        Status = ClassSessionStatus.Suspended;
        CancellationReason = reason.Trim();
        SuspendedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Cancel(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Debe especificar un motivo para cancelar la clase.", nameof(reason));
        }

        Status = ClassSessionStatus.Cancelled;
        CancellationReason = reason.Trim();
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void AssignSubstituteInstructor(Guid substituteId)
    {
        if (OriginalInstructorId == null)
        {
            OriginalInstructorId = InstructorId;
        }

        InstructorId = substituteId;
        SubstituteInstructorId = substituteId;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
