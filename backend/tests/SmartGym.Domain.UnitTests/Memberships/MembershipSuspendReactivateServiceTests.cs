using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Modules.Memberships.Dtos;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Application.Modules.Promotions.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Entities.Reservations;
using SmartGym.Domain.Enums;
using SmartGym.Domain.UnitTests.Common;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Memberships;

public class MembershipSuspendReactivateServiceTests
{
    private sealed class StubGymCalendar : IGymCalendar
    {
        public StubGymCalendar(DateOnly today, DateTime? utcNow = null)
        {
            Today = today;
            UtcNow = utcNow ?? today.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);
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

    // Con despacho de eventos de dominio: las reservas las cancelan los handlers, como en la aplicación.
    private static SmartGymDbContext CreateDbContext() =>
        MembershipTestSupport.CreateDbContextWithMembershipEventHandlers();

    private static MembershipService CreateService(SmartGymDbContext db, IGymCalendar calendar)
    {
        var creditLedger = new CreditLedgerService(db, calendar);
        var auditService = new AuditService(db);
        var activationService = new MembershipActivationService(db, creditLedger, calendar, auditService);
        var paymentService = new PaymentService(db);
        var promotionService = new PromotionService(db, calendar);
        var eligibilityService = new MembershipEligibilityService(
            db,
            calendar,
            Options.Create(new GymOptions { DefaultCurrency = "ARS" }));

        return new MembershipService(
            db,
            creditLedger,
            activationService,
            eligibilityService,
            paymentService,
            promotionService,
            calendar,
            Options.Create(new GymOptions { DefaultCurrency = "ARS" }),
            Options.Create(new MembershipsOptions()),
            auditService);
    }

    private static async Task<(Person Student, MembershipPlan Plan, Activity Activity, Room Room, Person Instructor)> SeedAsync(SmartGymDbContext db)
    {
        var role = new Role
        {
            Id = (int)RoleType.Student,
            Name = RoleType.Student.ToString(),
            Description = "Alumno"
        };
        db.Roles.Add(role);

        var student = Person.Create("Camila", "Sosa", "39111222");
        student.AddRole(role);

        var instructor = Person.Create("Diego", "Pérez", "29111222");
        var activity = new Activity("FUNC", "Funcional", defaultCapacity: 20);
        var room = new Room { Id = Guid.NewGuid(), Name = "Sala 1", Capacity = 20, IsActive = true };

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan 8 Clases",
            Price = 20000m,
            DurationDays = 30,
            Credits = 8,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            IsActive = true
        };

        db.People.AddRange(student, instructor);
        db.Activities.Add(activity);
        db.Rooms.Add(room);
        db.MembershipPlans.Add(plan);
        await db.SaveChangesAsync();

        return (student, plan, activity, room, instructor);
    }

