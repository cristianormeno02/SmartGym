using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Promotions.Dtos;
using SmartGym.Application.Modules.Promotions.Services;

namespace SmartGym.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FamilyGroupsController : ControllerBase
{
    private readonly IFamilyGroupService _familyGroupService;

    public FamilyGroupsController(IFamilyGroupService familyGroupService)
    {
        _familyGroupService = familyGroupService;
    }

    [HttpGet]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var groups = await _familyGroupService.GetAllAsync(cancellationToken);
        return Ok(groups);
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var group = await _familyGroupService.GetByIdAsync(id, cancellationToken);
        if (group == null)
        {
            return NotFound(new { message = "Grupo familiar no encontrado." });
        }

        return Ok(group);
    }

    [HttpGet("person/{personId:guid}")]
    [Authorize]
    public async Task<IActionResult> GetByPersonId(Guid personId, CancellationToken cancellationToken)
    {
        var group = await _familyGroupService.GetByPersonIdAsync(personId, cancellationToken);
        if (group == null)
        {
            return NotFound(new { message = "La persona no pertenece a ningún grupo familiar activo." });
        }

        return Ok(group);
    }

    [HttpPost]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> Create([FromBody] CreateFamilyGroupRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var created = await _familyGroupService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/members")]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> AddMember(Guid id, [FromBody] AddFamilyMemberRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _familyGroupService.AddMemberAsync(id, request, cancellationToken);
            if (updated == null)
            {
                return NotFound(new { message = "Grupo familiar no encontrado." });
            }

            return Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}/members/{personId:guid}")]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> RemoveMember(Guid id, Guid personId, CancellationToken cancellationToken)
    {
        var removed = await _familyGroupService.RemoveMemberAsync(id, personId, cancellationToken);
        if (!removed)
        {
            return NotFound(new { message = "Grupo familiar no encontrado o el miembro no pertenece al mismo." });
        }

        return NoContent();
    }
}
