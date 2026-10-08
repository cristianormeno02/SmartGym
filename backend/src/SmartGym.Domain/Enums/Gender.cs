using System.Text.Json.Serialization;
using SmartGym.Domain.Common;

namespace SmartGym.Domain.Enums;

[JsonConverter(typeof(StrictStringEnumConverter<Gender>))]
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
