using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Modules.Operations;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Application.Modules.Operations.Interfaces;
using SmartGym.Domain.Enums;
using SmartGym.Domain.UnitTests.Common;
using Xunit;
using static SmartGym.Domain.UnitTests.Common.PaymentGatewayHarness;

namespace SmartGym.Domain.UnitTests.Operations;

public class PaymentWebhookAndReconciliationTests
{
    private static async Task<(PaymentGatewayHarness H, Domain.Entities.Identity.Person Student, Domain.Entities.Memberships.Membership Membership, PaymentDto Checkout)> CheckoutScenarioAsync(decimal price = 60000m)
    {
        var h = new PaymentGatewayHarness();
        var student = await h.AddStudentAsync();
        var membership = await h.AddMembershipAsync(student, price);
        var checkout = await h.CheckoutAsync(membership);
        return (h, student, membership, checkout);
    }

    // ---- Firma y deduplicación ----

    [Fact]
    public async Task Webhook_WithInvalidSignature_IsRejectedWithoutWritingAnything()
    {
        var (h, _, membership, checkout) = await CheckoutScenarioAsync();
        await using var _ = h;
        h.Provider.SetPayment("MP-1", checkout.Id, checkout.Amount);
        var auditsBefore = await h.Db.AuditLogs.CountAsync();

        var result = await h.WebhookAsync("MP-1", validSignature: false);

        Assert.Equal(WebhookProcessingResult.InvalidSignature, result);
        Assert.Empty(await h.Db.PaymentWebhookEvents.ToListAsync());
        Assert.Empty(await h.DiscrepanciesAsync());
        Assert.Equal(auditsBefore, await h.Db.AuditLogs.CountAsync());
        Assert.Equal(PaymentStatus.Pending, (await h.Service.GetByIdAsync(checkout.Id))!.Status);
        Assert.Equal(MembershipStatus.Pending, await h.MembershipStatusAsync(membership.Id));
    }

    [Fact]
    public async Task Webhook_WithOversizedIdentifiers_IsRejectedLikeAnInvalidSignature()
    {
        await using var h = new PaymentGatewayHarness();

        Assert.Equal(WebhookProcessingResult.InvalidSignature, await h.WebhookAsync(new string('9', 151)));
        Assert.Equal(WebhookProcessingResult.InvalidSignature, await h.WebhookAsync("MP-1", eventId: new string('e', 151)));
        Assert.Empty(await h.Db.PaymentWebhookEvents.ToListAsync());
    }

    [Fact]
    public async Task Webhook_ForUnknownProvider_ThrowsNotFound()
    {
        await using var h = new PaymentGatewayHarness(withProvider: false);

        await Assert.ThrowsAsync<NotFoundException>(() => h.WebhookAsync("MP-1"));
    }

    [Fact]
    public async Task Webhook_Approved_ConfirmsThePayment_UsingProviderData_AndActivatesTheMembership()
    {
        var (h, _, membership, checkout) = await CheckoutScenarioAsync();
        await using var _ = h;
        h.Provider.SetPayment("MP-1001", checkout.Id, checkout.Amount);

        var result = await h.WebhookAsync("MP-1001", eventId: "evt-1");

        Assert.Equal(WebhookProcessingResult.Processed, result);
        var payment = (await h.Service.GetByIdAsync(checkout.Id))!;
        Assert.Equal(PaymentStatus.Completed, payment.Status);
        Assert.Equal("MP-1001", payment.ExternalTransactionId);
        Assert.Equal(h.Calendar.UtcNow, payment.ConfirmedAtUtc);
        Assert.Equal(MembershipStatus.Active, await h.MembershipStatusAsync(membership.Id));

        var evt = Assert.Single(await h.Db.PaymentWebhookEvents.AsNoTracking().ToListAsync());
        Assert.Equal("evt-1", evt.ExternalEventId);
        Assert.Equal("MP-1001", evt.ExternalTransactionId);
        Assert.Equal(PaymentWebhookEventStatus.Processed, evt.Status);
        Assert.Equal(checkout.Id, evt.PaymentId);

        Assert.Empty(await h.DiscrepanciesAsync());
        Assert.Contains(await h.Db.AuditLogs.ToListAsync(), a => a.Action == "PAYMENT_WEBHOOK_PROCESSED" && a.EntityId == checkout.Id.ToString());
    }

