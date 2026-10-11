using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Application.Modules.Promotions.Services;
using SmartGym.Application.Modules.Reservations.Dtos;
using SmartGym.Application.Modules.Reservations.Handlers;
using SmartGym.Application.Modules.Reservations.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Entities.Promotions;
using SmartGym.Domain.Entities.Reservations;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Activities;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Reservations;

public class CancelReservationsOnSessionVoidedTests
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

    private readonly DbContextOptions<SmartGymDbContext> _options;
    private readonly Guid _activityId;
    private readonly Guid _roomId;
    private readonly Guid _instructorId;
    private readonly Guid _student1Id = Guid.NewGuid();
    private readonly Guid _student2Id = Guid.NewGuid();
    private readonly Guid _student3Id = Guid.NewGuid();
    private readonly Guid _student4Id = Guid.NewGuid();
    private readonly Guid _student5Id = Guid.NewGuid();
    private readonly Guid _planId = Guid.NewGuid();

    public CancelReservationsOnSessionVoidedTests()
    {
        _options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        using var seed = new SmartGymDbContext(_options);

        var studentRole = new Role { Id = (int)RoleType.Student, Name = RoleType.Student.ToString(), Description = "Student" };
        var instructorRole = new Role { Id = (int)RoleType.Instructor, Name = RoleType.Instructor.ToString(), Description = "Instructor" };
        seed.Roles.AddRange(studentRole, instructorRole);

        var s1 = Person.Create("Student", "One", "30111001", birthDate: new DateTime(1995, 1, 1));
        s1.Id = _student1Id;
        s1.AddRole(studentRole);

        var s2 = Person.Create("Student", "Two", "30111002", birthDate: new DateTime(1995, 1, 2));
        s2.Id = _student2Id;
        s2.AddRole(studentRole);

        var s3 = Person.Create("Student", "Three", "30111003", birthDate: new DateTime(1995, 1, 3));
        s3.Id = _student3Id;
        s3.AddRole(studentRole);

        var s4 = Person.Create("Student", "Four", "30111004", birthDate: new DateTime(1995, 1, 4));
        s4.Id = _student4Id;
        s4.AddRole(studentRole);

        var s5 = Person.Create("Student", "Five", "30111005", birthDate: new DateTime(1995, 1, 5));
        s5.Id = _student5Id;
        s5.AddRole(studentRole);

        var inst = Person.Create("Profe", "Test", "20111000");
        _instructorId = inst.Id;
        inst.AddRole(instructorRole);

        var room = new Room { Id = Guid.NewGuid(), Name = "Sala A", Capacity = 30, IsActive = true };
        _roomId = room.Id;

        var activity = new Activity("PILATES", "Pilates", defaultCapacity: 10);
        _activityId = activity.Id;

        var plan = new MembershipPlan { Id = _planId, Name = "Plan Mensual", Credits = 10, Price = 1000, DurationDays = 30, AppliesToAllActivities = true, IsActive = true };

        seed.AddRange(s1, s2, s3, s4, s5, inst, room, activity, plan);
        seed.SaveChanges();
    }

    [Fact]
    public async Task SuspendSession_CancelsConfirmedPendingWaitlisted_RestoresAllModalitiesIdempotently_AndRecordsAudit()
    {
        using var setupDb = new SmartGymDbContext(_options);
        var today = new DateOnly(2026, 8, 20);
        var calendar = new StubGymCalendar(today);

        var starts = calendar.UtcNow.AddHours(1); // Incluso dentro de la ventana de cancelación tardía (< 2h), IsLateCancellation debe ser false
        var ends = starts.AddHours(1);
        var session = ClassSession.CreateExtraordinary(_activityId, _roomId, starts, ends, 10, _instructorId, null);
        session.Open(calendar.UtcNow);
        setupDb.ClassSessions.Add(session);

        // Alumno 1: con membresía (Confirmed)
        var m1 = new Membership
        {
            Id = Guid.NewGuid(),
            StudentId = _student1Id,
            MembershipPlanId = _planId,
            StartDate = today.AddDays(-5),
            EndDate = today.AddDays(15),
            TotalCredits = 10,
            AvailableCredits = 5,
            Status = MembershipStatus.Active,
            IsActive = true
        };
        setupDb.Memberships.Add(m1);

        var r1 = Reservation.CreateConfirmed(
            session.Id, _student1Id, ReservationOrigin.StudentSelfService, ReservationAccessType.MembershipCredit,
            calendar.UtcNow, membershipId: m1.Id);
        setupDb.Reservations.Add(r1);
        session.IncrementReservedCount();

        // Alumno 2: pase individual (Confirmed) -> queda cancelada sin movimiento de reembolso automático, identificada con AffectedBySessionChange = true
        var ticket = new MembershipCreditMovement
        {
            Id = Guid.NewGuid(),
            StudentId = _student2Id,
            Type = CreditMovementType.SingleClassTicket,
            Amount = 1,
            BalanceAfter = 1,
            Reason = "Pase",
            ActivityId = _activityId,
            ExpiresOn = today.AddDays(10),
            CreatedAtUtc = calendar.UtcNow,
            IsActive = true
        };
        setupDb.MembershipCreditMovements.Add(ticket);

        var r2 = Reservation.CreateConfirmed(
            session.Id, _student2Id, ReservationOrigin.StudentSelfService, ReservationAccessType.SingleClassTicket,
            calendar.UtcNow, singleClassTicketMovementId: ticket.Id);
        setupDb.Reservations.Add(r2);
        session.IncrementReservedCount();

        // Alumno 3: en Waitlisted
        var r3 = Reservation.CreateWaitlisted(
            session.Id, _student3Id, 1, ReservationOrigin.StudentSelfService, ReservationAccessType.MembershipCredit,
            calendar.UtcNow, membershipId: m1.Id);
        setupDb.Reservations.Add(r3);

        // Alumno 4: con promoción (Confirmed) -> el canje debe anularse (IsActive = false)
        var promo = new Promotion
        {
            Id = Guid.NewGuid(),
            Code = "PILATESFREE",
            Name = "Pilates Free",
            ConditionType = PromotionConditionType.CouponCode,
            BenefitType = PromotionBenefitType.PercentageDiscount,
            BenefitValue = 100m,
            ValidFrom = today.AddDays(-5),
            ValidTo = today.AddDays(10),
            MaxUsesPerPerson = 1,
            IsActive = true
        };
        setupDb.Promotions.Add(promo);

        var r4Id = Guid.NewGuid();
        var redemption = new PromotionRedemption
        {
            Id = Guid.NewGuid(),
            PromotionId = promo.Id,
            PersonId = _student4Id,
            ReservationId = r4Id,
            RedeemedAtUtc = calendar.UtcNow,
            IsActive = true
        };
        setupDb.PromotionRedemptions.Add(redemption);

        var r4 = Reservation.CreateConfirmed(
            session.Id, _student4Id, ReservationOrigin.StudentSelfService, ReservationAccessType.PromotionalBenefit,
            calendar.UtcNow, promotionRedemptionId: redemption.Id);
        r4.Id = r4Id;
        setupDb.Reservations.Add(r4);
        session.IncrementReservedCount();

        // Alumno 5: con crédito compensatorio (Confirmed) -> al quedar Cancelled con IsLateCancellation = false se libera para usar en otra sesión
        var comp = new MembershipCreditMovement
        {
            Id = Guid.NewGuid(),
            StudentId = _student5Id,
            Type = CreditMovementType.ClassSuspensionCompensation,
            Amount = 1,
            BalanceAfter = 1,
            Reason = "Compensación",
            ActivityId = _activityId,
            ExpiresOn = today.AddDays(15),
            CreatedAtUtc = calendar.UtcNow,
            IsActive = true
        };
        setupDb.MembershipCreditMovements.Add(comp);

        var r5 = Reservation.CreateConfirmed(
            session.Id, _student5Id, ReservationOrigin.StudentSelfService, ReservationAccessType.CompensatoryCredit,
            calendar.UtcNow, singleClassTicketMovementId: comp.Id);
        setupDb.Reservations.Add(r5);
        session.IncrementReservedCount();

        await setupDb.SaveChangesAsync();

        // Ejecutamos la suspensión con despacho de domain events
        var services = new ServiceCollection();
        SmartGymDbContext? dbWithEvents = null;
        services.AddScoped<ISmartGymDbContext>(_ => dbWithEvents!);
        services.AddSingleton<IGymCalendar>(calendar);
        services.AddScoped<ICreditLedgerService, CreditLedgerService>();
        services.AddScoped<IPromotionService, PromotionService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<CancelReservationsOnSessionVoidedHandler>();
        services.AddScoped<IDomainEventHandler<ClassSessionSuspended>>(sp => sp.GetRequiredService<CancelReservationsOnSessionVoidedHandler>());
        services.AddScoped<IDomainEventHandler<ClassSessionCancelled>>(sp => sp.GetRequiredService<CancelReservationsOnSessionVoidedHandler>());
        services.AddScoped<IDomainEventHandler<ClassSessionRescheduled>>(sp => sp.GetRequiredService<CancelReservationsOnSessionVoidedHandler>());
        var sp = services.BuildServiceProvider();

        dbWithEvents = new SmartGymDbContext(_options, sp);

        var actorUserId = Guid.NewGuid();
        var sessionToSuspend = await dbWithEvents.ClassSessions.FirstAsync(cs => cs.Id == session.Id);
        sessionToSuspend.Suspend(ClassSessionSuspensionReason.Weather, "Lluvia intensa", actorUserId, calendar.UtcNow);

        await dbWithEvents.SaveChangesAsync();

        // Verificaciones
        using var verifyDb = new SmartGymDbContext(_options);
        var loadedSession = await verifyDb.ClassSessions.FirstAsync(cs => cs.Id == session.Id);
        Assert.Equal(ClassSessionStatus.Suspended, loadedSession.Status);
        Assert.Equal(0, loadedSession.ReservedCount);

        var reservations = await verifyDb.Reservations
            .Include(r => r.StatusChanges)
            .Where(r => r.ClassSessionId == session.Id)
            .ToListAsync();

        Assert.All(reservations, r =>
        {
            Assert.Equal(ReservationStatus.Cancelled, r.Status);
            Assert.False(r.IsLateCancellation);
            Assert.True(r.AffectedBySessionChange);
            Assert.Equal(ReservationCancellationSource.ClassSessionChange, r.CancellationSource);
            Assert.Null(r.WaitListPosition);
            Assert.Contains(r.StatusChanges, sc => sc.NewStatus == ReservationStatus.Cancelled && sc.Trigger == ReservationChangeTrigger.ClassSessionChange);
        });

        var r1Loaded = reservations.Single(r => r.Id == r1.Id);
        Assert.NotNull(r1Loaded.RefundCreditMovementId);
        var loadedM1 = await verifyDb.Memberships.FirstAsync(m => m.Id == m1.Id);
        Assert.Equal(6, loadedM1.AvailableCredits);

        // Idempotencia: si el handler vuelve a invocarse sobre la misma sesión, no duplica el reembolso
        var handler = sp.GetRequiredService<CancelReservationsOnSessionVoidedHandler>();
        await handler.HandleAsync(new ClassSessionSuspended(session.Id, ClassSessionSuspensionReason.Weather, "Lluvia intensa", actorUserId, calendar.UtcNow));
        await dbWithEvents.SaveChangesAsync();
        Assert.Equal(6, (await verifyDb.Memberships.AsNoTracking().FirstAsync(m => m.Id == m1.Id)).AvailableCredits);

        // Promoción anulada
        var voidedRedemption = await verifyDb.PromotionRedemptions.SingleAsync(pr => pr.Id == redemption.Id);
        Assert.False(voidedRedemption.IsActive);

        // Pase individual identificado para compensación (AffectedBySessionChange = true, AccessType = SingleClassTicket, RefundCreditMovementId = null)
        var r2Loaded = reservations.Single(r => r.Id == r2.Id);
        Assert.Equal(ReservationAccessType.SingleClassTicket, r2Loaded.AccessType);
        Assert.True(r2Loaded.AffectedBySessionChange);
        Assert.Null(r2Loaded.RefundCreditMovementId);

        // Crédito compensatorio liberado: puede volver a usarse en otra clase abierta
        var nextStart = calendar.UtcNow.AddHours(5);
        var nextSession = ClassSession.CreateExtraordinary(_activityId, _roomId, nextStart, nextStart.AddHours(1), 10, _instructorId, null);
        nextSession.Open(calendar.UtcNow);
        verifyDb.ClassSessions.Add(nextSession);
        await verifyDb.SaveChangesAsync();

        var reservationService = new ReservationService(verifyDb, calendar);
        var reusedComp = await reservationService.CreateReservationAsync(
            new CreateReservationRequest(nextSession.Id, _student5Id, SingleClassTicketMovementId: comp.Id),
            actorUserId);
        Assert.Equal(ReservationStatus.Confirmed, reusedComp.Status);

        // Auditoría registrada
        Assert.True(await verifyDb.AuditLogs.AnyAsync(a => a.Action == "RESERVATION_CANCELLED_BY_SESSION_CHANGE"));
    }

    [Fact]
    public async Task RescheduleSession_CancelsOriginalReservationsWithRescheduledToSessionId_WithoutCreatingReservationsInDestinationSession()
    {
        using var setupDb = new SmartGymDbContext(_options);
        var today = new DateOnly(2026, 8, 21);
        var calendar = new StubGymCalendar(today);

        var starts = calendar.UtcNow.AddHours(4);
        var session = ClassSession.CreateExtraordinary(_activityId, _roomId, starts, starts.AddHours(1), 10, _instructorId, null);
        session.Open(calendar.UtcNow);

        var newStarts = calendar.UtcNow.AddDays(1).AddHours(4);
        var newSession = ClassSession.CreateExtraordinary(_activityId, _roomId, newStarts, newStarts.AddHours(1), 10, _instructorId, null);
        newSession.Open(calendar.UtcNow);

        setupDb.ClassSessions.AddRange(session, newSession);

        var m1 = new Membership
        {
            Id = Guid.NewGuid(),
            StudentId = _student1Id,
            MembershipPlanId = _planId,
            StartDate = today.AddDays(-5),
            EndDate = today.AddDays(15),
            TotalCredits = 10,
            AvailableCredits = 3,
            Status = MembershipStatus.Active,
            IsActive = true
        };
        setupDb.Memberships.Add(m1);

        var r1 = Reservation.CreateConfirmed(
            session.Id, _student1Id, ReservationOrigin.StudentSelfService, ReservationAccessType.MembershipCredit,
            calendar.UtcNow, membershipId: m1.Id);
        setupDb.Reservations.Add(r1);
        session.IncrementReservedCount();
        await setupDb.SaveChangesAsync();

        var services = new ServiceCollection();
        SmartGymDbContext? dbWithEvents = null;
        services.AddScoped<ISmartGymDbContext>(_ => dbWithEvents!);
        services.AddSingleton<IGymCalendar>(calendar);
        services.AddScoped<ICreditLedgerService, CreditLedgerService>();
        services.AddScoped<IPromotionService, PromotionService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<CancelReservationsOnSessionVoidedHandler>();
        services.AddScoped<IDomainEventHandler<ClassSessionRescheduled>>(sp => sp.GetRequiredService<CancelReservationsOnSessionVoidedHandler>());
        var sp = services.BuildServiceProvider();

        dbWithEvents = new SmartGymDbContext(_options, sp);
        var handler = sp.GetRequiredService<CancelReservationsOnSessionVoidedHandler>();

        await handler.HandleAsync(new ClassSessionRescheduled(session.Id, newSession.Id, newStarts, newStarts.AddHours(1), Guid.NewGuid()));
        await dbWithEvents.SaveChangesAsync();

        using var verifyDb = new SmartGymDbContext(_options);
        var r1After = await verifyDb.Reservations.SingleAsync(r => r.Id == r1.Id);
        Assert.Equal(ReservationStatus.Cancelled, r1After.Status);
        Assert.True(r1After.AffectedBySessionChange);
        Assert.Equal(newSession.Id, r1After.RescheduledToSessionId);
        Assert.Equal(ReservationCancellationSource.ClassSessionChange, r1After.CancellationSource);
        Assert.Equal(4, (await verifyDb.Memberships.SingleAsync(m => m.Id == m1.Id)).AvailableCredits);

        // No se crea ninguna reserva automática en la sesión destino
        Assert.False(await verifyDb.Reservations.AnyAsync(r => r.ClassSessionId == newSession.Id));
        Assert.Equal(0, (await verifyDb.ClassSessions.SingleAsync(cs => cs.Id == newSession.Id)).ReservedCount);
    }
}
