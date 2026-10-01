namespace SmartGym.Infrastructure.Services.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";
    public const int MinSecretKeyBytes = 32;

    public string Issuer { get; set; } = "SmartGym";
    public string Audience { get; set; } = "SmartGymClient";
    // Sin valor por defecto: debe provenir de user-secrets o variables de entorno, nunca del código.
    public string SecretKey { get; set; } = string.Empty;
    public int ExpirationMinutes { get; set; } = 120;
}