    [Fact]
    public async Task Webhook_Repeated_IsRecognizedAsAlreadyProcessed_AndDoesNotConfirmTwice()
    {
        var (h, _, membership, checkout) = await CheckoutScenarioAsync();
        await using var _ = h;
        h.Provider.SetPayment("MP-1001", checkout.Id, checkout.Amount);

        Assert.Equal(WebhookProcessingResult.Processed, await h.WebhookAsync("MP-1001", eventId: "evt-1"));
        Assert.Equal(WebhookProcessingResult.AlreadyProcessed, await h.WebhookAsync("MP-1001", eventId: "evt-1"));
        // Otra notificación de la misma transacción ya confirmada: se procesa sin efectos
        Assert.Equal(WebhookProcessingResult.Processed, await h.WebhookAsync("MP-1001", eventId: "evt-2"));

        Assert.Equal(2, await h.Db.PaymentWebhookEvents.CountAsync());
        Assert.Equal(1, await h.Db.MembershipCreditMovements.CountAsync(m => m.MembershipId == membership.Id));
        Assert.Empty(await h.DiscrepanciesAsync());
    }

    [Fact]
    public async Task Webhook_RejectedAttempt_DoesNotCloseTheCheckout_AndALaterApprovalConfirmsIt()
    {
        var (h, _, _, checkout) = await CheckoutScenarioAsync();
        await using var _ = h;
        h.Provider.SetPayment("MP-REJ", checkout.Id, checkout.Amount, ExternalPaymentStatus.Rejected);
        h.Provider.SetPayment("MP-PEND", checkout.Id, checkout.Amount, ExternalPaymentStatus.Pending);

        await h.WebhookAsync("MP-REJ");
        await h.WebhookAsync("MP-PEND");
        Assert.Equal(PaymentStatus.Pending, (await h.Service.GetByIdAsync(checkout.Id))!.Status);

        h.Provider.SetPayment("MP-OK", checkout.Id, checkout.Amount);
        await h.WebhookAsync("MP-OK");

        var payment = (await h.Service.GetByIdAsync(checkout.Id))!;
        Assert.Equal(PaymentStatus.Completed, payment.Status);
        Assert.Equal("MP-OK", payment.ExternalTransactionId);
        Assert.Empty(await h.DiscrepanciesAsync());
    }

    [Fact]
    public async Task Webhook_WhenProviderLookupFails_SignalsARetryWithoutRecordingTheEvent()
    {
        var (h, _, _, checkout) = await CheckoutScenarioAsync();
        await using var _ = h;
        h.Provider.LookupFailure = new HttpRequestException("timeout");

        await AssertExternalFailureAsync(PaymentErrorCodes.ProviderUnavailable, () => h.WebhookAsync("MP-1", eventId: "evt-1"));

        Assert.Empty(await h.Db.PaymentWebhookEvents.ToListAsync());

        // El reintento del proveedor sí se procesa
        h.Provider.LookupFailure = null;
        h.Provider.SetPayment("MP-1", checkout.Id, checkout.Amount);
        Assert.Equal(WebhookProcessingResult.Processed, await h.WebhookAsync("MP-1", eventId: "evt-1"));
    }

    // ---- Discrepancias ----

