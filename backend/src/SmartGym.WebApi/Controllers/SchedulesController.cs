using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Application.Modules.Activities.Services;

namespace SmartGym.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SchedulesController : ControllerBase
{
    private readonly IRecurringScheduleService _scheduleService;

    public SchedulesController(IRecurringScheduleService scheduleService)
    {
        _scheduleService = scheduleService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? activityId,
        [FromQuery] Guid? instructorId,
        CancellationToken cancellationToken)
    {
        var schedules = await _scheduleService.GetAllAsync(activityId, instructorId, cancellationToken);
        return Ok(schedules);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var schedule = await _scheduleService.GetByIdAsync(id, cancellationToken);
        if (schedule == null)
        {
            return NotFound(new { message = "Horario recurrente no encontrado." });
        }

        return Ok(schedule);
    }

    [HttpPost]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> Create([FromBody] CreateRecurringScheduleRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var created = await _scheduleService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
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

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRecurringScheduleRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _scheduleService.UpdateAsync(id, request, cancellationToken);
            if (updated == null)
            {
                return NotFound(new { message = "Horario recurrente no encontrado." });
            }

            return Ok(updated);
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

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _scheduleService.DeleteAsync(id, cancellationToken);
        if (!deleted)
        {
            return NotFound(new { message = "Horario recurrente no encontrado." });
        }

        return NoContent();
    }
}
