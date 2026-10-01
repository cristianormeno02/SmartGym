using System.Text.Json.Serialization;

namespace SmartGym.Domain.Enums;

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