    [Theory]
    [InlineData(20000, "ARS", PaymentDiscrepancyType.AmountMismatch)]
    [InlineData(60000, "USD", PaymentDiscrepancyType.CurrencyMismatch)]
    public async Task Webhook_ApprovedWithDifferentAmountOrCurrency_RecordsDiscrepancy_AndKeepsThePaymentPending(
        decimal externalAmount, string externalCurrency, PaymentDiscrepancyType expectedType)
    {
        var (h, _, membership, checkout) = await CheckoutScenarioAsync();
        await using var _ = h;
        h.Provider.SetPayment("MP-1", checkout.Id, externalAmount, currency: externalCurrency);

        await h.WebhookAsync("MP-1");
        await h.WebhookAsync("MP-1");

        var payment = (await h.Service.GetByIdAsync(checkout.Id))!;
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(60000m, payment.Amount);
        Assert.Equal(MembershipStatus.Pending, await h.MembershipStatusAsync(membership.Id));

        var discrepancy = Assert.Single(await h.DiscrepanciesAsync());
        Assert.Equal(expectedType, discrepancy.Type);
        Assert.Equal(PaymentDiscrepancyStatus.Open, discrepancy.Status);
        Assert.Equal(checkout.Id, discrepancy.PaymentId);
        Assert.Equal(60000m, discrepancy.InternalAmount);
        Assert.Equal("ARS", discrepancy.InternalCurrency);
        Assert.Equal(externalAmount, discrepancy.ExternalAmount);
        Assert.Equal(externalCurrency, discrepancy.ExternalCurrency);
        Assert.Equal("MP-1", discrepancy.ExternalTransactionId);
        Assert.Contains(await h.Db.AuditLogs.ToListAsync(), a => a.Action == "PAYMENT_DISCREPANCY_DETECTED");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Webhook_ApprovedAfterCancellationOrExpiration_RecordsDiscrepancy_WithoutActivating(bool expired)
    {
        var (h, _, membership, checkout) = await CheckoutScenarioAsync();
        await using var _ = h;
        if (expired)
        {
            h.Calendar.UtcNow = h.Calendar.UtcNow.AddMinutes(61);
            await h.Service.ExpireCheckoutsAsync();
        }
        else
        {
            await h.Service.CancelPaymentAsync(checkout.Id, new CancelPaymentRequest("Desistió"), h.StaffUserId);
        }

        h.Provider.SetPayment("MP-LATE", checkout.Id, checkout.Amount);
        await h.WebhookAsync("MP-LATE");

        Assert.Equal(PaymentStatus.Cancelled, (await h.Service.GetByIdAsync(checkout.Id))!.Status);
        Assert.Equal(MembershipStatus.Pending, await h.MembershipStatusAsync(membership.Id));
        var discrepancy = Assert.Single(await h.DiscrepanciesAsync());
        Assert.Equal(PaymentDiscrepancyType.ApprovedAfterTerminalState, discrepancy.Type);
        Assert.Equal("Cancelled", discrepancy.InternalStatus);
    }

    [Fact]
    public async Task Webhook_ApprovedAfterACashPaymentCoveredTheMembership_ConfirmsAnyway_AndRecordsOverpayment()
    {
        var (h, student, membership, checkout) = await CheckoutScenarioAsync();
        await using var _ = h;
        await h.RegisterManualAsync(student, 60000m, membership.Id);
        Assert.Equal(MembershipStatus.Active, await h.MembershipStatusAsync(membership.Id));

        h.Provider.SetPayment("MP-1", checkout.Id, checkout.Amount);
        await h.WebhookAsync("MP-1");

        // El dinero ya fue cobrado por el proveedor: el pago se confirma
        Assert.Equal(PaymentStatus.Completed, (await h.Service.GetByIdAsync(checkout.Id))!.Status);
        Assert.Equal(MembershipStatus.Active, await h.MembershipStatusAsync(membership.Id));
        Assert.Equal(1, await h.Db.MembershipCreditMovements.CountAsync(m => m.MembershipId == membership.Id));

        var discrepancy = Assert.Single(await h.DiscrepanciesAsync());
        Assert.Equal(PaymentDiscrepancyType.Overpayment, discrepancy.Type);
        Assert.Equal(checkout.Id, discrepancy.PaymentId);

        var balance = await h.Service.GetMembershipBalanceAsync(membership.Id);
        Assert.Equal(120000m, balance.TotalPaidAmount);
        Assert.Equal(0m, balance.PendingBalance);
    }

    [Fact]
    public async Task Webhook_SecondApprovedTransactionForAConfirmedPayment_RecordsDuplicateCharge()
    {
        var (h, _, _, checkout) = await CheckoutScenarioAsync();
        await using var _ = h;
        await h.ApproveByWebhookAsync(checkout, "MP-1");

        h.Provider.SetPayment("MP-2", checkout.Id, checkout.Amount);
        await h.WebhookAsync("MP-2");

        var payment = (await h.Service.GetByIdAsync(checkout.Id))!;
        Assert.Equal("MP-1", payment.ExternalTransactionId);
        var discrepancy = Assert.Single(await h.DiscrepanciesAsync());
        Assert.Equal(PaymentDiscrepancyType.DuplicateCharge, discrepancy.Type);
        Assert.Equal("MP-2", discrepancy.ExternalTransactionId);
    }

    [Fact]
    public async Task Webhook_RefundMadeOutsideTheSystem_RecordsExternalRefund_WithoutChangingThePayment()
    {
        var (h, _, _, checkout) = await CheckoutScenarioAsync();
        await using var _ = h;
        await h.ApproveByWebhookAsync(checkout, "MP-1");

        h.Provider.SetPayment("MP-1", checkout.Id, checkout.Amount, ExternalPaymentStatus.Refunded, refundedAmount: checkout.Amount);
        await h.WebhookAsync("MP-1");

        var payment = (await h.Service.GetByIdAsync(checkout.Id))!;
        Assert.Equal(PaymentStatus.Completed, payment.Status);
        Assert.Equal(0m, payment.RefundedAmount);
        var discrepancy = Assert.Single(await h.DiscrepanciesAsync());
        Assert.Equal(PaymentDiscrepancyType.ExternalRefund, discrepancy.Type);
        Assert.Equal(checkout.Amount, discrepancy.ExternalAmount);
    }

    [Fact]
    public async Task Webhook_ApprovedTransactionWithUnknownReference_RecordsUnknownPayment_Once()
    {
        await using var h = new PaymentGatewayHarness();
        h.Provider.SetPayment("MP-X", Guid.NewGuid(), 15000m);
        h.Provider.SetPayment("MP-Y", null, 100m, ExternalPaymentStatus.Rejected);

        await h.WebhookAsync("MP-X", eventId: "evt-1");
        await h.WebhookAsync("MP-X", eventId: "evt-2");
        await h.WebhookAsync("MP-Y", eventId: "evt-3");

        var discrepancy = Assert.Single(await h.DiscrepanciesAsync());
        Assert.Equal(PaymentDiscrepancyType.UnknownPayment, discrepancy.Type);
        Assert.Null(discrepancy.PaymentId);
        Assert.Equal("MP-X", discrepancy.ExternalTransactionId);
        Assert.Equal(15000m, discrepancy.ExternalAmount);
        Assert.Equal(3, await h.Db.PaymentWebhookEvents.CountAsync());
    }

    [Fact]
    public async Task Webhook_ForATransactionTheProviderDoesNotKnow_IsIgnored()
    {
        await using var h = new PaymentGatewayHarness();

        var result = await h.WebhookAsync("MP-GHOST", eventId: "evt-1");

        Assert.Equal(WebhookProcessingResult.Ignored, result);
        var evt = Assert.Single(await h.Db.PaymentWebhookEvents.AsNoTracking().ToListAsync());
        Assert.Equal(PaymentWebhookEventStatus.Ignored, evt.Status);
        Assert.Empty(await h.DiscrepanciesAsync());
    }

    // ---- Conciliación ----

    [Fact]
    public async Task Reconcile_PendingPaymentApprovedAtTheProvider_ConfirmsIt_Idempotently()
    {
        var (h, _, membership, checkout) = await CheckoutScenarioAsync();
        await using var _ = h;
        // El webhook se perdió: el pago aún no tiene transacción y se busca por su referencia
        h.Provider.SetPayment("MP-55", checkout.Id, checkout.Amount);

        var reconciled = await h.Service.ReconcileAsync(checkout.Id, h.StaffUserId);
        var again = await h.Service.ReconcileAsync(checkout.Id, h.StaffUserId);

        Assert.Equal(PaymentStatus.Completed, reconciled.Status);
        Assert.Equal("MP-55", reconciled.ExternalTransactionId);
        Assert.Equal(PaymentStatus.Completed, again.Status);
        Assert.Equal(MembershipStatus.Active, await h.MembershipStatusAsync(membership.Id));
        Assert.Equal(1, await h.Db.MembershipCreditMovements.CountAsync(m => m.MembershipId == membership.Id));
        Assert.Empty(await h.DiscrepanciesAsync());
        Assert.Equal(2, await h.Db.AuditLogs.CountAsync(a => a.Action == "PAYMENT_RECONCILED"));
    }

    [Fact]
    public async Task Reconcile_WithAmountMismatch_RecordsASingleOpenDiscrepancy_AcrossRepetitions()
    {
        var (h, _, _, checkout) = await CheckoutScenarioAsync(25000m);
        await using var _ = h;
        h.Provider.SetPayment("MP-1", checkout.Id, 20000m);

        await h.Service.ReconcileAsync(checkout.Id, h.StaffUserId);
        var result = await h.Service.ReconcileAsync(checkout.Id, h.StaffUserId);

        Assert.Equal(PaymentStatus.Pending, result.Status);
        Assert.Equal(25000m, result.Amount);
        var discrepancy = Assert.Single(await h.DiscrepanciesAsync());
        Assert.Equal(PaymentDiscrepancyType.AmountMismatch, discrepancy.Type);
        Assert.Equal(25000m, discrepancy.InternalAmount);
        Assert.Equal(20000m, discrepancy.ExternalAmount);
    }

    [Fact]
    public async Task Reconcile_WithNothingAtTheProvider_LeavesThePaymentUntouched()
    {
        var (h, _, _, checkout) = await CheckoutScenarioAsync();
        await using var _ = h;

        var result = await h.Service.ReconcileAsync(checkout.Id, h.StaffUserId);

        Assert.Equal(PaymentStatus.Pending, result.Status);
        Assert.Empty(await h.DiscrepanciesAsync());
    }

    [Fact]
    public async Task Reconcile_RejectsManualPayments_UnknownPayments_AndProviderFailures()
    {
        var (h, student, _, checkout) = await CheckoutScenarioAsync();
        await using var _ = h;
        var manual = await h.RegisterManualAsync(student, 5000m);

        await AssertConflictAsync(PaymentErrorCodes.PaymentNotExternal, () => h.Service.ReconcileAsync(manual.Id, h.StaffUserId));
        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.ReconcileAsync(Guid.NewGuid(), h.StaffUserId));

        h.Provider.LookupFailure = new HttpRequestException("timeout");
        await AssertExternalFailureAsync(PaymentErrorCodes.ProviderUnavailable, () => h.Service.ReconcileAsync(checkout.Id, h.StaffUserId));
    }

