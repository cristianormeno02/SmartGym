using SmartGym.Domain.Entities.Identity;

namespace SmartGym.Application.Common.Interfaces;

public interface IJwtTokenGenerator
{
    string GenerateToken(User user, Person person, IEnumerable<string> roles);
}
