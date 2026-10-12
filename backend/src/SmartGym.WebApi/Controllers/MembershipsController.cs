using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartGym.Application.Common.Exceptions;
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
    private readonly IMembershipEligibilityService _eligibilityService;
    private readonly ICreditLedgerService _creditLedgerService;
    private readonly ICurrentUserService _currentUserService;

    public MembershipsController(
        IMembershipService membershipService,
        IMembershipEligibilityService eligibilityService,
        ICreditLedgerService creditLedgerService,
        ICurrentUserService currentUserService)
    {
        _membershipService = membershipService;
        _eligibilityService = eligibilityService;
        _creditLedgerService = creditLedgerService;
        _currentUserService = currentUserService;
    }

    [HttpGet]
    [Authorize(Policy = Policies.MembershipsRead)]
    public async Task<IActionResult> GetPaged(
        [FromQuery] MembershipListFilter filter,
        CancellationToken cancellationToken)
    {
        if (!IsStaff())
        {
            return Forbid();
        }

        var result = await _membershipService.GetPagedAsync(filter, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        return await ExecuteAuthorizedByMembershipIdAsync(
            id,
            async detail => await Task.FromResult<IActionResult>(Ok(detail)),
            cancellationToken);
    }

    [HttpGet("student/{studentId:guid}")]
    [Authorize]
    public async Task<IActionResult> GetByStudentId(Guid studentId, CancellationToken cancellationToken)
    {
        if (!CanAccessStudentData(studentId))
        {
            return Forbid();
        }

        var memberships = await _membershipService.GetByStudentIdAsync(studentId, cancellationToken);
        return Ok(memberships);
    }

    [HttpGet("student/{studentId:guid}/current")]
    [Authorize]
    public async Task<IActionResult> GetCurrentByStudentId(Guid studentId, CancellationToken cancellationToken)
    {
        if (!CanAccessStudentData(studentId))
        {
            return Forbid();
        }

        var memberships = await _membershipService.GetCurrentByStudentIdAsync(studentId, cancellationToken);
        return Ok(memberships);
    }

    [HttpGet("student/{studentId:guid}/active")]
    [Authorize]
    public async Task<IActionResult> GetActiveMembership(
        Guid studentId,
        [FromQuery] DateOnly? date,
        CancellationToken cancellationToken)
    {
        if (!CanAccessStudentData(studentId))
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

    [HttpGet("{id:guid}/eligibility")]
    [Authorize]
    public async Task<IActionResult> GetEligibility(
        Guid id,
        [FromQuery] DateOnly? date,
        [FromQuery] Guid? activityId,
        [FromQuery] Guid? classSessionId,
        CancellationToken cancellationToken)
    {
        return await ExecuteAuthorizedByMembershipIdAsync(
            id,
            async _ =>
            {
                var eligibility = await _eligibilityService.EvaluateEligibilityAsync(
                    id,
                    date,
                    activityId,
                    classSessionId,
                    cancellationToken);
                return Ok(eligibility);
            },
            cancellationToken);
    }

    [HttpGet("{id:guid}/credits")]
    [Authorize]
    public async Task<IActionResult> GetCreditsSummary(Guid id, CancellationToken cancellationToken)
    {
        return await ExecuteAuthorizedByMembershipIdAsync(
            id,
            async _ =>
            {
                var summary = await _creditLedgerService.GetMembershipCreditsSummaryAsync(id, cancellationToken);
                return Ok(summary);
            },
            cancellationToken);
    }

    [HttpPost]
    [Authorize(Policy = Policies.MembershipsCreate)]
    public async Task<IActionResult> Create(
        [FromBody] CreateMembershipRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsStaff())
        {
            return Forbid();
        }

        return await ExecuteWithExceptionMappingAsync(async () =>
        {
            var effectiveRequest = ApplyHeaderIdempotencyKeyIfMissing(request);
            var created = await _membershipService.CreateAsync(
                effectiveRequest,
                _currentUserService.UserId,
                _currentUserService.IsInRole(Roles.Administrator),
                cancellationToken);

            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        });
    }

    [HttpPost("assign")]
    [Authorize(Policy = Policies.MembershipsCreate)]
    public async Task<IActionResult> AssignMembership(
        [FromBody] AssignMembershipRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsStaff())
        {
            return Forbid();
        }

        return await ExecuteWithExceptionMappingAsync(async () =>
        {
            var created = await _membershipService.CreateAsync(
                new CreateMembershipRequest(
                    StudentId: request.StudentId,
                    MembershipPlanId: request.MembershipPlanId,
                    StartDate: request.StartDate,
                    ActivateImmediately: true,
                    ActivationReason: "Asignación directa por personal"),
                _currentUserService.UserId,
                isCallerAdministrator: true,
                cancellationToken);

            return Ok(created);
        });
    }

    [HttpPatch("{id:guid}/notes")]
    [Authorize(Policy = Policies.MembershipsManage)]
    public async Task<IActionResult> UpdateNotes(
        Guid id,
        [FromBody] UpdateMembershipNotesRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsStaff())
        {
            return Forbid();
        }

        return await ExecuteWithExceptionMappingAsync(async () =>
        {
            var updated = await _membershipService.UpdateNotesAsync(
                id,
                request,
                _currentUserService.UserId,
                cancellationToken);
            return Ok(updated);
        });
    }

    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = Policies.MembershipsActivate)]
    public async Task<IActionResult> Activate(
        Guid id,
        [FromBody] ActivateMembershipRequest? request,
        CancellationToken cancellationToken)
    {
        if (!IsStaff())
        {
            return Forbid();
        }

        return await ExecuteWithExceptionMappingAsync(async () =>
        {
            var activated = await _membershipService.ActivateAsync(
                id,
                request ?? new ActivateMembershipRequest(),
                _currentUserService.UserId,
                _currentUserService.IsInRole(Roles.Administrator),
                cancellationToken);
            return Ok(activated);
        });
    }

    [HttpPost("{id:guid}/suspend")]
    [Authorize(Policy = Policies.MembershipsSuspend)]
    public async Task<IActionResult> Suspend(
        Guid id,
        [FromBody] SuspendMembershipRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsStaff())
        {
            return Forbid();
        }

        return await ExecuteWithExceptionMappingAsync(async () =>
        {
            var suspended = await _membershipService.SuspendAsync(
                id,
                request,
                _currentUserService.UserId ?? Guid.Empty,
                cancellationToken);
            return Ok(suspended);
        });
    }

    [HttpPost("{id:guid}/reactivate")]
    [Authorize(Policy = Policies.MembershipsReactivate)]
    public async Task<IActionResult> Reactivate(
        Guid id,
        [FromBody] ReactivateMembershipRequest? request,
        CancellationToken cancellationToken)
    {
        if (!IsStaff())
        {
            return Forbid();
        }

        return await ExecuteWithExceptionMappingAsync(async () =>
        {
            var reactivated = await _membershipService.ReactivateAsync(
                id,
                request ?? new ReactivateMembershipRequest(),
                _currentUserService.UserId ?? Guid.Empty,
                cancellationToken);
            return Ok(reactivated);
        });
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = Policies.MembershipsCancel)]
    public async Task<IActionResult> Cancel(
        Guid id,
        [FromBody] CancelMembershipRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsStaff())
        {
            return Forbid();
        }

        return await ExecuteWithExceptionMappingAsync(async () =>
        {
            var cancelled = await _membershipService.CancelAsync(
                id,
                request,
                _currentUserService.UserId,
                cancellationToken);
            return Ok(cancelled);
        });
    }

    [HttpPost("{id:guid}/renew")]
    [Authorize(Policy = Policies.MembershipsRenew)]
    public async Task<IActionResult> Renew(
        Guid id,
        [FromBody] RenewMembershipRequest? request,
        CancellationToken cancellationToken)
    {
        if (!IsStaff())
        {
            return Forbid();
        }

        return await ExecuteWithExceptionMappingAsync(async () =>
        {
            var effectiveRequest = ApplyHeaderIdempotencyKeyIfMissing(request ?? new RenewMembershipRequest());
            var renewed = await _membershipService.RenewAsync(
                id,
                effectiveRequest,
                _currentUserService.UserId,
                _currentUserService.IsInRole(Roles.Administrator),
                cancellationToken);

            return CreatedAtAction(nameof(GetById), new { id = renewed.Id }, renewed);
        });
    }

    private bool IsStaff()
    {
        return _currentUserService.IsInRole(Roles.Administrator) ||
               _currentUserService.IsInRole(Roles.Secretary);
    }

    private bool CanAccessStudentData(Guid studentId)
    {
        if (IsStaff())
        {
            return true;
        }

        if (_currentUserService.IsInRole(Roles.Student) &&
            _currentUserService.PersonId.HasValue &&
            _currentUserService.PersonId.Value == studentId)
        {
            return true;
        }

        return false;
    }

    private async Task<IActionResult> ExecuteAuthorizedByMembershipIdAsync(
        Guid membershipId,
        Func<MembershipDetailDto, Task<IActionResult>> action,
        CancellationToken cancellationToken)
    {
        return await ExecuteWithExceptionMappingAsync(async () =>
        {
            var detail = await _membershipService.GetByIdAsync(membershipId, cancellationToken);
            if (!CanAccessStudentData(detail.StudentId))
            {
                return Forbid();
            }

            return await action(detail);
        });
    }

    private CreateMembershipRequest ApplyHeaderIdempotencyKeyIfMissing(CreateMembershipRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return request;
        }

        var headerKey = ReadIdempotencyKeyFromHeader();
        return string.IsNullOrWhiteSpace(headerKey)
            ? request
            : request with { IdempotencyKey = headerKey };
    }

    private RenewMembershipRequest ApplyHeaderIdempotencyKeyIfMissing(RenewMembershipRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return request;
        }

        var headerKey = ReadIdempotencyKeyFromHeader();
        return string.IsNullOrWhiteSpace(headerKey)
            ? request
            : request with { IdempotencyKey = headerKey };
    }

    private string? ReadIdempotencyKeyFromHeader()
    {
        if (HttpContext?.Request?.Headers != null &&
            HttpContext.Request.Headers.TryGetValue("Idempotency-Key", out var values))
        {
            var raw = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                return raw.Trim();
            }
        }

        return null;
    }

    private async Task<IActionResult> ExecuteWithExceptionMappingAsync(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (ForbiddenException)
        {
            return Forbid();
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ConflictException ex)
        {
            return Conflict(new { message = ex.Message, details = ex.Details });
        }
    }
}
