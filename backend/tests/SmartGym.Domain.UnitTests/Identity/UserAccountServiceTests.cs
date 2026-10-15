using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Identity.Dtos;
using SmartGym.Application.Modules.Identity.Services;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using SmartGym.Infrastructure.Services.Auth;
using Xunit;

namespace SmartGym.Domain.UnitTests.Identity;

public class UserAccountServiceTests
{
    private readonly SmartGymDbContext _dbContext;
    private readonly PasswordHasher _passwordHasher;
    private readonly TestCurrentUserService _currentUserService;
    private readonly AuditService _auditService;
    private readonly LastAdministratorGuard _lastAdminGuard;
    private readonly NoOpIdentityLock _identityLock;
    private readonly UserAccountService _service;

    private readonly Role _adminRole;
    private readonly Role _secretaryRole;
    private readonly Role _instructorRole;
    private readonly Role _studentRole;

    public UserAccountServiceTests()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new SmartGymDbContext(options);

        _adminRole = new Role { Id = (int)RoleType.Administrator, Name = "Administrator", Description = "Admin" };
        _secretaryRole = new Role { Id = (int)RoleType.Secretary, Name = "Secretary", Description = "Secretary" };
        _instructorRole = new Role { Id = (int)RoleType.Instructor, Name = "Instructor", Description = "Instructor" };
        _studentRole = new Role { Id = (int)RoleType.Student, Name = "Student", Description = "Student" };
        var publicRole = new Role { Id = (int)RoleType.Public, Name = "Public", Description = "Public" };

        _dbContext.Roles.AddRange(_adminRole, _secretaryRole, _instructorRole, _studentRole, publicRole);
        _dbContext.SaveChanges();

        _passwordHasher = new PasswordHasher();
        _currentUserService = new TestCurrentUserService
        {
            UserId = Guid.NewGuid(),
            Email = "current-admin@smartgym.com",
            IsAuthenticated = true,
            Roles = new List<string> { "Administrator" }
        };
        _auditService = new AuditService(_dbContext);
        _lastAdminGuard = new LastAdministratorGuard(_dbContext);
        _identityLock = new NoOpIdentityLock();

