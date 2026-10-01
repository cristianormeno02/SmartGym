using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;
using Xunit;

namespace SmartGym.Domain.UnitTests.Identity;

public class IdentificationDocumentTests
{
    [Theory]
    [InlineData("12.345.678", "12345678")]
    [InlineData("12-345-678", "12345678")]
    [InlineData(" 12 345 678 ", "12345678")]
    [InlineData("0012345678", "12345678")]
    [InlineData("1", "1")]
    [InlineData("123456789", "123456789")]
    public void Create_Dni_NormalizesCorrectlyAndForcesCountryAR(string inputNumber, string expectedNormalized)
    {
        // Act
        var doc = IdentificationDocument.Create(DocumentType.Dni, inputNumber, "US");

        // Assert
        Assert.Equal(DocumentType.Dni, doc.Type);
        Assert.Equal("AR", doc.IssuingCountry);
        Assert.Equal(inputNumber.Trim(), doc.Number);
        Assert.Equal(expectedNormalized, doc.NormalizedNumber);
    }

    [Theory]
    [InlineData("1234A567")]
    [InlineData("12.345.678X")]
    [InlineData("DNI123")]
    public void Create_Dni_RejectsLetters(string inputWithLetters)
    {
        Assert.Throws<ArgumentException>(() => IdentificationDocument.Create(DocumentType.Dni, inputWithLetters));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("0000")]
    [InlineData("1234567890")] // 10 digits > 9
    public void Create_Dni_RejectsEmptyZeroOrInvalidLength(string invalidInput)
    {
        Assert.Throws<ArgumentException>(() => IdentificationDocument.Create(DocumentType.Dni, invalidInput));
    }

    [Theory]
    [InlineData(DocumentType.Passport, " a-123.456/x ", "UY", "A123456X", "UY")]
    [InlineData(DocumentType.IdentityCard, "ci-987654", "cl", "CI987654", "CL")]
    [InlineData(DocumentType.Other, "LC 123 456", "ar", "LC123456", "AR")]
    public void Create_OtherTypes_NormalizesUppercaseAlphanumericAndRequiresCountry(
        DocumentType type, string number, string country, string expectedNormalized, string expectedCountry)
    {
        var doc = IdentificationDocument.Create(type, number, country);

        Assert.Equal(type, doc.Type);
        Assert.Equal(expectedCountry, doc.IssuingCountry);
        Assert.Equal(number.Trim(), doc.Number);
        Assert.Equal(expectedNormalized, doc.NormalizedNumber);
    }

    [Theory]
    [InlineData(DocumentType.Passport, "")]
    [InlineData(DocumentType.IdentityCard, null)]
    [InlineData(DocumentType.Other, "   ")]
    [InlineData(DocumentType.Passport, "ARG")] // 3 chars instead of 2
    [InlineData(DocumentType.Passport, "A")] // 1 char
    public void Create_NonDni_RequiresValidTwoLetterIsoCountry(DocumentType type, string? invalidCountry)
    {
        Assert.Throws<ArgumentException>(() => IdentificationDocument.Create(type, "AB12345", invalidCountry!));
    }

    [Theory]
    [InlineData(DocumentType.Passport, "AB")] // 2 chars < 3
    [InlineData(DocumentType.Passport, "1234567890123456789012345678901")] // 31 chars > 30
    [InlineData(DocumentType.Passport, ".-/ ")] // empty after normalization
    public void Create_NonDni_RejectsInvalidLength(DocumentType type, string invalidNumber)
    {
        Assert.Throws<ArgumentException>(() => IdentificationDocument.Create(type, invalidNumber, "US"));
    }
}
