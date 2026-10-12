using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Memberships.Dtos;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Application.Modules.Reservations.Dtos;
using SmartGym.Application.Modules.Reservations.Handlers;
using SmartGym.Application.Modules.Reservations.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Memberships;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Reservations;

public class ReservationMembershipChangeTests
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

    private static DbContextOptions<SmartGymDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    [Fact]
    public async Task SuspendMembership_CancelsReservationsInWindow_RefundsCredit_FreesSpot_AndPromotesWaitlist()
    {
        var options = CreateOptions();
        var today = new DateOnly(2026, 8, 10);
        var calendar = new StubGymCalendar(today);

        var services = new ServiceCollection();
        SmartGymDbContext? db = null;
        services.AddScoped<ISmartGymDbContext>(_ => db!);
        services.AddSingleton<IGymCalendar>(calendar);
        services.AddScoped<ICreditLedgerService, CreditLedgerService>();
        services.AddScoped<IMembershipEligibilityService, MembershipEligibilityService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IReservationService, ReservationService>();
        services.AddScoped<CancelReservationsOnMembershipSuspendedHandler>();
        services.AddScoped<IDomainEventHandler<MembershipSuspendedEvent>>(
            sp => sp.GetRequiredService<CancelReservationsOnMembershipSuspendedHandler>());
        var sp = services.BuildServiceProvider();

        db = new SmartGymDbContext(options, sp);

        var studentRole = new Role { Id = (int)RoleType.Student, Name = RoleType.Student.ToString(), Description = "Student" };
        db.Roles.Add(studentRole);

        var instructor = Person.Create("Instructor", "Uno", "20000111");
        var activity = new Activity("FUNC", "Funcional", defaultCapacity: 1);
        var room = new Room { Id = Guid.NewGuid(), Name = "Sala 1", Capacity = 10, IsActive = true };
        db.People.Add(instructor);
        db.Activities.Add(activity);
        db.Rooms.Add(room);

        // sessionInWindow: mañana (dentro de la suspensión de 5 días)
        var sInStart = calendar.ToUtc(today.AddDays(1), new TimeOnly(18, 0));
        var sessionInWindow = ClassSession.CreateExtraordinary(activity.Id, room.Id, sInStart, sInStart.AddHours(1), 1, instructor.Id, null);
        sessionInWindow.Open(calendar.UtcNow);

        // sessionOutsideWindow: en 10 días (fuera de la suspensión de 5 días)
        var sOutStart = calendar.ToUtc(today.AddDays(10), new TimeOnly(18, 0));
        var sessionOutsideWindow = ClassSession.CreateExtraordinary(activity.Id, room.Id, sOutStart, sOutStart.AddHours(1), 1, instructor.Id, null);
        sessionOutsideWindow.Open(calendar.UtcNow);

        db.ClassSessions.AddRange(sessionInWindow, sessionOutsideWindow);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan 8",
            DurationDays = 30,
            Price = 15000m,
            Credits = 8,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            IsActive = true
        };
        db.MembershipPlans.Add(plan);

        var ledger = new CreditLedgerService(db, calendar);
        var s1 = Person.Create("Alumno", "Suspendido", "33000001", birthDate: new DateTime(1995, 3, 1));
        var s2 = Person.Create("Alumno", "EnEspera", "33000002", birthDate: new DateTime(1995, 3, 2));
        s1.AddRole(studentRole);
        s2.AddRole(studentRole);
        db.People.AddRange(s1, s2);

        var m1 = Membership.Create(s1.Id, plan, today, calendar.UtcNow, 15000m, 0m, 15000m);
        m1.Activate(today, calendar.UtcNow, Guid.NewGuid());
        await ledger.RecordInitialCreditsAsync(m1);

        var m2 = Membership.Create(s2.Id, plan, today, calendar.UtcNow, 15000m, 0m, 15000m);
        m2.Activate(today, calendar.UtcNow, Guid.NewGuid());
        await ledger.RecordInitialCreditsAsync(m2);

        db.Memberships.AddRange(m1, m2);
        await db.SaveChangesAsync();

        var reservationService = sp.GetRequiredService<IReservationService>();
        var rIn = await reservationService.CreateReservationAsync(new CreateReservationRequest(sessionInWindow.Id, s1.Id, MembershipId: m1.Id), Guid.NewGuid());
        var rOut = await reservationService.CreateReservationAsync(new CreateReservationRequest(sessionOutsideWindow.Id, s1.Id, MembershipId: m1.Id), Guid.NewGuid());
        var wIn = await reservationService.CreateReservationAsync(new CreateReservationRequest(sessionInWindow.Id, s2.Id, MembershipId: m2.Id), Guid.NewGuid());

        Assert.Equal(ReservationStatus.Confirmed, rIn.Status);
        Assert.Equal(ReservationStatus.Confirmed, rOut.Status);
        Assert.Equal(ReservationStatus.Waitlisted, wIn.Status);
        Assert.Equal(6, (await db.Memberships.SingleAsync(m => m.Id == m1.Id)).AvailableCredits);

        // Suspendemos m1 desde hoy hasta hoy+5 días usando MembershipService
        var membershipService = new MembershipService(db, creditLedgerService: ledger, gymCalendar: calendar, auditService: new AuditService(db));
        var trackedM1 = await db.Memberships.SingleAsync(m => m.Id == m1.Id);
        await membershipService.SuspendAsync(
            m1.Id,
            new SuspendMembershipRequest(
                EndDate: today.AddDays(5),
                ExtendExpirationDate: true,
                Reason: "Viaje corto",
                Notes: null,
                Version: trackedM1.Version),
            Guid.NewGuid());

        // Verificaciones:
        // 1. rIn fue cancelada con MembershipChange, devolvió 1 crédito a m1 (6 -> 7) y liberó el cupo
        var rInAfter = await db.Reservations.Include(r => r.StatusChanges).SingleAsync(r => r.Id == rIn.Id);
        Assert.Equal(ReservationStatus.Cancelled, rInAfter.Status);
        Assert.Equal(ReservationCancellationSource.MembershipChange, rInAfter.CancellationSource);
        Assert.NotNull(rInAfter.RefundCreditMovementId);
        Assert.Contains(rInAfter.StatusChanges, sc => sc.NewStatus == ReservationStatus.Cancelled && sc.Trigger == ReservationChangeTrigger.MembershipChange);
        Assert.Equal(7, (await db.Memberships.SingleAsync(m => m.Id == m1.Id)).AvailableCredits);

        // 2. wIn fue promovida automáticamente a Confirmed en sessionInWindow y consumió 1 crédito de m2 (8 -> 7)
        var wInAfter = await db.Reservations.SingleAsync(r => r.Id == wIn.Id);
        Assert.Equal(ReservationStatus.Confirmed, wInAfter.Status);
        Assert.Equal(7, (await db.Memberships.SingleAsync(m => m.Id == m2.Id)).AvailableCredits);
        Assert.Equal(1, (await db.ClassSessions.SingleAsync(s => s.Id == sessionInWindow.Id)).ReservedCount);

        // 3. rOut (fuera de la ventana de suspensión) sigue Confirmed
        var rOutAfter = await db.Reservations.SingleAsync(r => r.Id == rOut.Id);
        Assert.Equal(ReservationStatus.Confirmed, rOutAfter.Status);
    }

    [Fact]
    public async Task CancelMembership_CancelsFutureReservationsWithoutCreditRefund_FreesSpot_AndPromotesWaitlist()
    {
        var options = CreateOptions();
        var today = new DateOnly(2026, 8, 10);
        var calendar = new StubGymCalendar(today);

        var services = new ServiceCollection();
        SmartGymDbContext? db = null;
        services.AddScoped<ISmartGymDbContext>(_ => db!);
        services.AddSingleton<IGymCalendar>(calendar);
        services.AddScoped<ICreditLedgerService, CreditLedgerService>();
        services.AddScoped<IMembershipEligibilityService, MembershipEligibilityService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IReservationService, ReservationService>();
        services.AddScoped<CancelFutureReservationsOnMembershipCancelledHandler>();
        services.AddScoped<IDomainEventHandler<MembershipCancelledEvent>>(
            sp => sp.GetRequiredService<CancelFutureReservationsOnMembershipCancelledHandler>());
        var sp = services.BuildServiceProvider();

        db = new SmartGymDbContext(options, sp);

        var studentRole = new Role { Id = (int)RoleType.Student, Name = RoleType.Student.ToString(), Description = "Student" };
        db.Roles.Add(studentRole);

        var instructor = Person.Create("Instructor", "Uno", "20000111");
        var activity = new Activity("SPIN", "Spinning", defaultCapacity: 1);
        var room = new Room { Id = Guid.NewGuid(), Name = "Sala Spin", Capacity = 10, IsActive = true };
        db.People.Add(instructor);
        db.Activities.Add(activity);
        db.Rooms.Add(room);

        var sStart = calendar.ToUtc(today.AddDays(2), new TimeOnly(19, 0));
        var session = ClassSession.CreateExtraordinary(activity.Id, room.Id, sStart, sStart.AddHours(1), 1, instructor.Id, null);
        session.Open(calendar.UtcNow);
        db.ClassSessions.Add(session);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan 4",
            DurationDays = 30,
            Price = 10000m,
            Credits = 4,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            IsActive = true
        };
        db.MembershipPlans.Add(plan);

        var ledger = new CreditLedgerService(db, calendar);
        var s1 = Person.Create("Alumno", "Cancelado", "34000001", birthDate: new DateTime(1995, 4, 1));
        var s2 = Person.Create("Alumno", "Espera", "34000002", birthDate: new DateTime(1995, 4, 2));
        s1.AddRole(studentRole);
        s2.AddRole(studentRole);
        db.People.AddRange(s1, s2);

        var m1 = Membership.Create(s1.Id, plan, today, calendar.UtcNow, 10000m, 0m, 10000m);
        m1.Activate(today, calendar.UtcNow, Guid.NewGuid());
        await ledger.RecordInitialCreditsAsync(m1);

        var m2 = Membership.Create(s2.Id, plan, today, calendar.UtcNow, 10000m, 0m, 10000m);
        m2.Activate(today, calendar.UtcNow, Guid.NewGuid());
        await ledger.RecordInitialCreditsAsync(m2);

        db.Memberships.AddRange(m1, m2);
        await db.SaveChangesAsync();

        var reservationService = sp.GetRequiredService<IReservationService>();
        var r1 = await reservationService.CreateReservationAsync(new CreateReservationRequest(session.Id, s1.Id, MembershipId: m1.Id), Guid.NewGuid());
        var w1 = await reservationService.CreateReservationAsync(new CreateReservationRequest(session.Id, s2.Id, MembershipId: m2.Id), Guid.NewGuid());

        var membershipService = new MembershipService(db, creditLedgerService: ledger, gymCalendar: calendar, auditService: new AuditService(db));
        var trackedM1 = await db.Memberships.SingleAsync(m => m.Id == m1.Id);
        await membershipService.CancelAsync(
            m1.Id,
            new CancelMembershipRequest(Reason: "Baja definitiva", Notes: null, Version: trackedM1.Version),
            Guid.NewGuid());

        var r1After = await db.Reservations.Include(r => r.StatusChanges).SingleAsync(r => r.Id == r1.Id);
        Assert.Equal(ReservationStatus.Cancelled, r1After.Status);
        Assert.Equal(ReservationCancellationSource.MembershipChange, r1After.CancellationSource);
        Assert.Null(r1After.RefundCreditMovementId);
        Assert.Equal(0, (await db.Memberships.SingleAsync(m => m.Id == m1.Id)).AvailableCredits);

        // w1 fue promovida automáticamente a Confirmed al liberarse el cupo
        var w1After = await db.Reservations.SingleAsync(r => r.Id == w1.Id);
        Assert.Equal(ReservationStatus.Confirmed, w1After.Status);
        Assert.Equal(3, (await db.Memberships.SingleAsync(m => m.Id == m2.Id)).AvailableCredits);
        Assert.Equal(1, (await db.ClassSessions.SingleAsync(s => s.Id == session.Id)).ReservedCount);
    }
}
