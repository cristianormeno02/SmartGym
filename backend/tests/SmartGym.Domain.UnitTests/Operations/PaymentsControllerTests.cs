using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Models;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Operations;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;
using SmartGym.Domain.UnitTests.Common;
using SmartGym.Infrastructure;
using SmartGym.Infrastructure.Persistence;
using SmartGym.WebApi.Controllers;
using Xunit;

namespace SmartGym.Domain.UnitTests.Operations;

public class PaymentsControllerTests : IAsyncDisposable
{
    private sealed class StubCurrentUserService : ICurrentUserService
    {
        public Guid? UserId { get; set; } = Guid.NewGuid();
        public Guid? PersonId { get; set; }
        public string? Email { get; set; } = "user@smartgym.com";
        public bool IsAuthenticated => true;
        public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
        public bool IsInRole(string role) => Roles.Contains(role);
    }

    private readonly TestGymCalendar _calendar = new(new DateOnly(2026, 10, 12));
    private readonly SmartGymDbContext _db = new(MembershipTestSupport.InMemoryOptions());
    private readonly StubCurrentUserService _currentUser = new();
    private readonly IPaymentService _service;
    private readonly PaymentsController _controller;

    public PaymentsControllerTests()
    {
        _service = new PaymentService(_db, _calendar);
        _controller = new PaymentsController(_service, _currentUser)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    private void ActAs(string role, Guid? personId = null)
    {
        _currentUser.Roles = new[] { role };
        _currentUser.PersonId = personId;
    }

    private void SetIdempotencyKey(string? key)
    {
        _controller.Request.Headers.Remove(PaymentsController.IdempotencyKeyHeader);
        if (key != null)
        {
            _controller.Request.Headers[PaymentsController.IdempotencyKeyHeader] = key;
        }
    }

    private async Task<Person> AddStudentAsync(string firstName, string document)
    {
        var student = Person.Create(firstName, "Gómez", document);
        _db.People.Add(student);
        await _db.SaveChangesAsync();
        return student;
    }

    private async Task<Membership> AddMembershipAsync(Person student, decimal price)
    {
        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(), Name = "Plan 8", DurationDays = 30, Price = price, Credits = 8,
            IsUnlimited = false, AppliesToAllActivities = true, IsActive = true
        };
        _db.MembershipPlans.Add(plan);
        var membership = Membership.Create(
            studentId: student.Id, plan: plan, startDate: _calendar.Today, purchasedAtUtc: _calendar.UtcNow,
            originalPrice: price, discountAmount: 0m, purchasePrice: price, currency: "ARS", extraCreditsGranted: 0);
        membership.Status = MembershipStatus.Pending;
        _db.Memberships.Add(membership);
        await _db.SaveChangesAsync();
        return membership;
    }

    private Task<PaymentDto> SeedPaymentAsync(Person payer, decimal amount, Guid? membershipId = null, PaymentStatus status = PaymentStatus.Completed) =>
        _service.RegisterPaymentAsync(
            new RegisterPaymentRequest(payer.Id, membershipId, null, amount, PaymentMethod.Cash, null, null, status),
            Guid.NewGuid());

