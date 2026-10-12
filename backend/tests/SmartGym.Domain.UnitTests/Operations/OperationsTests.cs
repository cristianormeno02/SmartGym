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
