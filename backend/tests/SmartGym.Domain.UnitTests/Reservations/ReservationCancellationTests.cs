using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Operations.Services;
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

public class ReservationCancellationTests
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

    private sealed class StubCurrentUserService : ICurrentUserService
    {
        public Guid? UserId { get; set; }
        public Guid? PersonId { get; set; }
        public string? Email { get; set; } = "user@smartgym.local";
        public bool IsAuthenticated { get; set; } = true;
        public IReadOnlyList<string> Roles { get; set; } = [SmartGym.Application.Common.Security.Roles.Student];
        public bool IsInRole(string role) => Roles.Contains(role);
    }

    private static SmartGymDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new SmartGymDbContext(options);
    }

    private static async Task<(Person Student, Activity Activity, Room Room, ClassSession Session1, ClassSession Session2)> SeedScenarioAsync(
        SmartGymDbContext db,
        StubGymCalendar calendar)
    {
        var studentRole = new Role { Id = (int)RoleType.Student, Name = RoleType.Student.ToString(), Description = "Student" };
        db.Roles.Add(studentRole);

        var student = Person.Create(
            "Camila",
            "Rojas",
            "37111222",
            birthDate: new DateTime(1997, 8, 12),
            emergencyContact: EmergencyContact.Create("Madre", "+5492644123456", "Madre"));
        student.AddRole(studentRole);

        var instructor = Person.Create("Lucas", "Vera", "25000111");
        var activity = new Activity("FUNC", "Funcional", defaultCapacity: 10);
        var room = new Room { Id = Guid.NewGuid(), Name = "Sala Funcional", Capacity = 10, IsActive = true };

        db.People.AddRange(student, instructor);
        db.Activities.Add(activity);
        db.Rooms.Add(room);

        var s1Start = calendar.UtcNow.AddHours(4);
        var session1 = ClassSession.CreateFromSchedule(
            Guid.NewGuid(), activity.Id, room.Id, s1Start, s1Start.AddHours(1), 10, instructor.Id, null);
        session1.Open(calendar.UtcNow);

        var s2Start = calendar.UtcNow.AddHours(6);
        var session2 = ClassSession.CreateFromSchedule(
            Guid.NewGuid(), activity.Id, room.Id, s2Start, s2Start.AddHours(1), 10, instructor.Id, null);
        session2.Open(calendar.UtcNow);

        db.ClassSessions.AddRange(session1, session2);
        await db.SaveChangesAsync();

        return (student, activity, room, session1, session2);
    }

    [Fact]
    public async Task EarlyCancellation_RestoresAccessRightAcrossAllModalities_UsesDefaultReasonAndRecordsAudit()
    {
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 8, 10);
        var calendar = new StubGymCalendar(today);
        var (student, activity, _, session1, session2) = await SeedScenarioAsync(db, calendar);
        var userId = Guid.NewGuid();
        var currentUser = new StubCurrentUserService
        {
            UserId = userId,
            PersonId = student.Id,
            Roles = [Roles.Student]
        };
        var service = new ReservationService(db, calendar, currentUserService: currentUser, auditService: new AuditService(db));

        // 1. Crédito compensatorio autónomo: cancelar con anticipación (faltan 4h >= 2h) lo libera para usar en session2 y asigna motivo por defecto
        var comp = new MembershipCreditMovement
        {
            Id = Guid.NewGuid(),
            StudentId = student.Id,
            Type = CreditMovementType.ClassSuspensionCompensation,
            Amount = 1,
            BalanceAfter = 1,
            Reason = "Compensación",
            ActivityId = activity.Id,
            ExpiresOn = today.AddDays(15),
            CreatedAtUtc = calendar.UtcNow,
            IsActive = true
        };
        db.MembershipCreditMovements.Add(comp);
        await db.SaveChangesAsync();

        var resComp = await service.CreateReservationAsync(
            new CreateReservationRequest(session1.Id, student.Id, SingleClassTicketMovementId: comp.Id),
            userId);

        var cancelledComp = await service.CancelReservationAsync(
            resComp.Id,
            new CancelReservationRequest(Reason: null),
            userId);

        Assert.Equal(ReservationStatus.Cancelled, cancelledComp.Status);
        Assert.False(cancelledComp.IsLateCancellation);
        Assert.Equal("Cancelada por el alumno", cancelledComp.CancellationReason);
        Assert.Equal(ReservationCancellationSource.Student, cancelledComp.CancellationSource);
        Assert.Equal(0, (await db.ClassSessions.SingleAsync(s => s.Id == session1.Id)).ReservedCount);

        // El crédito compensatorio quedó liberado y puede usarse en session2
        var reusedComp = await service.CreateReservationAsync(
            new CreateReservationRequest(session2.Id, student.Id, SingleClassTicketMovementId: comp.Id),
            userId);
        Assert.Equal(ReservationStatus.Confirmed, reusedComp.Status);

        // 2. Promoción: cancelar con anticipación anula el canje (VoidRedemptionAsync) y permite volver a usar el cupón
        var promo = new Promotion
        {
            Id = Guid.NewGuid(),
            Code = "CLASEGRATIS",
            Name = "Clase Gratis",
            ConditionType = PromotionConditionType.CouponCode,
            BenefitType = PromotionBenefitType.PercentageDiscount,
            BenefitValue = 100m,
            ValidFrom = today.AddDays(-5),
            ValidTo = today.AddDays(10),
            MaxUsesPerPerson = 1,
            IsActive = true
        };
        db.Promotions.Add(promo);
        await db.SaveChangesAsync();

        var resPromo = await service.CreateReservationAsync(
            new CreateReservationRequest(session1.Id, student.Id, PromotionCode: "CLASEGRATIS"),
            userId);
        Assert.NotNull(resPromo.PromotionRedemptionId);

        await service.CancelReservationAsync(resPromo.Id, new CancelReservationRequest("No llego"), userId);
        var redemption = await db.PromotionRedemptions.SingleAsync(r => r.Id == resPromo.PromotionRedemptionId);
        Assert.False(redemption.IsActive);

        // Puede volver a usar el mismo cupón en session1
        var resPromoAgain = await service.CreateReservationAsync(
            new CreateReservationRequest(session1.Id, student.Id, PromotionCode: "CLASEGRATIS"),
            userId);
        Assert.Equal(ReservationStatus.Confirmed, resPromoAgain.Status);
    }

    [Fact]
    public async Task LateCancellation_RetainsAccessRightUnlessStaffOverridesPolicyWithReason()
    {
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 8, 10);
        var calendar = new StubGymCalendar(today);
        var (student, activity, _, session1, session2) = await SeedScenarioAsync(db, calendar);
        var ledger = new CreditLedgerService(db, calendar);
        var userId = Guid.NewGuid();
        var currentUser = new StubCurrentUserService
        {
            UserId = userId,
            PersonId = student.Id,
            Roles = [Roles.Student]
        };
        var service = new ReservationService(db, calendar, currentUserService: currentUser, auditService: new AuditService(db));

        // 1. Pase individual / compensatorio cancelado a 30 min del inicio -> IsLateCancellation = true y NO puede reutilizarse
        var comp = new MembershipCreditMovement
        {
            Id = Guid.NewGuid(),
            StudentId = student.Id,
            Type = CreditMovementType.ClassSuspensionCompensation,
            Amount = 1,
            BalanceAfter = 1,
            Reason = "Compensación",
            ActivityId = activity.Id,
            ExpiresOn = today.AddDays(15),
            CreatedAtUtc = calendar.UtcNow,
            IsActive = true
        };
        db.MembershipCreditMovements.Add(comp);
        await db.SaveChangesAsync();

        var res1 = await service.CreateReservationAsync(
            new CreateReservationRequest(session1.Id, student.Id, SingleClassTicketMovementId: comp.Id),
            userId);

        // Avanzamos el reloj a 30 minutos antes de session1
        calendar.UtcNow = session1.StartsAtUtc.AddMinutes(-30);

        // Alumno intenta OverridePolicy -> 403 ForbiddenException
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.CancelReservationAsync(
                res1.Id,
                new CancelReservationRequest("Emergencia", OverridePolicy: true, PolicyOverrideReason: "Motivo"),
                userId));

        // Cancelación tardía normal -> IsLateCancellation = true y el crédito compensatorio queda retenido
        var lateCancelled = await service.CancelReservationAsync(
            res1.Id,
            new CancelReservationRequest("Llegué tarde"),
            userId);
        Assert.True(lateCancelled.IsLateCancellation);

        var exReuse = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateReservationAsync(
                new CreateReservationRequest(session2.Id, student.Id, SingleClassTicketMovementId: comp.Id),
                userId));
        Assert.Equal(ReservationErrorCodes.AccessRightInUse, exReuse.Code);

        // 2. Excepción administrativa (Secretario con OverridePolicy = true y motivo obligatorio) en session2
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
        var membership = Membership.Create(student.Id, plan, today, calendar.UtcNow, 10000m, 0m, 10000m);
        membership.Activate(today, calendar.UtcNow, Guid.NewGuid());
        await ledger.RecordInitialCreditsAsync(membership);
        db.Memberships.Add(membership);
        await db.SaveChangesAsync();

        var res2 = await service.CreateReservationAsync(
            new CreateReservationRequest(session2.Id, student.Id, MembershipId: membership.Id),
            userId);
        Assert.Equal(3, (await db.Memberships.SingleAsync(m => m.Id == membership.Id)).AvailableCredits);

        // Avanzamos el reloj a 20 minutos antes de session2
        calendar.UtcNow = session2.StartsAtUtc.AddMinutes(-20);
        currentUser.Roles = [Roles.Secretary];
        var secretaryUserId = Guid.NewGuid();
        currentUser.UserId = secretaryUserId;

        // Sin motivo obligatorio -> 400 ArgumentException
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CancelReservationAsync(
                res2.Id,
                new CancelReservationRequest("Emergencia", OverridePolicy: true, PolicyOverrideReason: ""),
                secretaryUserId));

        // Con motivo -> IsLateCancellation = false, IsPolicyOverride = true y devuelve el crédito
        var overrideCancelled = await service.CancelReservationAsync(
            res2.Id,
            new CancelReservationRequest(null, OverridePolicy: true, PolicyOverrideReason: "Certificado médico presentado"),
            secretaryUserId);

        Assert.False(overrideCancelled.IsLateCancellation);
        Assert.True(overrideCancelled.IsPolicyOverride);
        Assert.Equal("Certificado médico presentado", overrideCancelled.PolicyOverrideReason);
        Assert.Equal("Cancelada por el personal", overrideCancelled.CancellationReason);
        Assert.Equal(ReservationCancellationSource.Staff, overrideCancelled.CancellationSource);
        Assert.NotNull(overrideCancelled.RefundCreditMovementId);
        Assert.Equal(4, (await db.Memberships.SingleAsync(m => m.Id == membership.Id)).AvailableCredits);
    }

    [Fact]
    public async Task CancelReservationAsync_AfterSessionStartOrRepeatedCancellation_ThrowsConflictAndDoesNotDoubleRefund()
    {
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 8, 10);
        var calendar = new StubGymCalendar(today);
        var (student, _, _, session1, session2) = await SeedScenarioAsync(db, calendar);
        var ledger = new CreditLedgerService(db, calendar);
        var userId = Guid.NewGuid();
        var currentUser = new StubCurrentUserService
        {
            UserId = userId,
            PersonId = student.Id,
            Roles = [Roles.Administrator]
        };
        var service = new ReservationService(db, calendar, currentUserService: currentUser);

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
        var membership = Membership.Create(student.Id, plan, today, calendar.UtcNow, 15000m, 0m, 15000m);
        membership.Activate(today, calendar.UtcNow, Guid.NewGuid());
        await ledger.RecordInitialCreditsAsync(membership);
        db.Memberships.Add(membership);
        await db.SaveChangesAsync();

        var res1 = await service.CreateReservationAsync(new CreateReservationRequest(session1.Id, student.Id), userId);
        var res2 = await service.CreateReservationAsync(new CreateReservationRequest(session2.Id, student.Id), userId);
        Assert.Equal(6, (await db.Memberships.SingleAsync(m => m.Id == membership.Id)).AvailableCredits);

        // 1. Cancelamos res1 antes del inicio y luego intentamos cancelarla por segunda vez -> 409 sin segunda devolución
        await service.CancelReservationAsync(res1.Id, new CancelReservationRequest("Primera cancelación"), userId);
        Assert.Equal(7, (await db.Memberships.SingleAsync(m => m.Id == membership.Id)).AvailableCredits);

        var exRepeat = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CancelReservationAsync(res1.Id, new CancelReservationRequest("Segunda cancelación"), userId));
        Assert.Equal(ReservationErrorCodes.InvalidStateTransition, exRepeat.Code);
        Assert.Equal(7, (await db.Memberships.SingleAsync(m => m.Id == membership.Id)).AvailableCredits);

        // 2. Avanzamos el reloj al inicio de session2 -> ni siquiera un administrador con OverridePolicy puede cancelar después del inicio
        calendar.UtcNow = session2.StartsAtUtc;
        var exStarted = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CancelReservationAsync(
                res2.Id,
                new CancelReservationRequest("Ya empezó", OverridePolicy: true, PolicyOverrideReason: "Excepción"),
                userId));
        Assert.Equal(ReservationErrorCodes.ClassAlreadyStarted, exStarted.Code);
    }
}
