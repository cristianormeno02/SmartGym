using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Identity.Services;

public class AdminUserSeeder : IAdminUserSeeder
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly AdminSeedOptions _options;
    private readonly ILogger<AdminUserSeeder> _logger;

    public AdminUserSeeder(
        ISmartGymDbContext dbContext,
        IPasswordHasher passwordHasher,
        IOptions<AdminSeedOptions> options,
        ILogger<AdminUserSeeder> logger)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AdminSeedOutcome> SeedAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Email) || string.IsNullOrWhiteSpace(_options.Password))
        {
            return AdminSeedOutcome.NotConfigured;
        }

        var email = _options.Email.Trim().ToLowerInvariant();
        if (!MailAddress.TryCreate(email, out _) || _options.Password.Length < AdminSeedOptions.MinimumPasswordLength)
        {
            _logger.LogError(
                "AdminSeed: configuración inválida. Se requiere un email válido y una contraseña de al menos {MinimumLength} caracteres. No se creó el administrador inicial.",
                AdminSeedOptions.MinimumPasswordLength);
            return AdminSeedOutcome.InvalidConfiguration;
        }

        var administratorExists = await _dbContext.People
            .AnyAsync(p => p.PersonRoles.Any(pr => pr.RoleId == (int)RoleType.Administrator && pr.IsActive), cancellationToken);
        if (administratorExists)
        {
            return AdminSeedOutcome.AdministratorAlreadyExists;
        }

        // No se promueve una cuenta existente: quien controla la configuración no debe poder
        // apropiarse de la ficha (ni cambiar la contraseña) de otra persona.
        var emailInUse =
            await _dbContext.Users.AnyAsync(u => u.Username.ToLower() == email, cancellationToken) ||
            await _dbContext.People.AnyAsync(p => p.Email != null && p.Email.ToLower() == email, cancellationToken);
        if (emailInUse)
        {
            _logger.LogWarning(
                "AdminSeed: el email configurado ya pertenece a una cuenta o persona existente. No se creó el administrador inicial.");
            return AdminSeedOutcome.EmailAlreadyInUse;
        }

        var administratorRole = await _dbContext.Roles
            .FirstOrDefaultAsync(r => r.Id == (int)RoleType.Administrator, cancellationToken)
            ?? throw new InvalidOperationException("El rol Administrador no está configurado en el sistema.");

        var person = Person.Create(_options.FirstName, _options.LastName, email);
        _dbContext.People.Add(person);

        var user = new User
        {
            Id = Guid.NewGuid(),
            PersonId = person.Id,
            Person = person,
            Username = email,
            PasswordHash = _passwordHasher.HashPassword(_options.Password),
            IsEmailConfirmed = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        _dbContext.Users.Add(user);
        person.User = user;
        person.AddRole(administratorRole);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("AdminSeed: se creó el administrador inicial {Email}.", email);
        return AdminSeedOutcome.Created;
    }
}
