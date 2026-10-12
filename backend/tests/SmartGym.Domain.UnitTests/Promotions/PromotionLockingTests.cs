using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Modules.Promotions;
using SmartGym.Application.Modules.Promotions.Dtos;
using SmartGym.Application.Modules.Promotions.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Infrastructure.Persistence;
using Xunit;
using static SmartGym.Domain.UnitTests.Promotions.PromotionTestSupport;

namespace SmartGym.Domain.UnitTests.Promotions;

public class PromotionLockingTests
{
    // InMemory ignora las transacciones y los bloqueos; que el bloqueo ocurra dentro de una transacción lo garantiza
    // SmartGymDbContext.LockPromotionForUpdateAsync en PostgreSQL (lanza si no hay transacción activa).
    private sealed class LockSpyDbContext : SmartGymDbContext
    {
        public List<Guid> LockedPromotionIds { get; } = new();

        /// <summary>Simula lo que otra transacción confirmó mientras esta esperaba el bloqueo.</summary>
        public Func<Guid, Task>? OnLockAcquired { get; set; }

        public LockSpyDbContext(DbContextOptions<SmartGymDbContext> options) : base(options) { }

        public override async Task LockPromotionForUpdateAsync(Guid promotionId, CancellationToken cancellationToken = default)
        {
            LockedPromotionIds.Add(promotionId);
            if (OnLockAcquired != null)
            {
                await OnLockAcquired(promotionId);
            }
        }
    }

    private static async Task<(LockSpyDbContext Db, DbContextOptions<SmartGymDbContext> Options, Person Student, Guid PromotionId)> SeedAsync(
        Action<SmartGym.Domain.Entities.Promotions.Promotion>? configure = null)
    {
        var options = InMemoryOptions();
        var student = Person.Create("Ana", "Sosa", "30111222");
        var promo = NewPromotion("PROMO", p =>
        {
            p.BenefitValue = 100m;
            configure?.Invoke(p);
        });

        await using (var seed = new SmartGymDbContext(options))
        {
            seed.People.Add(student);
            seed.Promotions.Add(promo);
            await seed.SaveChangesAsync();
        }

        return (new LockSpyDbContext(options), options, student, promo.Id);
    }

    [Fact]
    public async Task RedeemPromotion_LocksThePromotionRow()
    {
        var (db, _, student, promotionId) = await SeedAsync();
        await using var _db = db;
        var service = new PromotionService(db, new StubGymCalendar());

        await service.RedeemPromotionAsync(new ApplyPromotionRequest("PROMO", student.Id, null, 10000m), membershipId: null);

        Assert.Equal(new[] { promotionId }, db.LockedPromotionIds);
    }

    [Fact]
    public async Task RedeemPromotionForReservation_LocksThePromotionRow()
    {
        var (db, _, student, promotionId) = await SeedAsync();
        await using var _db = db;
        var service = new PromotionService(db, new StubGymCalendar());

        await service.RedeemPromotionForReservationAsync(student.Id, Guid.NewGuid(), Guid.NewGuid(), "PROMO", Guid.NewGuid());

        Assert.Equal(new[] { promotionId }, db.LockedPromotionIds);
    }

    [Fact]
    public async Task EvaluatePromotion_DoesNotLock()
    {
        var (db, _, student, _) = await SeedAsync();
        await using var _db = db;
        var service = new PromotionService(db, new StubGymCalendar());

        await service.EvaluatePromotionAsync(new ApplyPromotionRequest("PROMO", student.Id, null, 10000m));
        await service.EvaluatePromotionForReservationAsync(student.Id, Guid.NewGuid(), Guid.NewGuid(), "PROMO");

        Assert.Empty(db.LockedPromotionIds);
    }

