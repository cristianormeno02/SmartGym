using FluentValidation.TestHelper;
using SmartGym.Application.Modules.People.Dtos;
using SmartGym.Application.Modules.People.Validators;
using SmartGym.Domain.Enums;
using Xunit;

namespace SmartGym.Domain.UnitTests.People;

public class PeopleValidatorTests
{
    private readonly CreatePersonRequestValidator _createValidator = new();
    private readonly ChangePersonStatusRequestValidator _statusValidator = new();
    private readonly UpdateOwnContactRequestValidator _ownContactValidator = new();

    [Fact]
    public void CreatePerson_MinimalValid_PassesValidation()
    {
        var request = new CreatePersonRequest("Juan", "Pérez");
        var result = _createValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("", "Pérez")]
    [InlineData("   ", "Pérez")]
    [InlineData("Juan", "")]
    [InlineData("Juan", "   ")]
    public void CreatePerson_EmptyNames_FailsValidation(string firstName, string lastName)
    {
        var request = new CreatePersonRequest(firstName, lastName);
        var result = _createValidator.TestValidate(request);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void CreatePerson_InvalidEmail_FailsValidation()
    {
        var request = new CreatePersonRequest("Juan", "Pérez", Email: "not-an-email");
        var result = _createValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void CreatePerson_ValidDni_PassesValidation()
    {
        var request = new CreatePersonRequest(
            "Juan",
            "Pérez",
            DocumentType: DocumentType.Dni,
            DocumentNumber: "12.345.678"
        );
        var result = _createValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void CreatePerson_DniWithLetters_FailsValidation()
    {
        var request = new CreatePersonRequest(
            "Juan",
            "Pérez",
            DocumentType: DocumentType.Dni,
            DocumentNumber: "12345678A"
        );
        var result = _createValidator.TestValidate(request);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void CreatePerson_PassportWithoutCountry_FailsValidation()
    {
        var request = new CreatePersonRequest(
            "Juan",
            "Pérez",
            DocumentType: DocumentType.Passport,
            DocumentNumber: "ABC123456"
        );
        var result = _createValidator.TestValidate(request);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void CreatePerson_PassportWithCountry_PassesValidation()
    {
        var request = new CreatePersonRequest(
            "Juan",
            "Pérez",
            DocumentType: DocumentType.Passport,
            DocumentNumber: "ABC123456",
            DocumentIssuingCountry: "ES"
        );
        var result = _createValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void CreatePerson_FutureBirthDate_FailsValidation()
    {
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));
        var request = new CreatePersonRequest("Juan", "Pérez", BirthDate: futureDate);
        var result = _createValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.BirthDate);
    }

    [Fact]
    public void CreatePerson_MinorWithoutEmergencyContact_FailsValidation()
    {
        var minorDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-15));
        var request = new CreatePersonRequest("Juan", "Pérez", BirthDate: minorDate);
        var result = _createValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.EmergencyContact);
    }

    [Fact]
    public void CreatePerson_MinorWithCompleteEmergencyContact_PassesValidation()
    {
        var minorDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-15));
        var request = new CreatePersonRequest(
            "Juan",
            "Pérez",
            BirthDate: minorDate,
            EmergencyContact: new EmergencyContactDto("María Pérez", "1122334455", "Madre")
        );
        var result = _createValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void ChangeStatus_BlockedWithoutReason_FailsValidation()
    {
        var request = new ChangePersonStatusRequest(PersonStatus.Blocked, null);
        var result = _statusValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void ChangeStatus_BlockedWithReason_PassesValidation()
    {
        var request = new ChangePersonStatusRequest(PersonStatus.Blocked, "Falta disciplinaria grave");
        var result = _statusValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void ChangeStatus_DeceasedWithoutReason_FailsValidation()
    {
        var request = new ChangePersonStatusRequest(PersonStatus.Deceased, "");
        var result = _statusValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void ChangeStatus_InactiveWithoutReason_PassesValidation()
    {
        var request = new ChangePersonStatusRequest(PersonStatus.Inactive, null);
        var result = _statusValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
