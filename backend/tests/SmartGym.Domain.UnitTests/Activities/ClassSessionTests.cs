using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Enums;
using Xunit;

namespace SmartGym.Domain.UnitTests.Activities;

public class ClassSessionTests
{
    [Fact]
    public void ClassSession_ShouldInitializeInScheduledState()
    {
        // Arrange
        var session = new ClassSession
        {
            Id = Guid.NewGuid(),
            ActivityId = Guid.NewGuid(),
            RoomId = Guid.NewGuid(),
            InstructorId = Guid.NewGuid(),
            Date = new DateOnly(2026, 4, 15),
            StartTime = new TimeOnly(10, 0),
            EndTime = new TimeOnly(11, 0),
            MaxCapacity = 20
        };

        // Assert
        Assert.Equal(ClassSessionStatus.Scheduled, session.Status);
        Assert.Equal(0, session.ReservedCount);
        Assert.True(session.HasAvailableSpots());
    }

    [Fact]
    public void ClassSession_ShouldTransitionLifecycleStatesCorrectly()
    {
        // Arrange
        var session = new ClassSession
        {
            Id = Guid.NewGuid(),
            ActivityId = Guid.NewGuid(),
            RoomId = Guid.NewGuid(),
            InstructorId = Guid.NewGuid(),
            Date = new DateOnly(2026, 4, 15),
            StartTime = new TimeOnly(10, 0),
            EndTime = new TimeOnly(11, 0),
            MaxCapacity = 20
        };

        // Act & Assert transitions
        session.Start();
        Assert.Equal(ClassSessionStatus.InProgress, session.Status);

        session.Finish();
        Assert.Equal(ClassSessionStatus.Finished, session.Status);
    }

    [Fact]
    public void ClassSession_ShouldSuspendWithReasonAndAudit()
    {
        // Arrange
        var session = new ClassSession
        {
            Id = Guid.NewGuid(),
            ActivityId = Guid.NewGuid(),
            RoomId = Guid.NewGuid(),
            InstructorId = Guid.NewGuid(),
            Date = new DateOnly(2026, 4, 15),
            StartTime = new TimeOnly(10, 0),
            EndTime = new TimeOnly(11, 0),
            MaxCapacity = 20
        };

        // Act
        session.Suspend("Mantenimiento de urgencia en la sala");

        // Assert
        Assert.Equal(ClassSessionStatus.Suspended, session.Status);
        Assert.Equal("Mantenimiento de urgencia en la sala", session.CancellationReason);
        Assert.NotNull(session.SuspendedAtUtc);
    }

    [Fact]
    public void ClassSession_ShouldTrackSubstituteInstructor()
    {
        // Arrange
        var originalInstructorId = Guid.NewGuid();
        var substituteInstructorId = Guid.NewGuid();

        var session = new ClassSession
        {
            Id = Guid.NewGuid(),
            ActivityId = Guid.NewGuid(),
            RoomId = Guid.NewGuid(),
            InstructorId = originalInstructorId,
            Date = new DateOnly(2026, 4, 15),
            StartTime = new TimeOnly(10, 0),
            EndTime = new TimeOnly(11, 0),
            MaxCapacity = 20
        };

        // Act
        session.AssignSubstituteInstructor(substituteInstructorId);

        // Assert
        Assert.Equal(substituteInstructorId, session.InstructorId);
        Assert.Equal(originalInstructorId, session.OriginalInstructorId);
        Assert.Equal(substituteInstructorId, session.SubstituteInstructorId);
    }
}
