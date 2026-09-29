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

    public UsersController(
        ICurrentUserService currentUserService,
        ISmartGymDbContext dbContext)
    {
        _currentUserService = currentUserService;
        _dbContext = dbContext;
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

        return Ok(new
        {
            userId = user.Id,
            personId = user.Person.Id,
            fullName = user.Person.FullName,
            email = user.Person.Email,
            dni = user.Person.Dni,
            phoneNumber = user.Person.PhoneNumber,
            photoUrl = user.Person.PhotoUrl,
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
