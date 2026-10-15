using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartGym.Application;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Identity.Dtos;
using SmartGym.Application.Modules.Identity.Services;
using SmartGym.Application.Modules.People.Dtos;
using SmartGym.Application.Modules.People.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure;
using SmartGym.Infrastructure.Persistence;
using SmartGym.IntegrationTests.Fixtures;
using SmartGym.WebApi.Controllers;
using Xunit;

namespace SmartGym.IntegrationTests.Identity;

// Arma los servicios con AddApplication + AddInfrastructure, igual que la API, para ejercitar
// la autenticación JWT (incluido OnTokenValidated) y las políticas de autorización reales.
// Los tests que cuentan administradores usan una base aislada: la base compartida de la colección
// acumula administradores creados por otros tests y la guarda siempre encontraría "otro" administrador.
[Collection("PostgresIntegration")]
public class UserRolesManagementIntegrationTests : IDisposable
{
    private const string AdminPassword = "AdminP@ss1";

    private readonly PostgresDatabaseFixture _fixture;
    private readonly List<ServiceProvider> _providers = new();
    private readonly TestCurrentUserService _currentUser = new()
    {
        UserId = Guid.NewGuid(),
        Email = "current-admin@smartgym.com",
        IsAuthenticated = true,
        Roles = new List<string> { Roles.Administrator }
    };

