using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Memberships;

public class CreditLedgerInitialLoadTests
{
    private sealed class StubGymCalendar : IGymCalendar
    {
        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;
        public DateTime UtcNow { get; set; } = new(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc);
        public DateOnly Today { get; set; } = new(2026, 4, 1);
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
    public async Task RecordInitialCreditsAsync_WithInitialAndPromotionBonus_ShouldRecordBothMovementsAndBeIdempotent()
    {
        await using var dbContext = CreateDbContext();
        var calendar = new StubGymCalendar();
        var service = new CreditLedgerService(dbContext, calendar);

        var student = Person.Create("Juan", "Pérez", "30111222");
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
        membership.Activate(calendar.Today, DateTime.UtcNow, Guid.NewGuid());
        dbContext.Memberships.Add(membership);
        await dbContext.SaveChangesAsync();

        var actorUserId = Guid.NewGuid();

        // First call
        await service.RecordInitialCreditsAsync(membership, actorUserId);
        await dbContext.SaveChangesAsync();

        Assert.True(membership.InitialCreditsLoaded);
        Assert.Equal(10, membership.TotalCredits);
        Assert.Equal(10, membership.AvailableCredits);

        var movements = await dbContext.MembershipCreditMovements
            .Where(m => m.MembershipId == membership.Id)
            .OrderBy(m => m.BalanceAfter)
            .ToListAsync();

        Assert.Equal(2, movements.Count);
        Assert.Equal(CreditMovementType.InitialPurchase, movements[0].Type);
        Assert.Equal(8, movements[0].Amount);
        Assert.Equal(8, movements[0].BalanceAfter);
        Assert.Equal(actorUserId, movements[0].PerformedByUserId);

        Assert.Equal(CreditMovementType.PromotionBonus, movements[1].Type);
        Assert.Equal(2, movements[1].Amount);
        Assert.Equal(10, movements[1].BalanceAfter);
        Assert.Equal(actorUserId, movements[1].PerformedByUserId);

        // Second call (idempotent)
        await service.RecordInitialCreditsAsync(membership, actorUserId);
        await dbContext.SaveChangesAsync();

        Assert.Equal(10, membership.TotalCredits);
        Assert.Equal(10, membership.AvailableCredits);
        Assert.Equal(2, await dbContext.MembershipCreditMovements.CountAsync(m => m.MembershipId == membership.Id));
    }

    [Fact]
    public async Task RecordInitialCreditsAsync_UnlimitedMembership_ShouldRecordSingleInformativeZeroMovement()
    {
        await using var dbContext = CreateDbContext();
        var calendar = new StubGymCalendar();
        var service = new CreditLedgerService(dbContext, calendar);

        var student = Person.Create("Ana", "Gómez", "32111222");
        dbContext.People.Add(student);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Pase Libre",
            DurationDays = 30,
            Price = 35000m,
            Credits = 0,
            IsUnlimited = true
        };
        dbContext.MembershipPlans.Add(plan);

        var membership = Membership.Create(
            studentId: student.Id,
            plan: plan,
            startDate: new DateOnly(2026, 4, 1),
            purchasedAtUtc: DateTime.UtcNow,
            originalPrice: 35000m,
            discountAmount: 0m,
            purchasePrice: 35000m,
            currency: "ARS");
        membership.Activate(calendar.Today, DateTime.UtcNow, Guid.NewGuid());
        dbContext.Memberships.Add(membership);
        await dbContext.SaveChangesAsync();

        await service.RecordInitialCreditsAsync(membership, Guid.NewGuid());
        await dbContext.SaveChangesAsync();

        Assert.True(membership.InitialCreditsLoaded);
        Assert.Equal(0, membership.TotalCredits);
        Assert.Equal(0, membership.AvailableCredits);

        var movement = Assert.Single(await dbContext.MembershipCreditMovements
            .Where(m => m.MembershipId == membership.Id)
            .ToListAsync());
        Assert.Equal(CreditMovementType.InitialPurchase, movement.Type);
        Assert.Equal(0, movement.Amount);
        Assert.Equal(0, movement.BalanceAfter);
    }
}
