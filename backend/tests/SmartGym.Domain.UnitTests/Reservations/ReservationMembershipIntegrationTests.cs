using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Modules.Memberships.Dtos;
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

public class ReservationMembershipIntegrationTests
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

    private static ReservationService CreateReservationService(SmartGymDbContext db, IGymCalendar calendar)
    {
        var ledger = new CreditLedgerService(db, calendar);
        var eligibility = new MembershipEligibilityService(db, calendar, Options.Create(new GymOptions()));
        return new ReservationService(db, calendar, ledger, eligibility);
    }

    private static async Task<(Person Student, Activity ActFunc, Activity ActYoga, Room Room, Person Instructor, ClassSession SessionFunc)> SeedBaseScenarioAsync(
        SmartGymDbContext db,
        StubGymCalendar calendar,
        int sessionCapacity = 10)
    {
        var role = new Role
        {
            Id = (int)RoleType.Student,
            Name = RoleType.Student.ToString(),
            Description = "Alumno"
        };
        db.Roles.Add(role);

        var student = Person.Create(
            "Lucía",
            "Fernández",
            "35888999",
            birthDate: new DateTime(1998, 4, 10),
            emergencyContact: EmergencyContact.Create("Madre", "+5492644000000", "Madre"));
        student.AddRole(role);

        var instructor = Person.Create("Carlos", "Ruiz", "28000111");
        var actFunc = new Activity("FUNC", "Funcional", defaultCapacity: sessionCapacity, minAge: 16, maxAge: 65);
        var actYoga = new Activity("YOGA", "Yoga", defaultCapacity: sessionCapacity, minAge: 16, maxAge: 65);
        var room = new Room { Id = Guid.NewGuid(), Name = "Sala Principal", Capacity = sessionCapacity, IsActive = true };

        db.People.AddRange(student, instructor);
        db.Activities.AddRange(actFunc, actYoga);
        db.Rooms.Add(room);

        var sessionStart = calendar.Today.AddDays(1).ToDateTime(new TimeOnly(18, 0), DateTimeKind.Utc);
        var sessionFunc = ClassSession.CreateFromSchedule(
            Guid.NewGuid(),
            actFunc.Id,
            room.Id,
            sessionStart,
            sessionStart.AddHours(1),
            sessionCapacity,
            instructor.Id,
            null);
        sessionFunc.Open(calendar.UtcNow);
        db.ClassSessions.Add(sessionFunc);

        await db.SaveChangesAsync();
        return (student, actFunc, actYoga, room, instructor, sessionFunc);
    }

    [Fact]
    public async Task CreateReservationAsync_SelectsMembershipAutomaticallyOrRespectsExplicitMembershipId_AndConsumesAndRefundsViaLedger()
    {
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 6, 10);
        var calendar = new StubGymCalendar(today);
        var (student, actFunc, _, _, _, sessionFunc) = await SeedBaseScenarioAsync(db, calendar);
        var ledger = new CreditLedgerService(db, calendar);
        var service = CreateReservationService(db, calendar);

        var planSoon = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan Vence Pronto",
            DurationDays = 10,
            Price = 10000m,
            Credits = 4,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            IsActive = true
        };
        var planLater = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan Vence Tarde",
            DurationDays = 30,
            Price = 20000m,
            Credits = 8,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            IsActive = true
        };
        db.MembershipPlans.AddRange(planSoon, planLater);

        var mSoon = Membership.Create(student.Id, planSoon, today, calendar.UtcNow, 10000m, 0m, 10000m);
        mSoon.Activate(today, calendar.UtcNow, Guid.NewGuid());
        await ledger.RecordInitialCreditsAsync(mSoon);

        var mLater = Membership.Create(student.Id, planLater, today, calendar.UtcNow.AddMinutes(5), 20000m, 0m, 20000m);
        mLater.Activate(today, calendar.UtcNow, Guid.NewGuid());
        await ledger.RecordInitialCreditsAsync(mLater);

        db.Memberships.AddRange(mSoon, mLater);
        await db.SaveChangesAsync();

        // 1. Sin MembershipId explícito -> elige mSoon (EndDate más próximo)
        var resAuto = await service.CreateReservationAsync(
            new CreateReservationRequest(sessionFunc.Id, student.Id),
            Guid.NewGuid());

        Assert.Equal(mSoon.Id, resAuto.MembershipId);
        Assert.Equal(3, (await db.Memberships.SingleAsync(m => m.Id == mSoon.Id)).AvailableCredits);
        Assert.Equal(8, (await db.Memberships.SingleAsync(m => m.Id == mLater.Id)).AvailableCredits);

        // Cancelar con anticipación (> 2h) -> devuelve el crédito a mSoon
        await service.CancelReservationAsync(
            resAuto.Id,
            new CancelReservationRequest("Cambio de horario"),
            Guid.NewGuid());
        Assert.Equal(4, (await db.Memberships.SingleAsync(m => m.Id == mSoon.Id)).AvailableCredits);

        // 2. Con MembershipId explícito -> usa mLater
        var resExplicit = await service.CreateReservationAsync(
            new CreateReservationRequest(sessionFunc.Id, student.Id, MembershipId: mLater.Id),
            Guid.NewGuid());

        Assert.Equal(mLater.Id, resExplicit.MembershipId);
        Assert.Equal(4, (await db.Memberships.SingleAsync(m => m.Id == mSoon.Id)).AvailableCredits);
        Assert.Equal(7, (await db.Memberships.SingleAsync(m => m.Id == mLater.Id)).AvailableCredits);
    }

    [Fact]
    public async Task CreateReservationAsync_BlocksWhenMembershipPending_Suspended_Expired_DisallowedActivity_OrDepleted()
    {
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 6, 10);
        var calendar = new StubGymCalendar(today);
        var (student, _, actYoga, _, _, sessionFunc) = await SeedBaseScenarioAsync(db, calendar);
        var ledger = new CreditLedgerService(db, calendar);
        var service = CreateReservationService(db, calendar);

        var yogaOnlyPlan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan Sólo Yoga",
            DurationDays = 30,
            Price = 15000m,
            Credits = 1,
            IsUnlimited = false,
            AppliesToAllActivities = false,
            IsActive = true
        };
        yogaOnlyPlan.AllowedActivities.Add(new MembershipPlanActivity { MembershipPlanId = yogaOnlyPlan.Id, ActivityId = actYoga.Id });
        db.MembershipPlans.Add(yogaOnlyPlan);

        // 1. Membresía Pending -> bloquea con código MEMBERSHIP_PENDING
        var m = Membership.Create(student.Id, yogaOnlyPlan, today, calendar.UtcNow, 15000m, 0m, 15000m);
        db.Memberships.Add(m);
        await db.SaveChangesAsync();

        var exPending = await Assert.ThrowsAsync<SmartGym.Application.Common.Exceptions.ConflictException>(() =>
            service.CreateReservationAsync(new CreateReservationRequest(sessionFunc.Id, student.Id, m.Id), Guid.NewGuid()));
        Assert.Contains(EligibilityIssueCodes.MembershipPending, exPending.Message);

        // 2. Activamos pero es sólo para Yoga y la clase es de Funcional -> ACTIVITY_NOT_ALLOWED
        m.Activate(today, calendar.UtcNow, Guid.NewGuid());
        await ledger.RecordInitialCreditsAsync(m);
        await db.SaveChangesAsync();

        var exActivity = await Assert.ThrowsAsync<SmartGym.Application.Common.Exceptions.ConflictException>(() =>
            service.CreateReservationAsync(new CreateReservationRequest(sessionFunc.Id, student.Id, m.Id), Guid.NewGuid()));
        Assert.Contains(EligibilityIssueCodes.ActivityNotAllowed, exActivity.Message);

        // 3. Permitimos todas las actividades pero suspendemos -> MEMBERSHIP_SUSPENDED
        m.AppliesToAllActivities = true;
        m.Suspend(today, today.AddDays(5), false, "Viaje", null, Guid.NewGuid(), calendar.UtcNow);
        await db.SaveChangesAsync();

        var exSuspended = await Assert.ThrowsAsync<SmartGym.Application.Common.Exceptions.ConflictException>(() =>
            service.CreateReservationAsync(new CreateReservationRequest(sessionFunc.Id, student.Id, m.Id), Guid.NewGuid()));
        Assert.Contains(EligibilityIssueCodes.MembershipSuspended, exSuspended.Message);

        // 4. Reactivamos y consumimos su único crédito -> NO_AVAILABLE_CREDITS
        m.Reactivate(today, calendar.UtcNow, Guid.NewGuid());
        await ledger.ConsumeCreditForReservationAsync(m, Guid.NewGuid(), today, "Otra clase");
        await db.SaveChangesAsync();

        var exNoCredits = await Assert.ThrowsAsync<SmartGym.Application.Common.Exceptions.ConflictException>(() =>
            service.CreateReservationAsync(new CreateReservationRequest(sessionFunc.Id, student.Id, m.Id), Guid.NewGuid()));
        Assert.Contains(EligibilityIssueCodes.NoAvailableCredits, exNoCredits.Message);
    }
}
