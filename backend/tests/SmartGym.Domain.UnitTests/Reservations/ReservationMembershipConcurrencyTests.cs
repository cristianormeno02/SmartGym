using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Reservations.Dtos;
using SmartGym.Application.Modules.Reservations.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;
using SmartGym.Domain.UnitTests.Common;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Reservations;

/// <summary>
/// Reservas frente a cambios concurrentes de la membresía y promoción de la lista de espera
/// cuando la membresía del candidato dejó de ser utilizable.
/// </summary>
public class ReservationMembershipConcurrencyTests
{
    private static readonly DateOnly Today = new(2026, 6, 10);

    // InMemory ignora transacciones y bloqueos; el bloqueo real lo aplica SmartGymDbContext en PostgreSQL.
    private sealed class SpyDbContext : SmartGymDbContext
    {
        public List<(Guid StudentId, bool MembershipsAlreadyLoaded)> StudentMembershipLockCalls { get; } = new();
        public bool FailNextSaveWithConcurrencyConflict { get; set; }

        public SpyDbContext(DbContextOptions<SmartGymDbContext> options) : base(options) { }

        public override Task LockStudentMembershipsForUpdateAsync(Guid studentId, CancellationToken cancellationToken = default)
        {
            StudentMembershipLockCalls.Add((
                studentId,
                ChangeTracker.Entries<Membership>().Any(e => e.Entity.StudentId == studentId)));
            return Task.CompletedTask;
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (FailNextSaveWithConcurrencyConflict)
            {
                FailNextSaveWithConcurrencyConflict = false;
                throw new DbUpdateConcurrencyException("Conflicto de concurrencia simulado.");
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed record Scenario(
        DbContextOptions<SmartGymDbContext> Options,
        TestGymCalendar Calendar,
        ClassSession Session,
        IReadOnlyList<Person> Students,
        IReadOnlyList<Membership> Memberships);

    private static ReservationService CreateService(SmartGymDbContext db, IGymCalendar calendar) =>
        new(
            db,
            calendar,
            new CreditLedgerService(db, calendar),
            new MembershipEligibilityService(db, calendar, Options.Create(new GymOptions())));

    /// <summary>Clase abierta para mañana y un alumno con membresía activa de 8 créditos por cada índice.</summary>
    private static async Task<Scenario> ArrangeAsync(int students, int sessionCapacity)
    {
        var options = MembershipTestSupport.InMemoryOptions();
        var calendar = new TestGymCalendar(Today);
        await using var db = new SmartGymDbContext(options);
        var ledger = new CreditLedgerService(db, calendar);

        var role = new Role { Id = (int)RoleType.Student, Name = RoleType.Student.ToString(), Description = "Alumno" };
        var instructor = Person.Create("Carlos", "Ruiz", "28000111");
        var activity = new Activity("FUNC", "Funcional", defaultCapacity: sessionCapacity);
        var room = new Room { Id = Guid.NewGuid(), Name = "Sala Principal", Capacity = 20, IsActive = true };
        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan 8 Clases",
            Price = 20000m,
            DurationDays = 30,
            Credits = 8,
            AppliesToAllActivities = true,
            IsActive = true
        };

        db.Roles.Add(role);
        db.People.Add(instructor);
        db.Activities.Add(activity);
        db.Rooms.Add(room);
        db.MembershipPlans.Add(plan);

        var people = new List<Person>();
        var memberships = new List<Membership>();
        for (var i = 0; i < students; i++)
        {
            var student = Person.Create($"Alumno{i}", "Test", $"3500000{i}");
            student.AddRole(role);
            var membership = Membership.Create(student.Id, plan, Today, calendar.UtcNow, 20000m, 0m, 20000m);
            membership.Activate(Today, calendar.UtcNow, Guid.NewGuid());
            await ledger.RecordInitialCreditsAsync(membership);

            db.People.Add(student);
            db.Memberships.Add(membership);
            people.Add(student);
            memberships.Add(membership);
        }

        var startsAt = Today.AddDays(1).ToDateTime(new TimeOnly(18, 0), DateTimeKind.Utc);
        var session = ClassSession.CreateFromSchedule(
            Guid.NewGuid(), activity.Id, room.Id, startsAt, startsAt.AddHours(1), sessionCapacity, instructor.Id, null);
        session.Open(calendar.UtcNow);
        db.ClassSessions.Add(session);

        await db.SaveChangesAsync();
        return new Scenario(options, calendar, session, people, memberships);
    }

    [Fact]
    public async Task CreateReservationAsync_LocksStudentMembershipsBeforeReadingThem()
    {
        var s = await ArrangeAsync(students: 1, sessionCapacity: 10);
        await using var db = new SpyDbContext(s.Options);
        var service = CreateService(db, s.Calendar);

        await service.CreateReservationAsync(
            new CreateReservationRequest(s.Session.Id, s.Students[0].Id),
            Guid.NewGuid());

        var call = Assert.Single(db.StudentMembershipLockCalls);
        Assert.Equal(s.Students[0].Id, call.StudentId);
        Assert.False(call.MembershipsAlreadyLoaded);
    }

    [Fact]
    public async Task CreateReservationAsync_WhenMembershipChangesConcurrently_ThrowsConflictException()
    {
        var s = await ArrangeAsync(students: 1, sessionCapacity: 10);
        await using var db = new SpyDbContext(s.Options);
        var service = CreateService(db, s.Calendar);
        db.FailNextSaveWithConcurrencyConflict = true;

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateReservationAsync(
                new CreateReservationRequest(s.Session.Id, s.Students[0].Id),
                Guid.NewGuid()));
    }