    // ---- Listado y resolución de discrepancias ----

    [Fact]
    public async Task Discrepancies_CanBeListedByStatus_AndResolvedOnceWithNotes()
    {
        var (h, _, _, checkout) = await CheckoutScenarioAsync();
        await using var _ = h;
        h.Provider.SetPayment("MP-1", checkout.Id, 1000m);
        await h.WebhookAsync("MP-1");
        h.Provider.SetPayment("MP-X", Guid.NewGuid(), 15000m);
        await h.WebhookAsync("MP-X");
        var adminId = Guid.NewGuid();

        var open = await h.Service.GetDiscrepanciesAsync(PaymentDiscrepancyStatus.Open, 1, 20);
        Assert.Equal(2, open.TotalCount);
        var amountMismatch = Assert.Single(open.Items, d => d.Type == PaymentDiscrepancyType.AmountMismatch);
        Assert.Equal(checkout.Id, amountMismatch.PaymentId);

        await Assert.ThrowsAsync<ValidationException>(() =>
            h.Service.ResolveDiscrepancyAsync(amountMismatch.Id, new ResolvePaymentDiscrepancyRequest("  "), adminId));

        var resolved = await h.Service.ResolveDiscrepancyAsync(
            amountMismatch.Id, new ResolvePaymentDiscrepancyRequest("Se devolvió desde el panel del proveedor"), adminId);

        Assert.Equal(PaymentDiscrepancyStatus.Resolved, resolved.Status);
        Assert.Equal("Se devolvió desde el panel del proveedor", resolved.ResolutionNotes);
        Assert.Equal(adminId, resolved.ResolvedByUserId);
        Assert.Equal(h.Calendar.UtcNow, resolved.ResolvedAtUtc);

        await AssertConflictAsync(PaymentErrorCodes.DiscrepancyAlreadyResolved, () =>
            h.Service.ResolveDiscrepancyAsync(amountMismatch.Id, new ResolvePaymentDiscrepancyRequest("Otra vez"), adminId));
        var notFound = await Assert.ThrowsAsync<NotFoundException>(() =>
            h.Service.ResolveDiscrepancyAsync(Guid.NewGuid(), new ResolvePaymentDiscrepancyRequest("Notas"), adminId));
        Assert.Equal(PaymentErrorCodes.DiscrepancyNotFound, notFound.Code);

        Assert.Equal(1, (await h.Service.GetDiscrepanciesAsync(PaymentDiscrepancyStatus.Open, 1, 20)).TotalCount);
        Assert.Equal(1, (await h.Service.GetDiscrepanciesAsync(PaymentDiscrepancyStatus.Resolved, 1, 20)).TotalCount);
        Assert.Equal(2, (await h.Service.GetDiscrepanciesAsync(null, 1, 1)).TotalCount);
        Assert.Contains(await h.Db.AuditLogs.ToListAsync(), a => a.Action == "PAYMENT_DISCREPANCY_RESOLVED" && a.UserId == adminId);

        // Resuelta la primera, la misma causa puede volver a registrarse
        await h.WebhookAsync("MP-1");
        Assert.Equal(2, (await h.Service.GetDiscrepanciesAsync(PaymentDiscrepancyStatus.Open, 1, 20)).TotalCount);
    }
}
