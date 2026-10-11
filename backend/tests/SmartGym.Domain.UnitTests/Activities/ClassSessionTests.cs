using System;
using System.Linq;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Activities;
using Xunit;

namespace SmartGym.Domain.UnitTests.Activities;

public class ClassSessionTests
{
    private readonly Guid _scheduleId = Guid.NewGuid();
    private readonly Guid _activityId = Guid.NewGuid();
    private readonly Guid _roomId = Guid.NewGuid();
    private readonly Guid _instructorId = Guid.NewGuid();
    private readonly Guid _secretaryId = Guid.NewGuid();

    [Fact]
    public void CreateFromSchedule_InitializesCorrectly()
    {
        var starts = new DateTime(2026, 10, 14, 21, 0, 0, DateTimeKind.Utc);
        var ends = new DateTime(2026, 10, 14, 22, 0, 0, DateTimeKind.Utc);

        var session = ClassSession.CreateFromSchedule(
            _scheduleId,
            _activityId,
            _roomId,
            starts,
            ends,
            20,
            _instructorId,
            _secretaryId,
            "Notes test");

        Assert.Equal(_scheduleId, session.RecurringScheduleId);
        Assert.Equal(_activityId, session.ActivityId);
        Assert.Equal(_roomId, session.RoomId);
        Assert.Equal(starts, session.StartsAtUtc);
        Assert.Equal(ends, session.EndsAtUtc);
        Assert.Equal(20, session.Capacity);
        Assert.Equal(_instructorId, session.ScheduledInstructorId);
        Assert.Equal(_instructorId, session.EffectiveInstructorId);
        Assert.Equal(_secretaryId, session.ScheduledSecretaryId);
        Assert.Equal(_secretaryId, session.EffectiveSecretaryId);
        Assert.Equal("Notes test", session.Notes);
        Assert.Equal(ClassSessionStatus.Scheduled, session.Status);
        Assert.False(session.IsDetachedFromSchedule);
        Assert.Equal(20, session.AvailableSpots);
        Assert.True(session.HasAvailableSpots());
    }

    [Fact]
    public void CreateExtraordinary_InitializesWithNullScheduleAndDetached()
    {
        var starts = new DateTime(2026, 10, 14, 21, 0, 0, DateTimeKind.Utc);
        var ends = new DateTime(2026, 10, 14, 22, 0, 0, DateTimeKind.Utc);

        var session = ClassSession.CreateExtraordinary(
            _activityId,
            _roomId,
            starts,
            ends,
            15,
            _instructorId,
            null);

        Assert.Null(session.RecurringScheduleId);
        Assert.True(session.IsDetachedFromSchedule);
        Assert.Equal(15, session.Capacity);
    }

    [Fact]
    public void Creation_ValidatesDatesAndCapacity()
    {
        var starts = new DateTime(2026, 10, 14, 21, 0, 0, DateTimeKind.Utc);

        // Ends before starts
        Assert.Throws<ArgumentException>(() => ClassSession.CreateExtraordinary(
            _activityId, _roomId, starts, starts.AddHours(-1), 10, _instructorId, null));

        // Duration > 24 hours
        Assert.Throws<ArgumentException>(() => ClassSession.CreateExtraordinary(
            _activityId, _roomId, starts, starts.AddHours(25), 10, _instructorId, null));

        // Capacity <= 0
        Assert.Throws<ArgumentException>(() => ClassSession.CreateExtraordinary(
            _activityId, _roomId, starts, starts.AddHours(1), 0, _instructorId, null));
    }

    [Fact]
    public void Lifecycle_Open_TransitionsCorrectly_AndValidatesTime()
    {
        var starts = new DateTime(2026, 10, 14, 21, 0, 0, DateTimeKind.Utc);
        var ends = new DateTime(2026, 10, 14, 22, 0, 0, DateTimeKind.Utc);
        var session = ClassSession.CreateFromSchedule(_scheduleId, _activityId, _roomId, starts, ends, 20, _instructorId, null);

        var beforeStart = starts.AddHours(-1);
        session.Open(beforeStart);
        Assert.Equal(ClassSessionStatus.Open, session.Status);

        // Cannot open again
        Assert.Throws<InvalidOperationException>(() => session.Open(beforeStart));

        // Cannot open if now >= starts
        var session2 = ClassSession.CreateFromSchedule(_scheduleId, _activityId, _roomId, starts, ends, 20, _instructorId, null);
        Assert.Throws<InvalidOperationException>(() => session2.Open(starts.AddMinutes(5)));
    }

