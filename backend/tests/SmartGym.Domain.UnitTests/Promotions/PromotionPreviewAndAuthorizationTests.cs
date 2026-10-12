using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Promotions;
using SmartGym.Application.Modules.Promotions.Dtos;
using SmartGym.Application.Modules.Promotions.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Infrastructure.Persistence;
using Xunit;
using static SmartGym.Domain.UnitTests.Promotions.PromotionTestSupport;

namespace SmartGym.Domain.UnitTests.Promotions;

public class PromotionPreviewAndAuthorizationTests : IDisposable
{
    private readonly SmartGymDbContext _db = CreateDbContext();
    private readonly Person _student = Person.Create("Ana", "Sosa", "30111222");
    private readonly Person _otherStudent = Person.Create("Luis", "Paz", "30333444");
    private readonly MembershipPlan _plan = new()
    {
        Id = Guid.NewGuid(),
        Name = "Plan 8 Clases",
        Price = 10000m,
        DurationDays = 30,
        Credits = 8,
        IsActive = true
    };

    public PromotionPreviewAndAuthorizationTests()
    {
        _db.People.AddRange(_student, _otherStudent);
        _db.MembershipPlans.Add(_plan);
        _db.Promotions.Add(NewPromotion("DESC20"));
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private PromotionService ServiceFor(ICurrentUserService? currentUser) =>
        new(_db, new StubGymCalendar(), currentUser);

    private StubCurrentUserService AsStudent(Person person) => new(person.Id, Roles.Student);

    [Fact]
    public async Task Preview_UsesThePlanPrice_AndHasNoSideEffects()
    {
        var service = ServiceFor(AsStudent(_student));

        var result = await service.PreviewForMembershipAsync(new EvaluatePromotionRequest("desc20", _student.Id, _plan.Id));

        Assert.Equal(10000m, result.OriginalAmount);
        Assert.Equal(2000m, result.DiscountAmount);
        Assert.Equal(8000m, result.FinalAmount);
        Assert.Equal(0, (await _db.Promotions.SingleAsync()).CurrentUsesTotal);
        Assert.Equal(0, await _db.PromotionRedemptions.CountAsync());
    }

    [Fact]
    public async Task Preview_UnknownOrInactivePlan_ThrowsValidation()
    {
        var service = ServiceFor(AsStudent(_student));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.PreviewForMembershipAsync(new EvaluatePromotionRequest("DESC20", _student.Id, Guid.NewGuid())));

        Assert.Contains(nameof(EvaluatePromotionRequest.MembershipPlanId), ex.Errors.Keys);
    }

    [Fact]
    public async Task Preview_RejectedPromotion_KeepsItsErrorCode()
    {
        var service = ServiceFor(AsStudent(_student));

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.PreviewForMembershipAsync(new EvaluatePromotionRequest("NOEXISTE", _student.Id, _plan.Id)));

        Assert.Equal(PromotionErrorCodes.NotApplicable, ex.Code);
    }

    [Fact]
    public async Task Preview_StudentForAnotherPerson_IsForbidden()
    {
        var service = ServiceFor(AsStudent(_student));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.PreviewForMembershipAsync(new EvaluatePromotionRequest("DESC20", _otherStudent.Id, _plan.Id)));
    }

    [Theory]
    [InlineData(Roles.Administrator)]
    [InlineData(Roles.Secretary)]
    public async Task Preview_StaffForAnyPerson_IsAllowed(string role)
    {
        var service = ServiceFor(new StubCurrentUserService(Guid.NewGuid(), role));

        var result = await service.PreviewForMembershipAsync(new EvaluatePromotionRequest("DESC20", _otherStudent.Id, _plan.Id));

        Assert.Equal(8000m, result.FinalAmount);
    }

    [Fact]
    public async Task GetRedemptionsByPerson_OwnerSeesOwn_OtherStudentIsForbidden_StaffSeesAny()
    {
        await ServiceFor(null).RedeemPromotionAsync(new ApplyPromotionRequest("DESC20", _student.Id, null, 10000m), membershipId: null);

        var own = await ServiceFor(AsStudent(_student)).GetRedemptionsByPersonAsync(_student.Id);
        Assert.Single(own);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            ServiceFor(AsStudent(_otherStudent)).GetRedemptionsByPersonAsync(_student.Id));

        // Un instructor no es personal administrativo
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            ServiceFor(new StubCurrentUserService(Guid.NewGuid(), Roles.Instructor)).GetRedemptionsByPersonAsync(_student.Id));

        var asStaff = await ServiceFor(new StubCurrentUserService(Guid.NewGuid(), Roles.Secretary)).GetRedemptionsByPersonAsync(_student.Id);
        Assert.Single(asStaff);
    }
}
