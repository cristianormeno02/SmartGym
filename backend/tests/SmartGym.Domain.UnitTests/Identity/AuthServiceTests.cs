using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartGym.Application.Modules.Identity.Dtos;
using SmartGym.Application.Modules.Identity.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using SmartGym.Infrastructure.Services.Auth;
using Xunit;

namespace SmartGym.Domain.UnitTests.Identity;

public class AuthServiceTests
{
    private readonly SmartGymDbContext _dbContext;
    private readonly PasswordHasher _passwordHasher;
    private readonly JwtTokenGenerator _jwtTokenGenerator;
    private readonly AuthService _authService;

    public AuthServiceTests()
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

        _authService = new AuthService(_dbContext, _passwordHasher, _jwtTokenGenerator, new TestGoogleTokenValidator(_ => null));
    }

    [Fact]
    public async Task RegisterAsync_ShouldCreatePersonUserAndReturnToken_WithStudentRole()
    {
        // Arrange
        var request = new RegisterRequest(
            FirstName: "Carlos",
            LastName: "Tevez",
            Dni: "30123456",
            Email: "carlos@example.com",
            Password: "Password123!"
        );

        // Act
        var result = await _authService.RegisterAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Token);
        Assert.Equal("carlos@example.com", result.Email);
        Assert.Contains("Student", result.Roles);

        var personInDb = await _dbContext.People
            .Include(p => p.User)
            .Include(p => p.PersonRoles)
            .FirstOrDefaultAsync(p => p.Email == "carlos@example.com");

        Assert.NotNull(personInDb);
        Assert.NotNull(personInDb.User);
        Assert.True(_passwordHasher.VerifyPassword("Password123!", personInDb.User.PasswordHash!));
        Assert.Single(personInDb.PersonRoles);
    }

    [Fact]
    public async Task LoginAsync_ShouldReturnToken_WhenCredentialsAreValid()
    {
        // Arrange
        var registerRequest = new RegisterRequest(
            FirstName: "Lionel",
            LastName: "Messi",
            Dni: "10101010",
            Email: "leo@example.com",
            Password: "ChampionsPassword10!"
        );
        await _authService.RegisterAsync(registerRequest);

        var loginRequest = new LoginRequest(
            Email: "leo@example.com",
            Password: "ChampionsPassword10!"
        );

        // Act
        var loginResult = await _authService.LoginAsync(loginRequest);

        // Assert
        Assert.NotNull(loginResult);
        Assert.NotEmpty(loginResult.Token);
        Assert.Equal("leo@example.com", loginResult.Email);
        Assert.Equal("Lionel Messi", loginResult.FullName);
    }

    [Fact]
    public async Task LoginAsync_ShouldThrowUnauthorizedAccessException_WhenPasswordIsIncorrect()
    {
        // Arrange
        var registerRequest = new RegisterRequest(
            FirstName: "Diego",
            LastName: "Maradona",
            Dni: "10101011",
            Email: "diego@example.com",
            Password: "ValidPassword10!"
        );
        await _authService.RegisterAsync(registerRequest);

        var loginRequest = new LoginRequest(
            Email: "diego@example.com",
            Password: "WrongPassword999!"
        );

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _authService.LoginAsync(loginRequest));
    }

    [Fact]
    public async Task RegisterAsync_ShouldThrowInvalidOperationException_WhenEmailAlreadyExists()
    {
        // Arrange
        var request = new RegisterRequest(
            FirstName: "Ana",
            LastName: "López",
            Dni: "40111222",
            Email: "ana@example.com",
            Password: "Password123!"
        );
        await _authService.RegisterAsync(request);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _authService.RegisterAsync(request));
    }

    [Fact]
    public async Task RegisterAsync_ShouldIgnoreRolesSentByClient_AndAssignOnlyStudent()
    {
        // Arrange: payload público que intenta auto-asignarse roles privilegiados
        const string json = """
        {
          "firstName": "Mallory",
          "lastName": "Attacker",
          "dni": "99000111",
          "email": "mallory@example.com",
          "password": "Password123!",
          "roles": ["Administrator", "Secretary"]
        }
        """;
        var request = JsonSerializer.Deserialize<RegisterRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        // Act
        var result = await _authService.RegisterAsync(request);

        // Assert
        Assert.Equal(new[] { "Student" }, result.Roles);

        var person = await _dbContext.People
            .Include(p => p.PersonRoles)
            .FirstAsync(p => p.Email == "mallory@example.com");
        Assert.False(person.HasRole(RoleType.Administrator));
        Assert.False(person.HasRole(RoleType.Secretary));
    }

    [Theory]
    [InlineData("otro@example.com", "20333444")] // mismo DNI, otro email
    [InlineData("instructor@example.com", "11111111")] // mismo email, otro DNI
    public async Task RegisterAsync_ShouldReject_WhenPersonAlreadyExistsWithoutAccount(string email, string dni)
    {
        // Arrange: persona cargada por administración, con rol privilegiado y sin cuenta de usuario
        var existing = Person.Create("Florencia", "Instructora", "instructor@example.com", document: IdentificationDocument.Create(DocumentType.Dni, "20333444"));
        existing.AddRole(await _dbContext.Roles.FirstAsync(r => r.Id == (int)RoleType.Administrator));
        _dbContext.People.Add(existing);
        await _dbContext.SaveChangesAsync();

        var request = new RegisterRequest(
            FirstName: "Mallory",
            LastName: "Attacker",
            Dni: dni,
            Email: email,
            Password: "Password123!"
        );

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _authService.RegisterAsync(request));

        Assert.False(await _dbContext.Users.AnyAsync(u => u.PersonId == existing.Id));
        Assert.False(await _dbContext.Users.AnyAsync(u => u.Username == email));
    }

    [Theory]
    [InlineData("", "Tester", "30111222", "a@example.com", "Password123!")]
    [InlineData("Ana", "", "30111222", "a@example.com", "Password123!")]
    [InlineData("Ana", "Tester", "", "a@example.com", "Password123!")]
    [InlineData("Ana", "Tester", "   ", "a@example.com", "Password123!")]
    [InlineData("Ana", "Tester", "30111222", "", "Password123!")]
    [InlineData("Ana", "Tester", "30111222", "a@example.com", "")]
    public async Task RegisterAsync_ShouldRejectMissingRequiredFields(
        string firstName, string lastName, string dni, string email, string password)
    {
        var request = new RegisterRequest(firstName, lastName, dni, email, password);

        await Assert.ThrowsAsync<ArgumentException>(() => _authService.RegisterAsync(request));
        Assert.False(await _dbContext.Users.AnyAsync());
    }

    [Fact]
    public async Task RegisterAsync_FormattedDni_NormalizesAndDetectsCollision()
    {
        var existing = Person.Create("Ana", "Silva", "ana@example.com", document: IdentificationDocument.Create(DocumentType.Dni, "34567890"));
        _dbContext.People.Add(existing);
        await _dbContext.SaveChangesAsync();

        var request = new RegisterRequest("Carlos", "Gomez", "34.567.890", "carlos@example.com", "Password123!");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _authService.RegisterAsync(request));
    }

    [Fact]
    public async Task LoginAsync_DeceasedPerson_ThrowsUnauthorizedAccessException()
    {
        var person = Person.Create("Juan", "Perez", "juan@example.com");
        person.ChangeStatus(PersonStatus.Deceased, "Fallecimiento", Guid.NewGuid(), isAdmin: true);
        _dbContext.People.Add(person);

        var user = new User
        {
            Id = Guid.NewGuid(),
            PersonId = person.Id,
            Person = person,
            Username = "juan@example.com",
            PasswordHash = _passwordHasher.HashPassword("Password123!")
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var request = new LoginRequest("juan@example.com", "Password123!");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _authService.LoginAsync(request));
    }
}
