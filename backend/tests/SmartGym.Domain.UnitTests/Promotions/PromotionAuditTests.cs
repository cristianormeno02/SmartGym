using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Promotions.Dtos;
using SmartGym.Application.Modules.Promotions.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Infrastructure.Persistence;
using Xunit;
using static SmartGym.Domain.UnitTests.Promotions.PromotionTestSupport;

namespace SmartGym.Domain.UnitTests.Promotions;

public class PromotionAuditTests : IDisposable
{
    private readonly SmartGymDbContext _db = CreateDbContext();
    private readonly StubCurrentUserService _operator = new(Guid.NewGuid(), Roles.Secretary);
    private readonly PromotionService _service;

    public PromotionAuditTests()
    {
        _service = new PromotionService(_db, new StubGymCalendar(), _operator);
    }

    public void Dispose() => _db.Dispose();

    private async Task<AuditLog> SingleLogAsync(string action)
    {
        var log = await _db.AuditLogs.SingleAsync(l => l.Action == action);
        Assert.Equal(_operator.UserId, log.UserId);
        Assert.Equal(_operator.Email, log.UserEmail);
        return log;
    }

    [Fact]
    public async Task CreateUpdateAndStatusChanges_AreAudited()
    {
        var created = await _service.CreateAsync(ValidCreateRequest());
        var createdLog = await SingleLogAsync("PROMOTION_CREATED");
        Assert.Equal("Promotion", createdLog.EntityName);
        Assert.Equal(created.Id.ToString(), createdLog.EntityId);
        Assert.Null(createdLog.OldValues);
        Assert.Contains("BIENVENIDA", createdLog.NewValues);

        var updated = await _service.UpdateAsync(created.Id, UpdateRequestFrom(created) with { Name = "Renombrada" });
        var updatedLog = await SingleLogAsync("PROMOTION_UPDATED");
        Assert.Contains("Bienvenida", updatedLog.OldValues);
        Assert.Contains("Renombrada", updatedLog.NewValues);

        await _service.PauseAsync(created.Id);
        var pausedLog = await SingleLogAsync("PROMOTION_PAUSED");
        Assert.Contains("Active", pausedLog.OldValues);
        Assert.Contains("Paused", pausedLog.NewValues);

        await _service.ActivateAsync(created.Id);
        var activatedLog = await SingleLogAsync("PROMOTION_ACTIVATED");
        Assert.Contains("Paused", activatedLog.OldValues);
        Assert.Contains("Active", activatedLog.NewValues);

        await _service.ArchiveAsync(created.Id);
        var archivedLog = await SingleLogAsync("PROMOTION_ARCHIVED");
        Assert.Contains("Archived", archivedLog.NewValues);

        Assert.Equal(5, await _db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task RejectedOperations_AreNotAudited()
    {
        var created = await _service.CreateAsync(ValidCreateRequest());

        await Assert.ThrowsAnyAsync<Exception>(() => _service.ActivateAsync(created.Id));
        await Assert.ThrowsAnyAsync<Exception>(() => _service.CreateAsync(ValidCreateRequest()));

        Assert.Equal(1, await _db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task RedeemAndVoid_AreAuditedWithPromotionPersonAndRelatedEntity()
    {
        var student = Person.Create("Ana", "Sosa", "30111222");
        _db.People.Add(student);
        _db.Promotions.Add(NewPromotion("CLASE", p => p.BenefitValue = 100m));
        await _db.SaveChangesAsync();
        var promotionId = (await _db.Promotions.SingleAsync()).Id;
        var membershipId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();

        var membershipRedemption = await _service.RedeemPromotionAsync(
            new ApplyPromotionRequest("CLASE", student.Id, null, 10000m), membershipId);
        await _service.VoidRedemptionAsync(membershipRedemption.Id);
        // Anular dos veces no duplica la auditoría
        await _service.VoidRedemptionAsync(membershipRedemption.Id);

        await _service.RedeemPromotionForReservationAsync(student.Id, Guid.NewGuid(), Guid.NewGuid(), "CLASE", reservationId);

        var redeemed = await _db.AuditLogs.Where(l => l.Action == "PROMOTION_REDEEMED").OrderBy(l => l.TimestampUtc).ToListAsync();
        Assert.Equal(2, redeemed.Count);
        Assert.All(redeemed, l =>
        {
            Assert.Equal("PromotionRedemption", l.EntityName);
            Assert.Equal(_operator.UserId, l.UserId);
            Assert.Contains(promotionId.ToString(), l.NewValues);
            Assert.Contains(student.Id.ToString(), l.NewValues);
        });
        Assert.Contains(redeemed, l => l.NewValues!.Contains(membershipId.ToString()));
        Assert.Contains(redeemed, l => l.NewValues!.Contains(reservationId.ToString()));

        var voided = await SingleLogAsync("PROMOTION_REDEMPTION_VOIDED");
        Assert.Equal(membershipRedemption.Id.ToString(), voided.EntityId);
        Assert.Contains(promotionId.ToString(), voided.NewValues);
        Assert.Contains(student.Id.ToString(), voided.NewValues);
        Assert.Contains(membershipId.ToString(), voided.NewValues);
    }

    [Fact]
    public async Task RedeemWithoutAuthenticatedUser_IsAuditedWithoutOperator()
    {
        var student = Person.Create("Ana", "Sosa", "30111222");
        _db.People.Add(student);
        _db.Promotions.Add(NewPromotion("CLASE"));
        await _db.SaveChangesAsync();
        var backgroundService = new PromotionService(_db, new StubGymCalendar());

        await backgroundService.RedeemPromotionAsync(new ApplyPromotionRequest("CLASE", student.Id, null, 10000m), null);

        var log = await _db.AuditLogs.SingleAsync(l => l.Action == "PROMOTION_REDEEMED");
        Assert.Null(log.UserId);
    }
}
