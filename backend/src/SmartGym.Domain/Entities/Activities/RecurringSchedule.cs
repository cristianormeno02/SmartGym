using SmartGym.Domain.Common;
using SmartGym.Domain.Entities.Identity;

namespace SmartGym.Domain.Entities.Activities;

public class RecurringSchedule : BaseEntity
{
    public Guid ActivityId { get; set; }
    public Activity Activity { get; set; } = null!;

    public Guid RoomId { get; set; }
    public Room Room { get; set; } = null!;

    public Guid InstructorId { get; set; }
    public Person Instructor { get; set; } = null!;

    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }

    public int? MaxCapacity { get; set; }

    public void SetTimeSlot(TimeOnly startTime, TimeOnly endTime)
    {
        if (endTime <= startTime)
        {
            throw new ArgumentException("La hora de finalización debe ser posterior a la de inicio.", nameof(endTime));
        }

        StartTime = startTime;
        EndTime = endTime;
    }

    public void SetValidityPeriod(DateOnly validFrom, DateOnly? validTo)
    {
        if (validTo.HasValue && validTo.Value < validFrom)
        {
            throw new ArgumentException("La fecha de fin de vigencia no puede ser anterior a la de inicio.", nameof(validTo));
        }

        ValidFrom = validFrom;
        ValidTo = validTo;
    }

    public bool IsActiveOn(DateOnly date)
    {
        if (!IsActive) return false;
        if (date < ValidFrom) return false;
        if (ValidTo.HasValue && date > ValidTo.Value) return false;
        return true;
    }

    public bool OccursOn(DateOnly date)
    {
        return IsActiveOn(date) && date.DayOfWeek == DayOfWeek;
    }
}
