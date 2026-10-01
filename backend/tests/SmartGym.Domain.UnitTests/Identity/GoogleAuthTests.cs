using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Identity.Dtos;
using SmartGym.Application.Modules.Identity.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using SmartGym.Infrastructure.Services.Auth;
using Xunit;

namespace SmartGym.Domain.UnitTests.Identity;

public class TestGoogleTokenValidator : IGoogleTokenValidator
{
    private readonly Func<string, GoogleAuthPayload?> _validatorFunc;

    public TestGoogleTokenValidator(Func<string, GoogleAuthPayload?> validatorFunc)
    {
        _validatorFunc = validatorFunc;
    }

    public Task<GoogleAuthPayload?> ValidateAsync(string idToken, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_validatorFunc(idToken));
    }
}

public class GoogleAuthTests
{
    private readonly SmartGymDbContext _dbContext;
    private readonly PasswordHasher _passwordHasher;
    private readonly JwtTokenGenerator _jwtTokenGenerator;

    public GoogleAuthTests()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new SmartGymDbContext(options);

        // Seed roles
        _dbContext.Roles.AddRange(
            new Role { Id = (int)RoleType.Administrator, Name = "Administrator", Description = "Admin" },
            new Role { Id = (int)RoleType.Secretary, Name = "Secretary", Description = "Secretary" },
            new Role { Id = (int)RoleType.Instructor, Name = "Instructor", Description = "Instructor" },
            new Role { Id = (int)RoleType.Student, Name = "Student", Description = "Student" },
            new Role { Id = (int)RoleType.Public, Name = "Public", Description = "Public" }
        );
        _dbContext.SaveChanges();

        _passwordHasher = new PasswordHasher();
        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = "SmartGymTest",
            Audience = "SmartGymClientTest",
            SecretKey = "super_secret_key_for_testing_purposes_only_at_least_32_bytes_long!",
            ExpirationMinutes = 60
        });
        _jwtTokenGenerator = new JwtTokenGenerator(jwtOptions);
    }

    [Fact]
    public async Task LoginWithGoogleAsync_ShouldLinkGoogleId_WhenLocalUserAlreadyExistsWithSameEmail()
    {
        // Arrange
        var person = Person.Create("Roberto", "Gómez", "roberto@example.com", document: IdentificationDocument.Create(DocumentType.Dni, "20123456"));
        var user = new User
        {
            Id = Guid.NewGuid(),
            PersonId = person.Id,
            Person = person,
            Username = "roberto@example.com",
            PasswordHash = _passwordHasher.HashPassword("Password123!")
        };
        _dbContext.People.Add(person);
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var mockValidator = new TestGoogleTokenValidator(token =>
            new GoogleAuthPayload(
                SubjectId: "google_subject_999",
                Email: "roberto@example.com",
                GivenName: "Roberto",
                FamilyName: "Gómez",
                PictureUrl: "https://lh3.googleusercontent.com/photo.jpg",
                EmailVerified: true
            ));

        var authService = new AuthService(_dbContext, _passwordHasher, _jwtTokenGenerator, mockValidator);

        // Act
        var result = await authService.LoginWithGoogleAsync(new GoogleLoginRequest("valid_google_token"));

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Token);
        Assert.Equal("roberto@example.com", result.Email);

        // Verificar que no se duplicó la persona ni el usuario
        var usersCount = await _dbContext.Users.CountAsync(u => u.Username == "roberto@example.com");
        var peopleCount = await _dbContext.People.CountAsync(p => p.Email == "roberto@example.com");
        Assert.Equal(1, usersCount);
        Assert.Equal(1, peopleCount);

        var updatedUser = await _dbContext.Users.Include(u => u.Person).FirstAsync(u => u.Username == "roberto@example.com");
        Assert.Equal("google_subject_999", updatedUser.GoogleSubjectId);
        Assert.Equal("https://lh3.googleusercontent.com/photo.jpg", updatedUser.Person.PhotoUrl);
    }

    [Fact]
    public async Task LoginWithGoogleAsync_ShouldCreateNewPersonAndUser_WhenUserDoesNotExist()
    {
        // Arrange
        var mockValidator = new TestGoogleTokenValidator(token =>
            new GoogleAuthPayload(
                SubjectId: "google_new_user_123",
                Email: "nuevo.google@example.com",
                GivenName: "Lucía",
                FamilyName: "Fernández",
                PictureUrl: "https://lh3.googleusercontent.com/lucia.jpg",
                EmailVerified: true
            ));

        var authService = new AuthService(_dbContext, _passwordHasher, _jwtTokenGenerator, mockValidator);

        // Act
        var result = await authService.LoginWithGoogleAsync(new GoogleLoginRequest("new_google_token"));

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Token);
        Assert.Equal("nuevo.google@example.com", result.Email);
        Assert.Contains("Student", result.Roles);

        var createdUser = await _dbContext.Users
            .Include(u => u.Person)
                .ThenInclude(p => p.PersonRoles)
                    .ThenInclude(pr => pr.Role)
            .FirstOrDefaultAsync(u => u.GoogleSubjectId == "google_new_user_123");

        Assert.NotNull(createdUser);
        Assert.Equal("Lucía Fernández", createdUser.Person.FullName);
        Assert.True(createdUser.Person.HasRole(RoleType.Student));
        // Google no provee DNI: se deja sin informar (null) en lugar de un valor vacío que colisione en el índice único
        Assert.Null(createdUser.Person.Dni);
    }

    [Fact]
    public async Task LoginWithGoogleAsync_ShouldThrowUnauthorizedAccessException_WhenTokenIsInvalid()
    {
        // Arrange
        var mockValidator = new TestGoogleTokenValidator(token => null); // token inválido
        var authService = new AuthService(_dbContext, _passwordHasher, _jwtTokenGenerator, mockValidator);

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            authService.LoginWithGoogleAsync(new GoogleLoginRequest("invalid_token")));
    }
}
