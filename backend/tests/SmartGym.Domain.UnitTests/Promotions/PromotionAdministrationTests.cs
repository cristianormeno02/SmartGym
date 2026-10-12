using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Modules.Promotions;
using SmartGym.Application.Modules.Promotions.Dtos;
using SmartGym.Application.Modules.Promotions.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Entities.Promotions;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using Xunit;
using static SmartGym.Domain.UnitTests.Promotions.PromotionTestSupport;

namespace SmartGym.Domain.UnitTests.Promotions;

public class PromotionAdministrationTests : IDisposable
{
    private readonly SmartGymDbContext _db = CreateDbContext();
    private readonly StubGymCalendar _calendar = new();
    private readonly PromotionService _service;

    public PromotionAdministrationTests()
    {
        _service = new PromotionService(_db, _calendar);
    }

    public void Dispose() => _db.Dispose();

    private async Task<Person> AddStudentAsync(string document)
    {
        var student = Person.Create("Alumno", document, document);
        _db.People.Add(student);
        await _db.SaveChangesAsync();
        return student;
    }

    private async Task<PromotionDto> CreateWithRedemptionAsync()
    {
        var created = await _service.CreateAsync(ValidCreateRequest());
        var student = await AddStudentAsync("30999888");
        await _service.RedeemPromotionAsync(new ApplyPromotionRequest(created.Code, student.Id, null, 10000m), membershipId: null);
        return (await _service.GetByIdAsync(created.Id))!;
    }

    // ---- Alta ----

    [Fact]
    public async Task Create_StartsActive_NormalizesCodeAndTrimsText()
    {
        var created = await _service.CreateAsync(ValidCreateRequest(" bienvenida ") with { Name = "  Bienvenida  " });

        Assert.Equal(PromotionStatus.Active, created.Status);
        Assert.Equal("BIENVENIDA", created.Code);
        Assert.Equal("Bienvenida", created.Name);
        Assert.Equal(0, created.CurrentUsesTotal);
    }

    [Fact]
    public async Task Create_WithInvalidValues_ThrowsValidationAndCreatesNothing()
    {
        var percentage = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CreateAsync(ValidCreateRequest() with { BenefitValue = 150m }));
        Assert.Contains(nameof(CreatePromotionRequest.BenefitValue), percentage.Errors.Keys);

