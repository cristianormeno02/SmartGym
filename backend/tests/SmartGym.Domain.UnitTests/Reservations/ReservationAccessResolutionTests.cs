using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Modules.Memberships.Dtos;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Promotions.Services;
using SmartGym.Application.Modules.Reservations.Dtos;
using SmartGym.Application.Modules.Reservations.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Entities.Promotions;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Reservations;

public class ReservationAccessResolutionTests
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

    private static async Task<(Person Student, Activity ActFunc, Activity ActPilates, Room Room, ClassSession SessionFunc)> SeedStudentAndSessionAsync(
        SmartGymDbContext db,
        StubGymCalendar calendar)
    {
        var studentRole = new Role { Id = (int)RoleType.Student, Name = RoleType.Student.ToString(), Description = "Student" };
        db.Roles.Add(studentRole);

        var student = Person.Create(
            "Sofía",
            "Martínez",
            "40222333",
            birthDate: new DateTime(1999, 3, 15),
            emergencyContact: EmergencyContact.Create("Madre", "+5492644999999", "Madre"));
        student.AddRole(studentRole);

        var instructor = Person.Create("Diego", "López", "26000111");
        var actFunc = new Activity("FUNC", "Funcional", defaultCapacity: 15, minAge: 16, maxAge: 65);
        var actPilates = new Activity("PILATES", "Pilates", defaultCapacity: 15, minAge: 16, maxAge: 65);
        var room = new Room { Id = Guid.NewGuid(), Name = "Sala 1", Capacity = 15, IsActive = true };

        db.People.AddRange(student, instructor);
        db.Activities.AddRange(actFunc, actPilates);
        db.Rooms.Add(room);

        var starts = calendar.UtcNow.AddDays(2);
        var session = ClassSession.CreateFromSchedule(
            Guid.NewGuid(),
            actFunc.Id,
            room.Id,
            starts,
            starts.AddHours(1),
            15,
            instructor.Id,
            null);
        session.Open(calendar.UtcNow);
        db.ClassSessions.Add(session);

        await db.SaveChangesAsync();
        return (student, actFunc, actPilates, room, session);
    }

    [Fact]
    public async Task AutomaticResolutionOrder_PrioritizesMembership_ThenSingleClassTicket_ThenCompensatoryCreditByEarliestExpiration()
    {
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 8, 1);
        var calendar = new StubGymCalendar(today);
        var (student, actFunc, _, room, session1) = await SeedStudentAndSessionAsync(db, calendar);
        var ledger = new CreditLedgerService(db, calendar);
        var service = new ReservationService(db, calendar);

        // Creamos 3 clases distintas para probar la cascada automática
        var session2 = ClassSession.CreateFromSchedule(
            Guid.NewGuid(), actFunc.Id, room.Id,
            calendar.UtcNow.AddDays(3), calendar.UtcNow.AddDays(3).AddHours(1), 15, session1.ScheduledInstructorId, null);
        session2.Open(calendar.UtcNow);

        var session3 = ClassSession.CreateFromSchedule(
            Guid.NewGuid(), actFunc.Id, room.Id,
            calendar.UtcNow.AddDays(4), calendar.UtcNow.AddDays(4).AddHours(1), 15, session1.ScheduledInstructorId, null);
        session3.Open(calendar.UtcNow);
        db.ClassSessions.AddRange(session2, session3);

        // 1. Membresía con 1 crédito
        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan 1 Clase",
            DurationDays = 30,
            Price = 5000m,
            Credits = 1,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            IsActive = true
        };
        db.MembershipPlans.Add(plan);
        var membership = Membership.Create(student.Id, plan, today, calendar.UtcNow, 5000m, 0m, 5000m);
        membership.Activate(today, calendar.UtcNow, Guid.NewGuid());
        await ledger.RecordInitialCreditsAsync(membership);
        db.Memberships.Add(membership);

        // 2. Pase individual para session2 con pago Completed
        var ticketSession2 = new MembershipCreditMovement
        {
            Id = Guid.NewGuid(),
            StudentId = student.Id,
            ClassSessionId = session2.Id,
            Type = CreditMovementType.SingleClassTicket,
            Amount = 1,
            Reason = "Pase individual clase 2",
            CreatedAtUtc = calendar.UtcNow,
            IsActive = true
        };
        var paymentSession2 = new Payment
        {
            Id = Guid.NewGuid(),
            PayerPersonId = student.Id,
            ClassSessionId = session2.Id,
            Amount = 3500m,
            Method = PaymentMethod.Cash,
            Status = PaymentStatus.Completed,
            PaidAtUtc = calendar.UtcNow,
            IsActive = true
        };
        db.MembershipCreditMovements.Add(ticketSession2);
        db.Payments.Add(paymentSession2);

        // 3. Dos créditos compensatorios autónomos (uno vence antes que el otro)
        var compLater = new MembershipCreditMovement
        {
            Id = Guid.NewGuid(),
            StudentId = student.Id,
            Type = CreditMovementType.ClassSuspensionCompensation,
            Amount = 1,
            BalanceAfter = 1,
            Reason = "Compensatorio vence tarde",
            ActivityId = actFunc.Id,
            ExpiresOn = today.AddDays(20),
            CreatedAtUtc = calendar.UtcNow,
            IsActive = true
        };
        var compSooner = new MembershipCreditMovement
        {
            Id = Guid.NewGuid(),
            StudentId = student.Id,
            Type = CreditMovementType.ClassSuspensionCompensation,
            Amount = 1,
            BalanceAfter = 1,
            Reason = "Compensatorio vence antes",
            ActivityId = actFunc.Id,
            ExpiresOn = today.AddDays(10),
            CreatedAtUtc = calendar.UtcNow.AddMinutes(1),
            IsActive = true
        };
        db.MembershipCreditMovements.AddRange(compLater, compSooner);

        await db.SaveChangesAsync();

        // Reserva en session1 -> usa la membresía (prioridad 1)
        var r1 = await service.CreateReservationAsync(new CreateReservationRequest(session1.Id, student.Id), Guid.NewGuid());
        Assert.Equal(ReservationAccessType.MembershipCredit, r1.AccessType);
        Assert.Equal(membership.Id, r1.MembershipId);

        // Reserva en session2 (membresía ya agotada) -> usa el pase individual de session2 (prioridad 2)
        var r2 = await service.CreateReservationAsync(new CreateReservationRequest(session2.Id, student.Id), Guid.NewGuid());
        Assert.Equal(ReservationAccessType.SingleClassTicket, r2.AccessType);
        Assert.Equal(ticketSession2.Id, r2.SingleClassTicketMovementId);
        Assert.Equal(paymentSession2.Id, r2.PaymentId);

        // Reserva en session3 -> usa el crédito compensatorio que vence antes (compSooner)
        var r3 = await service.CreateReservationAsync(new CreateReservationRequest(session3.Id, student.Id), Guid.NewGuid());
        Assert.Equal(ReservationAccessType.CompensatoryCredit, r3.AccessType);
        Assert.Equal(compSooner.Id, r3.SingleClassTicketMovementId);
        Assert.Null(compLater.ReservationId);
    }

    [Fact]
    public async Task ExplicitSelection_DoesNotFallbackToAlternativeAndRejectsWhenInvalid()
    {
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 8, 1);
        var calendar = new StubGymCalendar(today);
        var (student, actFunc, actPilates, _, sessionFunc) = await SeedStudentAndSessionAsync(db, calendar);
        var ledger = new CreditLedgerService(db, calendar);
        var service = new ReservationService(db, calendar);

        // El alumno tiene un crédito compensatorio válido para Funcional, pero pide usar explícitamente una membresía de Pilates
        await ledger.AddCompensatoryCreditAsync(
            new AddCompensatoryCreditRequest(student.Id, null, "Compensatorio válido", actFunc.Id),
            Guid.NewGuid());

        var pilatesPlan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Sólo Pilates",
            DurationDays = 30,
            Price = 10000m,
            Credits = 4,
            IsUnlimited = false,
            AppliesToAllActivities = false,
            IsActive = true
        };
        pilatesPlan.AllowedActivities.Add(new MembershipPlanActivity { MembershipPlanId = pilatesPlan.Id, ActivityId = actPilates.Id });
        db.MembershipPlans.Add(pilatesPlan);

        var pilatesMembership = Membership.Create(student.Id, pilatesPlan, today, calendar.UtcNow, 10000m, 0m, 10000m);
        pilatesMembership.Activate(today, calendar.UtcNow, Guid.NewGuid());
        await ledger.RecordInitialCreditsAsync(pilatesMembership);
        db.Memberships.Add(pilatesMembership);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateReservationAsync(
                new CreateReservationRequest(sessionFunc.Id, student.Id, MembershipId: pilatesMembership.Id),
                Guid.NewGuid()));

        Assert.Equal(EligibilityIssueCodes.ActivityNotAllowed, ex.Code);
        Assert.Empty(await db.Reservations.ToListAsync());
    }

    [Fact]
    public async Task SingleClassTicket_RejectsWhenPaymentPendingOrAlreadyHeldByAnotherReservation()
    {
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 8, 1);
        var calendar = new StubGymCalendar(today);
        var (student, _, _, _, session) = await SeedStudentAndSessionAsync(db, calendar);
        var service = new ReservationService(db, calendar);

        var ticket = new MembershipCreditMovement
        {
            Id = Guid.NewGuid(),
            StudentId = student.Id,
            ClassSessionId = session.Id,
            Type = CreditMovementType.SingleClassTicket,
            Amount = 1,
            Reason = "Pase individual",
            CreatedAtUtc = calendar.UtcNow,
            IsActive = true
        };
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            PayerPersonId = student.Id,
            ClassSessionId = session.Id,
            Amount = 3500m,
            Method = PaymentMethod.BankTransfer,
            Status = PaymentStatus.Pending,
            PaidAtUtc = calendar.UtcNow,
            IsActive = true
        };
        db.MembershipCreditMovements.Add(ticket);
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        // 1. Pago Pending -> 409 PAYMENT_NOT_CONFIRMED
        var exPending = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateReservationAsync(
                new CreateReservationRequest(session.Id, student.Id, SingleClassTicketMovementId: ticket.Id),
                Guid.NewGuid()));
        Assert.Equal(ReservationErrorCodes.PaymentNotConfirmed, exPending.Code);

        // 2. Confirmamos el pago -> reserva exitosa
        payment.Status = PaymentStatus.Completed;
        await db.SaveChangesAsync();

        var created = await service.CreateReservationAsync(
            new CreateReservationRequest(session.Id, student.Id, SingleClassTicketMovementId: ticket.Id),
            Guid.NewGuid());
        Assert.Equal(ReservationAccessType.SingleClassTicket, created.AccessType);
        Assert.Equal(ticket.Id, created.SingleClassTicketMovementId);
    }

    [Fact]
    public async Task CompensatoryCredit_RejectsWhenExpiredOrOutOfActivityScope_AndPreventsReuseWhileHeld()
    {
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 8, 1);
        var calendar = new StubGymCalendar(today);
        var (student, actFunc, actPilates, room, session1) = await SeedStudentAndSessionAsync(db, calendar);
        var service = new ReservationService(db, calendar);

        var session2 = ClassSession.CreateFromSchedule(
            Guid.NewGuid(), actFunc.Id, room.Id,
            calendar.UtcNow.AddDays(3), calendar.UtcNow.AddDays(3).AddHours(1), 15, session1.ScheduledInstructorId, null);
        session2.Open(calendar.UtcNow);
        db.ClassSessions.Add(session2);

        // Crédito vencido antes de la fecha de session1 (today + 2)
        var expiredComp = new MembershipCreditMovement
        {
            Id = Guid.NewGuid(),
            StudentId = student.Id,
            Type = CreditMovementType.ClassSuspensionCompensation,
            Amount = 1,
            BalanceAfter = 1,
            Reason = "Vencido",
            ActivityId = actFunc.Id,
            ExpiresOn = today.AddDays(1),
            CreatedAtUtc = calendar.UtcNow,
            IsActive = true
        };

        // Crédito válido pero sólo para Pilates
        var wrongActivityComp = new MembershipCreditMovement
        {
            Id = Guid.NewGuid(),
            StudentId = student.Id,
            Type = CreditMovementType.ClassSuspensionCompensation,
            Amount = 1,
            BalanceAfter = 1,
            Reason = "Sólo Pilates",
            ActivityId = actPilates.Id,
            ExpiresOn = today.AddDays(15),
            CreatedAtUtc = calendar.UtcNow,
            IsActive = true
        };
        db.MembershipCreditMovements.AddRange(expiredComp, wrongActivityComp);
        await db.SaveChangesAsync();

        // Sin otro derecho -> 409 NO_ACCESS_RIGHT
        var exAuto = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateReservationAsync(new CreateReservationRequest(session1.Id, student.Id), Guid.NewGuid()));
        Assert.Equal(ReservationErrorCodes.NoAccessRight, exAuto.Code);

        // Selección explícita del vencido -> 409 NO_ACCESS_RIGHT
        var exExpired = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateReservationAsync(
                new CreateReservationRequest(session1.Id, student.Id, SingleClassTicketMovementId: expiredComp.Id),
                Guid.NewGuid()));
        Assert.Equal(ReservationErrorCodes.NoAccessRight, exExpired.Code);

        // Selección explícita del de otra actividad -> 409 ACTIVITY_NOT_ALLOWED
        var exScope = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateReservationAsync(
                new CreateReservationRequest(session1.Id, student.Id, SingleClassTicketMovementId: wrongActivityComp.Id),
                Guid.NewGuid()));
        Assert.Equal(EligibilityIssueCodes.ActivityNotAllowed, exScope.Code);

        // Emitimos uno vigente para Funcional -> reserva session1 y no puede reusarse en session2
        var validComp = new MembershipCreditMovement
        {
            Id = Guid.NewGuid(),
            StudentId = student.Id,
            Type = CreditMovementType.ClassSuspensionCompensation,
            Amount = 1,
            BalanceAfter = 1,
            Reason = "Vigente Funcional",
            ActivityId = actFunc.Id,
            ExpiresOn = today.AddDays(15),
            CreatedAtUtc = calendar.UtcNow,
            IsActive = true
        };
        db.MembershipCreditMovements.Add(validComp);
        await db.SaveChangesAsync();

        var r1 = await service.CreateReservationAsync(
            new CreateReservationRequest(session1.Id, student.Id, SingleClassTicketMovementId: validComp.Id),
            Guid.NewGuid());
        Assert.Equal(ReservationAccessType.CompensatoryCredit, r1.AccessType);

        var exReuse = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateReservationAsync(
                new CreateReservationRequest(session2.Id, student.Id, SingleClassTicketMovementId: validComp.Id),
                Guid.NewGuid()));
        Assert.Equal(ReservationErrorCodes.AccessRightInUse, exReuse.Code);
    }

    [Fact]
    public async Task PromotionalBenefit_AcceptsFullAccessPromotionAndRejectsPartialDiscount_AndUnlimitedMembershipUsesZeroAmountLedgerTrace()
    {
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 8, 1);
        var calendar = new StubGymCalendar(today);
        var (student, actFunc, _, room, session1) = await SeedStudentAndSessionAsync(db, calendar);
        var service = new ReservationService(db, calendar);

        var session2 = ClassSession.CreateFromSchedule(
            Guid.NewGuid(), actFunc.Id, room.Id,
            calendar.UtcNow.AddDays(3), calendar.UtcNow.AddDays(3).AddHours(1), 15, session1.ScheduledInstructorId, null);
        session2.Open(calendar.UtcNow);
        db.ClassSessions.Add(session2);

        var partialPromo = new Promotion
        {
            Id = Guid.NewGuid(),
            Name = "20% Off",
            Code = "DESC20",
            Description = "Descuento parcial",
            ConditionType = PromotionConditionType.CouponCode,
            BenefitType = PromotionBenefitType.PercentageDiscount,
            BenefitValue = 20m,
            ValidFrom = today.AddDays(-5),
            ValidTo = today.AddDays(10),
            IsActive = true
        };
        var freeClassPromo = new Promotion
        {
            Id = Guid.NewGuid(),
            Name = "Clase de Prueba Gratis",
            Code = "PRUEBAGRATIS",
            Description = "100% bonificada",
            ConditionType = PromotionConditionType.CouponCode,
            BenefitType = PromotionBenefitType.PercentageDiscount,
            BenefitValue = 100m,
            ValidFrom = today.AddDays(-5),
            ValidTo = today.AddDays(10),
            IsActive = true
        };
        db.Promotions.AddRange(partialPromo, freeClassPromo);
        await db.SaveChangesAsync();

        // 1. Promoción parcial -> 409 PROMOTION_NOT_APPLICABLE
        var exPartial = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateReservationAsync(
                new CreateReservationRequest(session1.Id, student.Id, PromotionCode: "DESC20"),
                Guid.NewGuid()));
        Assert.Equal(ReservationErrorCodes.PromotionNotApplicable, exPartial.Code);

        // 2. Promoción de acceso completo -> CONFIRMED con PromotionalBenefit y PromotionRedemptionId
        var rPromo = await service.CreateReservationAsync(
            new CreateReservationRequest(session1.Id, student.Id, PromotionCode: "PRUEBAGRATIS"),
            Guid.NewGuid());
        Assert.Equal(ReservationAccessType.PromotionalBenefit, rPromo.AccessType);
        Assert.NotNull(rPromo.PromotionRedemptionId);

        // 3. Membresía ilimitada en session2 -> UnlimitedMembership con movimiento de cantidad 0
        var unlimitedPlan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Pase Libre",
            DurationDays = 30,
            Price = 30000m,
            Credits = 0,
            IsUnlimited = true,
            AppliesToAllActivities = true,
            IsActive = true
        };
        db.MembershipPlans.Add(unlimitedPlan);
        var unlimitedMembership = Membership.Create(student.Id, unlimitedPlan, today, calendar.UtcNow, 30000m, 0m, 30000m);
        unlimitedMembership.Activate(today, calendar.UtcNow, Guid.NewGuid());
        db.Memberships.Add(unlimitedMembership);
        await db.SaveChangesAsync();

        var rUnlimited = await service.CreateReservationAsync(
            new CreateReservationRequest(session2.Id, student.Id, MembershipId: unlimitedMembership.Id),
            Guid.NewGuid());
        Assert.Equal(ReservationAccessType.UnlimitedMembership, rUnlimited.AccessType);
        Assert.NotNull(rUnlimited.CreditMovementId);
        var traceMovement = await db.MembershipCreditMovements.SingleAsync(m => m.Id == rUnlimited.CreditMovementId);
        Assert.Equal(0, traceMovement.Amount);
        Assert.Equal(rUnlimited.Id, traceMovement.ReservationId);
    }
}
