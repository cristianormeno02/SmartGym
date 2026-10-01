using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;
using Xunit;

namespace SmartGym.Domain.UnitTests.Operations;

public class OperationsTests
{
    [Fact]
    public void Payment_ShouldInitializeAndCompleteSuccessfully()
    {
        // Arrange
        var payerId = Guid.NewGuid();
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            PayerPersonId = payerId,
            Amount = 35000.00m,
            Method = PaymentMethod.MercadoPago,
            Status = PaymentStatus.Pending,
            PaidAtUtc = DateTime.UtcNow
        };

        // Act
        payment.Complete("MP-987654321");

        // Assert
        Assert.Equal(PaymentStatus.Completed, payment.Status);
        Assert.Equal("MP-987654321", payment.ExternalTransactionId);
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
