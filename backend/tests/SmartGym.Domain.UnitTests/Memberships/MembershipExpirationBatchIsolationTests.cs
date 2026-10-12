using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Modules.Promotions.Dtos;
using SmartGym.Application.Modules.Promotions.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;
using SmartGym.Domain.UnitTests.Common;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Memberships;

/// <summary>
/// Un fallo al procesar una membresía del lote no debe detenerlo ni dejar cambios a medias
/// que terminen persistidos junto con los de otra membresía.
/// </summary>
public class MembershipExpirationBatchIsolationTests
{
    private sealed class FailingVoidPromotionService : IPromotionService
    {
        private readonly Guid _failingRedemptionId;

        public FailingVoidPromotionService(Guid failingRedemptionId)
        {
            _failingRedemptionId = failingRedemptionId;
        }

        public Task VoidRedemptionAsync(Guid redemptionId, CancellationToken cancellationToken = default) =>
            redemptionId == _failingRedemptionId
                ? throw new InvalidOperationException("Fallo simulado al anular el canje.")
                : Task.CompletedTask;

        public Task<List<PromotionDto>> GetAllAsync(bool onlyActive = false, SmartGym.Domain.Enums.PromotionStatus? status = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PromotionDto> UpdateAsync(Guid id, UpdatePromotionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PromotionDto> PauseAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PromotionDto> ActivateAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PromotionDto> ArchiveAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PromotionApplicationResultDto> PreviewForMembershipAsync(EvaluatePromotionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SmartGym.Application.Common.Models.PagedResult<PromotionRedemptionDto>> GetRedemptionsByPromotionAsync(Guid promotionId, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PromotionDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PromotionDto?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PromotionDto> CreateAsync(CreatePromotionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PromotionApplicationResultDto> EvaluatePromotionAsync(ApplyPromotionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PromotionRedemptionDto> RedeemPromotionAsync(ApplyPromotionRequest request, Guid? membershipId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PromotionApplicationResultDto> EvaluatePromotionForReservationAsync(Guid studentId, Guid classSessionId, Guid activityId, string code, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PromotionRedemptionDto> RedeemPromotionForReservationAsync(Guid studentId, Guid classSessionId, Guid activityId, string code, Guid reservationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AttachRedemptionToMembershipAsync(Guid redemptionId, Guid membershipId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<PromotionRedemptionDto>> GetRedemptionsByPersonAsync(Guid personId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task ProcessExpirationsAndWarningsAsync_WhenOneMembershipFails_ProcessesTheRestAndPersistsNothingOfTheFailedOne()
    {
        var options = MembershipTestSupport.InMemoryOptions();
        var start = new DateOnly(2026, 10, 1);
        var calendar = new TestGymCalendar(start);
        var failingRedemptionId = Guid.NewGuid();
        Guid failingMembershipId;
        Guid healthyMembershipId;

        await using (var seed = new SmartGymDbContext(options))
        {
            var role = new Role { Id = (int)RoleType.Student, Name = RoleType.Student.ToString(), Description = "Alumno" };
            var studentA = Person.Create("Ana", "Falla", "41000001");
            studentA.AddRole(role);
            var studentB = Person.Create("Beto", "Sano", "41000002");
            studentB.AddRole(role);
            var plan = new MembershipPlan
            {
                Id = Guid.NewGuid(),
                Name = "Plan Mensual 8",
                Price = 20000m,
                DurationDays = 30,
                Credits = 8,
                AppliesToAllActivities = true,
                IsActive = true
            };

            // Dos membresías Pending impagas fuera del plazo de gracia; anular el canje de la primera falla.
            var failing = Membership.Create(studentA.Id, plan, start, calendar.UtcNow, 20000m, 0m, 20000m,
                promotionRedemptionId: failingRedemptionId);
            var healthy = Membership.Create(studentB.Id, plan, start, calendar.UtcNow, 20000m, 0m, 20000m,
                promotionRedemptionId: Guid.NewGuid());
            failingMembershipId = failing.Id;
            healthyMembershipId = healthy.Id;

            seed.Roles.Add(role);
            seed.People.AddRange(studentA, studentB);
            seed.MembershipPlans.Add(plan);
            seed.Memberships.AddRange(failing, healthy);
            await seed.SaveChangesAsync();
        }

        calendar.Today = new DateOnly(2026, 10, 20);
        await using (var db = new SmartGymDbContext(options))
        {
            var service = MembershipTestSupport.CreateMembershipService(
                db,
                calendar,
                new FailingVoidPromotionService(failingRedemptionId));

            var result = await service.ProcessExpirationsAndWarningsAsync();

            Assert.Equal(1, result.AutoCancelledCount);
        }

        await using var verify = new SmartGymDbContext(options);
        Assert.Equal(MembershipStatus.Cancelled, (await verify.Memberships.SingleAsync(m => m.Id == healthyMembershipId)).Status);

        var failed = await verify.Memberships.Include(m => m.StatusChanges).SingleAsync(m => m.Id == failingMembershipId);
        Assert.Equal(MembershipStatus.Pending, failed.Status);
        Assert.Null(failed.CancelledAtUtc);
        Assert.Single(failed.StatusChanges);
    }
}
