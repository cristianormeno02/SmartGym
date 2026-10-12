using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Reservations.Dtos;
using SmartGym.Application.Modules.Reservations.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Reservations;

public class ReservationIdempotencyTests
{
    private sealed class StubGymCalendar : IGymCalendar
    {
        public StubGymCalendar(DateOnly today, DateTime? utcNow = null)
        {
            Today = today;
            UtcNow = utcNow ?? today.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc);
        }

        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;
        public DateOnly Today { get; set; }
        public DateTime UtcNow { get; set; }
        public DateTime ToUtc(DateOnly date, TimeOnly time) =>
            new(date.Year, date.Month, date.Day, time.Hour, time.Minute, time.Second, DateTimeKind.Utc);
        public DateTimeOffset ToLocal(DateTime utc) => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
        public (DateTime FromUtc, DateTime ToUtc) LocalDayRange(DateOnly from, DateOnly to) =>
            (ToUtc(from, TimeOnly.MinValue), ToUtc(to.AddDays(1), TimeOnly.MinValue));
    }

    private static SmartGymDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new SmartGymDbContext(options);
    }

    private static async Task<(Person Student, ClassSession Session1, ClassSession Session2, Membership Membership)> SeedScenarioAsync(
        SmartGymDbContext db,
        StubGymCalendar calendar)
    {
        var studentRole = new Role { Id = (int)RoleType.Student, Name = RoleType.Student.ToString(), Description = "Student" };
        db.Roles.Add(studentRole);

        var student = Person.Create(
            "Mateo",
            "Silva",
            "39555666",
            birthDate: new DateTime(1995, 1, 10),
            emergencyContact: EmergencyContact.Create("Hermano", "+5492644555666", "Hermano"));
        student.AddRole(studentRole);

        var instructor = Person.Create("Paula", "Vega", "29000111");
        var activity = new Activity("BOXEO", "Boxeo Recreativo", defaultCapacity: 10);
        var room = new Room { Id = Guid.NewGuid(), Name = "Ring", Capacity = 10, IsActive = true };

        db.People.AddRange(student, instructor);
        db.Activities.Add(activity);
        db.Rooms.Add(room);

        var s1Start = calendar.UtcNow.AddDays(2);
        var session1 = ClassSession.CreateFromSchedule(
            Guid.NewGuid(), activity.Id, room.Id, s1Start, s1Start.AddHours(1), 10, instructor.Id, null);
        session1.Open(calendar.UtcNow);

        var s2Start = calendar.UtcNow.AddDays(3);
        var session2 = ClassSession.CreateFromSchedule(
            Guid.NewGuid(), activity.Id, room.Id, s2Start, s2Start.AddHours(1), 10, instructor.Id, null);
        session2.Open(calendar.UtcNow);

        db.ClassSessions.AddRange(session1, session2);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan 8 Clases",
            DurationDays = 30,
            Price = 15000m,
            Credits = 8,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            IsActive = true
        };
        db.MembershipPlans.Add(plan);

        var membership = Membership.Create(student.Id, plan, calendar.Today, calendar.UtcNow, 15000m, 0m, 15000m);
        membership.Activate(calendar.Today, calendar.UtcNow, Guid.NewGuid());
        var ledger = new CreditLedgerService(db, calendar);
        await ledger.RecordInitialCreditsAsync(membership);
        db.Memberships.Add(membership);

        await db.SaveChangesAsync();
        return (student, session1, session2, membership);
    }

    [Fact]
    public async Task CreateReservationAsync_SameIdempotencyKeyAndSession_ReturnsExistingReservationWithoutSecondDebitAndSetsReplayFlag()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar(new DateOnly(2026, 8, 5));
        var (student, session1, _, membership) = await SeedScenarioAsync(db, calendar);
        var service = new ReservationService(db, calendar);
        var userId = Guid.NewGuid();

        var first = await service.CreateReservationAsync(
            new CreateReservationRequest(session1.Id, student.Id, IdempotencyKey: "req-abc-001"),
            userId);
        Assert.False(first.WasIdempotentReplay);

        var second = await service.CreateReservationAsync(
            new CreateReservationRequest(session1.Id, student.Id, IdempotencyKey: "req-abc-001"),
            userId);

        Assert.True(second.WasIdempotentReplay);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await db.Reservations.CountAsync());
        Assert.Equal(7, (await db.Memberships.SingleAsync(m => m.Id == membership.Id)).AvailableCredits);
        Assert.Equal(1, (await db.ClassSessions.SingleAsync(s => s.Id == session1.Id)).ReservedCount);
    }

    [Fact]
    public async Task CreateReservationAsync_SameIdempotencyKeyForDifferentSession_ThrowsIdempotencyKeyMismatchConflict()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar(new DateOnly(2026, 8, 5));
        var (student, session1, session2, _) = await SeedScenarioAsync(db, calendar);
        var service = new ReservationService(db, calendar);
        var userId = Guid.NewGuid();

        await service.CreateReservationAsync(
            new CreateReservationRequest(session1.Id, student.Id, IdempotencyKey: "req-shared-key"),
            userId);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateReservationAsync(
                new CreateReservationRequest(session2.Id, student.Id, IdempotencyKey: "req-shared-key"),
                userId));

        Assert.Equal(ReservationErrorCodes.IdempotencyKeyMismatch, ex.Code);
        Assert.Equal(1, await db.Reservations.CountAsync());
    }

    [Fact]
    public async Task CreateReservationAsync_ReplayWithSameIdempotencyKeyAfterCancellation_ReturnsCancelledReservationInCurrentState()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar(new DateOnly(2026, 8, 5));
        var (student, session1, _, membership) = await SeedScenarioAsync(db, calendar);
        var service = new ReservationService(db, calendar);
        var userId = Guid.NewGuid();

        var created = await service.CreateReservationAsync(
            new CreateReservationRequest(session1.Id, student.Id, IdempotencyKey: "req-cancel-replay"),
            userId);

        await service.CancelReservationAsync(created.Id, new CancelReservationRequest("Cancelo a tiempo"), userId);

        // Reintento tardío con la misma clave -> devuelve la misma reserva en su estado actual (Cancelled) sin crear otra
        var replay = await service.CreateReservationAsync(
            new CreateReservationRequest(session1.Id, student.Id, IdempotencyKey: "req-cancel-replay"),
            userId);

        Assert.True(replay.WasIdempotentReplay);
        Assert.Equal(created.Id, replay.Id);
        Assert.Equal(ReservationStatus.Cancelled, replay.Status);
        Assert.Equal(1, await db.Reservations.CountAsync());
        Assert.Equal(8, (await db.Memberships.SingleAsync(m => m.Id == membership.Id)).AvailableCredits);
    }
}