        // No se corrige en silencio a 1
        var perPerson = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CreateAsync(ValidCreateRequest() with { MaxUsesPerPerson = 0 }));
        Assert.Contains(nameof(CreatePromotionRequest.MaxUsesPerPerson), perPerson.Errors.Keys);

        var referral = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CreateAsync(ValidCreateRequest() with { ConditionType = PromotionConditionType.Referral }));
        Assert.Contains(nameof(CreatePromotionRequest.ConditionType), referral.Errors.Keys);

        Assert.Equal(0, await _db.Promotions.CountAsync());
    }

    [Fact]
    public async Task Create_WithUnknownOrInactivePlan_ThrowsValidation()
    {
        var inactivePlan = new MembershipPlan { Id = Guid.NewGuid(), Name = "Plan viejo", Price = 1000m, DurationDays = 30, Credits = 4, IsActive = false };
        _db.MembershipPlans.Add(inactivePlan);
        await _db.SaveChangesAsync();

        var unknown = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CreateAsync(ValidCreateRequest() with { ApplicablePlanId = Guid.NewGuid() }));
        Assert.Contains(nameof(CreatePromotionRequest.ApplicablePlanId), unknown.Errors.Keys);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CreateAsync(ValidCreateRequest() with { ApplicablePlanId = inactivePlan.Id }));
    }

    // ---- Edición ----

    [Fact]
    public async Task Update_WithoutRedemptions_ChangesEveryField()
    {
        var created = await _service.CreateAsync(ValidCreateRequest());

        var updated = await _service.UpdateAsync(created.Id, UpdateRequestFrom(created) with
        {
            Code = "otra",
            Name = "Otra",
            BenefitType = PromotionBenefitType.FixedAmountDiscount,
            BenefitValue = 5000m,
            ConditionType = PromotionConditionType.NewStudent,
            MaxUsesTotal = 10
        });

        Assert.Equal("OTRA", updated.Code);
        Assert.Equal("Otra", updated.Name);
        Assert.Equal(PromotionBenefitType.FixedAmountDiscount, updated.BenefitType);
        Assert.Equal(5000m, updated.BenefitValue);
        Assert.Equal(PromotionConditionType.NewStudent, updated.ConditionType);
        Assert.Equal(10, updated.MaxUsesTotal);
    }

    [Fact]
    public async Task Update_UnknownPromotion_ThrowsNotFound()
    {
        var created = await _service.CreateAsync(ValidCreateRequest());

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.UpdateAsync(Guid.NewGuid(), UpdateRequestFrom(created)));
    }

    [Fact]
    public async Task Update_CodeTakenByAnotherPromotion_ThrowsCodeTaken()
    {
        await _service.CreateAsync(ValidCreateRequest("PRIMERA"));
        var second = await _service.CreateAsync(ValidCreateRequest("SEGUNDA"));

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateAsync(second.Id, UpdateRequestFrom(second) with { Code = "primera" }));

        Assert.Equal(PromotionErrorCodes.CodeTaken, ex.Code);
    }

    [Fact]
    public async Task Update_WithRedemptions_RejectsLockedFieldsAndKeepsValues()
    {
        var promo = await CreateWithRedemptionAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateAsync(promo.Id, UpdateRequestFrom(promo) with { BenefitValue = 50m }));

        Assert.Equal(PromotionErrorCodes.LockedFields, ex.Code);
        Assert.Equal(20m, (await _service.GetByIdAsync(promo.Id))!.BenefitValue);
    }

    [Fact]
    public async Task Update_WithVoidedRedemptionsOnly_StillLocksFields()
    {
        var promo = await CreateWithRedemptionAsync();
        var redemption = await _db.PromotionRedemptions.SingleAsync();
        await _service.VoidRedemptionAsync(redemption.Id);
        promo = (await _service.GetByIdAsync(promo.Id))!;

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateAsync(promo.Id, UpdateRequestFrom(promo) with { BenefitValue = 50m }));

        Assert.Equal(PromotionErrorCodes.LockedFields, ex.Code);
    }

    [Fact]
    public async Task Update_WithRedemptions_AllowsExtendingValidityAndRenaming()
    {
        var promo = await CreateWithRedemptionAsync();

        var updated = await _service.UpdateAsync(promo.Id, UpdateRequestFrom(promo) with
        {
            Name = "Renombrada",
            ValidTo = new DateOnly(2026, 7, 31),
            MaxUsesTotal = 200
        });

        Assert.Equal("Renombrada", updated.Name);
        Assert.Equal(new DateOnly(2026, 7, 31), updated.ValidTo);
        Assert.Equal(200, updated.MaxUsesTotal);
    }

    [Fact]
    public async Task Update_TotalLimitBelowConsumedUses_ThrowsValidation()
    {
        var promo = await CreateWithRedemptionAsync();
        var tracked = await _db.Promotions.SingleAsync(p => p.Id == promo.Id);
        tracked.CurrentUsesTotal = 40;
        await _db.SaveChangesAsync();
        promo = (await _service.GetByIdAsync(promo.Id))!;

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateAsync(promo.Id, UpdateRequestFrom(promo) with { MaxUsesTotal = 30 }));

        Assert.Contains(nameof(UpdatePromotionRequest.MaxUsesTotal), ex.Errors.Keys);
        Assert.Equal(100, (await _service.GetByIdAsync(promo.Id))!.MaxUsesTotal);
    }

    [Fact]
    public async Task Update_PerPersonLimitBelowUsesOfASinglePerson_ThrowsValidation()
    {
        var created = await _service.CreateAsync(ValidCreateRequest() with { MaxUsesPerPerson = 3 });
        var student = await AddStudentAsync("30777666");
        await _service.RedeemPromotionAsync(new ApplyPromotionRequest(created.Code, student.Id, null, 10000m), null);
        await _service.RedeemPromotionAsync(new ApplyPromotionRequest(created.Code, student.Id, null, 10000m), null);
        var promo = (await _service.GetByIdAsync(created.Id))!;

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateAsync(promo.Id, UpdateRequestFrom(promo) with { MaxUsesPerPerson = 1 }));

        Assert.Contains(nameof(UpdatePromotionRequest.MaxUsesPerPerson), ex.Errors.Keys);
    }

    [Fact]
    public async Task Update_WithStaleVersion_ThrowsConcurrencyConflict()
    {
        var created = await _service.CreateAsync(ValidCreateRequest());

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateAsync(created.Id, UpdateRequestFrom(created) with { Name = "Otro", Version = created.Version + 1 }));

        Assert.Equal(PromotionErrorCodes.ConcurrencyConflict, ex.Code);
        Assert.Equal("Bienvenida", (await _service.GetByIdAsync(created.Id))!.Name);
    }

    [Fact]
    public async Task Update_ArchivedPromotion_IsRejected()
    {
        var created = await _service.CreateAsync(ValidCreateRequest());
        var archived = await _service.ArchiveAsync(created.Id);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateAsync(archived.Id, UpdateRequestFrom(archived) with { Name = "Otro" }));

        Assert.Equal(PromotionErrorCodes.LockedFields, ex.Code);
    }

    // ---- Ciclo de vida ----

    [Fact]
    public async Task PauseActivateArchive_FollowTheStateMachine()
    {
        var created = await _service.CreateAsync(ValidCreateRequest());
        var student = await AddStudentAsync("30555444");
        var request = new ApplyPromotionRequest(created.Code, student.Id, null, 10000m);

        var paused = await _service.PauseAsync(created.Id);
        Assert.Equal(PromotionStatus.Paused, paused.Status);
        var pausedEx = await Assert.ThrowsAsync<ConflictException>(() => _service.EvaluatePromotionAsync(request));
        Assert.Equal(PromotionErrorCodes.NotApplicable, pausedEx.Code);

        var reactivated = await _service.ActivateAsync(created.Id);
        Assert.Equal(PromotionStatus.Active, reactivated.Status);
        await _service.RedeemPromotionAsync(request, membershipId: null);

        var archived = await _service.ArchiveAsync(created.Id);
        Assert.Equal(PromotionStatus.Archived, archived.Status);

        // El archivo conserva los canjes y la promoción sigue siendo consultable
        Assert.Equal(1, archived.CurrentUsesTotal);
        Assert.True((await _db.PromotionRedemptions.SingleAsync()).IsActive);
        Assert.NotNull(await _service.GetByIdAsync(created.Id));
    }

    [Fact]
    public async Task InvalidTransitions_ThrowInvalidTransition()
    {
        var created = await _service.CreateAsync(ValidCreateRequest());

        var activateActive = await Assert.ThrowsAsync<ConflictException>(() => _service.ActivateAsync(created.Id));
        Assert.Equal(PromotionErrorCodes.InvalidTransition, activateActive.Code);

        await _service.ArchiveAsync(created.Id);

        var activateArchived = await Assert.ThrowsAsync<ConflictException>(() => _service.ActivateAsync(created.Id));
        Assert.Equal(PromotionErrorCodes.InvalidTransition, activateArchived.Code);
        var pauseArchived = await Assert.ThrowsAsync<ConflictException>(() => _service.PauseAsync(created.Id));
        Assert.Equal(PromotionErrorCodes.InvalidTransition, pauseArchived.Code);

        await Assert.ThrowsAsync<NotFoundException>(() => _service.PauseAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetAll_FiltersByStatus_AndOnlyActiveExcludesPausedAndArchived()
    {
        var active = await _service.CreateAsync(ValidCreateRequest("ACTIVA"));
        var paused = await _service.CreateAsync(ValidCreateRequest("PAUSADA"));
        var archived = await _service.CreateAsync(ValidCreateRequest("ARCHIVADA"));
        await _service.PauseAsync(paused.Id);
        await _service.ArchiveAsync(archived.Id);

        Assert.Equal(3, (await _service.GetAllAsync()).Count);
        Assert.Equal("ACTIVA", Assert.Single(await _service.GetAllAsync(onlyActive: true)).Code);
        Assert.Equal("PAUSADA", Assert.Single(await _service.GetAllAsync(status: PromotionStatus.Paused)).Code);
        Assert.Equal("ARCHIVADA", Assert.Single(await _service.GetAllAsync(status: PromotionStatus.Archived)).Code);
        Assert.Equal(active.Id, Assert.Single(await _service.GetAllAsync(status: PromotionStatus.Active)).Id);
    }

    // ---- Canjes por promoción ----

    [Fact]
    public async Task GetRedemptionsByPromotion_IsPaged_AndIncludesVoidedOnes()
    {
        var created = await _service.CreateAsync(ValidCreateRequest());
        var redemptions = new List<PromotionRedemptionDto>();
        for (var i = 0; i < 3; i++)
        {
            var student = await AddStudentAsync($"3100000{i}");
            _calendar.UtcNow = _calendar.UtcNow.AddMinutes(1);
            redemptions.Add(await _service.RedeemPromotionAsync(
                new ApplyPromotionRequest(created.Code, student.Id, null, 10000m), membershipId: null));
        }
        await _service.VoidRedemptionAsync(redemptions[0].Id);

        var firstPage = await _service.GetRedemptionsByPromotionAsync(created.Id, page: 1, pageSize: 2);
        var secondPage = await _service.GetRedemptionsByPromotionAsync(created.Id, page: 2, pageSize: 2);

        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(2, firstPage.Items.Count);
        // Más recientes primero
        Assert.Equal(redemptions[2].Id, firstPage.Items[0].Id);
        Assert.False(firstPage.Items[0].IsVoided);

        var voided = Assert.Single(secondPage.Items);
        Assert.Equal(redemptions[0].Id, voided.Id);
        Assert.True(voided.IsVoided);

        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetRedemptionsByPromotionAsync(Guid.NewGuid(), 1, 10));
    }
}
