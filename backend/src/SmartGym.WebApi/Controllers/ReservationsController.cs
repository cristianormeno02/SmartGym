using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Reservations.Dtos;
using SmartGym.Application.Modules.Reservations.Services;

namespace SmartGym.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReservationsController : ControllerBase
{
    private readonly IReservationService _reservationService;
    private readonly ICurrentUserService _currentUserService;

    public ReservationsController(
        IReservationService reservationService,
        ICurrentUserService currentUserService)
    {
        _reservationService = reservationService;
        _currentUserService = currentUserService;
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateReservation([FromBody] CreateReservationRequest request, CancellationToken cancellationToken)
    {
        // El alumno solo puede reservar para sí mismo a menos que sea Staff
        if (_currentUserService.PersonId.HasValue &&
            _currentUserService.PersonId.Value != request.StudentId &&
            !_currentUserService.IsInRole(Roles.Administrator) &&
            !_currentUserService.IsInRole(Roles.Secretary))
        {
            return Forbid();
        }

        try
        {
            var userId = _currentUserService.UserId ?? Guid.Empty;
            var reservation = await _reservationService.CreateReservationAsync(request, userId, cancellationToken);
            return Ok(reservation);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize]
    public async Task<IActionResult> CancelReservation(Guid id, [FromBody] CancelReservationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _currentUserService.UserId ?? Guid.Empty;
            var cancelled = await _reservationService.CancelReservationAsync(id, request, userId, cancellationToken);
            return Ok(cancelled);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
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
            !_currentUserService.IsInRole(Roles.Secretary) &&
            !_currentUserService.IsInRole(Roles.Instructor))
        {
            return Forbid();
        }

        var list = await _reservationService.GetByStudentAsync(studentId, cancellationToken);
        return Ok(list);
    }

    [HttpGet("session/{classSessionId:guid}")]
    [Authorize(Policy = Policies.RequireTeachingAndStaff)]
    public async Task<IActionResult> GetByClassSession(Guid classSessionId, CancellationToken cancellationToken)
    {
        var list = await _reservationService.GetByClassSessionAsync(classSessionId, cancellationToken);
        return Ok(list);
    }

    [HttpGet("session/{classSessionId:guid}/attendance-summary")]
    [Authorize(Policy = Policies.RequireTeachingAndStaff)]
    public async Task<IActionResult> GetSessionAttendanceSummary(Guid classSessionId, CancellationToken cancellationToken)
    {
        try
        {
            var summary = await _reservationService.GetSessionAttendanceSummaryAsync(classSessionId, cancellationToken);
            return Ok(summary);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("attendance")]
    [Authorize(Policy = Policies.RequireTeachingAndStaff)]
    public async Task<IActionResult> RecordAttendance([FromBody] RecordAttendanceRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _currentUserService.UserId ?? Guid.Empty;
            var result = await _reservationService.RecordAttendanceAsync(request, userId, cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/no-show")]
    [Authorize(Policy = Policies.RequireTeachingAndStaff)]
    public async Task<IActionResult> RecordNoShow(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _currentUserService.UserId ?? Guid.Empty;
            var result = await _reservationService.RecordNoShowAsync(id, userId, cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
