using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Modules.Identity.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Identity;

public class LastAdministratorGuardTests
{
    private readonly SmartGymDbContext _dbContext;
    private readonly LastAdministratorGuard _guard;
    private readonly Role _adminRole;

    public LastAdministratorGuardTests()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new SmartGymDbContext(options);

        _adminRole = new Role { Id = (int)RoleType.Administrator, Name = "Administrator", Description = "Admin" };
        _dbContext.Roles.Add(_adminRole);
        _dbContext.SaveChanges();

        _guard = new LastAdministratorGuard(_dbContext);
    }

    private Person CreateOperationalAdmin(string email)
    {
        var person = Person.Create("Admin", "User", email);
        _dbContext.People.Add(person);

        var user = new User
        {
            Id = Guid.NewGuid(),
            PersonId = person.Id,
            Person = person,
            Username = email,
            IsActive = true
        };
        _dbContext.Users.Add(user);
        person.User = user;

        person.AddRole(_adminRole);
        _dbContext.SaveChanges();

        return person;
    }

    [Fact]
    public async Task IsOperationalAdministratorAsync_ReturnsTrue_WhenAllConditionsMet()
    {
        var admin = CreateOperationalAdmin("admin1@smartgym.com");

        var result = await _guard.IsOperationalAdministratorAsync(admin.Id);

        Assert.True(result);
    }

    [Fact]
    public async Task IsOperationalAdministratorAsync_ReturnsFalse_WhenPersonInactive()
    {
        var admin = CreateOperationalAdmin("admin_inactive@smartgym.com");
        admin.ChangeStatus(PersonStatus.Inactive, "Inactivo", Guid.NewGuid(), isAdmin: true);
        await _dbContext.SaveChangesAsync();

        var result = await _guard.IsOperationalAdministratorAsync(admin.Id);

        Assert.False(result);
    }

    [Fact]
    public async Task IsOperationalAdministratorAsync_ReturnsFalse_WhenUserInactive()
    {
        var admin = CreateOperationalAdmin("admin_user_inactive@smartgym.com");
        admin.User!.IsActive = false;
        await _dbContext.SaveChangesAsync();

        var result = await _guard.IsOperationalAdministratorAsync(admin.Id);

        Assert.False(result);
    }

    [Fact]
    public async Task IsOperationalAdministratorAsync_ReturnsFalse_WhenRoleInactive()
    {
        var admin = CreateOperationalAdmin("admin_role_inactive@smartgym.com");
        admin.RemoveRole((int)RoleType.Administrator);
        await _dbContext.SaveChangesAsync();

        var result = await _guard.IsOperationalAdministratorAsync(admin.Id);

        Assert.False(result);
    }

    [Fact]
    public async Task EnsureAnotherOperationalAdministratorAsync_Succeeds_WhenAnotherAdminExists()
    {
        var admin1 = CreateOperationalAdmin("admin1@smartgym.com");
        var admin2 = CreateOperationalAdmin("admin2@smartgym.com");

        // Excluding admin1: admin2 still remains
        var exception = await Record.ExceptionAsync(() =>
            _guard.EnsureAnotherOperationalAdministratorAsync(admin1.Id));

        Assert.Null(exception);
    }

    [Fact]
    public async Task EnsureAnotherOperationalAdministratorAsync_ThrowsConflict_WhenTargetIsOnlyAdmin()
    {
        var onlyAdmin = CreateOperationalAdmin("only_admin@smartgym.com");

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _guard.EnsureAnotherOperationalAdministratorAsync(onlyAdmin.Id));

        Assert.Contains("último administrador", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnsureAnotherOperationalAdministratorAsync_ThrowsConflict_WhenOtherAdminUserIsInactive()
    {
        var targetAdmin = CreateOperationalAdmin("target@smartgym.com");
        var otherAdmin = CreateOperationalAdmin("other@smartgym.com");
        otherAdmin.User!.IsActive = false;
        await _dbContext.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _guard.EnsureAnotherOperationalAdministratorAsync(targetAdmin.Id));

        Assert.Contains("último administrador", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnsureAnotherOperationalAdministratorAsync_ThrowsConflict_WhenOtherAdminPersonIsInactive()
    {
        var targetAdmin = CreateOperationalAdmin("target@smartgym.com");
        var otherAdmin = CreateOperationalAdmin("other@smartgym.com");
        otherAdmin.ChangeStatus(PersonStatus.Inactive, "Inactivo", Guid.NewGuid(), isAdmin: true);
        await _dbContext.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _guard.EnsureAnotherOperationalAdministratorAsync(targetAdmin.Id));

        Assert.Contains("último administrador", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnsureAnotherOperationalAdministratorAsync_ThrowsConflict_WhenOtherAdminRoleIsInactive()
    {
        var targetAdmin = CreateOperationalAdmin("target@smartgym.com");
        var otherAdmin = CreateOperationalAdmin("other@smartgym.com");
        otherAdmin.RemoveRole((int)RoleType.Administrator);
        await _dbContext.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _guard.EnsureAnotherOperationalAdministratorAsync(targetAdmin.Id));

        Assert.Contains("último administrador", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
