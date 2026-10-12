using SmartGym.Domain.Common;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Operations;
using Xunit;

namespace SmartGym.Domain.UnitTests.Operations;

public class OperationsTests
{
    [Fact]
    public void Payment_ShouldInitializeAndCompleteSuccessfully_EmittingPaymentCompletedEventOnce()
    {
        // Arrange
        var payerId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            PayerPersonId = payerId,
            MembershipId = membershipId,
            Amount = 35000.00m,
            Method = PaymentMethod.DigitalWallet,
            Status = PaymentStatus.Pending,
            PaidAtUtc = DateTime.UtcNow
        };

        // Act
        payment.Complete("MP-987654321");

        // Assert
        Assert.IsAssignableFrom<IHasDomainEvents>(payment);
        Assert.Equal(PaymentStatus.Completed, payment.Status);
        Assert.Equal("MP-987654321", payment.ExternalTransactionId);

        var evt = Assert.Single(payment.DomainEvents.OfType<PaymentCompletedEvent>());
        Assert.Equal(payment.Id, evt.PaymentId);
        Assert.Equal(payerId, evt.PayerPersonId);
        Assert.Equal(membershipId, evt.MembershipId);
        Assert.Equal(35000.00m, evt.Amount);
    }

    [Fact]
    public void Payment_Fail_FromPending_ShouldTransitionToFailed_AndAppendReasonToNotes()
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            PayerPersonId = Guid.NewGuid(),
            Amount = 20000m,
            Method = PaymentMethod.BankTransfer,
            Status = PaymentStatus.Pending
        };

        payment.Fail("Transferencia rechazada por el banco");

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Contains("Transferencia rechazada por el banco", payment.Notes);
        Assert.Empty(payment.DomainEvents.OfType<PaymentCompletedEvent>());
        Assert.Single(payment.DomainEvents.OfType<PaymentFailedNotificationEvent>());
    }

    [Theory]
    [InlineData(PaymentStatus.Completed)]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Refunded)]
    [InlineData(PaymentStatus.Cancelled)]
    [InlineData(PaymentStatus.PartiallyRefunded)]
    public void Payment_Fail_FromNonPendingStatus_ShouldThrowInvalidOperationException(PaymentStatus currentStatus)
    {
        var payment = NewPayment(currentStatus);

        Assert.Throws<InvalidOperationException>(() => payment.Fail("Rechazo inválido"));
    }

    private static Payment NewPayment(PaymentStatus status, decimal amount = 20000m) => new()
    {
        Id = Guid.NewGuid(),
        PayerPersonId = Guid.NewGuid(),
        Amount = amount,
        Method = PaymentMethod.Cash,
        Status = status
    };

    private static Payment NewCompletedPayment(decimal amount = 30000m)
    {
        var payment = NewPayment(PaymentStatus.Pending, amount);
        payment.Complete();
        payment.ClearDomainEvents();
        return payment;
    }

    [Fact]
    public void PaymentEnums_ShouldSeparateMethodFromProvider_AndExposeNewStatuses()
    {
        Assert.Equal(5, (int)PaymentMethod.DigitalWallet);
        Assert.Equal(6, (int)PaymentMethod.Other);
        Assert.Equal(1, (int)PaymentProvider.Manual);
        Assert.Equal(5, (int)PaymentStatus.Cancelled);
        Assert.Equal(6, (int)PaymentStatus.PartiallyRefunded);
        Assert.Equal(21, (int)NotificationType.PaymentFailed);
        Assert.Equal(22, (int)NotificationType.PaymentRefunded);
    }

    [Fact]
    public void Payment_Defaults_ShouldBeManualProviderWithZeroRefunded()
    {
        var payment = NewPayment(PaymentStatus.Pending, 25000m);

        Assert.Equal(PaymentProvider.Manual, payment.Provider);
        Assert.Equal("ARS", payment.Currency);
        Assert.Equal(0m, payment.RefundedAmount);
        Assert.Equal(25000m, payment.NetAmount);
        Assert.False(payment.IsConfirmed);
    }

    [Fact]
    public void Payment_Complete_FromPending_ShouldRegisterConfirmedAtUtc_AndEmitNotificationEvent()
    {
        var payment = NewPayment(PaymentStatus.Pending);
        var now = new DateTime(2026, 10, 12, 15, 0, 0, DateTimeKind.Utc);

        payment.Complete(nowUtc: now);

        Assert.Equal(PaymentStatus.Completed, payment.Status);
        Assert.Equal(now, payment.ConfirmedAtUtc);
        Assert.True(payment.IsConfirmed);
        Assert.Single(payment.DomainEvents.OfType<PaymentCompletedEvent>());
        Assert.Single(payment.DomainEvents.OfType<PaymentConfirmedNotificationEvent>());
    }

    [Theory]
    [InlineData(PaymentStatus.Completed)]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Refunded)]
    [InlineData(PaymentStatus.Cancelled)]
    [InlineData(PaymentStatus.PartiallyRefunded)]
    public void Payment_Complete_FromNonPendingStatus_ShouldThrow(PaymentStatus currentStatus)
    {
        var payment = NewPayment(currentStatus);

        Assert.Throws<InvalidOperationException>(() => payment.Complete());
        Assert.Empty(payment.DomainEvents);
    }

    [Fact]
    public void Payment_Cancel_FromPending_ShouldTransitionToCancelled_AndRegisterTimestampAndReason()
    {
        var payment = NewPayment(PaymentStatus.Pending);
        var now = new DateTime(2026, 10, 12, 15, 0, 0, DateTimeKind.Utc);

        payment.Cancel("El alumno desistió", now);

        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        Assert.Equal(now, payment.CancelledAtUtc);
        Assert.Contains("El alumno desistió", payment.Notes);
        Assert.Empty(payment.DomainEvents);
    }

    [Theory]
    [InlineData(PaymentStatus.Completed)]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Refunded)]
    [InlineData(PaymentStatus.Cancelled)]
    [InlineData(PaymentStatus.PartiallyRefunded)]
    public void Payment_Cancel_FromNonPendingStatus_ShouldThrow(PaymentStatus currentStatus)
    {
        var payment = NewPayment(currentStatus);

        Assert.Throws<InvalidOperationException>(() => payment.Cancel("Motivo"));
    }

    [Fact]
    public void Payment_RegisterRefund_Partial_ShouldKeepAmount_AndTransitionToPartiallyRefunded()
    {
        var payment = NewCompletedPayment(30000m);
        var adminId = Guid.NewGuid();

        var refund = payment.RegisterRefund(10000m, "Descuento comercial post-venta", adminId);

        Assert.Equal(30000m, payment.Amount);
        Assert.Equal(10000m, payment.RefundedAmount);
        Assert.Equal(20000m, payment.NetAmount);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.True(payment.IsConfirmed);

        Assert.Equal(payment.Id, refund.PaymentId);
        Assert.Equal(10000m, refund.Amount);
        Assert.Equal(payment.Currency, refund.Currency);
        Assert.Equal("Descuento comercial post-venta", refund.Reason);
        Assert.Equal(adminId, refund.RequestedByUserId);
        Assert.Same(refund, Assert.Single(payment.Refunds));

        var evt = Assert.Single(payment.DomainEvents.OfType<PaymentRefundedNotificationEvent>());
        Assert.Equal(10000m, evt.RefundAmount);
        Assert.Equal(refund.Id, evt.RefundId);
    }

    [Fact]
    public void Payment_RegisterRefund_ForRemainingBalance_ShouldTransitionToRefunded()
    {
        var payment = NewCompletedPayment(30000m);

        payment.RegisterRefund(10000m, "Primera devolución", null);
        payment.RegisterRefund(20000m, "Saldo restante", null);

        Assert.Equal(30000m, payment.RefundedAmount);
        Assert.Equal(0m, payment.NetAmount);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(2, payment.Refunds.Count);
    }

    [Fact]
    public void Payment_RegisterRefund_ExceedingAvailable_ShouldThrow_AndKeepState()
    {
        var payment = NewCompletedPayment(20000m);
        payment.RegisterRefund(5000m, "Parcial", null);

        Assert.Throws<InvalidOperationException>(() => payment.RegisterRefund(15000.01m, "Excede", null));

        Assert.Equal(5000m, payment.RefundedAmount);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Single(payment.Refunds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Payment_RegisterRefund_WithNonPositiveAmount_ShouldThrow(decimal amount)
    {
        var payment = NewCompletedPayment();

        Assert.Throws<ArgumentException>(() => payment.RegisterRefund(amount, "Motivo", null));
    }

    [Fact]
    public void Payment_RegisterRefund_WithoutReason_ShouldThrow()
    {
        var payment = NewCompletedPayment();

        Assert.Throws<ArgumentException>(() => payment.RegisterRefund(1000m, "  ", null));
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Cancelled)]
    [InlineData(PaymentStatus.Refunded)]
    public void Payment_RegisterRefund_OnNonRefundableStatus_ShouldThrow(PaymentStatus currentStatus)
    {
        var payment = NewPayment(currentStatus);

        Assert.Throws<InvalidOperationException>(() => payment.RegisterRefund(1000m, "Motivo", null));
    }

    [Fact]
    public void Payment_ApplyToMembership_WhenCompleted_ShouldLinkAndEmitPaymentAppliedEvent()
    {
        var payment = NewCompletedPayment(25000m);
        var membershipId = Guid.NewGuid();
        var staffId = Guid.NewGuid();

        payment.ApplyToMembership(membershipId, staffId);

        Assert.Equal(membershipId, payment.MembershipId);
        var evt = Assert.Single(payment.DomainEvents.OfType<PaymentAppliedEvent>());
        Assert.Equal(payment.Id, evt.PaymentId);
        Assert.Equal(membershipId, evt.MembershipId);
        Assert.Equal(25000m, evt.Amount);
        Assert.Equal(staffId, evt.AppliedByUserId);
    }

    [Fact]
    public void Payment_ApplyToMembership_WhenPending_ShouldLinkWithoutEmittingEvent()
    {
        var payment = NewPayment(PaymentStatus.Pending);
        var membershipId = Guid.NewGuid();

        payment.ApplyToMembership(membershipId, null);

        Assert.Equal(membershipId, payment.MembershipId);
        Assert.Empty(payment.DomainEvents);
    }

    [Fact]
    public void Payment_ApplyToClassSession_ShouldLinkWithoutEmittingEvent()
    {
        var payment = NewCompletedPayment();
        var sessionId = Guid.NewGuid();

        payment.ApplyToClassSession(sessionId);

        Assert.Equal(sessionId, payment.ClassSessionId);
        Assert.Empty(payment.DomainEvents);
    }

    [Fact]
    public void Payment_Apply_WhenAlreadyApplied_ShouldThrow()
    {
        var applied = NewCompletedPayment();
        applied.ApplyToMembership(Guid.NewGuid(), null);

        Assert.Throws<InvalidOperationException>(() => applied.ApplyToMembership(Guid.NewGuid(), null));
        Assert.Throws<InvalidOperationException>(() => applied.ApplyToClassSession(Guid.NewGuid()));
    }

    [Theory]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Cancelled)]
    [InlineData(PaymentStatus.Refunded)]
    [InlineData(PaymentStatus.PartiallyRefunded)]
    public void Payment_Apply_OnStatusOtherThanPendingOrCompleted_ShouldThrow(PaymentStatus currentStatus)
    {
        var payment = NewPayment(currentStatus);

        Assert.Throws<InvalidOperationException>(() => payment.ApplyToMembership(Guid.NewGuid(), null));
    }

    // ---- Pasarela externa ----

    private static Payment NewExternalPayment(PaymentStatus status = PaymentStatus.Pending, decimal amount = 40000m)
    {
        var payment = NewPayment(status, amount);
        payment.Provider = PaymentProvider.MercadoPago;
        payment.MembershipId = Guid.NewGuid();
        return payment;
    }

    [Fact]
    public void GatewayEnums_ShouldBeDefined()
    {
        Assert.Equal(2, (int)PaymentProvider.MercadoPago);
        Assert.Equal(1, (int)PaymentRefundStatus.Pending);
        Assert.Equal(2, (int)PaymentRefundStatus.Completed);
        Assert.Equal(3, (int)PaymentRefundStatus.Failed);
        Assert.Equal(1, (int)PaymentDiscrepancyStatus.Open);
        Assert.Equal(2, (int)PaymentDiscrepancyStatus.Resolved);
        Assert.Equal(1, (int)PaymentWebhookEventStatus.Received);
        Assert.Equal(2, (int)PaymentWebhookEventStatus.Processed);
        Assert.Equal(3, (int)PaymentWebhookEventStatus.Ignored);
        Assert.Equal(
            new[] { "AmountMismatch", "CurrencyMismatch", "ApprovedAfterTerminalState", "Overpayment", "DuplicateCharge", "ExternalRefund", "UnknownPayment" },
            Enum.GetNames<PaymentDiscrepancyType>());
    }

    [Fact]
    public void Payment_IsExternal_AndCheckoutExpiration()
    {
        var now = new DateTime(2026, 10, 12, 15, 0, 0, DateTimeKind.Utc);
        var manual = NewPayment(PaymentStatus.Pending);
        var external = NewExternalPayment();
        external.ExpiresAtUtc = now.AddMinutes(60);

        Assert.False(manual.IsExternal);
        Assert.False(manual.IsCheckoutExpired(now));
        Assert.True(external.IsExternal);
        Assert.False(external.IsCheckoutExpired(now));
        Assert.True(external.IsCheckoutExpired(now.AddMinutes(60)));

        // Sólo un checkout pendiente puede vencer
        external.Complete("MP-1", now);
        Assert.False(external.IsCheckoutExpired(now.AddDays(1)));
    }

    [Fact]
    public void Payment_RegisterRefund_OnManualPayment_CreatesCompletedRefund()
    {
        var payment = NewCompletedPayment(30000m);
        var now = new DateTime(2026, 10, 12, 15, 0, 0, DateTimeKind.Utc);

        var refund = payment.RegisterRefund(10000m, "Ajuste", null, nowUtc: now);

        Assert.Equal(PaymentRefundStatus.Completed, refund.Status);
        Assert.Equal(now, refund.CompletedAtUtc);
        Assert.Null(refund.ExternalRefundId);
    }

    [Fact]
    public void Payment_RequestRefund_CreatesPendingRefund_WithoutTouchingThePayment()
    {
        var payment = NewExternalPayment();
        payment.Complete("MP-1");
        payment.ClearDomainEvents();

        var refund = payment.RequestRefund(10000m, "Ajuste", Guid.NewGuid(), pendingRefundsAmount: 0m);

        Assert.Equal(PaymentRefundStatus.Pending, refund.Status);
        Assert.Null(refund.CompletedAtUtc);
        Assert.Equal(0m, payment.RefundedAmount);
        Assert.Equal(PaymentStatus.Completed, payment.Status);
        Assert.Empty(payment.DomainEvents);
    }

    [Fact]
    public void Payment_RequestRefund_ReservesPendingRefunds()
    {
        var payment = NewExternalPayment(amount: 20000m);
        payment.Complete("MP-1");

        payment.RequestRefund(15000m, "Primera", null, pendingRefundsAmount: 0m);

        // Disponible: 20000 - 0 devueltos - 15000 pendientes = 5000
        Assert.Throws<InvalidOperationException>(() =>
            payment.RequestRefund(5000.01m, "Excede", null, pendingRefundsAmount: 15000m));
        var fits = payment.RequestRefund(5000m, "Cabe", null, pendingRefundsAmount: 15000m);
        Assert.Equal(PaymentRefundStatus.Pending, fits.Status);
    }

    [Fact]
    public void Payment_CompleteRefund_AppliesAmountStatusAndNotification()
    {
        var payment = NewExternalPayment(amount: 40000m);
        payment.Complete("MP-1");
        var refund = payment.RequestRefund(10000m, "Ajuste", null, pendingRefundsAmount: 0m);
        payment.ClearDomainEvents();
        var now = new DateTime(2026, 10, 12, 16, 0, 0, DateTimeKind.Utc);

        payment.CompleteRefund(refund, "MP-REF-9", now);

        Assert.Equal(PaymentRefundStatus.Completed, refund.Status);
        Assert.Equal("MP-REF-9", refund.ExternalRefundId);
        Assert.Equal(now, refund.CompletedAtUtc);
        Assert.Equal(10000m, payment.RefundedAmount);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        var evt = Assert.Single(payment.DomainEvents.OfType<PaymentRefundedNotificationEvent>());
        Assert.Equal(refund.Id, evt.RefundId);

        // Una devolución ya resuelta no se vuelve a aplicar
        Assert.Throws<InvalidOperationException>(() => payment.CompleteRefund(refund, "MP-REF-9", now));
        Assert.Equal(10000m, payment.RefundedAmount);
    }

    [Fact]
    public void PaymentRefund_Fail_KeepsThePaymentUntouched_AndIsTerminal()
    {
        var payment = NewExternalPayment();
        payment.Complete("MP-1");
        var refund = payment.RequestRefund(10000m, "Ajuste", null, pendingRefundsAmount: 0m);

        refund.Fail("El proveedor rechazó el reintegro", new DateTime(2026, 10, 12, 16, 0, 0, DateTimeKind.Utc));

        Assert.Equal(PaymentRefundStatus.Failed, refund.Status);
        Assert.Equal("El proveedor rechazó el reintegro", refund.FailureReason);
        Assert.Equal(0m, payment.RefundedAmount);
        Assert.Throws<InvalidOperationException>(() => refund.Fail("Otra vez"));
        Assert.Throws<InvalidOperationException>(() => payment.CompleteRefund(refund, "MP-REF-1"));
    }

    [Fact]
    public void PaymentDiscrepancy_Create_CapturesInternalAndExternalValues()
    {
        var payment = NewExternalPayment(amount: 25000m);
        var now = new DateTime(2026, 10, 12, 15, 0, 0, DateTimeKind.Utc);

        var discrepancy = PaymentDiscrepancy.Create(
            PaymentDiscrepancyType.AmountMismatch, PaymentProvider.MercadoPago, payment, "MP-77", 20000m, "ARS", "Approved", now);

        Assert.Equal(PaymentDiscrepancyStatus.Open, discrepancy.Status);
        Assert.Equal(payment.Id, discrepancy.PaymentId);
        Assert.Equal(25000m, discrepancy.InternalAmount);
        Assert.Equal("ARS", discrepancy.InternalCurrency);
        Assert.Equal("Pending", discrepancy.InternalStatus);
        Assert.Equal(20000m, discrepancy.ExternalAmount);
        Assert.Equal("Approved", discrepancy.ExternalStatus);
        Assert.Equal("MP-77", discrepancy.ExternalTransactionId);
        Assert.Equal(now, discrepancy.DetectedAtUtc);
    }

    [Fact]
    public void PaymentDiscrepancy_Resolve_RequiresNotes_AndRejectsASecondResolution()
    {
        var discrepancy = PaymentDiscrepancy.Create(
            PaymentDiscrepancyType.UnknownPayment, PaymentProvider.MercadoPago, null, "MP-88", 1000m, "ARS", "Approved");
        var adminId = Guid.NewGuid();
        var now = new DateTime(2026, 10, 12, 17, 0, 0, DateTimeKind.Utc);

        Assert.Null(discrepancy.PaymentId);
        Assert.Throws<ArgumentException>(() => discrepancy.Resolve("  ", adminId, now));

        discrepancy.Resolve("Devuelto desde el panel del proveedor", adminId, now);

        Assert.Equal(PaymentDiscrepancyStatus.Resolved, discrepancy.Status);
        Assert.Equal("Devuelto desde el panel del proveedor", discrepancy.ResolutionNotes);
        Assert.Equal(adminId, discrepancy.ResolvedByUserId);
        Assert.Equal(now, discrepancy.ResolvedAtUtc);
        Assert.Throws<InvalidOperationException>(() => discrepancy.Resolve("Otra nota", adminId, now));
    }

    [Fact]
    public void NewEnumValues_ForCreditMovementTypeAndNotificationType_ShouldBeDefined()
    {
        Assert.Equal(7, (int)CreditMovementType.PromotionBonus);
        Assert.Equal(8, (int)CreditMovementType.CancellationForfeiture);
        Assert.Equal(9, (int)CreditMovementType.Expiration);

        Assert.Equal(6, (int)NotificationType.MembershipActivated);
        Assert.Equal(7, (int)NotificationType.MembershipSuspended);
        Assert.Equal(8, (int)NotificationType.MembershipExpired);
    }

    [Fact]
    public void Person_MedicalCertificate_ShouldValidateExpirationCorrectly()
    {
        // Arrange
        var person = Person.Create("Carlos", "Tevez", birthDate: new DateTime(1984, 2, 5));

        // Act & Assert (Sin certificado aún)
        Assert.False(person.IsMedicalCertificateValid);

        // Actualizamos certificado médico válido por 6 meses
        person.UpdateMedicalCertificate("https://s3.smartgym.com/med/cert1.pdf", DateTime.UtcNow.AddMonths(6));
        Assert.True(person.IsMedicalCertificateValid);

        // Certificado médico vencido
        person.UpdateMedicalCertificate("https://s3.smartgym.com/med/cert1.pdf", DateTime.UtcNow.AddDays(-1));
        Assert.False(person.IsMedicalCertificateValid);
    }

    [Fact]
    public void Person_Underage_ShouldValidateEmergencyContact()
    {
        // Arrange & Act
        var minor = Person.Create(
            "Lucas",
            "Menor",
            birthDate: DateTime.UtcNow.AddYears(-15),
            emergencyContact: EmergencyContact.Create("Padre Responsable", "+5491123456789", "Padre"));

        // Assert
        Assert.True(minor.IsUnderage());
        Assert.True(minor.HasValidEmergencyContact());
        Assert.Equal("Padre Responsable", minor.EmergencyContactName);
        Assert.Equal("+5491123456789", minor.EmergencyContactPhone);
        Assert.Equal("Padre", minor.EmergencyContactRelationship);
    }
}
