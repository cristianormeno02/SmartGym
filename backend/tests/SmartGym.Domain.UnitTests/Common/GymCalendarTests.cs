using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using Xunit;

namespace SmartGym.Domain.UnitTests.Common;

public class GymCalendarTests
{
    private class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public FakeTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }
    }

    [Fact]
    public void ToUtc_And_ToLocal_SanJuan_ConvertsCorrectly()
    {
        // San Juan, Argentina is UTC-3 year-round (no DST)
        var options = Options.Create(new GymOptions { TimeZoneId = "America/Argentina/San_Juan" });
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var calendar = new GymCalendar(options, timeProvider);

        var date = new DateOnly(2026, 10, 14);
        var time = new TimeOnly(19, 0);

        var utc = calendar.ToUtc(date, time);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(new DateTime(2026, 10, 14, 22, 0, 0, DateTimeKind.Utc), utc);

        var local = calendar.ToLocal(utc);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 19, 0, 0, TimeSpan.FromHours(-3)), local);
    }

    [Fact]
    public void Today_NearMidnightUtc_ReturnsGymLocalDay()
    {
        // At 01:00 UTC on Oct 15, in San Juan (UTC-3) it is 22:00 on Oct 14.
        var options = Options.Create(new GymOptions { TimeZoneId = "America/Argentina/San_Juan" });
        var utcTime = new DateTimeOffset(2026, 10, 15, 1, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(utcTime);
        var calendar = new GymCalendar(options, timeProvider);

        Assert.Equal(new DateOnly(2026, 10, 14), calendar.Today);
    }

    [Fact]
    public void LocalDayRange_ReturnsInclusiveUtcBounds()
    {
        var options = Options.Create(new GymOptions { TimeZoneId = "America/Argentina/San_Juan" });
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var calendar = new GymCalendar(options, timeProvider);

        var from = new DateOnly(2026, 10, 14);
        var to = new DateOnly(2026, 10, 14);

        var (fromUtc, toUtc) = calendar.LocalDayRange(from, to);

        // 14/10 00:00 local is 14/10 03:00 UTC
        Assert.Equal(new DateTime(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc), fromUtc);
        // 15/10 00:00 local is 15/10 03:00 UTC
        Assert.Equal(new DateTime(2026, 10, 15, 3, 0, 0, DateTimeKind.Utc), toUtc);
    }

    [Fact]
    public void ToUtc_AmbiguousOrInvalidTimeInDstZone_HandlesGracefully()
    {
        // America/Santiago has DST. Test that an invalid or ambiguous local time does not throw.
        var options = Options.Create(new GymOptions { TimeZoneId = "America/Santiago" });
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var calendar = new GymCalendar(options, timeProvider);

        // Santiago DST gap typically happens in September (00:00 -> 01:00 skip, so 00:30 is invalid local time)
        var date = new DateOnly(2026, 9, 6);
        var invalidLocalTime = new TimeOnly(0, 30);

        var utc = calendar.ToUtc(date, invalidLocalTime);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
    }
}
