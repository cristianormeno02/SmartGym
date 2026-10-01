namespace SmartGym.Application.Modules.Identity.Dtos;

public record RegisterRequest(
    string FirstName,
    string LastName,
    string Dni,
    string Email,
    string Password,
    string? PhoneNumber = null
);

public record LoginRequest(
    string Email,
    string Password
);

public record AuthResponse(
    string Token,
    Guid UserId,
    Guid PersonId,
    string Email,
    string FullName,
    List<string> Roles
);
