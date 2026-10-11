using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common;
using SmartGym.Application.Modules.Memberships.Dtos;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Memberships;

public class CreditLedgerClosingAndSummaryTests
{
    private sealed class StubGymCalendar : IGymCalendar
    {
        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;
        public DateTime UtcNow { get; set; } = new(2026, 4, 15, 10, 0, 0, DateTimeKind.Utc);
        public DateOnly Today { get; set; } = new(2026, 4, 15);
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
    public async Task ForfeitRemainingCreditsOnCancelAsync_ShouldSetBalanceToZero_AndRecordCancellationForfeiture()
    {
        await using var dbContext = CreateDbContext();
        var calendar = new StubGymCalendar();
        var service = new CreditLedgerService(dbContext, calendar);

        var student = Person.Create("Martín", "Gómez", "30555666");
        dbContext.People.Add(student);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Pack 8 Clases",
            DurationDays = 30,
            Price = 20000m,
            Credits = 8,
            IsUnlimited = false
        };
        dbContext.MembershipPlans.Add(plan);

        var membership = Membership.Create(
            studentId: student.Id,
            plan: plan,
            startDate: new DateOnly(2026, 4, 1),
            purchasedAtUtc: DateTime.UtcNow,
            originalPrice: 20000m,
            discountAmount: 0m,
            purchasePrice: 20000m,
            currency: "ARS");
        membership.Activate(new DateOnly(2026, 4, 1), DateTime.UtcNow, Guid.NewGuid());
        await service.RecordInitialCreditsAsync(membership);
        dbContext.Memberships.Add(membership);
        await dbContext.SaveChangesAsync();

        // Consume 3 credits -> 5 left
        for (int i = 0; i < 3; i++)
        {
            await service.ConsumeCreditForReservationAsync(membership, Guid.NewGuid(), new DateOnly(2026, 4, 10), $"Clase {i + 1}");
        }
        await dbContext.SaveChangesAsync();
        Assert.Equal(5, membership.AvailableCredits);

        var actorId = Guid.NewGuid();
        var movement = await service.ForfeitRemainingCreditsOnCancelAsync(membership, "Cancelación administrativa", actorId);
        await dbContext.SaveChangesAsync();

        Assert.NotNull(movement);
        Assert.Equal(CreditMovementType.CancellationForfeiture, movement!.Type);
        Assert.Equal(-5, movement.Amount);
        Assert.Equal(0, movement.BalanceAfter);
        Assert.Equal(0, membership.AvailableCredits);
    }

    [Fact]
    public async Task ExpireRemainingCreditsAsync_ShouldSetBalanceToZero_AndRecordExpirationMovement()
    {
        await using var dbContext = CreateDbContext();
        var calendar = new StubGymCalendar();
        var service = new CreditLedgerService(dbContext, calendar);

        var student = Person.Create("Laura", "Vega", "34555666");
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

        var movement = await service.ExpireRemainingCreditsAsync(membership);
        await dbContext.SaveChangesAsync();

        Assert.NotNull(movement);
        Assert.Equal(CreditMovementType.Expiration, movement!.Type);
        Assert.Equal(-4, movement.Amount);
        Assert.Equal(0, movement.BalanceAfter);
        Assert.Equal(0, membership.AvailableCredits);
    }

    [Fact]
    public async Task GetMembershipCreditsSummaryAsync_ShouldReturnExactBreakdown()
    {
        await using var dbContext = CreateDbContext();
        var calendar = new StubGymCalendar();
        var service = new CreditLedgerService(dbContext, calendar);

        var student = Person.Create("Elena", "Paz", "35777888");
        dbContext.People.Add(student);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Pack 8 Clases",
            DurationDays = 30,
            Price = 20000m,
            Credits = 8,
            IsUnlimited = false
        };
        dbContext.MembershipPlans.Add(plan);

        var membership = Membership.Create(
            studentId: student.Id,
            plan: plan,
            startDate: new DateOnly(2026, 4, 1),
            purchasedAtUtc: DateTime.UtcNow,
            originalPrice: 20000m,
            discountAmount: 0m,
            purchasePrice: 20000m,
            currency: "ARS",
            extraCreditsGranted: 2);
        membership.Activate(new DateOnly(2026, 4, 1), DateTime.UtcNow, Guid.NewGuid());
        await service.RecordInitialCreditsAsync(membership);
        dbContext.Memberships.Add(membership);
        await dbContext.SaveChangesAsync();

        var session1 = Guid.NewGuid();
        var session2 = Guid.NewGuid();
        await service.ConsumeCreditForReservationAsync(membership, session1, new DateOnly(2026, 4, 10), "Reserva 1");
        await service.ConsumeCreditForReservationAsync(membership, session2, new DateOnly(2026, 4, 11), "Reserva 2");
        await service.RefundCreditForReservationAsync(membership, session2, "Cancelación 2");
        await dbContext.SaveChangesAsync();

        await service.AddCompensatoryCreditAsync(new AddCompensatoryCreditRequest(student.Id, null, "Compensación"), Guid.NewGuid());
        await service.ForfeitRemainingCreditsOnCancelAsync(membership, "Cancelación final");
        await dbContext.SaveChangesAsync();

        var summary = await service.GetMembershipCreditsSummaryAsync(membership.Id);

        Assert.Equal(membership.Id, summary.MembershipId);
        Assert.Equal(student.Id, summary.StudentId);
        Assert.False(summary.IsUnlimited);
        Assert.Equal(8, summary.InitialCredits);
        Assert.Equal(2, summary.PromotionBonusCredits);
        Assert.Equal(1, summary.CompensatoryCredits);
        Assert.Equal(2, summary.ConsumedCredits);
        Assert.Equal(1, summary.RefundedCredits);
        Assert.Equal(10, summary.ExpiredOrForfeitedCredits); // 8 + 2 - 2 + 1 + 1 = 10 forfeited
        Assert.Equal(0, summary.AvailableCredits);
        Assert.True(summary.IsDepleted);
        Assert.Equal(7, summary.Movements.Count);
    }

    [Fact]
    public void Membership_ShouldNotExposeDirectCreditMutationMethods()
    {
        // Criterio 8: quitar de Membership los métodos DeductCredit, RefundCredit y AddCompensatoryCredit
        var methods = typeof(Membership).GetMethods().Select(m => m.Name).ToList();
        Assert.DoesNotContain("DeductCredit", methods);
        Assert.DoesNotContain("RefundCredit", methods);
        Assert.DoesNotContain("AddCompensatoryCredit", methods);
    }
}
