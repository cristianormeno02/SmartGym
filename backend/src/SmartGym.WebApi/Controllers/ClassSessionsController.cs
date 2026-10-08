using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Application.Modules.Activities.Services;
using SmartGym.Domain.Enums;

namespace SmartGym.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClassSessionsController : ControllerBase
{
    private readonly IClassSessionService _classSessionService;

    public ClassSessionsController(IClassSessionService classSessionService)
    {
        _classSessionService = classSessionService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] Guid? activityId,
        [FromQuery] Guid? instructorId,
        [FromQuery] ClassSessionStatus? status,
        CancellationToken cancellationToken)
    {
        var sessions = await _classSessionService.GetAllAsync(fromDate, toDate, activityId, instructorId, status, cancellationToken);
        return Ok(sessions);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var session = await _classSessionService.GetByIdAsync(id, cancellationToken);
        if (session == null)
        {
            return NotFound(new { message = "Clase no encontrada." });
        }

        return Ok(session);
    }

    [HttpPost("generate")]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> Generate([FromBody] GenerateSessionsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var count = await _classSessionService.GenerateSessionsFromSchedulesAsync(
                request.FromDate,
                request.ToDate,
                request.RecurringScheduleId,
                cancellationToken);

            return Ok(new
            {
                message = $"Se generaron {count} nuevas clases en el rango indicado.",
                generatedCount = count
            });
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

    [HttpPost("manual")]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> CreateManual([FromBody] ManualCreateClassSessionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var created = await _classSessionService.CreateManualAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/start")]
    [Authorize(Policy = Policies.RequireTeachingAndStaff)]
    public async Task<IActionResult> Start(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var session = await _classSessionService.StartSessionAsync(id, cancellationToken);
            if (session == null)
            {
                return NotFound(new { message = "Clase no encontrada." });
            }

            return Ok(session);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/finish")]
    [Authorize(Policy = Policies.RequireTeachingAndStaff)]
    public async Task<IActionResult> Finish(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var session = await _classSessionService.FinishSessionAsync(id, cancellationToken);
            if (session == null)
            {
                return NotFound(new { message = "Clase no encontrada." });
            }

            return Ok(session);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/suspend")]
    [Authorize(Policy = Policies.RequireTeachingAndStaff)]
    public async Task<IActionResult> Suspend(Guid id, [FromBody] SuspendClassSessionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var session = await _classSessionService.SuspendSessionAsync(id, request.Reason, cancellationToken);
            if (session == null)
            {
                return NotFound(new { message = "Clase no encontrada." });
            }

            return Ok(session);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelClassSessionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var session = await _classSessionService.CancelSessionAsync(id, request.Reason, cancellationToken);
            if (session == null)
            {
                return NotFound(new { message = "Clase no encontrada." });
            }

            return Ok(session);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/substitute")]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> AssignSubstitute(Guid id, [FromBody] SubstituteInstructorRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var session = await _classSessionService.AssignSubstituteAsync(id, request.SubstituteInstructorId, cancellationToken);
            if (session == null)
            {
                return NotFound(new { message = "Clase no encontrada." });
            }

            return Ok(session);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
