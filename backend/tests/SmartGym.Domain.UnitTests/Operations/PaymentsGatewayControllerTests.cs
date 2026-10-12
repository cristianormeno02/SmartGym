using System.Reflection;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Models;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Operations;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Domain.Enums;
using SmartGym.Domain.UnitTests.Common;
using SmartGym.Infrastructure;
using SmartGym.WebApi.Controllers;
using Xunit;

namespace SmartGym.Domain.UnitTests.Operations;

public class PaymentsGatewayControllerTests : IAsyncDisposable
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

    private readonly PaymentGatewayHarness _h = new();
    private readonly StubCurrentUserService _currentUser = new();
    private readonly PaymentsController _controller;

    public PaymentsGatewayControllerTests()
    {
        _controller = new PaymentsController(_h.Service, _currentUser)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private void ActAs(string role, Guid? personId = null)
    {
        _currentUser.Roles = new[] { role };
        _currentUser.PersonId = personId;
    }

    private void SetWebhookRequest(string transactionId, string? eventId = null, bool validSignature = true)
    {
        var request = _controller.Request;
        request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"type\":\"payment\"}"));
        request.Headers.Clear();
        request.Headers[FakePaymentProvider.SignatureHeader] = validSignature ? "valid" : "forged";
        request.Headers["x-request-id"] = eventId ?? Guid.NewGuid().ToString();
        request.QueryString = new QueryString($"?data.id={transactionId}&type=payment");
    }

    private static void AssertForbidden(IActionResult result)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
    }

    private static PaymentApiError AssertError(IActionResult result, int expectedStatus, string expectedCode)
    {
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(expectedStatus, objectResult.StatusCode);
        var error = Assert.IsType<PaymentApiError>(objectResult.Value);
        Assert.Equal(expectedCode, error.Code);
        return error;
    }

    // ---- Políticas ----

    [Theory]
    [InlineData(nameof(PaymentsController.Reconcile), Policies.PaymentsReconcile)]
    [InlineData(nameof(PaymentsController.GetDiscrepancies), Policies.PaymentsReconcile)]
    [InlineData(nameof(PaymentsController.ResolveDiscrepancy), Policies.PaymentsReconcile)]
    [InlineData(nameof(PaymentsController.CreateCheckout), null)]
    [InlineData(nameof(PaymentsController.CancelPayment), null)]
    public void Endpoint_DeclaresExpectedPolicy(string actionName, string? expectedPolicy)
    {
        var authorize = typeof(PaymentsController).GetMethod(actionName)!.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(expectedPolicy, authorize!.Policy);
    }

    [Fact]
    public void Webhook_IsAnonymous_AndRateLimited()
    {
        var method = typeof(PaymentsController).GetMethod(nameof(PaymentsController.ReceiveWebhook))!;

        Assert.NotNull(method.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(method.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal(PaymentsController.WebhookRateLimitPolicy, method.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
    }

    [Theory]
    [InlineData(Roles.Administrator, true)]
    [InlineData(Roles.Secretary, false)]
    [InlineData(Roles.Instructor, false)]
    [InlineData(Roles.Student, false)]
    public async Task ReconcilePolicy_IsReservedToAdministrators(string role, bool expected)
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
        var result = await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, null, Policies.PaymentsReconcile);

        Assert.Equal(expected, result.Succeeded);
    }

    // ---- Checkout y cancelación por el titular ----

    [Fact]
    public async Task Student_CanStartAndCancelOnlyTheirOwnCheckout()
    {
        var owner = await _h.AddStudentAsync("Martín", "martin.ctrl@test.com");
        var other = await _h.AddStudentAsync("Lucía", "lucia.ctrl@test.com");
        var ownMembership = await _h.AddMembershipAsync(owner);
        var otherMembership = await _h.AddMembershipAsync(other);
        var otherCheckout = await _h.CheckoutAsync(otherMembership);

        ActAs(Roles.Student, owner.Id);

        AssertForbidden(await _controller.CreateCheckout(new CreatePaymentCheckoutRequest(otherMembership.Id), CancellationToken.None));
        AssertForbidden(await _controller.CancelPayment(otherCheckout.Id, new CancelPaymentRequest("No es mío"), CancellationToken.None));

        var created = Assert.IsType<CreatedAtActionResult>(
            await _controller.CreateCheckout(new CreatePaymentCheckoutRequest(ownMembership.Id), CancellationToken.None));
        var checkout = Assert.IsType<PaymentDto>(created.Value);
        Assert.Equal(60000m, checkout.Amount);
        Assert.False(string.IsNullOrWhiteSpace(checkout.CheckoutUrl));

        var cancelled = Assert.IsType<OkObjectResult>(
            await _controller.CancelPayment(checkout.Id, new CancelPaymentRequest("Prefiero pagar en mostrador"), CancellationToken.None));
        Assert.Equal(PaymentStatus.Cancelled, Assert.IsType<PaymentDto>(cancelled.Value).Status);
    }

    [Fact]
    public async Task Student_CannotCancelAManualPendingPayment_EvenIfItIsTheirs()
    {
        var owner = await _h.AddStudentAsync();
        var manualPending = await _h.RegisterManualAsync(owner, 5000m, status: PaymentStatus.Pending);

        ActAs(Roles.Student, owner.Id);
        AssertForbidden(await _controller.CancelPayment(manualPending.Id, new CancelPaymentRequest("Quiero anularlo"), CancellationToken.None));

        ActAs(Roles.Secretary);
        Assert.IsType<OkObjectResult>(
            await _controller.CancelPayment(manualPending.Id, new CancelPaymentRequest("Anulado en mostrador"), CancellationToken.None));
    }

    [Fact]
    public async Task Instructor_CannotStartACheckout()
    {
        var person = await _h.AddStudentAsync();
        var membership = await _h.AddMembershipAsync(person);

        ActAs(Roles.Instructor, person.Id);

        AssertForbidden(await _controller.CreateCheckout(new CreatePaymentCheckoutRequest(membership.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Checkout_Returns201Then200ForTheSameKey_AndTranslatesErrors()
    {
        ActAs(Roles.Secretary);
        var student = await _h.AddStudentAsync();
        var membership = await _h.AddMembershipAsync(student);
        _controller.Request.Headers[PaymentsController.IdempotencyKeyHeader] = "chk-1";

        Assert.IsType<CreatedAtActionResult>(await _controller.CreateCheckout(new CreatePaymentCheckoutRequest(membership.Id), CancellationToken.None));
        Assert.IsType<OkObjectResult>(await _controller.CreateCheckout(new CreatePaymentCheckoutRequest(membership.Id), CancellationToken.None));

        _controller.Request.Headers.Remove(PaymentsController.IdempotencyKeyHeader);
        AssertError(
            await _controller.CreateCheckout(new CreatePaymentCheckoutRequest(membership.Id), CancellationToken.None),
            StatusCodes.Status409Conflict, PaymentErrorCodes.CheckoutAlreadyOpen);
        AssertError(
            await _controller.CreateCheckout(new CreatePaymentCheckoutRequest(Guid.NewGuid()), CancellationToken.None),
            StatusCodes.Status404NotFound, PaymentErrorCodes.ObligationNotFound);

        var otherMembership = await _h.AddMembershipAsync(student, 10000m);
        _h.Provider.CheckoutFailure = new HttpRequestException("timeout");
        AssertError(
            await _controller.CreateCheckout(new CreatePaymentCheckoutRequest(otherMembership.Id), CancellationToken.None),
            StatusCodes.Status502BadGateway, PaymentErrorCodes.ProviderUnavailable);
    }

    [Fact]
    public async Task ManualConfirmationOfAnExternalPayment_Returns409()
    {
        ActAs(Roles.Secretary);
        var student = await _h.AddStudentAsync();
        var checkout = await _h.CheckoutAsync(await _h.AddMembershipAsync(student));

        AssertError(
            await _controller.ConfirmPayment(checkout.Id, null, CancellationToken.None),
            StatusCodes.Status409Conflict, PaymentErrorCodes.ExternalPaymentManualConfirmForbidden);
    }

    // ---- Webhook ----

    [Fact]
    public async Task Webhook_WithVerifiedEvent_Returns200_AndConfirmsThePayment()
    {
        var student = await _h.AddStudentAsync();
        var membership = await _h.AddMembershipAsync(student);
        var checkout = await _h.CheckoutAsync(membership);
        _h.Provider.SetPayment("MP-1", checkout.Id, checkout.Amount);
        // El webhook no lleva usuario: llega sin autenticación
        _currentUser.Roles = Array.Empty<string>();
        SetWebhookRequest("MP-1", eventId: "evt-1");

        Assert.IsType<OkObjectResult>(await _controller.ReceiveWebhook("mercadopago", CancellationToken.None));

        Assert.Equal(PaymentStatus.Completed, (await _h.Service.GetByIdAsync(checkout.Id))!.Status);
        Assert.Equal(MembershipStatus.Active, await _h.MembershipStatusAsync(membership.Id));

        // La repetición también responde 200, para que el proveedor deje de reintentar
        SetWebhookRequest("MP-1", eventId: "evt-1");
        Assert.IsType<OkObjectResult>(await _controller.ReceiveWebhook("MercadoPago", CancellationToken.None));
    }

    [Fact]
    public async Task Webhook_WithInvalidSignature_Returns401_AndChangesNothing()
    {
        var student = await _h.AddStudentAsync();
        var checkout = await _h.CheckoutAsync(await _h.AddMembershipAsync(student));
        _h.Provider.SetPayment("MP-1", checkout.Id, checkout.Amount);
        SetWebhookRequest("MP-1", validSignature: false);

        AssertError(
            await _controller.ReceiveWebhook("mercadopago", CancellationToken.None),
            StatusCodes.Status401Unauthorized, PaymentErrorCodes.WebhookSignatureInvalid);

        Assert.Equal(PaymentStatus.Pending, (await _h.Service.GetByIdAsync(checkout.Id))!.Status);
        Assert.Empty(await _h.Db.PaymentWebhookEvents.ToListAsync());
    }

    [Theory]
    [InlineData("stripe")]
    [InlineData("manual")]
    [InlineData("2")]
    public async Task Webhook_ForUnknownProvider_Returns404(string provider)
    {
        SetWebhookRequest("MP-1");

        var result = Assert.IsType<NotFoundObjectResult>(await _controller.ReceiveWebhook(provider, CancellationToken.None));
        Assert.Equal(PaymentErrorCodes.ProviderNotSupported, Assert.IsType<PaymentApiError>(result.Value).Code);
    }

    [Fact]
    public async Task Webhook_WhenProviderLookupFails_Returns502SoTheProviderRetries()
    {
        _h.Provider.LookupFailure = new HttpRequestException("timeout");
        SetWebhookRequest("MP-1");

        AssertError(
            await _controller.ReceiveWebhook("mercadopago", CancellationToken.None),
            StatusCodes.Status502BadGateway, PaymentErrorCodes.ProviderUnavailable);
    }

    // ---- Conciliación y discrepancias ----

    [Fact]
    public async Task Reconcile_Discrepancies_AndResolution_TranslateResultsAndErrors()
    {
        ActAs(Roles.Administrator);
        var student = await _h.AddStudentAsync();
        var checkout = await _h.CheckoutAsync(await _h.AddMembershipAsync(student, 25000m));
        var manual = await _h.RegisterManualAsync(student, 1000m);
        _h.Provider.SetPayment("MP-1", checkout.Id, 20000m);

        var reconciled = Assert.IsType<OkObjectResult>(await _controller.Reconcile(checkout.Id, CancellationToken.None));
        Assert.Equal(PaymentStatus.Pending, Assert.IsType<PaymentDto>(reconciled.Value).Status);

        AssertError(await _controller.Reconcile(manual.Id, CancellationToken.None),
            StatusCodes.Status409Conflict, PaymentErrorCodes.PaymentNotExternal);
        AssertError(await _controller.Reconcile(Guid.NewGuid(), CancellationToken.None),
            StatusCodes.Status404NotFound, PaymentErrorCodes.NotFound);

        var listed = Assert.IsType<PagedResult<PaymentDiscrepancyDto>>(
            Assert.IsType<OkObjectResult>(await _controller.GetDiscrepancies(PaymentDiscrepancyStatus.Open)).Value);
        var discrepancy = Assert.Single(listed.Items);
        Assert.Equal(PaymentDiscrepancyType.AmountMismatch, discrepancy.Type);

        AssertError(
            await _controller.ResolveDiscrepancy(discrepancy.Id, new ResolvePaymentDiscrepancyRequest(""), CancellationToken.None),
            StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var resolved = Assert.IsType<OkObjectResult>(
            await _controller.ResolveDiscrepancy(discrepancy.Id, new ResolvePaymentDiscrepancyRequest("Reintegrado en el panel"), CancellationToken.None));
        var resolvedDto = Assert.IsType<PaymentDiscrepancyDto>(resolved.Value);
        Assert.Equal(PaymentDiscrepancyStatus.Resolved, resolvedDto.Status);
        Assert.Equal(_currentUser.UserId, resolvedDto.ResolvedByUserId);

        AssertError(
            await _controller.ResolveDiscrepancy(discrepancy.Id, new ResolvePaymentDiscrepancyRequest("Otra vez"), CancellationToken.None),
            StatusCodes.Status409Conflict, PaymentErrorCodes.DiscrepancyAlreadyResolved);
        AssertError(
            await _controller.ResolveDiscrepancy(Guid.NewGuid(), new ResolvePaymentDiscrepancyRequest("Notas"), CancellationToken.None),
            StatusCodes.Status404NotFound, PaymentErrorCodes.DiscrepancyNotFound);
    }

    [Fact]
    public async Task ExternalRefundRejectedByProvider_Returns502()
    {
        ActAs(Roles.Administrator);
        var student = await _h.AddStudentAsync();
        var checkout = await _h.CheckoutAsync(await _h.AddMembershipAsync(student));
        await _h.ApproveByWebhookAsync(checkout);
        _h.Provider.RefundFailure = new HttpRequestException("timeout");

        AssertError(
            await _controller.CreateRefund(checkout.Id, new CreatePaymentRefundRequest(1000m, "Ajuste"), CancellationToken.None),
            StatusCodes.Status502BadGateway, PaymentErrorCodes.ProviderRefundFailed);
    }

    [Fact]
    public async Task Listing_FiltersByProvider()
    {
        ActAs(Roles.Secretary);
        var student = await _h.AddStudentAsync();
        await _h.RegisterManualAsync(student, 1000m);
        await _h.CheckoutAsync(await _h.AddMembershipAsync(student));

        var external = Assert.IsType<PagedResult<PaymentDto>>(
            Assert.IsType<OkObjectResult>(await _controller.GetAll(provider: PaymentProvider.MercadoPago)).Value);

        Assert.Equal(PaymentProvider.MercadoPago, Assert.Single(external.Items).Provider);
    }
}
