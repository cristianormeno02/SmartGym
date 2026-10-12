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
            Method = PaymentMethod.MercadoPago,
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
        Assert.Empty(payment.DomainEvents);
    }

    [Theory]
    [InlineData(PaymentStatus.Completed)]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Refunded)]
    public void Payment_Fail_FromNonPendingStatus_ShouldThrowInvalidOperationException(PaymentStatus currentStatus)
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            PayerPersonId = Guid.NewGuid(),
            Amount = 20000m,
            Method = PaymentMethod.Cash,
            Status = currentStatus
        };

        Assert.Throws<InvalidOperationException>(() => payment.Fail("Rechazo inválido"));
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
