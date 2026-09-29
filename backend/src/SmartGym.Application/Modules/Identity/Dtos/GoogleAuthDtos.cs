namespace SmartGym.Application.Modules.Identity.Dtos;

public record GoogleAuthPayload(
    string SubjectId,
    string Email,
    string GivenName,
    string FamilyName,
    string? PictureUrl,
    bool EmailVerified
);

public record GoogleLoginRequest(
    string IdToken
);
