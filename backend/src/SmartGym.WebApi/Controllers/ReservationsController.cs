using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Reservations.Dtos;
using SmartGym.Application.Modules.Reservations.Services;

namespace SmartGym.WebApi.Controllers;

/// <summary>Contrato de error de <c>/api/reservations</c>: código estable, mensaje y motivos de inelegibilidad.</summary>
public record ReservationApiError(string Code, string Message, object? Reasons = null);

/// <summary>
/// Las políticas habilitan cada endpoint por rol; el ámbito (alumno dueño, instructor de la clase)
/// lo valida <see cref="IReservationService"/> con el usuario autenticado.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ReservationsController : ControllerBase
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    private readonly IReservationService _reservationService;
    private readonly ICurrentUserService _currentUserService;

    public ReservationsController(
        IReservationService reservationService,
        ICurrentUserService currentUserService)
    {
        _reservationService = reservationService;
        _currentUserService = currentUserService;
    }

    // ---- Consultas ----

    [HttpGet]
    [Authorize(Policy = Policies.ReservationsRead)]
    public Task<IActionResult> GetPaged([FromQuery] ReservationFilterRequest filter, CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await _reservationService.GetPagedAsync(filter, cancellationToken)));

    [HttpGet("{id:guid}")]
    [Authorize]
    public Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await _reservationService.GetByIdAsync(id, cancellationToken)));

    [HttpGet("student/{studentId:guid}")]
    [Authorize]
    public Task<IActionResult> GetByStudentId(
        Guid studentId,
        [FromQuery] StudentReservationHistoryFilterRequest filter,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await _reservationService.GetByStudentAsync(studentId, filter, cancellationToken)));

    [HttpGet("my")]
    [Authorize(Policy = Policies.RequireStudent)]
    public Task<IActionResult> GetMine(
        [FromQuery] StudentReservationHistoryFilterRequest filter,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await _reservationService.GetMyReservationsAsync(filter, cancellationToken)));

    [HttpGet("my/upcoming")]
    [Authorize(Policy = Policies.RequireStudent)]
    public Task<IActionResult> GetMyUpcoming(CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await _reservationService.GetMyUpcomingAsync(cancellationToken)));

    [HttpGet("available-classes")]
    [Authorize(Policy = Policies.ReservationsCreate)]
    public Task<IActionResult> GetAvailableClasses(
        [FromQuery] AvailableClassesFilterRequest filter,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await _reservationService.GetAvailableClassesAsync(filter, cancellationToken)));

    [HttpGet("class-session/{classSessionId:guid}")]
    [HttpGet("session/{classSessionId:guid}")]
    [Authorize(Policy = Policies.RequireTeachingAndStaff)]
    public Task<IActionResult> GetByClassSession(Guid classSessionId, CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await _reservationService.GetByClassSessionAsync(classSessionId, cancellationToken)));

    [HttpGet("class-session/{classSessionId:guid}/waitlist")]
    [Authorize(Policy = Policies.ReservationsWaitlistRead)]
    public Task<IActionResult> GetWaitlist(Guid classSessionId, CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await _reservationService.GetWaitlistAsync(classSessionId, cancellationToken)));

    [HttpGet("class-session/{classSessionId:guid}/attendance-summary")]
    [HttpGet("session/{classSessionId:guid}/attendance-summary")]
    [Authorize(Policy = Policies.RequireTeachingAndStaff)]
    public Task<IActionResult> GetSessionAttendanceSummary(Guid classSessionId, CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await _reservationService.GetSessionAttendanceSummaryAsync(classSessionId, cancellationToken)));

    // ---- Reservas y lista de espera ----

    [HttpPost]
    [Authorize(Policy = Policies.ReservationsCreate)]
    public Task<IActionResult> CreateReservation([FromBody] CreateReservationRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var reservation = await _reservationService.CreateReservationAsync(
                request with { IdempotencyKey = ReadIdempotencyKey() ?? request.IdempotencyKey },
                CurrentUserId,
                cancellationToken);

            return CreatedOrReplayed(reservation);
        });

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = Policies.ReservationsCancel)]
    public Task<IActionResult> CancelReservation(Guid id, [FromBody] CancelReservationRequest? request, CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await _reservationService.CancelReservationAsync(
            id,
            request ?? new CancelReservationRequest(),
            CurrentUserId,
            cancellationToken)));

    [HttpPost("{id:guid}/accept-waitlist-offer")]
    [Authorize(Policy = Policies.ReservationsCreate)]
    public Task<IActionResult> AcceptWaitlistOffer(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await _reservationService.AcceptWaitlistOfferAsync(id, CurrentUserId, cancellationToken)));

    [HttpPost("{id:guid}/decline-waitlist-offer")]
    [Authorize(Policy = Policies.ReservationsCancel)]
    public Task<IActionResult> DeclineWaitlistOffer(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await _reservationService.DeclineWaitlistOfferAsync(id, CurrentUserId, cancellationToken)));

    [HttpPost("class-session/{classSessionId:guid}/waitlist")]
    [Authorize(Policy = Policies.ReservationsCreate)]
    public Task<IActionResult> JoinWaitlist(Guid classSessionId, [FromBody] JoinWaitlistRequest? request, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var body = request ?? new JoinWaitlistRequest();
            var reservation = await _reservationService.JoinWaitlistAsync(
                classSessionId,
                body with { IdempotencyKey = ReadIdempotencyKey() ?? body.IdempotencyKey },
                CurrentUserId,
                cancellationToken);

            return CreatedOrReplayed(reservation);
        });

    [HttpDelete("class-session/{classSessionId:guid}/waitlist/me")]
    [Authorize(Policy = Policies.RequireStudent)]
    public Task<IActionResult> LeaveWaitlist(Guid classSessionId, CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await _reservationService.LeaveWaitlistAsync(classSessionId, CurrentUserId, cancellationToken)));

    // ---- Asistencia ----

    [HttpPost("attendance")]
    [Authorize(Policy = Policies.RequireTeachingAndStaff)]
    public Task<IActionResult> RecordAttendance([FromBody] RecordAttendanceRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await _reservationService.RecordAttendanceAsync(request, CurrentUserId, cancellationToken)));

    [HttpPost("{id:guid}/no-show")]
    [Authorize(Policy = Policies.RequireTeachingAndStaff)]
    public Task<IActionResult> RecordNoShow(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await _reservationService.RecordNoShowAsync(id, CurrentUserId, cancellationToken)));

    // ---- Mostrador y Check-in Directo ----

    [HttpGet("desk-eligibility")]
    [Authorize(Policy = Policies.RequireStaff)]
    public Task<IActionResult> GetDeskEligibility(
        [FromQuery] Guid classSessionId,
        [FromQuery] Guid studentId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await _reservationService.GetDeskEligibilityAsync(classSessionId, studentId, cancellationToken)));

    [HttpPost("desk-drop-in")]
    [Authorize(Policy = Policies.RequireStaff)]
    public Task<IActionResult> CheckInDeskDropIn(
        [FromBody] DeskDropInRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var key = ReadIdempotencyKey() ?? request.IdempotencyKey;
            var effectiveRequest = string.IsNullOrWhiteSpace(key) ? request : request with { IdempotencyKey = key };
            var response = await _reservationService.CheckInDeskDropInAsync(effectiveRequest, CurrentUserId, cancellationToken);
            return response.WasIdempotentReplay
                ? Ok(response)
                : StatusCode(StatusCodes.Status201Created, response);
        });

    [HttpPost("{id:guid}/revoke-attendance")]
    [Authorize(Policy = Policies.RequireStaff)]
    public Task<IActionResult> RevokeAttendance(
        Guid id,
        [FromBody] RevokeAttendanceRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await _reservationService.RevokeAttendanceAsync(id, request, CurrentUserId, cancellationToken)));

    private Guid CurrentUserId => _currentUserService.UserId ?? Guid.Empty;

    private string? ReadIdempotencyKey()
    {
        var value = Request.Headers[IdempotencyKeyHeader].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private IActionResult CreatedOrReplayed(ReservationDto reservation) =>
        reservation.WasIdempotentReplay
            ? Ok(reservation)
            : CreatedAtAction(nameof(GetById), new { id = reservation.Id }, reservation);

    private async Task<IActionResult> ExecuteAsync(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ForbiddenException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ReservationApiError("FORBIDDEN", ex.Message));
        }
        catch (NotFoundException ex)
        {
            return NotFound(new ReservationApiError("NOT_FOUND", ex.Message));
        }
        catch (ConflictException ex)
        {
            return Conflict(new ReservationApiError(ex.Code ?? "CONFLICT", ex.Message, ex.Details));
        }
        catch (ValidationException ex)
        {
            return BadRequest(new ReservationApiError("VALIDATION_ERROR", ex.Message, ex.Errors));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ReservationApiError("VALIDATION_ERROR", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ReservationApiError(ReservationErrorCodes.InvalidStateTransition, ex.Message));
        }
    }
}
