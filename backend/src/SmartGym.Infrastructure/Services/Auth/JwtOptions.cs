namespace SmartGym.Infrastructure.Services.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "SmartGym";
    public string Audience { get; set; } = "SmartGymClient";
    public string SecretKey { get; set; } = "SmartGym_Default_Secret_Key_At_Least_32_Bytes_Long_2026";
    public int ExpirationMinutes { get; set; } = 120;
}
