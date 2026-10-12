using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Modules.Memberships.Dtos;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Memberships;

public class CreditLedgerConsumptionTests
{
    private sealed class StubGymCalendar : IGymCalendar
    {
        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;
        public DateTime UtcNow { get; set; } = new(2026, 4, 10, 10, 0, 0, DateTimeKind.Utc);
        public DateOnly Today { get; set; } = new(2026, 4, 10);
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
            .Options;
        return new SmartGymDbContext(options);
    }

    [Fact]
    public async Task ConsumeAndRefund_ShouldKeepMembershipActiveEvenAtZeroBalance_AndPreventDoubleConsumption()
    {
        await using var dbContext = CreateDbContext();
        var calendar = new StubGymCalendar();
        var service = new CreditLedgerService(dbContext, calendar);

        var student = Person.Create("Carlos", "Ruiz", "28999888");
        dbContext.People.Add(student);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Pack 1 Clase",
            DurationDays = 30,
            Price = 5000m,
            Credits = 1,
            IsUnlimited = false
        };
        dbContext.MembershipPlans.Add(plan);

        var membership = Membership.Create(
            studentId: student.Id,
            plan: plan,
            startDate: new DateOnly(2026, 4, 1),
            purchasedAtUtc: DateTime.UtcNow,
            originalPrice: 5000m,
            discountAmount: 0m,
            purchasePrice: 5000m,
            currency: "ARS");
        membership.Activate(new DateOnly(2026, 4, 1), DateTime.UtcNow, Guid.NewGuid());
        await service.RecordInitialCreditsAsync(membership);
        dbContext.Memberships.Add(membership);
        await dbContext.SaveChangesAsync();

        var sessionId = Guid.NewGuid();
        var sessionDate = new DateOnly(2026, 4, 15);

        // Consume the single credit
        var debit = await service.ConsumeCreditForReservationAsync(
            membership,
            sessionId,
            sessionDate,
            "Reserva de Spinning");
        await dbContext.SaveChangesAsync();

        Assert.Equal(0, membership.AvailableCredits);
        Assert.Equal(MembershipStatus.Active, membership.Status);
        Assert.True(membership.IsDepleted);
        Assert.Equal(CreditMovementType.ClassReservationDebit, debit.Type);
        Assert.Equal(-1, debit.Amount);
        Assert.Equal(0, debit.BalanceAfter);

        // Refund the credit
        var refund = await service.RefundCreditForReservationAsync(
            membership,
            sessionId,
            "Cancelación con antelación");
        await dbContext.SaveChangesAsync();

