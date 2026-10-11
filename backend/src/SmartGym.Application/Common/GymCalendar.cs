using Microsoft.Extensions.Options;

namespace SmartGym.Application.Common;

public class GymCalendar : IGymCalendar
{
    private readonly TimeProvider _timeProvider;

    public TimeZoneInfo TimeZone { get; }

    public GymCalendar(IOptions<GymOptions> options, TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        var timeZoneId = options.Value.TimeZoneId;
        TimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
    }

    public DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    public DateOnly Today
    {
        get
        {
            var localOffset = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), TimeZone);
            return DateOnly.FromDateTime(localOffset.DateTime);
        }
    }

    public DateTime ToUtc(DateOnly date, TimeOnly time)
    {
        var localDateTime = date.ToDateTime(time);

        // If localDateTime falls in an invalid gap (e.g. DST jump 00:00 -> 01:00)
        if (TimeZone.IsInvalidTime(localDateTime))
        {
            var gap = TimeZone.GetAdjustmentRules()
                .Where(r => r.DateStart <= localDateTime && r.DateEnd >= localDateTime)
                .Select(r => r.DaylightDelta)
                .FirstOrDefault();

            if (gap == TimeSpan.Zero)
            {
                gap = TimeSpan.FromHours(1);
            }

            localDateTime = localDateTime.Add(gap);
        }

        // If localDateTime is ambiguous, convert using standard or daylight
        var utcDateTime = TimeZoneInfo.ConvertTimeToUtc(localDateTime, TimeZone);
        return DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
    }

    public DateTimeOffset ToLocal(DateTime utc)
    {
        var utcKind = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTime(new DateTimeOffset(utcKind), TimeZone);
    }

    public (DateTime FromUtc, DateTime ToUtc) LocalDayRange(DateOnly from, DateOnly to)
    {
        var startUtc = ToUtc(from, TimeOnly.MinValue);
        var endUtc = ToUtc(to.AddDays(1), TimeOnly.MinValue);
        return (startUtc, endUtc);
    }
}
