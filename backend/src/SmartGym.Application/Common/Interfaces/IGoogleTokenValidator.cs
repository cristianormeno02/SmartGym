using SmartGym.Application.Modules.Identity.Dtos;

namespace SmartGym.Application.Common.Interfaces;

public interface IGoogleTokenValidator
{
    Task<GoogleAuthPayload?> ValidateAsync(string idToken, CancellationToken cancellationToken = default);
}