        Assert.Equal(1, membership.AvailableCredits);
        Assert.Equal(MembershipStatus.Active, membership.Status);
        Assert.False(membership.IsDepleted);
        Assert.Equal(CreditMovementType.ClassCancellationRefund, refund.Type);
        Assert.Equal(1, refund.Amount);
        Assert.Equal(1, refund.BalanceAfter);
    }

    [Fact]
    public async Task ConsumeCreditForReservationAsync_WhenAlreadyConsumedForSameSession_ShouldThrowConflictException()
    {
        await using var dbContext = CreateDbContext();
        var calendar = new StubGymCalendar();
        var service = new CreditLedgerService(dbContext, calendar);

        var student = Person.Create("Lucía", "Méndez", "31222333");
        dbContext.People.Add(student);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Pack 4 Clases",
            DurationDays = 30,
            Price = 15000m,
            Credits = 4,
            IsUnlimited = false
        };
        dbContext.MembershipPlans.Add(plan);

        var membership = Membership.Create(
            studentId: student.Id,
            plan: plan,
            startDate: new DateOnly(2026, 4, 1),
            purchasedAtUtc: DateTime.UtcNow,
            originalPrice: 15000m,
            discountAmount: 0m,
            purchasePrice: 15000m,
            currency: "ARS");
        membership.Activate(new DateOnly(2026, 4, 1), DateTime.UtcNow, Guid.NewGuid());
        await service.RecordInitialCreditsAsync(membership);
        dbContext.Memberships.Add(membership);
        await dbContext.SaveChangesAsync();

        var sessionId = Guid.NewGuid();
        var sessionDate = new DateOnly(2026, 4, 12);

        await service.ConsumeCreditForReservationAsync(membership, sessionId, sessionDate, "Primera reserva");
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.ConsumeCreditForReservationAsync(membership, sessionId, sessionDate, "Intento duplicado"));
    }

    [Fact]
    public async Task ConsumeAndRefund_UnlimitedPlan_ShouldRecordZeroAmountMovements()
    {
        await using var dbContext = CreateDbContext();
        var calendar = new StubGymCalendar();
        var service = new CreditLedgerService(dbContext, calendar);

        var student = Person.Create("Pedro", "Sánchez", "29444555");
        dbContext.People.Add(student);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Pase Libre",
            DurationDays = 30,
            Price = 30000m,
            Credits = 0,
            IsUnlimited = true
        };
        dbContext.MembershipPlans.Add(plan);

        var membership = Membership.Create(
            studentId: student.Id,
            plan: plan,
            startDate: new DateOnly(2026, 4, 1),
            purchasedAtUtc: DateTime.UtcNow,
            originalPrice: 30000m,
            discountAmount: 0m,
            purchasePrice: 30000m,
            currency: "ARS");
        membership.Activate(new DateOnly(2026, 4, 1), DateTime.UtcNow, Guid.NewGuid());
        await service.RecordInitialCreditsAsync(membership);
        dbContext.Memberships.Add(membership);
        await dbContext.SaveChangesAsync();

        var sessionId = Guid.NewGuid();
        var debit = await service.ConsumeCreditForReservationAsync(membership, sessionId, new DateOnly(2026, 4, 10), "Reserva ilimitada");
        await dbContext.SaveChangesAsync();

        Assert.Equal(0, debit.Amount);
        Assert.Equal(0, debit.BalanceAfter);
        Assert.Equal(0, membership.AvailableCredits);

        var refund = await service.RefundCreditForReservationAsync(membership, sessionId, "Cancelación ilimitada");
        await dbContext.SaveChangesAsync();

        Assert.Equal(0, refund.Amount);
        Assert.Equal(0, refund.BalanceAfter);
    }

    [Fact]
    public async Task AddCompensatoryCreditAsync_ShouldUseGymCalendarToday_AndAddCreditToDepletedActiveMembership()
    {
        await using var dbContext = CreateDbContext();
        var calendar = new StubGymCalendar { Today = new DateOnly(2026, 4, 20) };
        var service = new CreditLedgerService(dbContext, calendar);

        var student = Person.Create("Sofía", "Torres", "33666777");
        dbContext.People.Add(student);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Pack 4 Clases",
            DurationDays = 30,
            Price = 15000m,
            Credits = 4,
            IsUnlimited = false
        };
        dbContext.MembershipPlans.Add(plan);

        // Membership with 0 available credits (depleted) but still Active and valid on calendar.Today (2026-04-20 <= 2026-04-30)
        var membership = Membership.Create(
            studentId: student.Id,
            plan: plan,
            startDate: new DateOnly(2026, 4, 1),
            purchasedAtUtc: DateTime.UtcNow,
            originalPrice: 15000m,
            discountAmount: 0m,
            purchasePrice: 15000m,
            currency: "ARS");
        membership.Activate(new DateOnly(2026, 4, 1), DateTime.UtcNow, Guid.NewGuid());
        membership.InitialCreditsLoaded = true;
        membership.TotalCredits = 4;
        membership.AvailableCredits = 0;
        dbContext.Memberships.Add(membership);
        await dbContext.SaveChangesAsync();

        var staffUserId = Guid.NewGuid();
        var dto = await service.AddCompensatoryCreditAsync(
            new AddCompensatoryCreditRequest(student.Id, null, "Compensación por feriado"),
            staffUserId);

        Assert.Equal(membership.Id, dto.MembershipId);
        Assert.Equal(CreditMovementType.ClassSuspensionCompensation, dto.Type);
        Assert.Equal(1, dto.Amount);
        Assert.Equal(1, dto.BalanceAfter);
        Assert.Equal(1, membership.AvailableCredits);
        Assert.Equal(5, membership.TotalCredits);
    }

    [Fact]
    public async Task ConsumeAndRefund_WithReservationId_LinksMovementsAndRefundIsIdempotent()
    {
        await using var dbContext = CreateDbContext();
        var calendar = new StubGymCalendar();
        var service = new CreditLedgerService(dbContext, calendar);

        var student = Person.Create("Ana", "Gómez", "30111222");
        dbContext.People.Add(student);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Pack 4 Clases",
            DurationDays = 30,
            Price = 15000m,
            Credits = 4,
            IsUnlimited = false
        };
        dbContext.MembershipPlans.Add(plan);

        var membership = Membership.Create(
            studentId: student.Id,
            plan: plan,
            startDate: new DateOnly(2026, 4, 1),
            purchasedAtUtc: calendar.UtcNow,
            originalPrice: 15000m,
            discountAmount: 0m,
            purchasePrice: 15000m,
            currency: "ARS");
        membership.Activate(new DateOnly(2026, 4, 1), calendar.UtcNow, Guid.NewGuid());
        await service.RecordInitialCreditsAsync(membership);
        dbContext.Memberships.Add(membership);
        await dbContext.SaveChangesAsync();

        var sessionId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();

        var debit = await service.ConsumeCreditForReservationAsync(
            membership,
            sessionId,
            new DateOnly(2026, 4, 15),
            "Reserva confirmada",
            performedByUserId: operatorId,
            reservationId: reservationId);
        await dbContext.SaveChangesAsync();

        Assert.Equal(reservationId, debit.ReservationId);
        Assert.Equal(3, membership.AvailableCredits);

        var firstRefund = await service.RefundCreditForReservationAsync(
            membership,
            sessionId,
            "Cancelación anticipada",
            performedByUserId: operatorId,
            reservationId: reservationId);
        await dbContext.SaveChangesAsync();

        Assert.Equal(reservationId, firstRefund.ReservationId);
        Assert.Equal(4, membership.AvailableCredits);

        // Second refund for the same ReservationId must return the existing movement and not increment balance again
        var secondRefund = await service.RefundCreditForReservationAsync(
            membership,
            sessionId,
            "Reintento de cancelación",
            performedByUserId: operatorId,
            reservationId: reservationId);
        await dbContext.SaveChangesAsync();

        Assert.Equal(firstRefund.Id, secondRefund.Id);
        Assert.Equal(4, membership.AvailableCredits);

        var refundCount = await dbContext.MembershipCreditMovements
            .CountAsync(cm => cm.ReservationId == reservationId && cm.Type == CreditMovementType.ClassCancellationRefund);
        Assert.Equal(1, refundCount);
    }

    [Fact]
    public async Task AddCompensatoryCreditAsync_WithoutActiveMembership_CreatesAutonomousCreditWithExpiresOnAndActivityScope()
    {
        await using var dbContext = CreateDbContext();
        var calendar = new StubGymCalendar { Today = new DateOnly(2026, 4, 10) };
        var options = Microsoft.Extensions.Options.Options.Create(new MembershipsOptions
        {
            CompensatoryCreditValidityDays = 15
        });
        var service = new CreditLedgerService(dbContext, calendar, options);

        var student = Person.Create("Martín", "Pérez", "35888999");
        dbContext.People.Add(student);
        await dbContext.SaveChangesAsync();

        var activityId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var staffUserId = Guid.NewGuid();

        var dto = await service.AddCompensatoryCreditAsync(
            new AddCompensatoryCreditRequest(student.Id, sessionId, "Compensación por clase suspendida", activityId),
            staffUserId);

        Assert.Null(dto.MembershipId);
        Assert.Equal(CreditMovementType.ClassSuspensionCompensation, dto.Type);
        Assert.Equal(1, dto.Amount);
        Assert.Equal(new DateOnly(2026, 4, 25), dto.ExpiresOn);
        Assert.Equal(activityId, dto.ActivityId);

        var persisted = await dbContext.MembershipCreditMovements.SingleAsync(cm => cm.Id == dto.Id);
        Assert.Equal(new DateOnly(2026, 4, 25), persisted.ExpiresOn);
        Assert.Equal(activityId, persisted.ActivityId);
    }
}

