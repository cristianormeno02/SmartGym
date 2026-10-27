using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Application.Modules.Activities.Services;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Activities;

public class ClassSessionScheduleSyncTests
{
    private readonly SmartGymDbContext _dbContext;
    private readonly IGymCalendar _gymCalendar;
    private readonly IClassSessionGenerator _generator;
    private readonly ClassSessionScheduleSync _sync;
    private readonly DateTime _baseNowUtc;

    public ClassSessionScheduleSyncTests()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new SmartGymDbContext(options);

        var gymOptions = Options.Create(new GymOptions
        {
            TimeZoneId = "America/Argentina/San_Juan"
        });
        _gymCalendar = new GymCalendar(gymOptions);

        var classSessionsOptions = Options.Create(new ClassSessionsOptions
        {
            WindowDays = 60,
            AutoOpenLeadTime = TimeSpan.FromDays(7)
        });

        var mockAudit = new Mock<IAuditService>();
        _generator = new ClassSessionGenerator(
            _dbContext,
            _gymCalendar,
            classSessionsOptions,
            mockAudit.Object,
            NullLogger<ClassSessionGenerator>.Instance);

        _sync = new ClassSessionScheduleSync(_dbContext, _gymCalendar, _generator);

        // Monday 2026-10-12 12:00 UTC = 09:00 ART
        _baseNowUtc = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
    }

    private (Activity activity, Room room, Person instructor, RecurringSchedule schedule) SeedData(
        DayOfWeek day = DayOfWeek.Wednesday,
        TimeOnly start = default,
        TimeOnly end = default,
        int? scheduleCapacity = 20)
    {
        if (start == default) start = new TimeOnly(18, 0);
        if (end == default) end = new TimeOnly(19, 0);

        var activity = new Activity("Zumba", "Zumba", defaultCapacity: 25);

        var room = new Room
        {
            Id = Guid.NewGuid(),
            Name = "Sala A",
            Capacity = 30,
            IsActive = true
        };

        var instructor = Person.Create("Juan", "Perez", "juan@gym.com");

        var schedule = new RecurringSchedule
        {
            Id = Guid.NewGuid(),
            ActivityId = activity.Id,
            RoomId = room.Id,
            InstructorId = instructor.Id,
            DayOfWeek = day,
            StartTime = start,
            EndTime = end,
            ValidFrom = new DateOnly(2026, 10, 1),
            ValidTo = new DateOnly(2026, 12, 31),
            MaxCapacity = scheduleCapacity,
            IsActive = true
        };

        _dbContext.Activities.Add(activity);
        _dbContext.Rooms.Add(room);
        _dbContext.People.Add(instructor);
        _dbContext.RecurringSchedules.Add(schedule);
        _dbContext.SaveChanges();

        return (activity, room, instructor, schedule);
    }

    [Fact]
    public async Task SyncAsync_CandidateSessions_ShouldUpdateInPlace()
    {
        // Wednesday 2026-10-14 18:00 - 19:00 ART
        var (activity, room, instructor, schedule) = SeedData(DayOfWeek.Wednesday, new TimeOnly(18, 0), new TimeOnly(19, 0), 20);

        var startsUtc = _gymCalendar.ToUtc(new DateOnly(2026, 10, 14), new TimeOnly(18, 0));
        var endsUtc = _gymCalendar.ToUtc(new DateOnly(2026, 10, 14), new TimeOnly(19, 0));

        var session = ClassSession.CreateFromSchedule(
            schedule.Id, activity.Id, room.Id, startsUtc, endsUtc, 20, instructor.Id, null);
        _dbContext.ClassSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        // Mutate schedule: time 19:00 - 20:00, capacity 22
        var newRoom = new Room { Id = Guid.NewGuid(), Name = "Sala B", Capacity = 40, IsActive = true };
        _dbContext.Rooms.Add(newRoom);
        schedule.RoomId = newRoom.Id;
        schedule.SetTimeSlot(new TimeOnly(19, 0), new TimeOnly(20, 0));
        schedule.MaxCapacity = 22;
        await _dbContext.SaveChangesAsync();

        // Act
        var divergent = await _sync.SyncAsync(schedule.Id, _baseNowUtc);

        // Assert
        Assert.Empty(divergent);
        var updated = await _dbContext.ClassSessions.FindAsync(session.Id);
        Assert.NotNull(updated);
        Assert.Equal(ClassSessionStatus.Scheduled, updated.Status);
        Assert.Equal(newRoom.Id, updated.RoomId);
        Assert.Equal(22, updated.Capacity);

        var expectedStarts = _gymCalendar.ToUtc(new DateOnly(2026, 10, 14), new TimeOnly(19, 0));
        var expectedEnds = _gymCalendar.ToUtc(new DateOnly(2026, 10, 14), new TimeOnly(20, 0));
        Assert.Equal(expectedStarts, updated.StartsAtUtc);
        Assert.Equal(expectedEnds, updated.EndsAtUtc);
    }

    [Fact]
    public async Task SyncAsync_WhenDayChangesOrValidityCloses_ShouldCancelCandidatesWithScheduleEnded()
    {
        // Wednesday 2026-10-14
        var (activity, room, instructor, schedule) = SeedData(DayOfWeek.Wednesday, new TimeOnly(18, 0), new TimeOnly(19, 0));

        var startsUtc = _gymCalendar.ToUtc(new DateOnly(2026, 10, 14), new TimeOnly(18, 0));
        var endsUtc = _gymCalendar.ToUtc(new DateOnly(2026, 10, 14), new TimeOnly(19, 0));

        var session = ClassSession.CreateFromSchedule(
            schedule.Id, activity.Id, room.Id, startsUtc, endsUtc, 20, instructor.Id, null);
        _dbContext.ClassSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        // Change schedule DayOfWeek to Friday
        schedule.DayOfWeek = DayOfWeek.Friday;
        await _dbContext.SaveChangesAsync();

        // Act
        var divergent = await _sync.SyncAsync(schedule.Id, _baseNowUtc);

        // Assert
        Assert.Empty(divergent);
        var updated = await _dbContext.ClassSessions.FindAsync(session.Id);
        Assert.NotNull(updated);
        Assert.Equal(ClassSessionStatus.Cancelled, updated.Status);
        Assert.Equal(ClassSessionCancellationReason.ScheduleEnded, updated.CancellationReason);
        Assert.Null(updated.CancelledByUserId);
    }

    [Fact]
    public async Task SyncAsync_NonCandidateSessions_ShouldRemainUntouchedAndReturnAsDivergent()
    {
        var (activity, room, instructor, schedule) = SeedData(DayOfWeek.Wednesday, new TimeOnly(18, 0), new TimeOnly(19, 0));

        // Session 1: Open with reservation
        var starts1 = _gymCalendar.ToUtc(new DateOnly(2026, 10, 14), new TimeOnly(18, 0));
        var ends1 = _gymCalendar.ToUtc(new DateOnly(2026, 10, 14), new TimeOnly(19, 0));
        var session1 = ClassSession.CreateFromSchedule(schedule.Id, activity.Id, room.Id, starts1, ends1, 20, instructor.Id, null);
        session1.Open(_baseNowUtc);
        session1.IncrementReservedCount();

        // Session 2: Detached (manual room change)
        var starts2 = _gymCalendar.ToUtc(new DateOnly(2026, 10, 21), new TimeOnly(18, 0));
        var ends2 = _gymCalendar.ToUtc(new DateOnly(2026, 10, 21), new TimeOnly(19, 0));
        var otherRoom = new Room { Id = Guid.NewGuid(), Name = "Sala C", Capacity = 20, IsActive = true };
        _dbContext.Rooms.Add(otherRoom);
        var session2 = ClassSession.CreateFromSchedule(schedule.Id, activity.Id, room.Id, starts2, ends2, 20, instructor.Id, null);
        session2.ChangeRoom(otherRoom.Id, Guid.NewGuid(), _baseNowUtc);

        // Session 3: Candidate
        var starts3 = _gymCalendar.ToUtc(new DateOnly(2026, 10, 28), new TimeOnly(18, 0));
        var ends3 = _gymCalendar.ToUtc(new DateOnly(2026, 10, 28), new TimeOnly(19, 0));
        var session3 = ClassSession.CreateFromSchedule(schedule.Id, activity.Id, room.Id, starts3, ends3, 20, instructor.Id, null);

        _dbContext.ClassSessions.AddRange(session1, session2, session3);
        await _dbContext.SaveChangesAsync();

        // Mutate schedule time to 20:00 - 21:00
        schedule.SetTimeSlot(new TimeOnly(20, 0), new TimeOnly(21, 0));
        await _dbContext.SaveChangesAsync();

        // Act
        var divergent = await _sync.SyncAsync(schedule.Id, _baseNowUtc);

        // Assert
        Assert.Equal(2, divergent.Count);
        Assert.Contains(session1.Id, divergent);
        Assert.Contains(session2.Id, divergent);

        // Untouched
        var check1 = await _dbContext.ClassSessions.FindAsync(session1.Id);
        Assert.Equal(ClassSessionStatus.Open, check1!.Status);
        Assert.Equal(starts1, check1.StartsAtUtc);

        var check2 = await _dbContext.ClassSessions.FindAsync(session2.Id);
        Assert.Equal(otherRoom.Id, check2!.RoomId);
        Assert.Equal(starts2, check2.StartsAtUtc);

        // Candidate was updated
        var check3 = await _dbContext.ClassSessions.FindAsync(session3.Id);
        var expectedStarts3 = _gymCalendar.ToUtc(new DateOnly(2026, 10, 28), new TimeOnly(20, 0));
        Assert.Equal(expectedStarts3, check3!.StartsAtUtc);
    }

    [Fact]
    public async Task SyncAsync_WhenCollisionOccursWithExistingSession_CandidateIsCancelled()
    {
        var (activity, room, instructor, schedule) = SeedData(DayOfWeek.Wednesday, new TimeOnly(18, 0), new TimeOnly(19, 0));

        // Candidate session at 18:00
        var starts18 = _gymCalendar.ToUtc(new DateOnly(2026, 10, 14), new TimeOnly(18, 0));
        var ends18 = _gymCalendar.ToUtc(new DateOnly(2026, 10, 14), new TimeOnly(19, 0));
        var candidateSession = ClassSession.CreateFromSchedule(schedule.Id, activity.Id, room.Id, starts18, ends18, 20, instructor.Id, null);

        // Existing session already at 19:00 for the same schedule
        var starts19 = _gymCalendar.ToUtc(new DateOnly(2026, 10, 14), new TimeOnly(19, 0));
        var ends19 = _gymCalendar.ToUtc(new DateOnly(2026, 10, 14), new TimeOnly(20, 0));
        var existingSession = ClassSession.CreateFromSchedule(schedule.Id, activity.Id, room.Id, starts19, ends19, 20, instructor.Id, null);

        _dbContext.ClassSessions.AddRange(candidateSession, existingSession);
        await _dbContext.SaveChangesAsync();

        // Mutate schedule to 19:00 (which collides with existingSession)
        schedule.SetTimeSlot(new TimeOnly(19, 0), new TimeOnly(20, 0));
        await _dbContext.SaveChangesAsync();

        // Act
        var divergent = await _sync.SyncAsync(schedule.Id, _baseNowUtc);

        // Assert
        var updatedCandidate = await _dbContext.ClassSessions.FindAsync(candidateSession.Id);
        Assert.NotNull(updatedCandidate);
        Assert.Equal(ClassSessionStatus.Cancelled, updatedCandidate.Status);
        Assert.Equal(ClassSessionCancellationReason.ScheduleEnded, updatedCandidate.CancellationReason);
    }

    [Fact]
    public async Task SyncAsync_WhenSessionIsOpenAndNoReservations_ShouldUpdateInstructorAndDetails()
    {
        // Martes a las 20:00 (caso reportado por usuario)
        var (activity, room, instructor1, schedule) = SeedData(DayOfWeek.Tuesday, new TimeOnly(20, 0), new TimeOnly(21, 0), 20);

        var startsUtc = _gymCalendar.ToUtc(new DateOnly(2026, 10, 13), new TimeOnly(20, 0));
        var endsUtc = _gymCalendar.ToUtc(new DateOnly(2026, 10, 13), new TimeOnly(21, 0));

        var session = ClassSession.CreateFromSchedule(
            schedule.Id, activity.Id, room.Id, startsUtc, endsUtc, 20, instructor1.Id, null);
        session.Open(_baseNowUtc);
        _dbContext.ClassSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        // Se edita el horario asignando un nuevo instructor
        var instructor2 = Person.Create("Carlos", "Gomez", "carlos@gym.com");
        _dbContext.People.Add(instructor2);
        schedule.InstructorId = instructor2.Id;
        await _dbContext.SaveChangesAsync();

        // Act
        var divergent = await _sync.SyncAsync(schedule.Id, _baseNowUtc);

        // Assert
        Assert.Empty(divergent);
        var updated = await _dbContext.ClassSessions.FindAsync(session.Id);
        Assert.NotNull(updated);
        Assert.Equal(ClassSessionStatus.Open, updated.Status);
        Assert.Equal(instructor2.Id, updated.EffectiveInstructorId);
        Assert.Equal(instructor2.Id, updated.ScheduledInstructorId);
    }

    [Fact]
    public async Task SyncAsync_WhenSessionIsOpenWithReservationsAndInstructorChanges_ShouldUpdateInstructorPreservingReservations()
    {
        var (activity, room, instructor1, schedule) = SeedData(DayOfWeek.Tuesday, new TimeOnly(20, 0), new TimeOnly(21, 0), 20);

        var startsUtc = _gymCalendar.ToUtc(new DateOnly(2026, 10, 13), new TimeOnly(20, 0));
        var endsUtc = _gymCalendar.ToUtc(new DateOnly(2026, 10, 13), new TimeOnly(21, 0));

        var session = ClassSession.CreateFromSchedule(
            schedule.Id, activity.Id, room.Id, startsUtc, endsUtc, 20, instructor1.Id, null);
        session.Open(_baseNowUtc);
        session.IncrementReservedCount(); // Ya tiene reservas
        _dbContext.ClassSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        // Modifica solo el instructor en el horario recurrente (misma hora)
        var instructor2 = Person.Create("Laura", "Fernandez", "laura@gym.com");
        _dbContext.People.Add(instructor2);
        schedule.InstructorId = instructor2.Id;
        await _dbContext.SaveChangesAsync();

        // Act
        var divergent = await _sync.SyncAsync(schedule.Id, _baseNowUtc);

        // Assert
        Assert.Empty(divergent); // La hora no cambió, por lo que no es divergente en horario
        var updated = await _dbContext.ClassSessions.FindAsync(session.Id);
        Assert.NotNull(updated);
        Assert.Equal(1, updated.ReservedCount);
        Assert.Equal(instructor2.Id, updated.EffectiveInstructorId);
        Assert.Equal(instructor2.Id, updated.ScheduledInstructorId);
    }

    [Fact]
    public async Task SyncAllActiveAsync_ShouldSyncAllActiveSchedulesInDatabase()
    {
        var (activity, room, instructor1, schedule) = SeedData(DayOfWeek.Tuesday, new TimeOnly(20, 0), new TimeOnly(21, 0), 20);

        var startsUtc = _gymCalendar.ToUtc(new DateOnly(2026, 10, 13), new TimeOnly(20, 0));
        var endsUtc = _gymCalendar.ToUtc(new DateOnly(2026, 10, 13), new TimeOnly(21, 0));

        var session = ClassSession.CreateFromSchedule(
            schedule.Id, activity.Id, room.Id, startsUtc, endsUtc, 20, instructor1.Id, null);
        session.Open(_baseNowUtc);
        _dbContext.ClassSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var instructor2 = Person.Create("Laura", "Fernandez", "laura@gym.com");
        _dbContext.People.Add(instructor2);
        schedule.InstructorId = instructor2.Id;
        await _dbContext.SaveChangesAsync();

        // Act
        var count = await _sync.SyncAllActiveAsync(_baseNowUtc);

        // Assert
        Assert.True(count >= 1);
        var updated = await _dbContext.ClassSessions.FindAsync(session.Id);
        Assert.NotNull(updated);
        Assert.Equal(instructor2.Id, updated.EffectiveInstructorId);
    }
}