    private static void AssertForbidden(IActionResult result)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        Assert.Equal("FORBIDDEN", Assert.IsType<PaymentApiError>(objectResult.Value).Code);
    }

    private static PaymentApiError AssertError<TResult>(IActionResult result, string expectedCode) where TResult : ObjectResult
    {
        var error = Assert.IsType<PaymentApiError>(Assert.IsType<TResult>(result).Value);
        Assert.Equal(expectedCode, error.Code);
        return error;
    }

    // ---- Políticas ----

    [Theory]
    [InlineData(nameof(PaymentsController.GetAll), Policies.PaymentsRead)]
    [InlineData(nameof(PaymentsController.GetPending), Policies.PaymentsRead)]
    [InlineData(nameof(PaymentsController.RegisterPayment), Policies.PaymentsCreate)]
    [InlineData(nameof(PaymentsController.ConfirmPayment), Policies.PaymentsConfirm)]
    [InlineData(nameof(PaymentsController.FailPayment), Policies.PaymentsConfirm)]
    // La cancelación admite además al titular de un checkout en línea: se autoriza dentro de la acción
    [InlineData(nameof(PaymentsController.CancelPayment), null)]
    [InlineData(nameof(PaymentsController.ApplyPayment), Policies.PaymentsAllocate)]
    [InlineData(nameof(PaymentsController.CreateRefund), Policies.PaymentsRefund)]
    [InlineData(nameof(PaymentsController.GetById), null)]
    [InlineData(nameof(PaymentsController.GetByPersonId), null)]
    [InlineData(nameof(PaymentsController.GetRefunds), null)]
    [InlineData(nameof(PaymentsController.GetMembershipBalance), null)]
    public void Endpoint_DeclaresExpectedPolicy(string actionName, string? expectedPolicy)
    {
        var authorize = typeof(PaymentsController).GetMethod(actionName)!.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(expectedPolicy, authorize!.Policy);
    }

    [Fact]
    public void EveryEndpoint_RequiresAuthentication_AndLegacyRoutesAreKeptOrReplaced()
    {
        var actions = typeof(PaymentsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
            .ToList();

        // Sólo el webhook del proveedor es anónimo: lo autentica su firma
        Assert.All(
            actions.Where(m => m.Name != nameof(PaymentsController.ReceiveWebhook)),
            m => Assert.NotNull(m.GetCustomAttribute<AuthorizeAttribute>()));

        var templates = actions.SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>()).Select(a => a.Template).ToList();
        Assert.Contains("person/{studentId:guid}", templates);
        Assert.Contains("student/{studentId:guid}", templates);
        Assert.Contains("{id:guid}/refunds", templates);
        Assert.DoesNotContain("{id:guid}/refund", templates);
    }

    [Theory]
    [InlineData(Policies.PaymentsRead, Roles.Administrator, true)]
    [InlineData(Policies.PaymentsRead, Roles.Secretary, true)]
    [InlineData(Policies.PaymentsRead, Roles.Instructor, false)]
    [InlineData(Policies.PaymentsRead, Roles.Student, false)]
    [InlineData(Policies.PaymentsCreate, Roles.Secretary, true)]
    [InlineData(Policies.PaymentsCreate, Roles.Student, false)]
    [InlineData(Policies.PaymentsConfirm, Roles.Secretary, true)]
    [InlineData(Policies.PaymentsCancel, Roles.Secretary, true)]
    [InlineData(Policies.PaymentsCancel, Roles.Student, false)]
    [InlineData(Policies.PaymentsAllocate, Roles.Secretary, true)]
    [InlineData(Policies.PaymentsAllocate, Roles.Instructor, false)]
    [InlineData(Policies.PaymentsRefund, Roles.Administrator, true)]
    [InlineData(Policies.PaymentsRefund, Roles.Secretary, false)]
    [InlineData(Policies.PaymentsRefund, Roles.Student, false)]
    public async Task RegisteredPolicy_GrantsOnlyTheExpectedRoles(string policy, string role, bool expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=policy_only",
                ["Jwt:SecretKey"] = new string('k', 64)
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, "test"));
        var result = await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, null, policy);

        Assert.Equal(expected, result.Succeeded);
    }

    // ---- Titularidad en las consultas ----

    [Fact]
    public async Task Student_CanReadOnlyOwnPaymentsRefundsAndMembershipBalance()
    {
        var owner = await AddStudentAsync("Martín", "31222333");
        var other = await AddStudentAsync("Lucía", "32444555");
        var ownMembership = await AddMembershipAsync(owner, 20000m);
        var otherMembership = await AddMembershipAsync(other, 20000m);
        var ownPayment = await SeedPaymentAsync(owner, 20000m, ownMembership.Id);
        var otherPayment = await SeedPaymentAsync(other, 20000m, otherMembership.Id);

        ActAs(Roles.Student, owner.Id);

        Assert.IsType<OkObjectResult>(await _controller.GetById(ownPayment.Id, CancellationToken.None));
        Assert.IsType<OkObjectResult>(await _controller.GetByPersonId(owner.Id, CancellationToken.None));
        Assert.IsType<OkObjectResult>(await _controller.GetRefunds(ownPayment.Id, CancellationToken.None));
        Assert.IsType<OkObjectResult>(await _controller.GetMembershipBalance(ownMembership.Id, CancellationToken.None));

        AssertForbidden(await _controller.GetById(otherPayment.Id, CancellationToken.None));
        AssertForbidden(await _controller.GetByPersonId(other.Id, CancellationToken.None));
        AssertForbidden(await _controller.GetRefunds(otherPayment.Id, CancellationToken.None));
        AssertForbidden(await _controller.GetMembershipBalance(otherMembership.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Instructor_HasNoAccess_EvenToPaymentsMadeByThemselves()
    {
        var person = await AddStudentAsync("Diego", "33555666");
        var membership = await AddMembershipAsync(person, 20000m);
        var payment = await SeedPaymentAsync(person, 20000m, membership.Id);

        ActAs(Roles.Instructor, person.Id);

        AssertForbidden(await _controller.GetById(payment.Id, CancellationToken.None));
        AssertForbidden(await _controller.GetByPersonId(person.Id, CancellationToken.None));
        AssertForbidden(await _controller.GetRefunds(payment.Id, CancellationToken.None));
        AssertForbidden(await _controller.GetMembershipBalance(membership.Id, CancellationToken.None));
    }

    [Fact]
    public async Task UserWithoutPersonId_IsNotTreatedAsOwner()
    {
        var person = await AddStudentAsync("Valeria", "34666777");
        var payment = await SeedPaymentAsync(person, 5000m);

        ActAs(Roles.Student, personId: null);

        AssertForbidden(await _controller.GetById(payment.Id, CancellationToken.None));
    }

    [Theory]
    [InlineData(Roles.Secretary)]
    [InlineData(Roles.Administrator)]
    public async Task Staff_CanReadAnyPayment_AndGetsPagedListing(string role)
    {
        var person = await AddStudentAsync("Martín", "31222333");
        var payment = await SeedPaymentAsync(person, 5000m);
        await SeedPaymentAsync(person, 6000m, status: PaymentStatus.Pending);

        ActAs(role);

        Assert.IsType<OkObjectResult>(await _controller.GetById(payment.Id, CancellationToken.None));
        Assert.IsType<OkObjectResult>(await _controller.GetByPersonId(person.Id, CancellationToken.None));

        var all = Assert.IsType<PagedResult<PaymentDto>>(
            Assert.IsType<OkObjectResult>(await _controller.GetAll(pageSize: 1)).Value);
        Assert.Equal(2, all.TotalCount);
        Assert.Single(all.Items);

        var pending = Assert.IsType<PagedResult<PaymentDto>>(
            Assert.IsType<OkObjectResult>(await _controller.GetPending()).Value);
        Assert.Equal(PaymentStatus.Pending, Assert.Single(pending.Items).Status);
    }

    // ---- Idempotencia y códigos HTTP ----

    [Fact]
    public async Task RegisterPayment_Returns201_ThenReturns200ForSameIdempotencyKey_And409ForDifferentData()
    {
        ActAs(Roles.Secretary);
        var payer = await AddStudentAsync("Martín", "31222333");
        var request = new RegisterPaymentRequest(payer.Id, null, null, 12000m, PaymentMethod.Cash, null, null);
        SetIdempotencyKey("pay-001");

        var created = Assert.IsType<CreatedAtActionResult>(await _controller.RegisterPayment(request, CancellationToken.None));
        var repeated = Assert.IsType<OkObjectResult>(await _controller.RegisterPayment(request, CancellationToken.None));

        Assert.Equal(((PaymentDto)created.Value!).Id, ((PaymentDto)repeated.Value!).Id);
        Assert.Equal(1, await _db.Payments.CountAsync());

        AssertError<ConflictObjectResult>(
            await _controller.RegisterPayment(request with { Amount = 1m }, CancellationToken.None),
            PaymentErrorCodes.IdempotencyKeyMismatch);

        // Sin clave no hay deduplicación
        SetIdempotencyKey(null);
        Assert.IsType<CreatedAtActionResult>(await _controller.RegisterPayment(request, CancellationToken.None));
        Assert.Equal(2, await _db.Payments.CountAsync());
    }

    [Fact]
    public async Task CreateRefund_Returns201_ThenReturns200ForSameIdempotencyKey()
    {
        ActAs(Roles.Administrator);
        var payer = await AddStudentAsync("Martín", "31222333");
        var payment = await SeedPaymentAsync(payer, 30000m);
        var request = new CreatePaymentRefundRequest(10000m, "Descuento");
        SetIdempotencyKey("refund-001");

        var created = Assert.IsType<CreatedAtActionResult>(await _controller.CreateRefund(payment.Id, request, CancellationToken.None));
        var repeated = Assert.IsType<OkObjectResult>(await _controller.CreateRefund(payment.Id, request, CancellationToken.None));

        Assert.Equal(((PaymentRefundDto)created.Value!).Id, ((PaymentRefundDto)repeated.Value!).Id);
        Assert.Equal(1, await _db.PaymentRefunds.CountAsync());
    }

    [Fact]
    public async Task Errors_AreTranslatedToStableCodes()
    {
        ActAs(Roles.Administrator);
        var payer = await AddStudentAsync("Martín", "31222333");
        var membership = await AddMembershipAsync(payer, 10000m);
        var pending = await SeedPaymentAsync(payer, 5000m, status: PaymentStatus.Pending);
        var completed = await SeedPaymentAsync(payer, 5000m);

        AssertError<NotFoundObjectResult>(
            await _controller.GetById(Guid.NewGuid(), CancellationToken.None), PaymentErrorCodes.NotFound);
        AssertError<NotFoundObjectResult>(
            await _controller.ConfirmPayment(Guid.NewGuid(), null, CancellationToken.None), PaymentErrorCodes.NotFound);
        AssertError<NotFoundObjectResult>(
            await _controller.GetMembershipBalance(Guid.NewGuid(), CancellationToken.None), PaymentErrorCodes.ObligationNotFound);
        AssertError<NotFoundObjectResult>(
            await _controller.RegisterPayment(
                new RegisterPaymentRequest(payer.Id, Guid.NewGuid(), null, 100m, PaymentMethod.Cash, null, null), CancellationToken.None),
            PaymentErrorCodes.ObligationNotFound);

        AssertError<BadRequestObjectResult>(
            await _controller.RegisterPayment(
                new RegisterPaymentRequest(payer.Id, null, null, 0m, PaymentMethod.Cash, null, null), CancellationToken.None),
            "VALIDATION_ERROR");
        AssertError<BadRequestObjectResult>(
            await _controller.CancelPayment(pending.Id, new CancelPaymentRequest(""), CancellationToken.None), "VALIDATION_ERROR");
        AssertError<BadRequestObjectResult>(
            await _controller.ApplyPayment(completed.Id, new ApplyPaymentRequest(null, null), CancellationToken.None), "VALIDATION_ERROR");

        AssertError<ConflictObjectResult>(
            await _controller.RegisterPayment(
                new RegisterPaymentRequest(payer.Id, membership.Id, null, 10000.01m, PaymentMethod.Cash, null, null), CancellationToken.None),
            PaymentErrorCodes.ObligationOverpaid);
        AssertError<ConflictObjectResult>(
            await _controller.CancelPayment(completed.Id, new CancelPaymentRequest("Tarde"), CancellationToken.None),
            PaymentErrorCodes.InvalidTransition);
        AssertError<ConflictObjectResult>(
            await _controller.CreateRefund(completed.Id, new CreatePaymentRefundRequest(5000.01m, "Excede"), CancellationToken.None),
            PaymentErrorCodes.RefundAmountExceeded);

        Assert.IsType<OkObjectResult>(
            await _controller.ApplyPayment(completed.Id, new ApplyPaymentRequest(membership.Id, null), CancellationToken.None));
        AssertError<ConflictObjectResult>(
            await _controller.ApplyPayment(completed.Id, new ApplyPaymentRequest(membership.Id, null), CancellationToken.None),
            PaymentErrorCodes.AlreadyApplied);

        Assert.IsType<OkObjectResult>(
            await _controller.CancelPayment(pending.Id, new CancelPaymentRequest("El alumno desistió"), CancellationToken.None));
    }
}
