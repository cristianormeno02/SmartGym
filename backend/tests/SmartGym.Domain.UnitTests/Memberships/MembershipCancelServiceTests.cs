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
using SmartGym.Domain.Entities.Promotions;
using SmartGym.Domain.Entities.Reservations;
using SmartGym.Domain.Enums;
using SmartGym.Domain.UnitTests.Common;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Memberships;

public class MembershipCancelServiceTests
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

        var student = Person.Create("Federico", "Castro", "32444555");
        student.AddRole(role);

        var instructor = Person.Create("Diego", "Pérez", "29111222");
        var activity = new Activity("FUNC", "Funcional", defaultCapacity: 20);
        var room = new Room { Id = Guid.NewGuid(), Name = "Sala 1", Capacity = 20, IsActive = true };

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan 4 Clases",
            Price = 15000m,
            DurationDays = 30,
            Credits = 4,
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
    public async Task CancelAsync_ActiveMembershipWithRemainingCreditsAndFutureReservations_ForfeitsCredits_CancelsFutureReservations_AndPreservesHistory()
    {
        // Arrange (Criterio 9)
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 7, 1);
        var calendar = new StubGymCalendar(today);
        var (student, plan, activity, room, instructor) = await SeedAsync(db);
        var service = CreateService(db, calendar);
        var ledger = new CreditLedgerService(db, calendar);

        var promo = new Promotion
        {
            Id = Guid.NewGuid(),
            Code = "DESC10",
            Name = "10% Off",
            ConditionType = PromotionConditionType.CouponCode,
            BenefitType = PromotionBenefitType.PercentageDiscount,
            BenefitValue = 10m,
            ValidFrom = today.AddDays(-5),
            ValidTo = today.AddDays(30),
            MaxUsesPerPerson = 1,
            IsActive = true
        };
        db.Promotions.Add(promo);
        await db.SaveChangesAsync();

        var created = await service.CreateAsync(
            new CreateMembershipRequest(
                student.Id,
                plan.Id,
                StartDate: today,
                PromotionCode: "DESC10",
                InitialPayment: new InitialMembershipPaymentRequest(13500m, PaymentMethod.Cash)),
            Guid.NewGuid(),
            isCallerAdministrator: false);

        var membership = await db.Memberships.SingleAsync(m => m.Id == created.Id);

        // 1 reserva futura
        var futureSession = ClassSession.CreateFromSchedule(
            Guid.NewGuid(),
            activity.Id,
            room.Id,
            new DateTime(2026, 7, 10, 18, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 10, 19, 0, 0, DateTimeKind.Utc),
            20,
            instructor.Id,
            null);
        futureSession.IncrementReservedCount();
        db.ClassSessions.Add(futureSession);

        var debitMov = await ledger.ConsumeCreditForReservationAsync(membership, futureSession.Id, new DateOnly(2026, 7, 10), "Reserva futura");
        var futureRes = new Reservation
        {
            Id = Guid.NewGuid(),
            ClassSessionId = futureSession.Id,
            StudentId = student.Id,
            MembershipId = membership.Id,
            CreditMovementId = debitMov.Id,
            Status = ReservationStatus.Confirmed,
            ReservedAtUtc = calendar.UtcNow,
            IsActive = true
        };
        db.Reservations.Add(futureRes);
        await db.SaveChangesAsync();
        Assert.Equal(3, membership.AvailableCredits);

        // Motivo obligatorio
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CancelAsync(membership.Id, new CancelMembershipRequest("   "), Guid.NewGuid()));

        // Act
        var cancelled = await service.CancelAsync(
            membership.Id,
            new CancelMembershipRequest("Baja solicitada en recepción"),
            Guid.NewGuid());

        // Assert
        Assert.Equal(MembershipStatus.Cancelled, cancelled.Status);
        Assert.Equal(0, cancelled.AvailableCredits);
        Assert.Equal("Baja solicitada en recepción", cancelled.CancellationReason);
        Assert.NotNull(cancelled.CancelledAtUtc);

        // Baja de los 3 créditos remanentes registrada en el ledger
        var forfeitureMov = await db.MembershipCreditMovements
            .SingleAsync(m => m.MembershipId == membership.Id && m.Type == CreditMovementType.CancellationForfeiture);
        Assert.Equal(-3, forfeitureMov.Amount);
        Assert.Equal(0, forfeitureMov.BalanceAfter);

        // Reserva futura cancelada y cupo liberado
        var updatedRes = await db.Reservations.SingleAsync(r => r.Id == futureRes.Id);
        Assert.Equal(ReservationStatus.Cancelled, updatedRes.Status);
        Assert.Equal(0, (await db.ClassSessions.SingleAsync(cs => cs.Id == futureSession.Id)).ReservedCount);

        // Historial intacto: el pago y los movimientos previos siguen existiendo; el canje de una membresía ya activada NO se devuelve
        Assert.Single(cancelled.Payments);
        var redemption = await db.PromotionRedemptions.SingleAsync(r => r.Id == cancelled.PromotionRedemptionId);
        Assert.True(redemption.IsActive);
        Assert.Equal(1, (await db.Promotions.SingleAsync(p => p.Id == promo.Id)).CurrentUsesTotal);
    }

    [Fact]
    public async Task CancelAsync_PendingMembershipWithPromotion_VoidsRedemptionAndRestoresPromotionUse()
    {
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 7, 1);
        var calendar = new StubGymCalendar(today);
        var (student, plan, _, _, _) = await SeedAsync(db);
        var service = CreateService(db, calendar);

        var promo = new Promotion
        {
            Id = Guid.NewGuid(),
            Code = "UNICOUSO",
            Name = "15% Off",
            ConditionType = PromotionConditionType.CouponCode,
            BenefitType = PromotionBenefitType.PercentageDiscount,
            BenefitValue = 15m,
            ValidFrom = today.AddDays(-5),
            ValidTo = today.AddDays(30),
            MaxUsesPerPerson = 1,
            MaxUsesTotal = 5,
            IsActive = true
        };
        db.Promotions.Add(promo);
        await db.SaveChangesAsync();

        var pending = await service.CreateAsync(
            new CreateMembershipRequest(
                student.Id,
                plan.Id,
                StartDate: today,
                PromotionCode: "UNICOUSO"),
            Guid.NewGuid(),
            isCallerAdministrator: false);
        Assert.Equal(MembershipStatus.Pending, pending.Status);
        Assert.Equal(1, (await db.Promotions.SingleAsync(p => p.Id == promo.Id)).CurrentUsesTotal);

        // Cancelar la Pending -> debe anular el canje y permitir al alumno volver a usar el cupón
        await service.CancelAsync(pending.Id, new CancelMembershipRequest("Error de carga"), Guid.NewGuid());

        var redemption = await db.PromotionRedemptions.SingleAsync(r => r.Id == pending.PromotionRedemptionId);
        Assert.False(redemption.IsActive);
        Assert.Equal(0, (await db.Promotions.SingleAsync(p => p.Id == promo.Id)).CurrentUsesTotal);

        // El alumno puede volver a contratar usando el mismo código con MaxUsesPerPerson = 1
        var secondAttempt = await service.CreateAsync(
            new CreateMembershipRequest(
                student.Id,
                plan.Id,
                StartDate: today,
                PromotionCode: "UNICOUSO",
                InitialPayment: new InitialMembershipPaymentRequest(12750m, PaymentMethod.Cash)),
            Guid.NewGuid(),
            isCallerAdministrator: false);
        Assert.Equal(MembershipStatus.Active, secondAttempt.Status);
    }
}
