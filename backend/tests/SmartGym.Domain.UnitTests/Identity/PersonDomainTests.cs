using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;
using Xunit;

namespace SmartGym.Domain.UnitTests.Identity;

public class PersonDomainTests
{
    private static EmergencyContact CreateValidEmergencyContact() =>
        EmergencyContact.Create("Padre Tutor", "+54911111111", "Padre");

    [Fact]
    public void Create_WithValidData_SetsPropertiesAndInitialStatusActive()
    {
        var person = Person.Create("Juan", "Pérez", "  Juan.Perez@Example.com ");

        Assert.Equal("Juan", person.FirstName);
        Assert.Equal("Pérez", person.LastName);
        Assert.Equal("juan.perez@example.com", person.Email);
        Assert.Equal("juan perez", person.SearchName);
        Assert.Equal(PersonStatus.Active, person.Status);
        Assert.True(person.IsActive);
    }

    [Theory]
    [InlineData("", "Pérez")]
    [InlineData("   ", "Pérez")]
    [InlineData("Juan", "")]
    [InlineData("Juan", "   ")]
    public void Create_WithEmptyNameOrLastName_ThrowsArgumentException(string firstName, string lastName)
    {
        Assert.Throws<ArgumentException>(() => Person.Create(firstName, lastName));
    }

    [Fact]
    public void Create_MinorWithoutCompleteEmergencyContact_ThrowsInvalidOperationException()
    {
        var minorBirthDate = DateTime.UtcNow.AddYears(-15);

        Assert.Throws<InvalidOperationException>(() =>
            Person.Create("Carlitos", "Tevez", null, minorBirthDate));
    }

    [Fact]
    public void Create_MinorWithCompleteEmergencyContact_Succeeds()
    {
        var minorBirthDate = DateTime.UtcNow.AddYears(-15);
        var contact = CreateValidEmergencyContact();

        var person = Person.Create("Carlitos", "Tevez", null, minorBirthDate, emergencyContact: contact);

        Assert.True(person.IsUnderage());
        Assert.NotNull(person.EmergencyContact);
        Assert.True(person.EmergencyContact.IsComplete);
    }

    [Fact]
    public void UpdatePersonalData_UpdatesNamesAndRecalculatesSearchName()
    {
        var person = Person.Create("María", "Gómez");
        person.UpdatePersonalData("María José", "Gómez López", Gender.Female, null);

        Assert.Equal("María José", person.FirstName);
        Assert.Equal("Gómez López", person.LastName);
        Assert.Equal("maria jose gomez lopez", person.SearchName);
        Assert.Equal(Gender.Female, person.Gender);
    }

    [Fact]
    public void SetDocument_SetsDocumentCorrectly()
    {
        var person = Person.Create("Carlos", "García");
        var doc = IdentificationDocument.Create(DocumentType.Dni, "12.345.678");

        person.SetDocument(doc);

        Assert.NotNull(person.Document);
        Assert.Equal("12345678", person.Document.NormalizedNumber);
    }

    // Status transition matrix tests
    [Theory]
    [InlineData(PersonStatus.Active, PersonStatus.Active)]
    [InlineData(PersonStatus.Inactive, PersonStatus.Inactive)]
    [InlineData(PersonStatus.Blocked, PersonStatus.Blocked)]
    [InlineData(PersonStatus.Deceased, PersonStatus.Deceased)]
    public void ChangeStatus_SameState_ThrowsInvalidOperationException(PersonStatus current, PersonStatus target)
    {
        var person = Person.Create("Test", "User");
        if (current != PersonStatus.Active)
        {
            person.ChangeStatus(current, "Setup", Guid.NewGuid(), isAdmin: true);
        }

        Assert.Throws<InvalidOperationException>(() =>
            person.ChangeStatus(target, "Reason", Guid.NewGuid(), isAdmin: true));
    }

