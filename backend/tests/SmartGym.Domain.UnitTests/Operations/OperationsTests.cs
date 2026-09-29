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
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Carlos",
            LastName = "Tevez",
            BirthDate = new DateTime(1984, 2, 5)
        };

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
        // Arrange
        var minor = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Lucas",
            LastName = "Menor",
            BirthDate = DateTime.UtcNow.AddYears(-15) // 15 años
        };

        // Assert
        Assert.True(minor.IsUnderage());
        Assert.False(minor.HasValidEmergencyContact());

        // Act
        minor.EmergencyContactName = "Padre Responsable";
        minor.EmergencyContactPhone = "+5491123456789";
        minor.EmergencyContactRelationship = "Padre";

        // Assert
        Assert.True(minor.HasValidEmergencyContact());
    }
}
