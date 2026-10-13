namespace SmartGym.Application.Common;

/// <summary>Credenciales del administrador inicial. Se configuran sólo por user-secrets o variables de entorno.</summary>
public class AdminSeedOptions
{
    public const string SectionName = "AdminSeed";

    public const int MinimumPasswordLength = 12;

    public string? Email { get; set; }

    public string? Password { get; set; }

    public string FirstName { get; set; } = "Administrador";

    public string LastName { get; set; } = "SmartGym";
}
