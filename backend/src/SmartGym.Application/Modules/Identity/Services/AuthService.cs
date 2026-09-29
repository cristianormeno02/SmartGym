using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Identity.Dtos;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Identity.Services;

public class AuthService : IAuthService
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IGoogleTokenValidator _googleTokenValidator;

    public AuthService(
        ISmartGymDbContext dbContext,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        IGoogleTokenValidator googleTokenValidator)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _googleTokenValidator = googleTokenValidator;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var existingUser = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Username.ToLower() == request.Email.ToLower(), cancellationToken);

        if (existingUser != null)
        {
            throw new InvalidOperationException("Ya existe un usuario registrado con este correo electrónico.");
        }

        // Buscar si ya existe la Persona física (ej. cargada previamente por administración)
        var person = await _dbContext.People
            .Include(p => p.PersonRoles)
                .ThenInclude(pr => pr.Role)
            .FirstOrDefaultAsync(p => p.Email.ToLower() == request.Email.ToLower() || p.Dni == request.Dni, cancellationToken);

        if (person == null)
        {
            person = new Person
            {
                Id = Guid.NewGuid(),
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                Dni = request.Dni.Trim(),
                Email = request.Email.Trim().ToLowerInvariant(),
                PhoneNumber = request.PhoneNumber?.Trim(),
                CreatedAtUtc = DateTime.UtcNow
            };
            _dbContext.People.Add(person);
        }

        // Crear cuenta de usuario asociada
        var user = new User
        {
            Id = Guid.NewGuid(),
            PersonId = person.Id,
            Person = person,
            Username = request.Email.Trim().ToLowerInvariant(),
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            IsEmailConfirmed = false,
            CreatedAtUtc = DateTime.UtcNow
        };
        _dbContext.Users.Add(user);
        person.User = user;

        // Asignar rol por defecto (Student) o roles especificados
        var requestedRoles = (request.Roles != null && request.Roles.Count > 0)
            ? request.Roles
            : new List<string> { RoleType.Student.ToString() };

        var allRoles = await _dbContext.Roles.ToListAsync(cancellationToken);

        foreach (var roleName in requestedRoles)
        {
            var role = allRoles.FirstOrDefault(r => r.Name.Equals(roleName, StringComparison.OrdinalIgnoreCase));
            if (role != null)
            {
                person.AddRole(role);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var activeRoleNames = person.PersonRoles
            .Where(pr => pr.IsActive)
            .Select(pr => pr.Role.Name)
            .ToList();

        var token = _jwtTokenGenerator.GenerateToken(user, person, activeRoleNames);

        return new AuthResponse(
            Token: token,
            UserId: user.Id,
            PersonId: person.Id,
            Email: person.Email,
            FullName: person.FullName,
            Roles: activeRoleNames
        );
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .Include(u => u.Person)
                .ThenInclude(p => p.PersonRoles)
                    .ThenInclude(pr => pr.Role)
            .FirstOrDefaultAsync(u => u.Username.ToLower() == request.Email.ToLower(), cancellationToken);

        if (user == null || string.IsNullOrEmpty(user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Credenciales inválidas.");
        }

        if (user.IsLockedOut)
        {
            throw new UnauthorizedAccessException("La cuenta se encuentra temporalmente bloqueada.");
        }

        var isPasswordValid = _passwordHasher.VerifyPassword(request.Password, user.PasswordHash);
        if (!isPasswordValid)
        {
            throw new UnauthorizedAccessException("Credenciales inválidas.");
        }

        user.LastLoginUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var activeRoleNames = user.Person.PersonRoles
            .Where(pr => pr.IsActive)
            .Select(pr => pr.Role.Name)
            .ToList();

        var token = _jwtTokenGenerator.GenerateToken(user, user.Person, activeRoleNames);

        return new AuthResponse(
            Token: token,
            UserId: user.Id,
            PersonId: user.Person.Id,
            Email: user.Person.Email,
            FullName: user.Person.FullName,
            Roles: activeRoleNames
        );
    }

    public async Task<AuthResponse> LoginWithGoogleAsync(GoogleLoginRequest request, CancellationToken cancellationToken = default)
    {
        var googlePayload = await _googleTokenValidator.ValidateAsync(request.IdToken, cancellationToken);
        if (googlePayload == null || !googlePayload.EmailVerified)
        {
            throw new UnauthorizedAccessException("El token de Google es inválido o no ha sido verificado.");
        }

        var normalizedEmail = googlePayload.Email.Trim().ToLowerInvariant();

        // 1. Buscar si ya existe un usuario con este GoogleSubjectId
        var user = await _dbContext.Users
            .Include(u => u.Person)
                .ThenInclude(p => p.PersonRoles)
                    .ThenInclude(pr => pr.Role)
            .FirstOrDefaultAsync(u => u.GoogleSubjectId == googlePayload.SubjectId, cancellationToken);

        if (user != null)
        {
            if (user.IsLockedOut)
            {
                throw new UnauthorizedAccessException("La cuenta se encuentra temporalmente bloqueada.");
            }

            user.LastLoginUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            var roles = user.Person.PersonRoles.Where(pr => pr.IsActive).Select(pr => pr.Role.Name).ToList();
            var token = _jwtTokenGenerator.GenerateToken(user, user.Person, roles);

            return new AuthResponse(token, user.Id, user.Person.Id, user.Person.Email, user.Person.FullName, roles);
        }

        // 2. Buscar si existe un usuario registrado previamente con el mismo email (cuenta local)
        user = await _dbContext.Users
            .Include(u => u.Person)
                .ThenInclude(p => p.PersonRoles)
                    .ThenInclude(pr => pr.Role)
            .FirstOrDefaultAsync(u => u.Username.ToLower() == normalizedEmail, cancellationToken);

        if (user != null)
        {
            // Vincular identidad Google a la cuenta y persona existente
            user.GoogleSubjectId = googlePayload.SubjectId;
            user.IsEmailConfirmed = true;
            user.LastLoginUtc = DateTime.UtcNow;

            if (string.IsNullOrEmpty(user.Person.PhotoUrl) && !string.IsNullOrEmpty(googlePayload.PictureUrl))
            {
                user.Person.PhotoUrl = googlePayload.PictureUrl;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            var roles = user.Person.PersonRoles.Where(pr => pr.IsActive).Select(pr => pr.Role.Name).ToList();
            var token = _jwtTokenGenerator.GenerateToken(user, user.Person, roles);

            return new AuthResponse(token, user.Id, user.Person.Id, user.Person.Email, user.Person.FullName, roles);
        }

        // 3. Buscar si existe una Persona física registrada con ese email pero sin Usuario
        var person = await _dbContext.People
            .Include(p => p.PersonRoles)
                .ThenInclude(pr => pr.Role)
            .FirstOrDefaultAsync(p => p.Email.ToLower() == normalizedEmail, cancellationToken);

        if (person == null)
        {
            // Crear nueva Persona
            person = new Person
            {
                Id = Guid.NewGuid(),
                FirstName = string.IsNullOrWhiteSpace(googlePayload.GivenName) ? "Usuario" : googlePayload.GivenName.Trim(),
                LastName = googlePayload.FamilyName?.Trim() ?? string.Empty,
                Email = normalizedEmail,
                Dni = string.Empty,
                PhotoUrl = googlePayload.PictureUrl,
                CreatedAtUtc = DateTime.UtcNow
            };
            _dbContext.People.Add(person);
        }

        user = new User
        {
            Id = Guid.NewGuid(),
            PersonId = person.Id,
            Person = person,
            Username = normalizedEmail,
            GoogleSubjectId = googlePayload.SubjectId,
            IsEmailConfirmed = true,
            LastLoginUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        };
        _dbContext.Users.Add(user);
        person.User = user;

        // Asignar rol Student por defecto si no tiene roles
        if (!person.PersonRoles.Any())
        {
            var studentRole = await _dbContext.Roles
                .FirstOrDefaultAsync(r => r.Id == (int)RoleType.Student, cancellationToken);
            if (studentRole != null)
            {
                person.AddRole(studentRole);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var activeRoles = person.PersonRoles.Where(pr => pr.IsActive).Select(pr => pr.Role.Name).ToList();
        var newToken = _jwtTokenGenerator.GenerateToken(user, person, activeRoles);

        return new AuthResponse(newToken, user.Id, person.Id, person.Email, person.FullName, activeRoles);
    }
}
