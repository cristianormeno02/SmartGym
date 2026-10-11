namespace SmartGym.Application.Common;

public interface IGymCalendar
{
    TimeZoneInfo TimeZone { get; }
    DateTime UtcNow { get; }
    DateOnly Today { get; }
    DateTime ToUtc(DateOnly date, TimeOnly time);
    DateTimeOffset ToLocal(DateTime utc);
    (DateTime FromUtc, DateTime ToUtc) LocalDayRange(DateOnly from, DateOnly to);
}