    [Fact]
    public void Lifecycle_Suspend_And_Cancel_EmitEvents_AndValidateReasons()
    {
        var starts = new DateTime(2026, 10, 14, 21, 0, 0, DateTimeKind.Utc);
        var ends = new DateTime(2026, 10, 14, 22, 0, 0, DateTimeKind.Utc);
        var user = Guid.NewGuid();

        // Suspend with reason Other requires observations
        var s1 = ClassSession.CreateFromSchedule(_scheduleId, _activityId, _roomId, starts, ends, 20, _instructorId, null);
        Assert.Throws<ArgumentException>(() => s1.Suspend(ClassSessionSuspensionReason.Other, null, user, starts.AddMinutes(10)));
        Assert.Throws<ArgumentException>(() => s1.Suspend(ClassSessionSuspensionReason.Other, "   ", user, starts.AddMinutes(10)));

        s1.Suspend(ClassSessionSuspensionReason.Weather, "Lluvia torrencial", user, starts.AddMinutes(10));
        Assert.Equal(ClassSessionStatus.Suspended, s1.Status);
        Assert.Equal(ClassSessionSuspensionReason.Weather, s1.SuspensionReason);
        Assert.Equal("Lluvia torrencial", s1.SuspensionObservations);
        var suspendedEvent = Assert.Single(s1.DomainEvents.OfType<ClassSessionSuspended>());
        Assert.Equal(s1.Id, suspendedEvent.SessionId);

        // Cancel requires now < StartsAtUtc
        var s2 = ClassSession.CreateFromSchedule(_scheduleId, _activityId, _roomId, starts, ends, 20, _instructorId, null);
        Assert.Throws<InvalidOperationException>(() => s2.Cancel(ClassSessionCancellationReason.AdministrativeDecision, null, user, starts.AddMinutes(1)));

        s2.Cancel(ClassSessionCancellationReason.LowDemand, null, user, starts.AddHours(-2));
        Assert.Equal(ClassSessionStatus.Cancelled, s2.Status);
        var cancelledEvent = Assert.Single(s2.DomainEvents.OfType<ClassSessionCancelled>());
        Assert.Equal(s2.Id, cancelledEvent.SessionId);

        // Terminal states cannot transition
        Assert.Throws<InvalidOperationException>(() => s1.Open(starts.AddHours(-1)));
        Assert.Throws<InvalidOperationException>(() => s1.Cancel(ClassSessionCancellationReason.LowDemand, null, user, starts.AddHours(-1)));
        Assert.Throws<InvalidOperationException>(() => s2.Suspend(ClassSessionSuspensionReason.Weather, null, user, starts.AddHours(-1)));
    }

    [Fact]
    public void DisplayStatus_EvaluatesPrecedenceProperly()
    {
        var starts = new DateTime(2026, 10, 14, 21, 0, 0, DateTimeKind.Utc);
        var ends = new DateTime(2026, 10, 14, 22, 0, 0, DateTimeKind.Utc);
        var session = ClassSession.CreateFromSchedule(_scheduleId, _activityId, _roomId, starts, ends, 20, _instructorId, null);

        // Scheduled in future
        Assert.Equal(ClassSessionDisplayStatus.Scheduled, session.GetDisplayStatus(starts.AddHours(-1)));

        // Scheduled past endsAt -> Finished
        Assert.Equal(ClassSessionDisplayStatus.Finished, session.GetDisplayStatus(ends.AddMinutes(1)));

        // Open in future
        session.Open(starts.AddHours(-2));
        Assert.Equal(ClassSessionDisplayStatus.Open, session.GetDisplayStatus(starts.AddHours(-1)));

        // InProgress
        Assert.Equal(ClassSessionDisplayStatus.InProgress, session.GetDisplayStatus(starts.AddMinutes(10)));

        // Finished
        Assert.Equal(ClassSessionDisplayStatus.Finished, session.GetDisplayStatus(ends.AddMinutes(5)));

        // Full
        for (int i = 0; i < 20; i++) session.IncrementReservedCount();
        Assert.Equal(ClassSessionDisplayStatus.Full, session.GetDisplayStatus(starts.AddHours(-1)));
    }

    [Fact]
    public void StaffAndRoom_Changes_TrackHistoryAndDetached()
    {
        var starts = new DateTime(2026, 10, 14, 21, 0, 0, DateTimeKind.Utc);
        var ends = new DateTime(2026, 10, 14, 22, 0, 0, DateTimeKind.Utc);
        var session = ClassSession.CreateFromSchedule(_scheduleId, _activityId, _roomId, starts, ends, 20, _instructorId, _secretaryId);

        var newInstructor = Guid.NewGuid();
        var admin = Guid.NewGuid();
        var now = starts.AddHours(-5);

        // Cannot replace with current effective instructor
        Assert.Throws<InvalidOperationException>(() => session.ReplaceInstructor(_instructorId, "same", admin, now));

        session.ReplaceInstructor(newInstructor, "Enfermedad", admin, now);
        Assert.Equal(_instructorId, session.ScheduledInstructorId);
        Assert.Equal(newInstructor, session.EffectiveInstructorId);
        Assert.True(session.IsDetachedFromSchedule);

        var change = Assert.Single(session.StaffChanges);
        Assert.Equal("Instructor", change.Role);
        Assert.Equal(_instructorId, change.PreviousPersonId);
        Assert.Equal(newInstructor, change.NewPersonId);
        Assert.Equal("Enfermedad", change.Reason);

        // Change room
        var newRoom = Guid.NewGuid();
        session.ChangeRoom(newRoom, admin, now);
        Assert.Equal(newRoom, session.RoomId);
        var roomEvent = Assert.Single(session.DomainEvents.OfType<ClassSessionRoomChanged>());
        Assert.Equal(_roomId, roomEvent.PreviousRoomId);
        Assert.Equal(newRoom, roomEvent.NewRoomId);
    }

    [Fact]
    public void ImmutabilityAndCapacityGuards()
    {
        var starts = new DateTime(2026, 10, 14, 21, 0, 0, DateTimeKind.Utc);
        var ends = new DateTime(2026, 10, 14, 22, 0, 0, DateTimeKind.Utc);
        var session = ClassSession.CreateFromSchedule(_scheduleId, _activityId, _roomId, starts, ends, 20, _instructorId, null);

        session.IncrementReservedCount();
        session.IncrementReservedCount(); // 2 reserves

        // Update capacity < reserved
        Assert.Throws<InvalidOperationException>(() => session.UpdateCapacity(1, starts.AddHours(-2)));

        session.UpdateCapacity(25, starts.AddHours(-2));
        Assert.Equal(25, session.Capacity);

        // Cannot update capacity after start
        Assert.Throws<InvalidOperationException>(() => session.UpdateCapacity(30, starts.AddMinutes(5)));

        // Notes can be updated even if closed
        session.UpdateNotes("Closed note", ends.AddHours(1));
        Assert.Equal("Closed note", session.Notes);
    }
}
