using System.Text.Json;
using System.Text.Json.Serialization;
using SmartGym.Application.Modules.People.Dtos;
using SmartGym.Domain.Enums;
using Xunit;

namespace SmartGym.Domain.UnitTests.Enums;

public class PeopleEnumSerializationTests
{
    // Mismas opciones que usa ASP.NET Core por defecto (sin converters adicionales):
    // el contrato en español debe salir de los propios tipos, no de una configuración externa.
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(DocumentType.Dni, "\"DNI\"")]
    [InlineData(DocumentType.Passport, "\"PASAPORTE\"")]
    [InlineData(DocumentType.IdentityCard, "\"CI\"")]
    [InlineData(DocumentType.Other, "\"OTRO\"")]
    public void DocumentType_SerializesAndDeserializes_Correctly(DocumentType type, string expectedJson)
    {
        var json = JsonSerializer.Serialize(type, _jsonOptions);
        Assert.Equal(expectedJson, json);

        var deserialized = JsonSerializer.Deserialize<DocumentType>(expectedJson, _jsonOptions);
        Assert.Equal(type, deserialized);
    }

    [Theory]
    [InlineData(Gender.Male, "\"MASCULINO\"")]
    [InlineData(Gender.Female, "\"FEMENINO\"")]
    [InlineData(Gender.X, "\"X\"")]
    [InlineData(Gender.NotInformed, "\"NO_INFORMA\"")]
    public void Gender_SerializesAndDeserializes_Correctly(Gender gender, string expectedJson)
    {
        var json = JsonSerializer.Serialize(gender, _jsonOptions);
        Assert.Equal(expectedJson, json);

        var deserialized = JsonSerializer.Deserialize<Gender>(expectedJson, _jsonOptions);
        Assert.Equal(gender, deserialized);
    }

    [Theory]
    [InlineData(PersonStatus.Active, "\"ACTIVA\"")]
    [InlineData(PersonStatus.Inactive, "\"INACTIVA\"")]
    [InlineData(PersonStatus.Blocked, "\"BLOQUEADA\"")]
    [InlineData(PersonStatus.Deceased, "\"FALLECIDA\"")]
    public void PersonStatus_SerializesAndDeserializes_Correctly(PersonStatus status, string expectedJson)
    {
        var json = JsonSerializer.Serialize(status, _jsonOptions);
        Assert.Equal(expectedJson, json);

        var deserialized = JsonSerializer.Deserialize<PersonStatus>(expectedJson, _jsonOptions);
        Assert.Equal(status, deserialized);
    }

    [Fact]
    public void PersonSummaryDto_SerializesEnumsAsSpanishCodes_WithAspNetCoreDefaults()
    {
        var dto = new PersonSummaryDto(
            Guid.NewGuid(), "Ana", "Gómez",
            new IdentificationDocumentDto(DocumentType.Passport, "A123", "BR", "A123"),
            null, null, null, PersonStatus.Blocked, false);

        var json = JsonSerializer.Serialize(dto, _jsonOptions);

        Assert.Contains("\"status\":\"BLOQUEADA\"", json);
        Assert.Contains("\"type\":\"PASAPORTE\"", json);
    }

    [Fact]
    public void ChangePersonStatusRequest_DeserializesSpanishCode_WithAspNetCoreDefaults()
    {
        var request = JsonSerializer.Deserialize<ChangePersonStatusRequest>(
            "{\"targetStatus\":\"FALLECIDA\",\"reason\":\"Acta\"}", _jsonOptions);

        Assert.NotNull(request);
        Assert.Equal(PersonStatus.Deceased, request.TargetStatus);
    }

    [Theory]
    [InlineData("3")]
    [InlineData("\"3\"")]
    [InlineData("\"Blocked\"")]
    public void PersonStatus_RejectsNumbersAndCSharpNames(string json)
    {
        // Un cliente que envíe el valor numérico (o el nombre interno) debe recibir un error,
        // no un estado distinto al que pretendía.
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PersonStatus>(json, _jsonOptions));
    }
}
