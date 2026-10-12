using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Modules.Operations;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Domain.Enums;
using SmartGym.Domain.UnitTests.Common;
using Xunit;
using static SmartGym.Domain.UnitTests.Common.PaymentGatewayHarness;

namespace SmartGym.Domain.UnitTests.Operations;

public class PaymentCheckoutTests
{
    [Fact]
    public async Task Checkout_CreatesPendingExternalPayment_ForThePendingBalanceComputedOnTheServer()
    {
        await using var h = new PaymentGatewayHarness();
        var student = await h.AddStudentAsync();
        var membership = await h.AddMembershipAsync(student, 60000m);
        await h.RegisterManualAsync(student, 20000m, membership.Id);

        var result = await h.Service.CreateCheckoutAsync(new CreatePaymentCheckoutRequest(membership.Id), h.StaffUserId, null);

        Assert.True(result.Created);
        var checkout = result.Value;
        Assert.Equal(PaymentStatus.Pending, checkout.Status);
        Assert.Equal(PaymentProvider.MercadoPago, checkout.Provider);
        Assert.Equal(40000m, checkout.Amount);
        Assert.Equal("ARS", checkout.Currency);
        Assert.Equal(membership.Id, checkout.MembershipId);
        Assert.Equal(student.Id, checkout.PayerPersonId);
        Assert.Equal(h.Calendar.UtcNow.AddMinutes(60), checkout.ExpiresAtUtc);
        Assert.False(string.IsNullOrWhiteSpace(checkout.CheckoutUrl));

        // El proveedor recibe el Id del pago como referencia y el mismo importe y vencimiento
        var sent = Assert.Single(h.Provider.Checkouts);
        Assert.Equal(checkout.Id, sent.PaymentId);
        Assert.Equal(40000m, sent.Amount);
        Assert.Equal("ARS", sent.Currency);
        Assert.Equal(checkout.ExpiresAtUtc, sent.ExpiresAtUtc);

        var stored = await h.Db.Payments.AsNoTracking().SingleAsync(p => p.Id == checkout.Id);
        Assert.Equal($"pref-{checkout.Id:N}", stored.ExternalReference);
        Assert.Null(stored.ExternalTransactionId);

        // Un checkout pendiente no activa la membresía
        Assert.Equal(MembershipStatus.Pending, await h.MembershipStatusAsync(membership.Id));
        Assert.Contains(await h.Db.AuditLogs.ToListAsync(), a => a.Action == "PAYMENT_CHECKOUT_INITIATED" && a.EntityId == checkout.Id.ToString());
    }

    [Fact]
    public async Task Checkout_ValidatesTheObligation()
    {
        await using var h = new PaymentGatewayHarness();
        var student = await h.AddStudentAsync();

        var notFound = await Assert.ThrowsAsync<NotFoundException>(() =>
            h.Service.CreateCheckoutAsync(new CreatePaymentCheckoutRequest(Guid.NewGuid()), h.StaffUserId, null));
        Assert.Equal(PaymentErrorCodes.ObligationNotFound, notFound.Code);

        var cancelled = await h.AddMembershipAsync(student, 20000m, MembershipStatus.Cancelled);
        await AssertConflictAsync(PaymentErrorCodes.ObligationCancelled, () => h.CheckoutAsync(cancelled));

        var paid = await h.AddMembershipAsync(student, 20000m);
        await h.RegisterManualAsync(student, 20000m, paid.Id);
        await AssertConflictAsync(PaymentErrorCodes.ObligationOverpaid, () => h.CheckoutAsync(paid));

        Assert.Empty(h.Provider.Checkouts);
    }

    [Fact]
    public async Task Checkout_WithUnconfiguredOrManualProvider_IsNotSupported()
    {
        await using var h = new PaymentGatewayHarness(withProvider: false);
        var student = await h.AddStudentAsync();
        var membership = await h.AddMembershipAsync(student);

        await AssertConflictAsync(PaymentErrorCodes.ProviderNotSupported, () => h.CheckoutAsync(membership));
        await AssertConflictAsync(PaymentErrorCodes.ProviderNotSupported, () =>
            h.Service.CreateCheckoutAsync(new CreatePaymentCheckoutRequest(membership.Id, PaymentProvider.Manual), h.StaffUserId, null));

        Assert.Empty(await h.Db.Payments.ToListAsync());
    }

