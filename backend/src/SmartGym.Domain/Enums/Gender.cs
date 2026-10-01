using System.Text.Json.Serialization;

namespace SmartGym.Domain.Enums;

public enum Gender
{
    [JsonStringEnumMemberName("MASCULINO")]
    Male = 1,

    [JsonStringEnumMemberName("FEMENINO")]
    Female = 2,

    [JsonStringEnumMemberName("X")]
    X = 3,

    [JsonStringEnumMemberName("NO_INFORMA")]
    NotInformed = 4
}