    [Fact]
    public void ChangeStatus_ActiveToBlocked_RequiresReason()
    {
        var person = Person.Create("Test", "User");

        Assert.Throws<ArgumentException>(() =>
            person.ChangeStatus(PersonStatus.Blocked, "", Guid.NewGuid(), isAdmin: false));

        person.ChangeStatus(PersonStatus.Blocked, "Conducta antideportiva", Guid.NewGuid(), isAdmin: false);
        Assert.Equal(PersonStatus.Blocked, person.Status);
        Assert.False(person.IsActive);
        Assert.Equal("Conducta antideportiva", person.StatusReason);
    }

    [Fact]
    public void ChangeStatus_ActiveToInactive_AndBackToActive_Succeeds()
    {
        var person = Person.Create("Test", "User");
        var userId = Guid.NewGuid();

        person.ChangeStatus(PersonStatus.Inactive, null, userId, isAdmin: false);
        Assert.Equal(PersonStatus.Inactive, person.Status);
        Assert.False(person.IsActive);
        Assert.Null(person.StatusReason);
        Assert.Equal(userId, person.StatusChangedByUserId);

        person.ChangeStatus(PersonStatus.Active, null, userId, isAdmin: false);
        Assert.Equal(PersonStatus.Active, person.Status);
        Assert.True(person.IsActive);
    }

    [Fact]
    public void ChangeStatus_ToDeceased_RequiresAdminAndReason()
    {
        var person = Person.Create("Test", "User");
        var adminId = Guid.NewGuid();

        // Non-admin rejected
        Assert.Throws<UnauthorizedAccessException>(() =>
            person.ChangeStatus(PersonStatus.Deceased, "Partida de defunción #123", adminId, isAdmin: false));

        // Missing reason rejected
        Assert.Throws<ArgumentException>(() =>
            person.ChangeStatus(PersonStatus.Deceased, "   ", adminId, isAdmin: true));

        // Admin with reason succeeds
        person.ChangeStatus(PersonStatus.Deceased, "Partida de defunción #123", adminId, isAdmin: true);
        Assert.Equal(PersonStatus.Deceased, person.Status);
        Assert.False(person.IsActive);
    }

    [Fact]
    public void ChangeStatus_FromDeceased_OnlyAdminCanRevertToActive()
    {
        var person = Person.Create("Test", "User");
        var adminId = Guid.NewGuid();
        person.ChangeStatus(PersonStatus.Deceased, "Error inicial", adminId, isAdmin: true);

        // Cannot transition from Deceased to Inactive or Blocked
        Assert.Throws<InvalidOperationException>(() =>
            person.ChangeStatus(PersonStatus.Inactive, "Error", adminId, isAdmin: true));
        Assert.Throws<InvalidOperationException>(() =>
            person.ChangeStatus(PersonStatus.Blocked, "Error", adminId, isAdmin: true));

        // Non-admin cannot revert to Active
        Assert.Throws<UnauthorizedAccessException>(() =>
            person.ChangeStatus(PersonStatus.Active, "Corrección", adminId, isAdmin: false));

        // Admin without reason rejected
        Assert.Throws<ArgumentException>(() =>
            person.ChangeStatus(PersonStatus.Active, null, adminId, isAdmin: true));

        // Admin with reason succeeds
        person.ChangeStatus(PersonStatus.Active, "Corrección de error de carga", adminId, isAdmin: true);
        Assert.Equal(PersonStatus.Active, person.Status);
        Assert.True(person.IsActive);
    }

    [Fact]
    public void DeceasedPerson_IsReadOnly()
    {
        var person = Person.Create("Test", "User");
        person.ChangeStatus(PersonStatus.Deceased, "Defunción", Guid.NewGuid(), isAdmin: true);

        Assert.Throws<InvalidOperationException>(() =>
            person.UpdatePersonalData("Nuevo", "Nombre", null, null));

        Assert.Throws<InvalidOperationException>(() =>
            person.UpdateContact(null, null, null, null));

        Assert.Throws<InvalidOperationException>(() =>
            person.SetDocument(IdentificationDocument.Create(DocumentType.Dni, "12345678")));

        Assert.Throws<InvalidOperationException>(() =>
            person.SetProfileImage(ProfileImage.Create("key", "image/webp", 100)));

        Assert.Throws<InvalidOperationException>(() =>
            person.ClearProfileImage());
    }
}
