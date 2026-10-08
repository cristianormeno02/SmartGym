using System.Reflection;
using System.Text.Json.Serialization;

namespace SmartGym.WebApi.Common;

/// <summary>
/// Traduce los códigos de API de un enum (<see cref="JsonStringEnumMemberNameAttribute"/>) cuando llegan
/// por query string, donde no interviene el serializador JSON. No acepta números ni nombres internos.
/// </summary>
public static class ApiEnumCodes
{
    public static bool TryParse<TEnum>(string code, out TEnum value) where TEnum : struct, Enum
    {
        foreach (var field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var apiCode = field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name;
            if (apiCode != null && string.Equals(apiCode, code.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                value = (TEnum)field.GetValue(null)!;
                return true;
            }
        }

        value = default;
        return false;
    }
}
