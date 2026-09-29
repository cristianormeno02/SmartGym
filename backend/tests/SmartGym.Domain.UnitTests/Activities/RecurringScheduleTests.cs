using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using Xunit;

namespace SmartGym.Domain.UnitTests.Activities;

public class RecurringScheduleTests
{
    [Fact]
    public void RecurringSchedule_ShouldBeValid_WhenProperlyConfigured()
    {
        // Arrange
        var activityId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        var instructorId = Guid.NewGuid();
        var validFrom = new DateOnly(2026, 3, 1);
        var validTo = new DateOnly(2026, 12, 31);
        var startTime = new TimeOnly(18, 0);
        var endTime = new TimeOnly(19, 0);

        // Act
        var schedule = new RecurringSchedule
        {
            Id = Guid.NewGuid(),
            ActivityId = activityId,
            RoomId = roomId,
            InstructorId = instructorId,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = startTime,
            EndTime = endTime,
            ValidFrom = validFrom,
            ValidTo = validTo,
            MaxCapacity = 25
        };

        // Assert
        Assert.Equal(DayOfWeek.Monday, schedule.DayOfWeek);
        Assert.Equal(startTime, schedule.StartTime);
        Assert.Equal(endTime, schedule.EndTime);
        Assert.True(schedule.IsActiveOn(new DateOnly(2026, 5, 4))); // Monday in range
        Assert.True(schedule.OccursOn(new DateOnly(2026, 5, 4))); // Monday in range
        Assert.False(schedule.OccursOn(new DateOnly(2026, 5, 5))); // Tuesday in range
        Assert.False(schedule.OccursOn(new DateOnly(2026, 2, 23))); // Monday before ValidFrom
    }

    [Fact]
    public void RecurringSchedule_ShouldThrowException_WhenEndTimeIsBeforeOrEqualStartTime()
    {
        // Arrange & Act & Assert
        var schedule = new RecurringSchedule();
        Assert.Throws<ArgumentException>(() =>
            schedule.SetTimeSlot(new TimeOnly(18, 0), new TimeOnly(17, 30)));
    }

    [Fact]
    public void RecurringSchedule_ShouldThrowException_WhenValidToIsBeforeValidFrom()
    {
        // Arrange & Act & Assert
        var schedule = new RecurringSchedule();
        Assert.Throws<ArgumentException>(() =>
            schedule.SetValidityPeriod(new DateOnly(2026, 6, 1), new DateOnly(2026, 5, 1)));
    }
}
