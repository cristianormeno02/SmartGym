using System.Text.Json.Serialization;

namespace SmartGym.Domain.Common;

/// <summary>
/// Serializa el enum con los códigos de <see cref="JsonStringEnumMemberNameAttribute"/> y rechaza
/// valores numéricos, para que un cliente desactualizado reciba un error en lugar de otro valor.
/// </summary>
public sealed class StrictStringEnumConverter<TEnum> : JsonStringEnumConverter<TEnum>
    where TEnum : struct, Enum
{
    public StrictStringEnumConverter()
        : base(namingPolicy: null, allowIntegerValues: false)
    {
    }
}
