using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Security;

namespace SmartGym.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly ISmartGymDbContext _dbContext;
    private readonly IFileStorageService _fileStorageService;

    public UsersController(
        ICurrentUserService currentUserService,
        ISmartGymDbContext dbContext,
        IFileStorageService fileStorageService)
    {
        _currentUserService = currentUserService;
        _dbContext = dbContext;
        _fileStorageService = fileStorageService;
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetCurrentUser(CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || !_currentUserService.UserId.HasValue)
        {
            return Unauthorized();
        }

        var user = await _dbContext.Users
            .Include(u => u.Person)
                .ThenInclude(p => p.PersonRoles)
                    .ThenInclude(pr => pr.Role)
            .FirstOrDefaultAsync(u => u.Id == _currentUserService.UserId.Value, cancellationToken);

        if (user == null)
        {
            return NotFound(new { message = "Usuario no encontrado." });
        }

        string? photoUrl = null;
        if (user.Person.ProfileImage?.Key != null)
        {
            photoUrl = await _fileStorageService.GetAccessUrlAsync(user.Person.ProfileImage.Key, TimeSpan.FromMinutes(15), cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(user.Person.ExternalAvatarUrl))
        {
            photoUrl = user.Person.ExternalAvatarUrl;
        }
        else
        {
            photoUrl = user.Person.PhotoUrl;
        }

        return Ok(new
        {
            userId = user.Id,
            personId = user.Person.Id,
            fullName = user.Person.FullName,
            email = user.Person.Email,
            documentType = user.Person.Document?.Type,
            documentNumber = user.Person.Document?.Number ?? user.Person.Dni,
            dni = user.Person.Dni,
            phoneNumber = user.Person.PhoneNumber,
            photoUrl = photoUrl,
            roles = user.Person.PersonRoles.Where(pr => pr.IsActive).Select(pr => pr.Role.Name).ToList()
        });
    }

    [HttpGet("admin-overview")]
    [Authorize(Policy = Policies.RequireAdministrator)]
    public async Task<IActionResult> GetAdminOverview(CancellationToken cancellationToken)
    {
        var users = await _dbContext.Users
            .Include(u => u.Person)
                .ThenInclude(p => p.PersonRoles)
                    .ThenInclude(pr => pr.Role)
            .Select(u => new
            {
                userId = u.Id,
                personId = u.Person.Id,
                fullName = u.Person.FullName,
                email = u.Person.Email,
                documentType = u.Person.Document != null ? (SmartGym.Domain.Enums.DocumentType?)u.Person.Document.Type : null,
                documentNumber = u.Person.Document != null ? u.Person.Document.Number : u.Person.Dni,
                dni = u.Person.Dni,
                isActive = u.IsActive,
                roles = u.Person.PersonRoles.Where(pr => pr.IsActive).Select(pr => pr.Role.Name).ToList()
            })
            .ToListAsync(cancellationToken);

        return Ok(users);
    }

    [HttpGet("staff-desk")]
    [Authorize(Policy = Policies.RequireStaff)]
    public IActionResult GetStaffDeskInfo()
    {
        return Ok(new
        {
            message = "Bienvenido al portal de recepción y administración.",
            staffUser = _currentUserService.Email,
            roles = _currentUserService.Roles
        });
    }
}
