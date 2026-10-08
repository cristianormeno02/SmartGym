using System.Text.Json.Serialization;
using SmartGym.Domain.Common;

namespace SmartGym.Domain.Enums;

[JsonConverter(typeof(StrictStringEnumConverter<PersonStatus>))]
public enum PersonStatus
{
    [JsonStringEnumMemberName("ACTIVA")]
    Active = 1,

    [JsonStringEnumMemberName("INACTIVA")]
    Inactive = 2,

    [JsonStringEnumMemberName("BLOQUEADA")]
    Blocked = 3,

    [JsonStringEnumMemberName("FALLECIDA")]
    Deceased = 4
}
