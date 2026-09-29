using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Memberships.Dtos;
using SmartGym.Application.Modules.Memberships.Services;

namespace SmartGym.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MembershipPlansController : ControllerBase
{
    private readonly IMembershipPlanService _planService;

    public MembershipPlansController(IMembershipPlanService planService)
    {
        _planService = planService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool onlyActive = false, CancellationToken cancellationToken = default)
    {
        var plans = await _planService.GetAllAsync(onlyActive, cancellationToken);
        return Ok(plans);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var plan = await _planService.GetByIdAsync(id, cancellationToken);
        if (plan == null)
        {
            return NotFound(new { message = "Plan de membresía no encontrado." });
        }

        return Ok(plan);
    }

    [HttpPost]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> Create([FromBody] CreateMembershipPlanRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var created = await _planService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateMembershipPlanRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _planService.UpdateAsync(id, request, cancellationToken);
            if (updated == null)
            {
                return NotFound(new { message = "Plan de membresía no encontrado." });
            }

            return Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _planService.DeleteAsync(id, cancellationToken);
        if (!deleted)
        {
            return NotFound(new { message = "Plan de membresía no encontrado." });
        }

        return NoContent();
    }
}
