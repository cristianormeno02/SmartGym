using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartGym.Application.Common.Exceptions;
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
    [Authorize(Policy = Policies.RequireTeachingAndStaff)]
    public async Task<IActionResult> GetAll(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? activityId,
        [FromQuery] Guid? roomId,
        [FromQuery] Guid? instructorId,
        [FromQuery] Guid? secretaryId,
        [FromQuery] ClassSessionDisplayStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _classSessionService.GetPagedAsync(from, to, activityId, roomId, instructorId, secretaryId, status, page, pageSize, cancellationToken);
            return Ok(result);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.RequireTeachingAndStaff)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var session = await _classSessionService.GetByIdAsync(id, cancellationToken);
            if (session == null)
            {
                return NotFound(new { message = "Clase no encontrada." });
            }

            return Ok(session);
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("available")]
    [Authorize(Policy = Policies.RequireAllAuthenticated)]
    public async Task<IActionResult> GetAvailable(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? activityId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var list = await _classSessionService.GetAvailableAsync(from, to, activityId, cancellationToken);
            return Ok(list);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> Create([FromBody] CreateClassSessionRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var created = await _classSessionService.CreateExtraordinaryAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ConflictException ex)
        {
            return Conflict(new { message = ex.Message, details = ex.Details });
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateClassSessionRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var updated = await _classSessionService.UpdateAsync(id, request, cancellationToken);
            return Ok(updated);
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ConflictException ex)
        {
            return Conflict(new { message = ex.Message, details = ex.Details });
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/open")]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> Open(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var opened = await _classSessionService.OpenAsync(id, cancellationToken);
            return Ok(opened);
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

    [HttpPost("{id:guid}/suspend")]
    [Authorize(Policy = Policies.RequireTeachingAndStaff)]
    public async Task<IActionResult> Suspend(Guid id, [FromBody] SuspendClassSessionRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var suspended = await _classSessionService.SuspendAsync(id, request, cancellationToken);
            return Ok(suspended);
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ForbiddenException)
        {
            return Forbid();
        }
        catch (ConflictException ex)
        {
            return Conflict(new { message = ex.Message, details = ex.Details });
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelClassSessionRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var cancelled = await _classSessionService.CancelAsync(id, request, cancellationToken);
            return Ok(cancelled);
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ConflictException ex)
        {
            return Conflict(new { message = ex.Message, details = ex.Details });
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [HttpPost("{id:guid}/reschedule")]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> Reschedule(Guid id, [FromBody] RescheduleClassSessionRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var newSession = await _classSessionService.RescheduleAsync(id, request, cancellationToken);
            return Ok(newSession);
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ConflictException ex)
        {
            return Conflict(new { message = ex.Message, details = ex.Details });
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [HttpPost("{id:guid}/replace-instructor")]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> ReplaceInstructor(Guid id, [FromBody] ReplaceStaffRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var updated = await _classSessionService.ReplaceInstructorAsync(id, request, cancellationToken);
            return Ok(updated);
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ConflictException ex)
        {
            return Conflict(new { message = ex.Message, details = ex.Details });
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [HttpPost("{id:guid}/replace-secretary")]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> ReplaceSecretary(Guid id, [FromBody] ReplaceStaffRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var updated = await _classSessionService.ReplaceSecretaryAsync(id, request, cancellationToken);
            return Ok(updated);
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ConflictException ex)
        {
            return Conflict(new { message = ex.Message, details = ex.Details });
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [HttpPost("{id:guid}/change-room")]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> ChangeRoom(Guid id, [FromBody] ChangeRoomRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var updated = await _classSessionService.ChangeRoomAsync(id, request, cancellationToken);
            return Ok(updated);
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ConflictException ex)
        {
            return Conflict(new { message = ex.Message, details = ex.Details });
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [HttpPost("generate")]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> Generate([FromBody] GenerateSessionsRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _classSessionService.GenerateSessionsAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (ConflictException ex)
        {
            return Conflict(new { message = ex.Message, details = ex.Details });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }
}