    [Fact]
    public async Task SuspendAsync_WithWindow_CancelsAndRefundsReservationsInsideWindow_AndPreservesReservationsAfterWindow()
    {
        // Arrange (Scenario: Suspensión cancela reservas dentro de la ventana)
        await using var db = CreateDbContext();
        var start = new DateOnly(2026, 11, 1); // vence 30/11
        var calendar = new StubGymCalendar(start);
        var (student, plan, activity, room, instructor) = await SeedAsync(db);
        var service = CreateService(db, calendar);
        var ledger = new CreditLedgerService(db, calendar);

        var created = await service.CreateAsync(
            new CreateMembershipRequest(
                student.Id,
                plan.Id,
                StartDate: start,
                InitialPayment: new InitialMembershipPaymentRequest(20000m, PaymentMethod.Cash)),
            Guid.NewGuid(),
            isCallerAdministrator: false);

        var membership = await db.Memberships.SingleAsync(m => m.Id == created.Id);

        // Crear dos sesiones: una el 12/11 (dentro de ventana 10/11..14/11) y otra el 20/11 (fuera de ventana)
        var sessionInWindow = ClassSession.CreateFromSchedule(
            Guid.NewGuid(),
            activity.Id,
            room.Id,
            new DateTime(2026, 11, 12, 18, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 12, 19, 0, 0, DateTimeKind.Utc),
            20,
            instructor.Id,
            null);
        sessionInWindow.IncrementReservedCount();

        var sessionAfterWindow = ClassSession.CreateFromSchedule(
            Guid.NewGuid(),
            activity.Id,
            room.Id,
            new DateTime(2026, 11, 20, 18, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 20, 19, 0, 0, DateTimeKind.Utc),
            20,
            instructor.Id,
            null);
        sessionAfterWindow.IncrementReservedCount();

        db.ClassSessions.AddRange(sessionInWindow, sessionAfterWindow);

        var mov1 = await ledger.ConsumeCreditForReservationAsync(membership, sessionInWindow.Id, new DateOnly(2026, 11, 12), "Reserva 12/11");
        var mov2 = await ledger.ConsumeCreditForReservationAsync(membership, sessionAfterWindow.Id, new DateOnly(2026, 11, 20), "Reserva 20/11");

        var resInWindow = new Reservation
        {
            Id = Guid.NewGuid(),
            ClassSessionId = sessionInWindow.Id,
            StudentId = student.Id,
            MembershipId = membership.Id,
            CreditMovementId = mov1.Id,
            Status = ReservationStatus.Reserved,
            ReservedAtUtc = calendar.UtcNow,
            IsActive = true
        };

        var resAfterWindow = new Reservation
        {
            Id = Guid.NewGuid(),
            ClassSessionId = sessionAfterWindow.Id,
            StudentId = student.Id,
            MembershipId = membership.Id,
            CreditMovementId = mov2.Id,
            Status = ReservationStatus.Reserved,
            ReservedAtUtc = calendar.UtcNow,
            IsActive = true
        };

        db.Reservations.AddRange(resInWindow, resAfterWindow);
        await db.SaveChangesAsync();
        Assert.Equal(6, membership.AvailableCredits);

        // Act: suspender del 10/11 al 14/11 con extensión de vencimiento
        calendar.Today = new DateOnly(2026, 11, 10);
        calendar.UtcNow = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
        var suspended = await service.SuspendAsync(
            membership.Id,
            new SuspendMembershipRequest(
                Reason: "Viaje médico",
                EndDate: new DateOnly(2026, 11, 14),
                ExtendExpirationDate: true,
                Notes: "Presentó constancia"),
            Guid.NewGuid());

        // Assert
        Assert.Equal(MembershipStatus.Suspended, suspended.Status);
        Assert.Equal(7, suspended.AvailableCredits); // 1 crédito devuelto por la del 12/11
        Assert.Single(suspended.Suspensions);
        Assert.True(suspended.Suspensions[0].IsActiveSuspension);

        var updatedResIn = await db.Reservations.SingleAsync(r => r.Id == resInWindow.Id);
        var updatedResAfter = await db.Reservations.SingleAsync(r => r.Id == resAfterWindow.Id);
        Assert.Equal(ReservationStatus.Cancelled, updatedResIn.Status);
        Assert.False(updatedResIn.IsLateCancellation);
        Assert.NotNull(updatedResIn.RefundCreditMovementId);
        Assert.Equal(0, (await db.ClassSessions.SingleAsync(cs => cs.Id == sessionInWindow.Id)).ReservedCount);

        Assert.Equal(ReservationStatus.Reserved, updatedResAfter.Status);
        Assert.Equal(1, (await db.ClassSessions.SingleAsync(cs => cs.Id == sessionAfterWindow.Id)).ReservedCount);

        // Reactivar el 15/11 -> extiende EndDate en 5 días (del 30/11 al 05/12) y registra en AuditLogs
        calendar.Today = new DateOnly(2026, 11, 15);
        calendar.UtcNow = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc);
        var reactivated = await service.ReactivateAsync(
            membership.Id,
            new ReactivateMembershipRequest(Reason: "Regreso de viaje"),
            Guid.NewGuid());

        Assert.Equal(MembershipStatus.Active, reactivated.Status);
        Assert.Equal(new DateOnly(2026, 12, 5), reactivated.EndDate);
        Assert.Equal(5, reactivated.Suspensions[0].ExtensionDaysApplied);
        Assert.False(reactivated.Suspensions[0].IsActiveSuspension);
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "MEMBERSHIP_END_DATE_EXTENDED" && a.EntityId == membership.Id.ToString()));
    }

    [Fact]
    public async Task SuspendAsync_ExtendWithoutEndDate_ThrowsValidationException()
    {
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 11, 10);
        var calendar = new StubGymCalendar(today);
        var (student, plan, _, _, _) = await SeedAsync(db);
        var service = CreateService(db, calendar);

        var created = await service.CreateAsync(
            new CreateMembershipRequest(
                student.Id,
                plan.Id,
                StartDate: today,
                InitialPayment: new InitialMembershipPaymentRequest(20000m, PaymentMethod.Cash)),
            Guid.NewGuid(),
            isCallerAdministrator: false);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.SuspendAsync(
                created.Id,
                new SuspendMembershipRequest(
                    Reason: "Suspensión sin fecha de fin",
                    EndDate: null,
                    ExtendExpirationDate: true),
                Guid.NewGuid()));
    }
}