    [Fact]
    public async Task CancelReservationAsync_SkipsWaitListedStudentsWhoseMembershipIsNoLongerUsable()
    {
        var s = await ArrangeAsync(students: 3, sessionCapacity: 1);
        await using var db = new SmartGymDbContext(s.Options);
        var service = CreateService(db, s.Calendar);

        var reserved = await service.CreateReservationAsync(new CreateReservationRequest(s.Session.Id, s.Students[0].Id), Guid.NewGuid());
        var firstInLine = await service.CreateReservationAsync(new CreateReservationRequest(s.Session.Id, s.Students[1].Id), Guid.NewGuid());
        var secondInLine = await service.CreateReservationAsync(new CreateReservationRequest(s.Session.Id, s.Students[2].Id), Guid.NewGuid());
        Assert.Equal(ReservationStatus.WaitList, firstInLine.Status);
        Assert.Equal(ReservationStatus.WaitList, secondInLine.Status);

        // La membresía del primero en la lista queda suspendida después de anotarse.
        var suspendedMembership = await db.Memberships
            .Include(m => m.Suspensions)
            .SingleAsync(m => m.Id == s.Memberships[1].Id);
        suspendedMembership.Suspend(Today, null, false, "Lesión", null, Guid.NewGuid(), s.Calendar.UtcNow);
        await db.SaveChangesAsync();

        var cancelled = await service.CancelReservationAsync(
            reserved.Id,
            new CancelReservationRequest("No puedo asistir"),
            Guid.NewGuid());

        Assert.Equal(ReservationStatus.Cancelled, cancelled.Status);

        var skipped = await db.Reservations.SingleAsync(r => r.Id == firstInLine.Id);
        Assert.Equal(ReservationStatus.WaitList, skipped.Status);
        Assert.Null(skipped.CreditMovementId);

        var promoted = await db.Reservations.SingleAsync(r => r.Id == secondInLine.Id);
        Assert.Equal(ReservationStatus.Reserved, promoted.Status);
        Assert.NotNull(promoted.CreditMovementId);
        Assert.Equal(7, (await db.Memberships.SingleAsync(m => m.Id == s.Memberships[2].Id)).AvailableCredits);
        Assert.Equal(8, (await db.Memberships.SingleAsync(m => m.Id == s.Memberships[1].Id)).AvailableCredits);
        Assert.Equal(1, (await db.ClassSessions.SingleAsync(cs => cs.Id == s.Session.Id)).ReservedCount);
    }
}
