using System.Text.Json;
using System.Text.Json.Serialization;
using SmartGym.Domain.Enums;
using Xunit;

namespace SmartGym.Domain.UnitTests.Enums;

public class PeopleEnumSerializationTests
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

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
}
