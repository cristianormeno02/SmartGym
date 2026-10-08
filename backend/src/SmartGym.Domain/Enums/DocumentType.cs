using System.Text.Json.Serialization;
using SmartGym.Domain.Common;

namespace SmartGym.Domain.Enums;

[JsonConverter(typeof(StrictStringEnumConverter<DocumentType>))]
public enum DocumentType
{
    [JsonStringEnumMemberName("DNI")]
    Dni = 1,

    [JsonStringEnumMemberName("PASAPORTE")]
    Passport = 2,

    [JsonStringEnumMemberName("CI")]
    IdentityCard = 3,

    [JsonStringEnumMemberName("OTRO")]
    Other = 4
}
