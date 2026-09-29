using SmartGym.Application.Common.Interfaces;
using SmartGym.Infrastructure.Services.Auth;
using Xunit;

namespace SmartGym.Domain.UnitTests.Identity;

public class PasswordHasherTests
{
    private readonly IPasswordHasher _passwordHasher = new PasswordHasher();

    [Fact]
    public void HashPassword_ShouldReturnHashedString_NotEqualToPlaintext()
    {
        // Arrange
        var password = "SecurePassword123!";

        // Act
        var hash = _passwordHasher.HashPassword(password);

        // Assert
        Assert.NotNull(hash);
        Assert.NotEmpty(hash);
        Assert.NotEqual(password, hash);
    }

    [Fact]
    public void VerifyPassword_ShouldReturnTrue_WhenPasswordMatches()
    {
        // Arrange
        var password = "SecurePassword123!";
        var hash = _passwordHasher.HashPassword(password);

        // Act
        var result = _passwordHasher.VerifyPassword(password, hash);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void VerifyPassword_ShouldReturnFalse_WhenPasswordDoesNotMatch()
    {
        // Arrange
        var password = "SecurePassword123!";
        var wrongPassword = "WrongPassword999!";
        var hash = _passwordHasher.HashPassword(password);

        // Act
        var result = _passwordHasher.VerifyPassword(wrongPassword, hash);

        // Assert
        Assert.False(result);
    }
}
