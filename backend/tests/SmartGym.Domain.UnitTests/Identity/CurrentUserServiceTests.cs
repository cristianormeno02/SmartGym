using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SmartGym.Application.Common.Security;
using SmartGym.Infrastructure.Services.Auth;
using Xunit;

namespace SmartGym.Domain.UnitTests.Identity;

public class CurrentUserServiceTests
{
    [Fact]
    public void CurrentUserService_ShouldExtractClaimsCorrectly_WhenUserIsAuthenticated()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        var email = "admin@smartgym.com";

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("person_id", personId.ToString()),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Role, Roles.Administrator),
            new(ClaimTypes.Role, Roles.Instructor)
        };

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };
        var httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

        var currentUserService = new CurrentUserService(httpContextAccessor);

        // Assert
        Assert.True(currentUserService.IsAuthenticated);
        Assert.Equal(userId, currentUserService.UserId);
        Assert.Equal(personId, currentUserService.PersonId);
        Assert.Equal(email, currentUserService.Email);
        Assert.True(currentUserService.IsInRole(Roles.Administrator));
        Assert.True(currentUserService.IsInRole(Roles.Instructor));
        Assert.False(currentUserService.IsInRole(Roles.Student));
        Assert.Equal(2, currentUserService.Roles.Count);
    }

    [Fact]
    public void CurrentUserService_ShouldReturnNulls_WhenUserIsNotAuthenticated()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

        var currentUserService = new CurrentUserService(httpContextAccessor);

        // Assert
        Assert.False(currentUserService.IsAuthenticated);
        Assert.Null(currentUserService.UserId);
        Assert.Null(currentUserService.PersonId);
        Assert.Null(currentUserService.Email);
        Assert.Empty(currentUserService.Roles);
        Assert.False(currentUserService.IsInRole(Roles.Administrator));
    }
}
