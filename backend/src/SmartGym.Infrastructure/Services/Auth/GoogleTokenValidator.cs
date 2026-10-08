using Google.Apis.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Identity.Dtos;

namespace SmartGym.Infrastructure.Services.Auth;

public class GoogleTokenValidator : IGoogleTokenValidator
{
    private readonly GoogleOptions _options;
    private readonly ILogger<GoogleTokenValidator> _logger;

    public GoogleTokenValidator(
        IOptions<GoogleOptions> options,
        ILogger<GoogleTokenValidator> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<GoogleAuthPayload?> ValidateAsync(string idToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings();
            if (!string.IsNullOrWhiteSpace(_options.ClientId))
            {
                settings.Audience = new[] { _options.ClientId };
            }

            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
            if (payload == null)
            {
                return null;
            }

            return new GoogleAuthPayload(
                SubjectId: payload.Subject,
                Email: payload.Email,
                GivenName: payload.GivenName ?? string.Empty,
                FamilyName: payload.FamilyName ?? string.Empty,
                PictureUrl: payload.Picture,
                EmailVerified: payload.EmailVerified,
                FullName: payload.Name
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falló la validación del token de Google OAuth.");
            return null;
        }
    }
}
