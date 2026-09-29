using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Services.Auth;
using Xunit;

namespace SmartGym.Domain.UnitTests.Identity;

public class JwtTokenGeneratorTests
{
    private readonly JwtOptions _options = new()
    {
        Issuer = "SmartGymTest",
        Audience = "SmartGymClientTest",
        SecretKey = "super_secret_key_for_testing_purposes_only_at_least_32_bytes_long!",
        ExpirationMinutes = 60
    };

    [Fact]
    public void GenerateToken_ShouldIncludeUserAndRoleClaims()
    {
        // Arrange
        var generator = new JwtTokenGenerator(Options.Create(_options));
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Juan",
            LastName = "Pérez",
            Email = "juan@example.com",
            Dni = "12345678"
        };
        var user = new User
        {
            Id = Guid.NewGuid(),
            PersonId = person.Id,
            Username = "juan@example.com"
        };
        var roles = new[] { RoleType.Student.ToString(), RoleType.Instructor.ToString() };

        // Act
        var tokenString = generator.GenerateToken(user, person, roles);

        // Assert
        Assert.NotNull(tokenString);
        Assert.NotEmpty(tokenString);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokenString);

        Assert.Equal(_options.Issuer, jwt.Issuer);
        Assert.Contains(_options.Audience, jwt.Audiences);

        var claims = jwt.Claims.ToList();
        Assert.Contains(claims, c => c.Type == JwtRegisteredClaimNames.Sub && c.Value == user.Id.ToString());
        Assert.Contains(claims, c => c.Type == JwtRegisteredClaimNames.Email && c.Value == person.Email);
        Assert.Contains(claims, c => c.Type == JwtRegisteredClaimNames.GivenName && c.Value == person.FirstName);
        Assert.Contains(claims, c => c.Type == JwtRegisteredClaimNames.FamilyName && c.Value == person.LastName);
        Assert.Contains(claims, c => c.Type == "person_id" && c.Value == person.Id.ToString());

        var roleClaims = claims.Where(c => c.Type == "role" || c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();
        Assert.Contains(RoleType.Student.ToString(), roleClaims);
        Assert.Contains(RoleType.Instructor.ToString(), roleClaims);
    }
}
