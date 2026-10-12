using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using SmartGym.Application.Common.Models;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Promotions;
using SmartGym.Application.Modules.Promotions.Dtos;
using SmartGym.Application.Modules.Promotions.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using SmartGym.WebApi.Controllers;
using Xunit;
using static SmartGym.Domain.UnitTests.Promotions.PromotionTestSupport;

namespace SmartGym.Domain.UnitTests.Promotions;

public class PromotionsControllerTests : IDisposable
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

    public PromotionsControllerTests()
    {
        _db.People.AddRange(_student, _otherStudent);
        _db.MembershipPlans.Add(_plan);
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private PromotionsController ControllerAs(Guid? personId, string role) =>
        new(new PromotionService(_db, new StubGymCalendar(), new StubCurrentUserService(personId, role)));

    private PromotionsController StaffController() => ControllerAs(Guid.NewGuid(), Roles.Secretary);

    private static PromotionApiError ErrorOf(IActionResult result, int expectedStatus)
    {
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(expectedStatus, objectResult.StatusCode);
        return Assert.IsType<PromotionApiError>(objectResult.Value);
    }

    private static T ValueOf<T>(IActionResult result)
    {
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.InRange(objectResult.StatusCode ?? 200, 200, 299);
        return Assert.IsAssignableFrom<T>(objectResult.Value);
    }

    // ---- Superficie y políticas (Decision 5) ----

    [Theory]
    [InlineData("GetAll", "GET", null, Policies.PromotionsRead)]
    [InlineData("GetById", "GET", "{id:guid}", Policies.PromotionsRead)]
    [InlineData("GetByCode", "GET", "code/{code}", Policies.PromotionsRead)]
    [InlineData("Create", "POST", null, Policies.PromotionsManage)]
    [InlineData("Update", "PUT", "{id:guid}", Policies.PromotionsManage)]
    [InlineData("Pause", "POST", "{id:guid}/pause", Policies.PromotionsManage)]
    [InlineData("Activate", "POST", "{id:guid}/activate", Policies.PromotionsManage)]
    [InlineData("Archive", "POST", "{id:guid}/archive", Policies.PromotionsManage)]
    [InlineData("GetRedemptions", "GET", "{id:guid}/redemptions", Policies.PromotionsRedemptionsRead)]
    [InlineData("GetRedemptionsByPerson", "GET", "redemptions/person/{personId:guid}", null)]
    [InlineData("Evaluate", "POST", "evaluate", null)]
    public void Actions_ExposeTheExpectedRouteAndPolicy(string action, string httpMethod, string? template, string? policy)
    {
        var method = typeof(PromotionsController).GetMethod(action);
        Assert.NotNull(method);

        var http = method!.GetCustomAttributes<HttpMethodAttribute>().Single();
        Assert.Equal(httpMethod, http.HttpMethods.Single());
        Assert.Equal(template, http.Template);

        // Toda acción exige autenticación; las que no fijan política validan el ámbito en el servicio
        var authorize = method.GetCustomAttributes<AuthorizeAttribute>().Single();
        Assert.Equal(policy, authorize.Policy);
        Assert.Empty(method.GetCustomAttributes<AllowAnonymousAttribute>());
    }

    [Fact]
    public void Controller_DoesNotExposeAStandaloneRedeemOperation()
    {
        var actions = typeof(PromotionsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
            .ToList();

        Assert.DoesNotContain(actions, m => m.Name.Contains("Redeem", StringComparison.OrdinalIgnoreCase)
            && m.GetCustomAttributes<HttpMethodAttribute>().Any(h => h.HttpMethods.Contains("POST")));
        Assert.DoesNotContain(actions, m => m.GetCustomAttributes<HttpMethodAttribute>().Any(h => h.Template == "redeem"));
        Assert.Equal(11, actions.Count);
    }

    // ---- Traducción de resultados y errores ----

    [Fact]
    public async Task Create_ReturnsCreated_AndDuplicateReturnsConflictWithCode()
    {
        var controller = StaffController();

        var created = await controller.Create(ValidCreateRequest(), default);
        var createdResult = Assert.IsType<CreatedAtActionResult>(created);
        Assert.Equal(PromotionStatus.Active, Assert.IsType<PromotionDto>(createdResult.Value).Status);

        var duplicate = await controller.Create(ValidCreateRequest("bienvenida"), default);
        Assert.Equal(PromotionErrorCodes.CodeTaken, ErrorOf(duplicate, StatusCodes.Status409Conflict).Code);
    }

    [Fact]
    public async Task Create_InvalidRequest_ReturnsBadRequestWithValidationErrors()
    {
        var result = await StaffController().Create(ValidCreateRequest() with { BenefitValue = 150m }, default);

        var error = ErrorOf(result, StatusCodes.Status400BadRequest);
        Assert.Equal("VALIDATION_ERROR", error.Code);
        Assert.NotNull(error.Details);
    }

    [Fact]
    public async Task GetByIdAndGetByCode_UnknownPromotion_ReturnNotFoundWithCode()
    {
        var controller = StaffController();

        Assert.Equal(PromotionErrorCodes.NotFound, ErrorOf(await controller.GetById(Guid.NewGuid(), default), StatusCodes.Status404NotFound).Code);
        Assert.Equal(PromotionErrorCodes.NotFound, ErrorOf(await controller.GetByCode("NOEXISTE", default), StatusCodes.Status404NotFound).Code);
        Assert.Equal(PromotionErrorCodes.NotFound, ErrorOf(await controller.Pause(Guid.NewGuid(), default), StatusCodes.Status404NotFound).Code);
    }

    [Fact]
    public async Task LifecycleAndUpdate_TranslateConflictsToTheirCodes()
    {
        var controller = StaffController();
        var created = Assert.IsType<PromotionDto>(((CreatedAtActionResult)await controller.Create(ValidCreateRequest(), default)).Value);

        Assert.Equal(PromotionStatus.Paused, ValueOf<PromotionDto>(await controller.Pause(created.Id, default)).Status);
        Assert.Equal(PromotionErrorCodes.InvalidTransition, ErrorOf(await controller.Pause(created.Id, default), StatusCodes.Status409Conflict).Code);
        Assert.Equal(PromotionStatus.Active, ValueOf<PromotionDto>(await controller.Activate(created.Id, default)).Status);

        var stale = await controller.Update(created.Id, UpdateRequestFrom(created) with { Version = created.Version + 7 }, default);
        Assert.Equal(PromotionErrorCodes.ConcurrencyConflict, ErrorOf(stale, StatusCodes.Status409Conflict).Code);

        var current = ValueOf<PromotionDto>(await controller.GetById(created.Id, default));
        var updated = ValueOf<PromotionDto>(await controller.Update(created.Id, UpdateRequestFrom(current) with { Name = "Otra" }, default));
        Assert.Equal("Otra", updated.Name);

        Assert.Equal(PromotionStatus.Archived, ValueOf<PromotionDto>(await controller.Archive(created.Id, default)).Status);
        Assert.Equal(PromotionErrorCodes.InvalidTransition, ErrorOf(await controller.Activate(created.Id, default), StatusCodes.Status409Conflict).Code);
    }

    [Fact]
    public async Task Evaluate_StudentForSelfSucceeds_ForAnotherPersonIsForbidden_AndRejectionsKeepTheirCode()
    {
        await StaffController().Create(ValidCreateRequest("DESC20"), default);
        var asStudent = ControllerAs(_student.Id, Roles.Student);

        var own = ValueOf<PromotionApplicationResultDto>(
            await asStudent.Evaluate(new EvaluatePromotionRequest("DESC20", _student.Id, _plan.Id), default));
        Assert.Equal(8000m, own.FinalAmount);

        var other = await asStudent.Evaluate(new EvaluatePromotionRequest("DESC20", _otherStudent.Id, _plan.Id), default);
        Assert.Equal("FORBIDDEN", ErrorOf(other, StatusCodes.Status403Forbidden).Code);

        var unknown = await asStudent.Evaluate(new EvaluatePromotionRequest("NOEXISTE", _student.Id, _plan.Id), default);
        Assert.Equal(PromotionErrorCodes.NotApplicable, ErrorOf(unknown, StatusCodes.Status409Conflict).Code);
    }

    [Fact]
    public async Task Redemptions_ByPromotionArePaged_AndByPersonRespectOwnership()
    {
        var staff = StaffController();
        var created = Assert.IsType<PromotionDto>(((CreatedAtActionResult)await staff.Create(ValidCreateRequest("DESC20"), default)).Value);
        await new PromotionService(_db, new StubGymCalendar())
            .RedeemPromotionAsync(new ApplyPromotionRequest("DESC20", _student.Id, null, 10000m), membershipId: null);

        var page = ValueOf<PagedResult<PromotionRedemptionDto>>(await staff.GetRedemptions(created.Id, 1, 20, default));
        Assert.Equal(1, page.TotalCount);

        var own = ValueOf<List<PromotionRedemptionDto>>(await ControllerAs(_student.Id, Roles.Student).GetRedemptionsByPerson(_student.Id, default));
        Assert.Single(own);

        var foreign = await ControllerAs(_otherStudent.Id, Roles.Student).GetRedemptionsByPerson(_student.Id, default);
        Assert.Equal("FORBIDDEN", ErrorOf(foreign, StatusCodes.Status403Forbidden).Code);
    }
}
