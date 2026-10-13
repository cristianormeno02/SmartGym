using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Identity.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using SmartGym.Infrastructure.Services.Auth;
using Xunit;

namespace SmartGym.Domain.UnitTests.Identity;

public class AdminUserSeederTests
{
    private const string AdminEmail = "admin@smartgym.example";
    private const string AdminPassword = "una-clave-larga-de-prueba";

    private readonly SmartGymDbContext _dbContext;
    private readonly PasswordHasher _passwordHasher = new();

    public AdminUserSeederTests()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new SmartGymDbContext(options);
        _dbContext.Roles.AddRange(
            new Role { Id = (int)RoleType.Administrator, Name = "Administrator", Description = "Admin" },
            new Role { Id = (int)RoleType.Student, Name = "Student", Description = "Student" });
        _dbContext.SaveChanges();
    }

    private AdminUserSeeder CreateSeeder(string? email = AdminEmail, string? password = AdminPassword) =>
        new(
            _dbContext,
            _passwordHasher,
            Options.Create(new AdminSeedOptions { Email = email, Password = password }),
            NullLogger<AdminUserSeeder>.Instance);

    private async Task<User> AddAccountAsync(string email, string password, RoleType role)
    {
        var person = Person.Create("Persona", "Existente", email);
        var user = new User
        {
            Id = Guid.NewGuid(),
            PersonId = person.Id,
            Person = person,
            Username = email,
            PasswordHash = _passwordHasher.HashPassword(password),
            CreatedAtUtc = DateTime.UtcNow
        };
        person.AddRole(await _dbContext.Roles.SingleAsync(r => r.Id == (int)role));
        _dbContext.People.Add(person);
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task SeedAsync_ShouldCreateAdministrator_WhenConfiguredAndNoneExists()
    {
        var outcome = await CreateSeeder(email: "  Admin@SmartGym.Example ").SeedAsync();

        Assert.Equal(AdminSeedOutcome.Created, outcome);

        var user = await _dbContext.Users
            .Include(u => u.Person).ThenInclude(p => p.PersonRoles)
            .SingleAsync();
        Assert.Equal(AdminEmail, user.Username);
        Assert.Equal(AdminEmail, user.Person.Email);
        Assert.Equal("Administrador", user.Person.FirstName);
        Assert.Equal("SmartGym", user.Person.LastName);
        Assert.True(user.IsEmailConfirmed);
        Assert.NotEqual(AdminPassword, user.PasswordHash);
        Assert.True(_passwordHasher.VerifyPassword(AdminPassword, user.PasswordHash!));

        var role = Assert.Single(user.Person.PersonRoles);
        Assert.Equal((int)RoleType.Administrator, role.RoleId);
        Assert.True(role.IsActive);
    }

    [Theory]
    [InlineData(null, AdminPassword)]
    [InlineData(AdminEmail, null)]
    [InlineData("   ", AdminPassword)]
    [InlineData(AdminEmail, "")]
    public async Task SeedAsync_ShouldDoNothing_WhenNotConfigured(string? email, string? password)
    {
        var outcome = await CreateSeeder(email, password).SeedAsync();

        Assert.Equal(AdminSeedOutcome.NotConfigured, outcome);
        Assert.Empty(_dbContext.Users);
        Assert.Empty(_dbContext.People);
    }

    [Theory]
    [InlineData("no-es-un-email", AdminPassword)]
    [InlineData(AdminEmail, "corta")]
    public async Task SeedAsync_ShouldDoNothing_WhenConfigurationIsInvalid(string email, string password)
    {
        var outcome = await CreateSeeder(email, password).SeedAsync();

        Assert.Equal(AdminSeedOutcome.InvalidConfiguration, outcome);
        Assert.Empty(_dbContext.Users);
    }

    [Fact]
    public async Task SeedAsync_ShouldNotCreateNorModify_WhenAnAdministratorAlreadyExists()
    {
        var existing = await AddAccountAsync("otro.admin@smartgym.example", "clave-original-del-admin", RoleType.Administrator);
        var originalHash = existing.PasswordHash;

        var outcome = await CreateSeeder().SeedAsync();

        Assert.Equal(AdminSeedOutcome.AdministratorAlreadyExists, outcome);
        var user = await _dbContext.Users.SingleAsync();
        Assert.Equal(existing.Id, user.Id);
        Assert.Equal(originalHash, user.PasswordHash);
    }

    [Fact]
    public async Task SeedAsync_ShouldNotPromoteExistingAccount_WhenEmailIsAlreadyInUse()
    {
        var student = await AddAccountAsync(AdminEmail, "clave-original-del-alumno", RoleType.Student);
        var originalHash = student.PasswordHash;

        var outcome = await CreateSeeder().SeedAsync();

        Assert.Equal(AdminSeedOutcome.EmailAlreadyInUse, outcome);
        var user = await _dbContext.Users
            .Include(u => u.Person).ThenInclude(p => p.PersonRoles)
            .SingleAsync();
        Assert.Equal(originalHash, user.PasswordHash);
        Assert.DoesNotContain(user.Person.PersonRoles, pr => pr.RoleId == (int)RoleType.Administrator);
    }

    [Fact]
    public async Task SeedAsync_ShouldBeIdempotent()
    {
        Assert.Equal(AdminSeedOutcome.Created, await CreateSeeder().SeedAsync());
        Assert.Equal(AdminSeedOutcome.AdministratorAlreadyExists, await CreateSeeder().SeedAsync());

        Assert.Single(_dbContext.Users);
        Assert.Single(_dbContext.People);
    }
}
