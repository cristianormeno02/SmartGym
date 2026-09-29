using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Memberships.Dtos;
using SmartGym.Application.Modules.Memberships.Services;

namespace SmartGym.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CreditsController : ControllerBase
{
    private readonly ICreditLedgerService _creditLedgerService;
    private readonly ICurrentUserService _currentUserService;

    public CreditsController(
        ICreditLedgerService creditLedgerService,
        ICurrentUserService currentUserService)
    {
        _creditLedgerService = creditLedgerService;
        _currentUserService = currentUserService;
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

        var movements = await _creditLedgerService.GetMovementsByStudentAsync(studentId, cancellationToken);
        return Ok(movements);
    }

    [HttpGet("membership/{membershipId:guid}")]
    [Authorize]
    public async Task<IActionResult> GetByMembershipId(Guid membershipId, CancellationToken cancellationToken)
    {
        var movements = await _creditLedgerService.GetMovementsByMembershipAsync(membershipId, cancellationToken);
        return Ok(movements);
    }

    [HttpPost("compensatory")]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> AddCompensatoryCredit([FromBody] AddCompensatoryCreditRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _currentUserService.UserId ?? Guid.Empty;
            var movement = await _creditLedgerService.AddCompensatoryCreditAsync(request, userId, cancellationToken);
            return Ok(movement);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("single-ticket")]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> PurchaseSingleTicket([FromBody] PurchaseSingleClassTicketRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _currentUserService.UserId ?? Guid.Empty;
            var movement = await _creditLedgerService.PurchaseSingleClassTicketAsync(request, userId, cancellationToken);
            return Ok(movement);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
