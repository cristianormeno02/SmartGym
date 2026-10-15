using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Identity.Dtos;
using SmartGym.Application.Modules.Identity.Validators;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Identity.Services;

public class UserAccountService : IUserAccountService
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuditService _auditService;
    private readonly ILastAdministratorGuard _lastAdministratorGuard;
    private readonly IIdentityLock _identityLock;

    private readonly ProvisionUserAccountRequestValidator _provisionValidator = new();
    private readonly UpdateUserRolesRequestValidator _rolesValidator = new();
    private readonly UpdateUserStatusRequestValidator _statusValidator = new();
    private readonly AdminResetPasswordRequestValidator _resetPasswordValidator = new();

    public UserAccountService(
        ISmartGymDbContext dbContext,
        IPasswordHasher passwordHasher,
        ICurrentUserService currentUserService,
        IAuditService auditService,
        ILastAdministratorGuard lastAdministratorGuard,
        IIdentityLock identityLock)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _currentUserService = currentUserService;
        _auditService = auditService;
        _lastAdministratorGuard = lastAdministratorGuard;
        _identityLock = identityLock;
    }

    public async Task<UserAccountDto> GetAccountAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var person = await _dbContext.People
            .Include(p => p.User)
            .Include(p => p.PersonRoles).ThenInclude(pr => pr.Role)
            .FirstOrDefaultAsync(p => p.Id == personId, cancellationToken);

        if (person == null)
        {
            throw new NotFoundException("Persona no encontrada.");
        }

        return MapAccountDto(person);
    }

    public async Task<UserAccountDto> ProvisionAsync(
        Guid personId,
        ProvisionUserAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        var person = await _dbContext.People
            .Include(p => p.User)
            .Include(p => p.PersonRoles).ThenInclude(pr => pr.Role)
            .FirstOrDefaultAsync(p => p.Id == personId, cancellationToken);

        if (person == null)
        {
            throw new NotFoundException("Persona no encontrada.");
        }

        if (person.Status == PersonStatus.Deceased)
        {
            throw new ConflictException("No se pueden realizar cambios en una persona en estado FALLECIDA.");
        }

        if (person.User != null || await _dbContext.Users.AnyAsync(u => u.PersonId == personId, cancellationToken))
        {
            throw new ConflictException("La persona ya posee una cuenta de usuario.");
        }

        if (string.IsNullOrWhiteSpace(person.Email))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "Email", new[] { "La persona no posee un correo electrónico cargado. Complete el correo en la ficha antes de crear la cuenta." } }
            });
        }

        var normalizedEmail = person.Email.Trim().ToLowerInvariant();
        var emailInUse = await _dbContext.Users.AnyAsync(u => u.Username.ToLower() == normalizedEmail, cancellationToken);
        if (emailInUse)
        {
            throw new ConflictException("El correo electrónico de la persona ya está vinculado a otra cuenta de usuario.");
        }

        var validationResult = await _provisionValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.ToDictionary());
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            PersonId = person.Id,
            Username = normalizedEmail,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            IsEmailConfirmed = false,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = DateTime.UtcNow
        };

        person.SetUser(user);
        _dbContext.Users.Add(user);

        var desiredRoleIds = (request.RoleIds ?? new List<int>()).Distinct().ToHashSet();
        foreach (var roleId in desiredRoleIds)
        {
            var role = await _dbContext.Roles.FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken);
            if (role != null)
            {
                person.AddRole(role);
            }
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new ConflictException("El correo electrónico o la persona ya está vinculada a una cuenta de usuario.");
        }

        var assignedRoleNames = person.PersonRoles
            .Where(pr => pr.IsActive)
            .OrderBy(pr => pr.RoleId)
            .Select(pr => pr.Role?.Name ?? ((RoleType)pr.RoleId).ToString())
            .ToList();

        await _auditService.LogAsync(
            action: "UserProvisioned",
            entityName: nameof(User),
            entityId: user.Id.ToString(),
            oldValues: null,
            newValues: JsonSerializer.Serialize(new
            {
                UserId = user.Id,
                PersonId = person.Id,
                Username = user.Username,
                Roles = assignedRoleNames
            }),
            userId: _currentUserService.UserId,
            userEmail: _currentUserService.Email,
            ipAddress: null,
            cancellationToken: cancellationToken);

        return MapAccountDto(person);
    }

    public async Task<UserAccountDto> UpdateRolesAsync(
        Guid personId,
        UpdateUserRolesRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _rolesValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.ToDictionary());
        }

        var person = await _dbContext.People
            .Include(p => p.User)
            .Include(p => p.PersonRoles).ThenInclude(pr => pr.Role)
            .FirstOrDefaultAsync(p => p.Id == personId, cancellationToken);

        if (person == null)
        {
            throw new NotFoundException("Persona no encontrada.");
        }

        if (person.Status == PersonStatus.Deceased)
        {
            throw new ConflictException("No se pueden realizar cambios en una persona en estado FALLECIDA.");
        }

        var desiredRoleIds = (request.RoleIds ?? new List<int>()).Distinct().ToHashSet();
        var currentActiveRoleIds = person.PersonRoles.Where(pr => pr.IsActive).Select(pr => pr.RoleId).ToHashSet();

        if (desiredRoleIds.SetEquals(currentActiveRoleIds))
        {
            return MapAccountDto(person);
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        await _identityLock.AcquireRolesLockAsync(cancellationToken);

        bool wasOperationalAdmin = currentActiveRoleIds.Contains((int)RoleType.Administrator)
            && person.Status == PersonStatus.Active
            && person.User is { IsActive: true };

        bool willBeAdmin = desiredRoleIds.Contains((int)RoleType.Administrator);

        if (wasOperationalAdmin && !willBeAdmin)
        {
            await _lastAdministratorGuard.EnsureAnotherOperationalAdministratorAsync(person.Id, cancellationToken);
        }

        var rolesToRemove = currentActiveRoleIds.Except(desiredRoleIds).ToList();
        var rolesToAdd = desiredRoleIds.Except(currentActiveRoleIds).ToList();

        foreach (var roleId in rolesToRemove)
        {
            person.RemoveRole(roleId);
        }

        foreach (var roleId in rolesToAdd)
        {
            var role = await _dbContext.Roles.FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken);
            if (role != null)
            {
                person.AddRole(role);
            }
        }

        if (person.User != null)
        {
            person.User.UpdateSecurityStamp();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        var oldRoleNames = currentActiveRoleIds.OrderBy(id => id).Select(id => ((RoleType)id).ToString()).ToList();
        var newRoleNames = desiredRoleIds.OrderBy(id => id).Select(id => ((RoleType)id).ToString()).ToList();

        await _auditService.LogAsync(
            action: "UserRolesChanged",
            entityName: nameof(Person),
            entityId: person.Id.ToString(),
            oldValues: JsonSerializer.Serialize(new { Roles = oldRoleNames }),
            newValues: JsonSerializer.Serialize(new { Roles = newRoleNames }),
            userId: _currentUserService.UserId,
            userEmail: _currentUserService.Email,
            ipAddress: null,
            cancellationToken: cancellationToken);

        return MapAccountDto(person);
    }

    public async Task<UserAccountDto> UpdateStatusAsync(
        Guid personId,
        UpdateUserStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _statusValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.ToDictionary());
        }

        var person = await _dbContext.People
            .Include(p => p.User)
            .Include(p => p.PersonRoles).ThenInclude(pr => pr.Role)
            .FirstOrDefaultAsync(p => p.Id == personId, cancellationToken);

        if (person == null)
        {
            throw new NotFoundException("Persona no encontrada.");
        }

        if (person.User == null)
        {
            throw new NotFoundException("La persona no posee una cuenta de usuario.");
        }

        if (person.User.IsActive == request.IsActive)
        {
            return MapAccountDto(person);
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        await _identityLock.AcquireRolesLockAsync(cancellationToken);

        if (!request.IsActive)
        {
            // Suspending
            bool isOperationalAdmin = person.HasRole(RoleType.Administrator)
                && person.Status == PersonStatus.Active
                && person.User.IsActive;

            if (isOperationalAdmin)
            {
                await _lastAdministratorGuard.EnsureAnotherOperationalAdministratorAsync(person.Id, cancellationToken);
            }
        }

        bool oldStatus = person.User.IsActive;
        person.User.IsActive = request.IsActive;
        person.User.UpdateSecurityStamp();

        await _dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        string action = request.IsActive ? "UserReactivated" : "UserSuspended";

        await _auditService.LogAsync(
            action: action,
            entityName: nameof(User),
            entityId: person.User.Id.ToString(),
            oldValues: JsonSerializer.Serialize(new { IsActive = oldStatus }),
            newValues: JsonSerializer.Serialize(new { IsActive = request.IsActive }),
            userId: _currentUserService.UserId,
            userEmail: _currentUserService.Email,
            ipAddress: null,
            cancellationToken: cancellationToken);

        return MapAccountDto(person);
    }

    public async Task ResetPasswordAsync(
        Guid personId,
        AdminResetPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _resetPasswordValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.ToDictionary());
        }

        var person = await _dbContext.People
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.Id == personId, cancellationToken);

        if (person == null)
        {
            throw new NotFoundException("Persona no encontrada.");
        }

        if (person.User == null)
        {
            throw new NotFoundException("La persona no posee una cuenta de usuario.");
        }

        person.User.PasswordHash = _passwordHasher.HashPassword(request.NewPassword);
        person.User.UpdateSecurityStamp();

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            action: "UserPasswordReset",
            entityName: nameof(User),
            entityId: person.User.Id.ToString(),
            oldValues: null,
            newValues: null,
            userId: _currentUserService.UserId,
            userEmail: _currentUserService.Email,
            ipAddress: null,
            cancellationToken: cancellationToken);
    }

    private static UserAccountDto MapAccountDto(Person person)
    {
        var activeRoles = person.PersonRoles
            .Where(pr => pr.IsActive)
            .OrderBy(pr => pr.RoleId)
            .Select(pr => pr.Role?.Name ?? ((RoleType)pr.RoleId).ToString())
            .ToList();

        if (person.User == null)
        {
            return new UserAccountDto(
                HasAccount: false,
                UserId: null,
                Username: null,
                IsActive: null,
                LastLoginUtc: null,
                Roles: activeRoles);
        }

        return new UserAccountDto(
            HasAccount: true,
            UserId: person.User.Id,
            Username: person.User.Username,
            IsActive: person.User.IsActive,
            LastLoginUtc: person.User.LastLoginUtc,
            Roles: activeRoles);
    }
}