        _service = new UserAccountService(
            _dbContext,
            _passwordHasher,
            _currentUserService,
            _auditService,
            _lastAdminGuard,
            _identityLock);
    }

    private Person CreatePerson(string email = "person@smartgym.com", PersonStatus status = PersonStatus.Active)
    {
        var person = Person.Create("John", "Doe", email);
        if (status != PersonStatus.Active)
        {
            person.ChangeStatus(status, "Status change", Guid.NewGuid(), isAdmin: true);
        }
        _dbContext.People.Add(person);
        _dbContext.SaveChanges();
        return person;
    }

    private User CreateUser(Person person, bool isActive = true)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            PersonId = person.Id,
            Person = person,
            Username = person.Email ?? "user@smartgym.com",
            PasswordHash = _passwordHasher.HashPassword("StrongP@ss1"),
            IsActive = isActive,
            SecurityStamp = Guid.NewGuid().ToString("N")
        };
        person.SetUser(user);
        _dbContext.Users.Add(user);
        _dbContext.SaveChanges();
        return user;
    }

    [Fact]
    public async Task GetAccountAsync_ThrowsNotFound_WhenPersonDoesNotExist()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetAccountAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetAccountAsync_ReturnsNoAccount_WhenPersonHasNoUser()
    {
        var person = CreatePerson();
        person.AddRole(_studentRole);
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetAccountAsync(person.Id);

        Assert.False(result.HasAccount);
        Assert.Null(result.UserId);
        Assert.Null(result.Username);
        Assert.Null(result.IsActive);
        Assert.Null(result.LastLoginUtc);
        Assert.Single(result.Roles);
        Assert.Equal("Student", result.Roles[0]);
    }

    [Fact]
    public async Task GetAccountAsync_ReturnsAccountDetails_WhenPersonHasUser()
    {
        var person = CreatePerson();
        person.AddRole(_secretaryRole);
        var user = CreateUser(person, isActive: true);

        var result = await _service.GetAccountAsync(person.Id);

        Assert.True(result.HasAccount);
        Assert.Equal(user.Id, result.UserId);
        Assert.Equal(user.Username, result.Username);
        Assert.True(result.IsActive);
        Assert.Single(result.Roles);
        Assert.Equal("Secretary", result.Roles[0]);
    }

    [Fact]
    public async Task ProvisionAsync_ThrowsNotFound_WhenPersonDoesNotExist()
    {
        var req = new ProvisionUserAccountRequest("StrongP@ss1", new List<int> { 4 });
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.ProvisionAsync(Guid.NewGuid(), req));
    }

    [Fact]
    public async Task ProvisionAsync_ThrowsConflict_WhenPersonIsDeceased()
    {
        var person = CreatePerson(status: PersonStatus.Deceased);
        var req = new ProvisionUserAccountRequest("StrongP@ss1", new List<int> { 4 });

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ProvisionAsync(person.Id, req));

        Assert.Contains("FALLECIDA", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProvisionAsync_ThrowsConflict_WhenPersonAlreadyHasUser()
    {
        var person = CreatePerson();
        CreateUser(person);
        var req = new ProvisionUserAccountRequest("StrongP@ss1", new List<int> { 4 });

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ProvisionAsync(person.Id, req));

        Assert.Contains("ya posee una cuenta", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProvisionAsync_ThrowsValidation_WhenPersonHasNoEmail()
    {
        var person = Person.Create("No", "Email", null);
        _dbContext.People.Add(person);
        await _dbContext.SaveChangesAsync();

        var req = new ProvisionUserAccountRequest("StrongP@ss1", new List<int> { 4 });

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.ProvisionAsync(person.Id, req));

        Assert.True(ex.Errors.ContainsKey("Email"));
    }

    [Fact]
    public async Task ProvisionAsync_ThrowsConflict_WhenEmailAlreadyUsedByAnotherUser()
    {
        var existingPerson = CreatePerson("shared@smartgym.com");
        CreateUser(existingPerson);

        var targetPerson = CreatePerson("SHARED@smartgym.com");
        var req = new ProvisionUserAccountRequest("StrongP@ss1", new List<int> { 4 });

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ProvisionAsync(targetPerson.Id, req));

        Assert.Contains("vinculado a otra cuenta", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProvisionAsync_ThrowsValidation_WhenPasswordOrRolesAreInvalid()
    {
        var person = CreatePerson();
        var weakPassReq = new ProvisionUserAccountRequest("weak", new List<int> { 4 });

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.ProvisionAsync(person.Id, weakPassReq));

        var invalidRoleReq = new ProvisionUserAccountRequest("StrongP@ss1", new List<int> { 5 }); // 5 is Public
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.ProvisionAsync(person.Id, invalidRoleReq));
    }

    [Fact]
    public async Task ProvisionAsync_Succeeds_CreatesUserAndAssignsRolesAndAudits()
    {
        var person = CreatePerson("newuser@smartgym.com");
        var req = new ProvisionUserAccountRequest("StrongP@ss1", new List<int> { 2, 3 }); // Secretary, Instructor

        var result = await _service.ProvisionAsync(person.Id, req);

        Assert.True(result.HasAccount);
        Assert.Equal("newuser@smartgym.com", result.Username);
        Assert.True(result.IsActive);
        Assert.Equal(2, result.Roles.Count);
        Assert.Contains("Secretary", result.Roles);
        Assert.Contains("Instructor", result.Roles);

        var createdUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.PersonId == person.Id);
        Assert.NotNull(createdUser);
        Assert.True(_passwordHasher.VerifyPassword("StrongP@ss1", createdUser.PasswordHash!));
        Assert.False(string.IsNullOrWhiteSpace(createdUser.SecurityStamp));

        var audit = await _dbContext.AuditLogs.FirstOrDefaultAsync(a => a.Action == "UserProvisioned");
        Assert.NotNull(audit);
        Assert.Equal(_currentUserService.UserId, audit.UserId);
    }

    [Fact]
    public async Task UpdateRolesAsync_ThrowsConflict_WhenRemovingLastOperationalAdmin()
    {
        var adminPerson = CreatePerson("onlyadmin@smartgym.com");
        adminPerson.AddRole(_adminRole);
        CreateUser(adminPerson, isActive: true);

        var req = new UpdateUserRolesRequest(new List<int> { 4 }); // Change to Student only

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateRolesAsync(adminPerson.Id, req));

        Assert.Contains("último administrador", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateRolesAsync_IsIdempotent_WhenSameRolesSent()
    {
        var person = CreatePerson("member@smartgym.com");
        person.AddRole(_studentRole);
        var user = CreateUser(person);
        var originalStamp = user.SecurityStamp;

        var req = new UpdateUserRolesRequest(new List<int> { 4 });
        var result = await _service.UpdateRolesAsync(person.Id, req);

        Assert.Single(result.Roles);
        Assert.Equal("Student", result.Roles[0]);
        Assert.Equal(originalStamp, user.SecurityStamp);

        var audit = await _dbContext.AuditLogs.FirstOrDefaultAsync(a => a.Action == "UserRolesChanged");
        Assert.Null(audit);
    }

    [Fact]
    public async Task UpdateRolesAsync_ReactivatesRevokedRole_AndRotatesSecurityStamp()
    {
        var admin1 = CreatePerson("admin1@smartgym.com");
        admin1.AddRole(_adminRole);
        CreateUser(admin1, isActive: true);

        var admin2 = CreatePerson("admin2@smartgym.com");
        admin2.AddRole(_adminRole);
        admin2.AddRole(_secretaryRole);
        var user2 = CreateUser(admin2, isActive: true);
        var oldStamp = user2.SecurityStamp;

        // Revoke secretary
        admin2.RemoveRole((int)RoleType.Secretary);
        await _dbContext.SaveChangesAsync();

        // Reassign secretary and admin
        var req = new UpdateUserRolesRequest(new List<int> { 1, 2 });
        var result = await _service.UpdateRolesAsync(admin2.Id, req);

        Assert.Equal(2, result.Roles.Count);
        Assert.Contains("Administrator", result.Roles);
        Assert.Contains("Secretary", result.Roles);
        Assert.NotEqual(oldStamp, user2.SecurityStamp);

        var audit = await _dbContext.AuditLogs.FirstOrDefaultAsync(a => a.Action == "UserRolesChanged");
        Assert.NotNull(audit);
    }

    [Fact]
    public async Task UpdateStatusAsync_ThrowsConflict_WhenSuspendingLastOperationalAdmin()
    {
        var adminPerson = CreatePerson("onlyadmin@smartgym.com");
        adminPerson.AddRole(_adminRole);
        CreateUser(adminPerson, isActive: true);

        var req = new UpdateUserStatusRequest(false);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateStatusAsync(adminPerson.Id, req));

        Assert.Contains("último administrador", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateStatusAsync_SuspendsAndRotatesStamp_WhenAnotherAdminExists()
    {
        var admin1 = CreatePerson("admin1@smartgym.com");
        admin1.AddRole(_adminRole);
        CreateUser(admin1, isActive: true);

        var target = CreatePerson("target@smartgym.com");
        target.AddRole(_studentRole);
        var user = CreateUser(target, isActive: true);
        var oldStamp = user.SecurityStamp;

        var req = new UpdateUserStatusRequest(false);
        var result = await _service.UpdateStatusAsync(target.Id, req);

        Assert.False(result.IsActive);
        Assert.False(user.IsActive);
        Assert.NotEqual(oldStamp, user.SecurityStamp);

        var audit = await _dbContext.AuditLogs.FirstOrDefaultAsync(a => a.Action == "UserSuspended");
        Assert.NotNull(audit);
    }

    [Fact]
    public async Task UpdateStatusAsync_ReactivatesUser()
    {
        var person = CreatePerson("target@smartgym.com");
        var user = CreateUser(person, isActive: false);
        var oldStamp = user.SecurityStamp;

        var req = new UpdateUserStatusRequest(true);
        var result = await _service.UpdateStatusAsync(person.Id, req);

        Assert.True(result.IsActive);
        Assert.True(user.IsActive);
        Assert.NotEqual(oldStamp, user.SecurityStamp);

        var audit = await _dbContext.AuditLogs.FirstOrDefaultAsync(a => a.Action == "UserReactivated");
        Assert.NotNull(audit);
    }

    [Fact]
    public async Task ResetPasswordAsync_ThrowsNotFound_WhenPersonHasNoUser()
    {
        var person = CreatePerson();
        var req = new AdminResetPasswordRequest("NewStrongP@ss1");

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.ResetPasswordAsync(person.Id, req));
    }

    [Fact]
    public async Task ResetPasswordAsync_ThrowsValidation_WhenPasswordIsWeak()
    {
        var person = CreatePerson();
        CreateUser(person);
        var req = new AdminResetPasswordRequest("weak");

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.ResetPasswordAsync(person.Id, req));
    }

    [Fact]
    public async Task ResetPasswordAsync_UpdatesPasswordAndRotatesStamp_AndAuditsWithoutPassword()
    {
        var person = CreatePerson();
        var user = CreateUser(person);
        var oldStamp = user.SecurityStamp;
        var oldHash = user.PasswordHash;

        var req = new AdminResetPasswordRequest("NewStrongP@ss99!");
        await _service.ResetPasswordAsync(person.Id, req);

        Assert.NotEqual(oldHash, user.PasswordHash);
        Assert.True(_passwordHasher.VerifyPassword("NewStrongP@ss99!", user.PasswordHash!));
        Assert.NotEqual(oldStamp, user.SecurityStamp);

        var audit = await _dbContext.AuditLogs.FirstOrDefaultAsync(a => a.Action == "UserPasswordReset");
        Assert.NotNull(audit);
        Assert.Null(audit.OldValues);
        Assert.Null(audit.NewValues);
    }
}

public class TestCurrentUserService : ICurrentUserService
{
    public Guid? UserId { get; set; }
    public Guid? PersonId { get; set; }
    public string? Email { get; set; }
    public bool IsAuthenticated { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = new List<string>();

    public bool IsInRole(string role) => Roles.Contains(role);
}