    [Fact]
    public async Task RedeemPromotion_LastUseTakenWhileWaitingForTheLock_ThrowsLimitReached()
    {
        var (db, options, student, promotionId) = await SeedAsync(p =>
        {
            p.MaxUsesTotal = 5;
            p.CurrentUsesTotal = 4;
        });
        await using var _db = db;
        db.OnLockAcquired = async id =>
        {
            await using var other = new SmartGymDbContext(options);
            var promo = await other.Promotions.SingleAsync(p => p.Id == id);
            promo.CurrentUsesTotal = 5;
            await other.SaveChangesAsync();
        };
        var service = new PromotionService(db, new StubGymCalendar());

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.RedeemPromotionAsync(new ApplyPromotionRequest("PROMO", student.Id, null, 10000m), membershipId: null));

        Assert.Equal(PromotionErrorCodes.LimitReached, ex.Code);
        await using var verify = new SmartGymDbContext(options);
        Assert.Equal(5, (await verify.Promotions.SingleAsync(p => p.Id == promotionId)).CurrentUsesTotal);
        Assert.Equal(0, await verify.PromotionRedemptions.CountAsync());
    }

    [Fact]
    public async Task RedeemPromotion_PausedWhileWaitingForTheLock_ThrowsNotApplicable()
    {
        var (db, options, student, _) = await SeedAsync();
        await using var _db = db;
        db.OnLockAcquired = async id =>
        {
            await using var other = new SmartGymDbContext(options);
            var promo = await other.Promotions.SingleAsync(p => p.Id == id);
            promo.Status = SmartGym.Domain.Enums.PromotionStatus.Paused;
            await other.SaveChangesAsync();
        };
        var service = new PromotionService(db, new StubGymCalendar());

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.RedeemPromotionForReservationAsync(student.Id, Guid.NewGuid(), Guid.NewGuid(), "PROMO", Guid.NewGuid()));

        Assert.Equal(PromotionErrorCodes.NotApplicable, ex.Code);
    }

    [Fact]
    public async Task VoidRedemption_LocksThePromotion_AndRepeatedVoidDoesNotDecrementTwice()
    {
        var (db, options, student, promotionId) = await SeedAsync(p => p.MaxUsesPerPerson = 5);
        await using var _db = db;
        var service = new PromotionService(db, new StubGymCalendar());

        var first = await service.RedeemPromotionAsync(new ApplyPromotionRequest("PROMO", student.Id, null, 10000m), membershipId: null);
        await service.RedeemPromotionAsync(new ApplyPromotionRequest("PROMO", student.Id, null, 10000m), membershipId: null);
        db.LockedPromotionIds.Clear();

        await service.VoidRedemptionAsync(first.Id);
        await service.VoidRedemptionAsync(first.Id);

        Assert.Equal(new[] { promotionId, promotionId }, db.LockedPromotionIds);
        await using var verify = new SmartGymDbContext(options);
        Assert.Equal(1, (await verify.Promotions.SingleAsync(p => p.Id == promotionId)).CurrentUsesTotal);
        Assert.Equal(1, await verify.PromotionRedemptions.CountAsync(r => r.IsActive));
    }

    [Fact]
    public async Task VoidRedemption_AlreadyVoidedByAnotherTransactionWhileWaitingForTheLock_DoesNotDecrement()
    {
        var (db, options, student, promotionId) = await SeedAsync();
        await using var _db = db;
        var service = new PromotionService(db, new StubGymCalendar());
        var redemption = await service.RedeemPromotionAsync(new ApplyPromotionRequest("PROMO", student.Id, null, 10000m), membershipId: null);

        db.OnLockAcquired = async id =>
        {
            await using var other = new SmartGymDbContext(options);
            var row = await other.PromotionRedemptions.SingleAsync(r => r.Id == redemption.Id);
            var promo = await other.Promotions.SingleAsync(p => p.Id == id);
            row.IsActive = false;
            promo.CurrentUsesTotal = 0;
            await other.SaveChangesAsync();
        };

        await service.VoidRedemptionAsync(redemption.Id);

        await using var verify = new SmartGymDbContext(options);
        Assert.Equal(0, (await verify.Promotions.SingleAsync(p => p.Id == promotionId)).CurrentUsesTotal);
    }
}
