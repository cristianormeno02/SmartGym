using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Memberships.Dtos;
using SmartGym.Application.Modules.Memberships.Services;

namespace SmartGym.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MembershipsController : ControllerBase
{
    private readonly IMembershipService _membershipService;
    private readonly ICurrentUserService _currentUserService;

    public MembershipsController(
        IMembershipService membershipService,
        ICurrentUserService currentUserService)
    {
        _membershipService = membershipService;
        _currentUserService = currentUserService;
    }

    [HttpPost("assign")]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> AssignMembership([FromBody] AssignMembershipRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var membership = await _membershipService.AssignMembershipAsync(request, cancellationToken);
            return Ok(membership);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("student/{studentId:guid}")]
    [Authorize]
    public async Task<IActionResult> GetByStudentId(Guid studentId, CancellationToken cancellationToken)
    {
        if (_currentUserService.PersonId.HasValue &&
            _currentUserService.PersonId.Value != studentId &&
            !_currentUserService.IsInRole(Roles.Administrator) &&
            !_currentUserService.IsInRole(Roles.Secretary))
        {
            return Forbid();
        }

        var memberships = await _membershipService.GetByStudentIdAsync(studentId, cancellationToken);
        return Ok(memberships);
    }

    [HttpGet("student/{studentId:guid}/active")]
    [Authorize]
    public async Task<IActionResult> GetActiveMembership(Guid studentId, [FromQuery] DateOnly? date, CancellationToken cancellationToken)
    {
        if (_currentUserService.PersonId.HasValue &&
            _currentUserService.PersonId.Value != studentId &&
            !_currentUserService.IsInRole(Roles.Administrator) &&
            !_currentUserService.IsInRole(Roles.Secretary))
        {
            return Forbid();
        }

        var membership = await _membershipService.GetActiveMembershipAsync(studentId, date, cancellationToken);
        if (membership == null)
        {
            return NotFound(new { message = "El alumno no cuenta con una membresía activa en la fecha indicada." });
        }

        return Ok(membership);
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelMembershipRequest request, CancellationToken cancellationToken)
    {
        var cancelled = await _membershipService.CancelMembershipAsync(id, request.Reason, cancellationToken);
        if (cancelled == null)
        {
            return NotFound(new { message = "Membresía no encontrada." });
        }

        return Ok(cancelled);
    }
}