    [Fact]
    public async Task Checkout_WithAnotherOpenCheckout_IsRejected_UntilItIsCancelled()
    {
        await using var h = new PaymentGatewayHarness();
        var student = await h.AddStudentAsync();
        var membership = await h.AddMembershipAsync(student);
        var first = await h.CheckoutAsync(membership);

        await AssertConflictAsync(PaymentErrorCodes.CheckoutAlreadyOpen, () => h.CheckoutAsync(membership));

        await h.Service.CancelPaymentAsync(first.Id, new CancelPaymentRequest("El alumno pidió otro medio"), h.StaffUserId);
        var second = await h.CheckoutAsync(membership);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(PaymentStatus.Pending, second.Status);
    }

    [Fact]
    public async Task Checkout_WithSameIdempotencyKey_ReturnsTheSamePaymentAndUrl()
    {
        await using var h = new PaymentGatewayHarness();
        var student = await h.AddStudentAsync();
        var membership = await h.AddMembershipAsync(student);
        var other = await h.AddMembershipAsync(student, 10000m);
        var request = new CreatePaymentCheckoutRequest(membership.Id);

        var first = await h.Service.CreateCheckoutAsync(request, h.StaffUserId, "chk-1");
        var second = await h.Service.CreateCheckoutAsync(request, h.StaffUserId, "chk-1");

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Value.Id, second.Value.Id);
        Assert.Equal(first.Value.CheckoutUrl, second.Value.CheckoutUrl);
        Assert.Single(h.Provider.Checkouts);
        Assert.Equal(1, await h.Db.Payments.CountAsync());

