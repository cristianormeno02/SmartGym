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
        ValidateRegisterRequest(request);

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var normalizedDni = request.Dni.Trim();

        var emailInUse = await _dbContext.Users
            .AnyAsync(u => u.Username.ToLower() == normalizedEmail, cancellationToken);

        // El registro público nunca se vincula a una Persona existente: sin verificar la identidad,
        // cualquiera podría apropiarse de la ficha (y los roles) de otra persona usando su DNI o email.
        var personExists = await _dbContext.People
            .AnyAsync(p => p.Email.ToLower() == normalizedEmail || p.Dni == normalizedDni, cancellationToken);

        if (emailInUse || personExists)
        {
            throw new InvalidOperationException(
                "No se pudo completar el registro con los datos ingresados. Si ya sos alumno del gimnasio, solicitá el alta de tu cuenta en recepción.");
        }

        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Dni = normalizedDni,
            Email = normalizedEmail,
            PhoneNumber = request.PhoneNumber?.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };
        _dbContext.People.Add(person);

        var user = new User
        {
            Id = Guid.NewGuid(),
            PersonId = person.Id,
            Person = person,
            Username = normalizedEmail,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            IsEmailConfirmed = false,
            CreatedAtUtc = DateTime.UtcNow
        };
        _dbContext.Users.Add(user);
        person.User = user;

        // El registro público solo otorga el rol Alumno; los roles privilegiados se asignan explícitamente por administración.
        var studentRole = await _dbContext.Roles
            .FirstOrDefaultAsync(r => r.Id == (int)RoleType.Student, cancellationToken)
            ?? throw new InvalidOperationException("El rol Alumno no está configurado en el sistema.");
        person.AddRole(studentRole);

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

    private static void ValidateRegisterRequest(RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) ||
            string.IsNullOrWhiteSpace(request.LastName) ||
            string.IsNullOrWhiteSpace(request.Dni) ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            throw new ArgumentException("Nombre, apellido, DNI, correo electrónico y contraseña son obligatorios.");
        }
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
                Dni = null,
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
