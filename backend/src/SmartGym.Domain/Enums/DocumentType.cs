using System.Text.Json.Serialization;

namespace SmartGym.Domain.Enums;

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