        await AssertConflictAsync(PaymentErrorCodes.IdempotencyKeyMismatch, () =>
            h.Service.CreateCheckoutAsync(new CreatePaymentCheckoutRequest(other.Id), h.StaffUserId, "chk-1"));
    }

    [Fact]
    public async Task Checkout_WhenProviderFails_LeavesThePaymentFailed_AndAllowsANewCheckout()
    {
        await using var h = new PaymentGatewayHarness();
        var student = await h.AddStudentAsync();
        var membership = await h.AddMembershipAsync(student);
        h.Provider.CheckoutFailure = new HttpRequestException("timeout");

        await AssertExternalFailureAsync(PaymentErrorCodes.ProviderUnavailable, () => h.CheckoutAsync(membership));

        var failed = await h.Db.Payments.AsNoTracking().SingleAsync();
        Assert.Equal(PaymentStatus.Failed, failed.Status);
        Assert.Null(failed.CheckoutUrl);
        // No es un rechazo del cobro: el alumno no recibe aviso de pago rechazado
        Assert.Empty(await h.Db.NotificationMessages.ToListAsync());

        h.Provider.CheckoutFailure = null;
        var retry = await h.CheckoutAsync(membership);
        Assert.Equal(PaymentStatus.Pending, retry.Status);
    }

    [Fact]
    public async Task Checkout_RetriedWithSameKeyAfterAnInterruption_CompletesTheSamePayment()
    {
        await using var h = new PaymentGatewayHarness();
        var student = await h.AddStudentAsync();
        var membership = await h.AddMembershipAsync(student);
        var checkout = (await h.Service.CreateCheckoutAsync(new CreatePaymentCheckoutRequest(membership.Id), h.StaffUserId, "chk-9")).Value;

        // Simula un corte entre la creación del pago y el guardado de la URL
        var stored = await h.Db.Payments.SingleAsync(p => p.Id == checkout.Id);
        stored.CheckoutUrl = null;
        stored.ExternalReference = null;
        await h.Db.SaveChangesAsync();

        var retried = await h.Service.CreateCheckoutAsync(new CreatePaymentCheckoutRequest(membership.Id), h.StaffUserId, "chk-9");

        Assert.False(retried.Created);
        Assert.Equal(checkout.Id, retried.Value.Id);
        Assert.False(string.IsNullOrWhiteSpace(retried.Value.CheckoutUrl));
        Assert.Equal(2, h.Provider.Checkouts.Count);
        Assert.Equal(1, await h.Db.Payments.CountAsync());
    }

    [Fact]
    public async Task ExternalPayment_CannotBeConfirmedOrFailedManually()
    {
        await using var h = new PaymentGatewayHarness();
        var student = await h.AddStudentAsync();
        var membership = await h.AddMembershipAsync(student);
        var checkout = await h.CheckoutAsync(membership);

        await AssertConflictAsync(PaymentErrorCodes.ExternalPaymentManualConfirmForbidden, () =>
            h.Service.ConfirmPaymentAsync(checkout.Id, null, h.StaffUserId));
        await AssertConflictAsync(PaymentErrorCodes.ExternalPaymentManualConfirmForbidden, () =>
            h.Service.FailPaymentAsync(checkout.Id, new FailPaymentRequest("Rechazo manual"), h.StaffUserId));

        Assert.Equal(PaymentStatus.Pending, (await h.Service.GetByIdAsync(checkout.Id))!.Status);
        Assert.Equal(MembershipStatus.Pending, await h.MembershipStatusAsync(membership.Id));
    }

    [Fact]
    public async Task ExpireCheckouts_CancelsOnlyExpiredOnes_Idempotently_AndAllowsANewCheckout()
    {
        await using var h = new PaymentGatewayHarness();
        var student = await h.AddStudentAsync();
        var expiring = await h.AddMembershipAsync(student);
        var fresh = await h.AddMembershipAsync(student, 10000m);
        var manualPending = await h.RegisterManualAsync(student, 5000m, status: PaymentStatus.Pending);

        var oldCheckout = await h.CheckoutAsync(expiring);
        h.Calendar.UtcNow = h.Calendar.UtcNow.AddMinutes(45);
        var newCheckout = await h.CheckoutAsync(fresh);
        h.Calendar.UtcNow = h.Calendar.UtcNow.AddMinutes(20);

        Assert.Equal(1, await h.Service.ExpireCheckoutsAsync());
        Assert.Equal(0, await h.Service.ExpireCheckoutsAsync());

        var expired = (await h.Service.GetByIdAsync(oldCheckout.Id))!;
        Assert.Equal(PaymentStatus.Cancelled, expired.Status);
        Assert.Equal(h.Calendar.UtcNow, expired.CancelledAtUtc);
        Assert.Equal(PaymentStatus.Pending, (await h.Service.GetByIdAsync(newCheckout.Id))!.Status);
        Assert.Equal(PaymentStatus.Pending, (await h.Service.GetByIdAsync(manualPending.Id))!.Status);

        var audit = Assert.Single(await h.Db.AuditLogs.Where(a => a.Action == "PAYMENT_EXPIRED").ToListAsync());
        Assert.Equal(oldCheckout.Id.ToString(), audit.EntityId);
        Assert.Null(audit.UserId);

        Assert.Equal(PaymentStatus.Pending, (await h.CheckoutAsync(expiring)).Status);
    }

    [Fact]
    public async Task Checkout_OverAnExpiredOne_CancelsItOnTheSpot()
    {
        await using var h = new PaymentGatewayHarness();
        var student = await h.AddStudentAsync();
        var membership = await h.AddMembershipAsync(student);
        var old = await h.CheckoutAsync(membership);
        h.Calendar.UtcNow = h.Calendar.UtcNow.AddMinutes(61);

        var replacement = await h.CheckoutAsync(membership);

        Assert.NotEqual(old.Id, replacement.Id);
        Assert.Equal(PaymentStatus.Cancelled, (await h.Service.GetByIdAsync(old.Id))!.Status);
    }

    [Fact]
    public async Task PagedListing_FiltersByProvider()
    {
        await using var h = new PaymentGatewayHarness();
        var student = await h.AddStudentAsync();
        var membership = await h.AddMembershipAsync(student);
        await h.RegisterManualAsync(student, 5000m);
        await h.CheckoutAsync(membership);

        Assert.Equal(1, (await h.Service.GetPagedAsync(new PaymentListFilter(Provider: PaymentProvider.MercadoPago))).TotalCount);
        Assert.Equal(1, (await h.Service.GetPagedAsync(new PaymentListFilter(Provider: PaymentProvider.Manual))).TotalCount);
        Assert.Equal(2, (await h.Service.GetPagedAsync(new PaymentListFilter())).TotalCount);
    }
}
