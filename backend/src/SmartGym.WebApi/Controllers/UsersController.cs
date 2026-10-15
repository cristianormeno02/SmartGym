using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Identity.Dtos;
using SmartGym.Application.Modules.Identity.Services;

namespace SmartGym.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly ISmartGymDbContext _dbContext;
    private readonly IFileStorageService _fileStorageService;
    private readonly IUserAccountService _userAccountService;

    public UsersController(
        ICurrentUserService currentUserService,
        ISmartGymDbContext dbContext,
        IFileStorageService fileStorageService,
        IUserAccountService userAccountService)
    {
        _currentUserService = currentUserService;
        _dbContext = dbContext;
        _fileStorageService = fileStorageService;
        _userAccountService = userAccountService;
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
        else
        {
            photoUrl = user.Person.ExternalAvatarUrl;
        }

        return Ok(new
        {
            userId = user.Id,
            personId = user.Person.Id,
            fullName = user.Person.FullName,
            email = user.Person.Email,
            documentType = user.Person.Document?.Type,
            documentNumber = user.Person.Document?.Number,
            dni = user.Person.Dni,
            phoneNumber = user.Person.PrimaryPhone,
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
                documentNumber = u.Person.Document != null ? u.Person.Document.Number : null,
                dni = u.Person.Document != null && u.Person.Document.Type == SmartGym.Domain.Enums.DocumentType.Dni ? u.Person.Document.Number : null,
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

    [HttpGet("{personId:guid}/account")]
    [Authorize(Policy = Policies.RequireAdministrator)]
    public async Task<IActionResult> GetAccount(Guid personId, CancellationToken cancellationToken)
    {
        try
        {
            var account = await _userAccountService.GetAccountAsync(personId, cancellationToken);
            return Ok(account);
        }
        catch (Exception ex) when (TryMapException(ex, out var error))
        {
            return error;
        }
    }

    [HttpPost("{personId:guid}/account")]
    [Authorize(Policy = Policies.RequireAdministrator)]
    public async Task<IActionResult> Provision(Guid personId, [FromBody] ProvisionUserAccountRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var account = await _userAccountService.ProvisionAsync(personId, request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, account);
        }
        catch (Exception ex) when (TryMapException(ex, out var error))
        {
            return error;
        }
    }

    [HttpPut("{personId:guid}/roles")]
    [Authorize(Policy = Policies.RequireAdministrator)]
    public async Task<IActionResult> UpdateRoles(Guid personId, [FromBody] UpdateUserRolesRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var account = await _userAccountService.UpdateRolesAsync(personId, request, cancellationToken);
            return Ok(account);
        }
        catch (Exception ex) when (TryMapException(ex, out var error))
        {
            return error;
        }
    }

    [HttpPatch("{personId:guid}/status")]
    [Authorize(Policy = Policies.RequireAdministrator)]
    public async Task<IActionResult> UpdateStatus(Guid personId, [FromBody] UpdateUserStatusRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var account = await _userAccountService.UpdateStatusAsync(personId, request, cancellationToken);
            return Ok(account);
        }
        catch (Exception ex) when (TryMapException(ex, out var error))
        {
            return error;
        }
    }

    [HttpPost("{personId:guid}/reset-password")]
    [Authorize(Policy = Policies.RequireAdministrator)]
    public async Task<IActionResult> ResetPassword(Guid personId, [FromBody] AdminResetPasswordRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await _userAccountService.ResetPasswordAsync(personId, request, cancellationToken);
            return NoContent();
        }
        catch (Exception ex) when (TryMapException(ex, out var error))
        {
            return error;
        }
    }

    private bool TryMapException(Exception ex, out IActionResult result)
    {
        IActionResult? mapped = ex switch
        {
            ValidationException vex => BadRequest(new { message = vex.Message, errors = vex.Errors }),
            NotFoundException nfe => NotFound(new { message = nfe.Message }),
            ForbiddenException fe => StatusCode(StatusCodes.Status403Forbidden, new { message = fe.Message }),
            ConflictException ce => Conflict(new { message = ce.Message }),
            ArgumentException aex => BadRequest(new { message = aex.Message }),
            InvalidOperationException ioex => Conflict(new { message = ioex.Message }),
            _ => null
        };

        result = mapped!;
        return mapped != null;
    }
}