    public UserRolesManagementIntegrationTests(PostgresDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }
    }

    private async Task<ServiceProvider> BuildProviderAsync(bool isolatedDatabase = false)
    {
        var connectionString = isolatedDatabase
            ? await _fixture.CreateIsolatedDatabaseAsync(applyAllMigrations: true)
            : _fixture.ConnectionString;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString,
                ["Jwt:Issuer"] = "SmartGymTest",
                ["Jwt:Audience"] = "SmartGymClientTest",
                ["Jwt:SecretKey"] = "super_secret_key_for_testing_purposes_only_at_least_32_bytes_long!",
                ["Jwt:ExpirationMinutes"] = "60",
                ["S3Storage:UseLocalStorage"] = "true",
                ["PublicS3Storage:UseLocalStorage"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.AddScoped<ICurrentUserService>(_ => _currentUser);

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    private static async Task<Person> CreateAdminPersonAsync(IServiceProvider provider, string email)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartGymDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var person = Person.Create("Admin", "User", email);
        db.People.Add(person);
        await db.SaveChangesAsync();

        var adminRole = await db.Roles.FirstAsync(r => r.Id == (int)RoleType.Administrator);
        person.AddRole(adminRole);

        var user = new User
        {
            Id = Guid.NewGuid(),
            PersonId = person.Id,
            Username = email.ToLowerInvariant(),
            PasswordHash = hasher.HashPassword(AdminPassword),
            IsActive = true
        };
        person.SetUser(user);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return person;
    }

    private static async Task<string> IssueTokenAsync(IServiceProvider provider, Guid personId)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartGymDbContext>();
        var jwtGenerator = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();

        var person = await db.People
            .Include(p => p.User)
            .Include(p => p.PersonRoles).ThenInclude(pr => pr.Role)
            .FirstAsync(p => p.Id == personId);
        var roles = person.PersonRoles.Where(pr => pr.IsActive).Select(pr => pr.Role.Name).ToList();

        return jwtGenerator.GenerateToken(person.User!, person, roles);
    }

    // Ejecuta el handler JwtBearer configurado por AddInfrastructure, como lo hace el middleware de autenticación.
    private static async Task<AuthenticateResult> AuthenticateAsync(IServiceProvider provider, string token)
    {
        using var scope = provider.CreateScope();
        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        httpContext.Request.Headers.Authorization = $"Bearer {token}";

        return await httpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
    }

    private static async Task<bool> IsAuthorizedAsync(IServiceProvider provider, ClaimsPrincipal principal, string policy)
    {
        using var scope = provider.CreateScope();
        var authorizationService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var result = await authorizationService.AuthorizeAsync(principal, resource: null, policy);
        return result.Succeeded;
    }

    [Fact]
    public async Task AccountEndpoints_RequireAdministratorPolicy_SecretaryIsDenied()
    {
        var provider = await BuildProviderAsync();

        string[] accountActions =
        [
            nameof(UsersController.GetAccount),
            nameof(UsersController.Provision),
            nameof(UsersController.UpdateRoles),
            nameof(UsersController.UpdateStatus),
            nameof(UsersController.ResetPassword)
        ];
        foreach (var action in accountActions)
        {
            var authorize = typeof(UsersController).GetMethod(action)!
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
                .Cast<AuthorizeAttribute>()
                .Single();
            Assert.Equal(Policies.RequireAdministrator, authorize.Policy);
        }

        var admin = await CreateAdminPersonAsync(provider, $"admin_{Guid.NewGuid():N}@smartgym.com");

        Guid secretaryId;
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SmartGymDbContext>();
            var secretary = Person.Create("Sec", "Retaria", $"sec_{Guid.NewGuid():N}@smartgym.com");
            db.People.Add(secretary);
            await db.SaveChangesAsync();
            secretaryId = secretary.Id;
        }

        using (var scope = provider.CreateScope())
        {
            var accountService = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
            await accountService.ProvisionAsync(
                secretaryId, new ProvisionUserAccountRequest("SecretP@ss1", new List<int> { (int)RoleType.Secretary }));
        }

        var secretaryAuth = await AuthenticateAsync(provider, await IssueTokenAsync(provider, secretaryId));
        var adminAuth = await AuthenticateAsync(provider, await IssueTokenAsync(provider, admin.Id));

        Assert.True(secretaryAuth.Succeeded);
        Assert.True(adminAuth.Succeeded);
        Assert.False(await IsAuthorizedAsync(provider, secretaryAuth.Principal!, Policies.RequireAdministrator));
        Assert.True(await IsAuthorizedAsync(provider, adminAuth.Principal!, Policies.RequireAdministrator));
    }

    [Fact]
    public async Task FullLifecycle_And_CompositeKeyReassignment_AgainstRealPostgres()
    {
        var provider = await BuildProviderAsync();
        await CreateAdminPersonAsync(provider, $"admin_{Guid.NewGuid():N}@smartgym.com");

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartGymDbContext>();
        var accountService = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var person = Person.Create("New", "Member", $"member_{Guid.NewGuid():N}@smartgym.com");
        db.People.Add(person);
        await db.SaveChangesAsync();

        var initialAccount = await accountService.GetAccountAsync(person.Id);
        Assert.False(initialAccount.HasAccount);

        var provisioned = await accountService.ProvisionAsync(
            person.Id, new ProvisionUserAccountRequest("InitialP@ss1", new List<int> { 3, 4 }));
        Assert.True(provisioned.HasAccount);
        Assert.Equal(2, provisioned.Roles.Count);

        var afterRevoke = await accountService.UpdateRolesAsync(person.Id, new UpdateUserRolesRequest(new List<int> { 4 }));
        Assert.Equal(new[] { "Student" }, afterRevoke.Roles);

        var instructorRoleRow = await db.PersonRoles.AsNoTracking()
            .FirstOrDefaultAsync(pr => pr.PersonId == person.Id && pr.RoleId == 3);
        Assert.NotNull(instructorRoleRow);
        Assert.False(instructorRoleRow.IsActive);

        // Reasignar debe reactivar la fila existente: un INSERT violaría la clave (PersonId, RoleId).
        var afterReassign = await accountService.UpdateRolesAsync(person.Id, new UpdateUserRolesRequest(new List<int> { 3, 4 }));
        Assert.Equal(2, afterReassign.Roles.Count);
        Assert.Equal(1, await db.PersonRoles.AsNoTracking().CountAsync(pr => pr.PersonId == person.Id && pr.RoleId == 3));

        var suspended = await accountService.UpdateStatusAsync(person.Id, new UpdateUserStatusRequest(false));
        Assert.False(suspended.IsActive);

        await accountService.ResetPasswordAsync(person.Id, new AdminResetPasswordRequest("NewP@ssw0rd99!"));
        var userInDb = await db.Users.AsNoTracking().FirstAsync(u => u.PersonId == person.Id);
        Assert.True(hasher.VerifyPassword("NewP@ssw0rd99!", userInDb.PasswordHash!));

        var reactivated = await accountService.UpdateStatusAsync(person.Id, new UpdateUserStatusRequest(true));
        Assert.True(reactivated.IsActive);

        var auditActions = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityId == person.Id.ToString() || a.EntityId == userInDb.Id.ToString())
            .Select(a => a.Action)
            .ToListAsync();
        Assert.Contains("UserProvisioned", auditActions);
        Assert.Contains("UserRolesChanged", auditActions);
        Assert.Contains("UserSuspended", auditActions);
        Assert.Contains("UserPasswordReset", auditActions);
        Assert.Contains("UserReactivated", auditActions);
    }

    [Fact]
    public async Task LastAdministratorProtection_GuardsRolesSuspensionAndPersonStatus()
    {
        var provider = await BuildProviderAsync(isolatedDatabase: true);
        var soleAdmin = await CreateAdminPersonAsync(provider, $"sole_admin_{Guid.NewGuid():N}@smartgym.com");

        using var scope = provider.CreateScope();
        var accountService = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
        var peopleService = scope.ServiceProvider.GetRequiredService<IPeopleService>();

        var roleEx = await Assert.ThrowsAsync<ConflictException>(() =>
            accountService.UpdateRolesAsync(soleAdmin.Id, new UpdateUserRolesRequest(new List<int> { 4 })));
        Assert.Contains("último administrador", roleEx.Message, StringComparison.OrdinalIgnoreCase);

        var statusEx = await Assert.ThrowsAsync<ConflictException>(() =>
            accountService.UpdateStatusAsync(soleAdmin.Id, new UpdateUserStatusRequest(false)));
        Assert.Contains("último administrador", statusEx.Message, StringComparison.OrdinalIgnoreCase);

        var personStatusEx = await Assert.ThrowsAsync<ConflictException>(() =>
            peopleService.ChangeStatusAsync(soleAdmin.Id, new ChangePersonStatusRequest(PersonStatus.Inactive, "Inactivo")));
        Assert.Contains("último administrador", personStatusEx.Message, StringComparison.OrdinalIgnoreCase);

        // Con otro administrador operativo, las mismas operaciones se permiten.
        await CreateAdminPersonAsync(provider, $"second_admin_{Guid.NewGuid():N}@smartgym.com");
        using var freshScope = provider.CreateScope();
        var freshAccountService = freshScope.ServiceProvider.GetRequiredService<IUserAccountService>();
        var afterRevoke = await freshAccountService.UpdateRolesAsync(soleAdmin.Id, new UpdateUserRolesRequest(new List<int> { 4 }));
        Assert.DoesNotContain("Administrator", afterRevoke.Roles);
    }

    [Fact]
    public async Task ConcurrentRevocations_OfTwoLastAdmins_SerializedByAdvisoryLock()
    {
        var provider = await BuildProviderAsync(isolatedDatabase: true);
        var admin1 = await CreateAdminPersonAsync(provider, $"adm1_{Guid.NewGuid():N}@smartgym.com");
        var admin2 = await CreateAdminPersonAsync(provider, $"adm2_{Guid.NewGuid():N}@smartgym.com");

        async Task<bool> TryRevokeAdministratorAsync(Guid personId)
        {
            using var scope = provider.CreateScope();
            var accountService = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
            try
            {
                await accountService.UpdateRolesAsync(personId, new UpdateUserRolesRequest(new List<int> { 4 }));
                return true;
            }
            catch (ConflictException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(
            Task.Run(() => TryRevokeAdministratorAsync(admin1.Id)),
            Task.Run(() => TryRevokeAdministratorAsync(admin2.Id)));

        Assert.Equal(1, results.Count(succeeded => succeeded));

        using var verifyScope = provider.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<SmartGymDbContext>();
        var activeAdmins = await db.PersonRoles.AsNoTracking()
            .CountAsync(pr => pr.RoleId == (int)RoleType.Administrator && pr.IsActive);
        Assert.Equal(1, activeAdmins);
    }

    [Fact]
    public async Task SecurityStampRotation_RejectsPreviousTokensInJwtValidation()
    {
        var provider = await BuildProviderAsync();
        await CreateAdminPersonAsync(provider, $"admin_{Guid.NewGuid():N}@smartgym.com");
        var member = await CreateAdminPersonAsync(provider, $"member_token_{Guid.NewGuid():N}@smartgym.com");

        async Task RunAsync(Func<IUserAccountService, Task> operation)
        {
            using var scope = provider.CreateScope();
            await operation(scope.ServiceProvider.GetRequiredService<IUserAccountService>());
        }

        // Cambio de contraseña
        var token = await IssueTokenAsync(provider, member.Id);
        Assert.True((await AuthenticateAsync(provider, token)).Succeeded);
        await RunAsync(s => s.ResetPasswordAsync(member.Id, new AdminResetPasswordRequest("NewBrandPassword1!")));
        Assert.False((await AuthenticateAsync(provider, token)).Succeeded);

        // Cambio de roles
        token = await IssueTokenAsync(provider, member.Id);
        Assert.True((await AuthenticateAsync(provider, token)).Succeeded);
        await RunAsync(s => s.UpdateRolesAsync(member.Id, new UpdateUserRolesRequest(new List<int> { 1, 4 })));
        Assert.False((await AuthenticateAsync(provider, token)).Succeeded);

        // Suspensión: tampoco un token nuevo es aceptado mientras la cuenta esté suspendida.
        token = await IssueTokenAsync(provider, member.Id);
        Assert.True((await AuthenticateAsync(provider, token)).Succeeded);
        await RunAsync(s => s.UpdateStatusAsync(member.Id, new UpdateUserStatusRequest(false)));
        Assert.False((await AuthenticateAsync(provider, token)).Succeeded);
        Assert.False((await AuthenticateAsync(provider, await IssueTokenAsync(provider, member.Id))).Succeeded);
    }

    [Fact]
    public async Task SuspendedAccount_RejectedByLoginAsync()
    {
        var provider = await BuildProviderAsync();
        await CreateAdminPersonAsync(provider, $"admin_active_{Guid.NewGuid():N}@smartgym.com");

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartGymDbContext>();
        var accountService = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();

        var member = Person.Create("Suspended", "Member", $"susp_{Guid.NewGuid():N}@smartgym.com");
        db.People.Add(member);
        await db.SaveChangesAsync();

        await accountService.ProvisionAsync(member.Id, new ProvisionUserAccountRequest("UserP@ss1", new List<int> { 4 }));
        await accountService.UpdateStatusAsync(member.Id, new UpdateUserStatusRequest(false));

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            authService.LoginAsync(new LoginRequest(member.Email!, "UserP@ss1")));
        Assert.Contains("suspendida", ex.Message, StringComparison.OrdinalIgnoreCase);
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
