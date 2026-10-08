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
[Authorize(Policy = Policies.RequireStaff)]
public class ActivitiesController : ControllerBase
{
    private readonly IActivityService _activityService;

    public ActivitiesController(IActivityService activityService)
    {
        _activityService = activityService;
    }

    [HttpGet]
    public async Task<IActionResult> GetPaged(
        [FromQuery] string? search,
        [FromQuery] ActivityStatus? status,
        [FromQuery] int? age,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await _activityService.GetPagedAsync(search, status, age, page, pageSize, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var activity = await _activityService.GetByIdAsync(id, cancellationToken);
        if (activity == null)
        {
            return NotFound(new { message = $"Actividad con ID '{id}' no encontrada." });
        }

        return Ok(activity);
    }

    [HttpGet("by-code/{code}")]
    public async Task<IActionResult> GetByCode(string code, CancellationToken cancellationToken)
    {
        var activity = await _activityService.GetByCodeAsync(code, cancellationToken);
        if (activity == null)
        {
            return NotFound(new { message = $"Actividad con código '{code}' no encontrada." });
        }

        return Ok(activity);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateActivityRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var created = await _activityService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
        catch (ConflictException ex)
        {
            return Conflict(new { message = ex.Message, details = ex.Details });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateActivityRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _activityService.UpdateAsync(id, request, cancellationToken);
            return Ok(updated);
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
        catch (ConflictException ex)
        {
            return Conflict(new { message = ex.Message, details = ex.Details });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeActivityStatusRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var changed = await _activityService.ChangeStatusAsync(id, request, cancellationToken);
            return Ok(new { changed });
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ConflictException ex)
        {
            return Conflict(new { message = ex.Message, details = ex.Details });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _activityService.DeleteAsync(id, cancellationToken);
            return NoContent();
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

    [HttpGet("{id:guid}/media")]
    public async Task<IActionResult> GetMedia(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var media = await _activityService.GetMediaAsync(id, cancellationToken);
            return Ok(media);
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/media")]
    public async Task<IActionResult> UploadMedia(
        Guid id,
        IFormFile file,
        [FromForm] ActivityMediaType type,
        CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "Archivo no proporcionado o vacío." });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var uploaded = await _activityService.UploadMediaAsync(
                id,
                stream,
                file.FileName,
                file.ContentType,
                file.Length,
                type,
                cancellationToken);

            return CreatedAtAction(nameof(GetMedia), new { id }, uploaded);
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (ConflictException ex)
        {
            return Conflict(new { message = ex.Message, details = ex.Details });
        }
    }

    [HttpDelete("{id:guid}/media/{mediaId:guid}")]
    public async Task<IActionResult> DeleteMedia(Guid id, Guid mediaId, CancellationToken cancellationToken)
    {
        try
        {
            await _activityService.DeleteMediaAsync(id, mediaId, cancellationToken);
            return NoContent();
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPatch("{id:guid}/media/{mediaId:guid}/primary")]
    public async Task<IActionResult> SetPrimaryMedia(Guid id, Guid mediaId, CancellationToken cancellationToken)
    {
        try
        {
            await _activityService.SetPrimaryMediaAsync(id, mediaId, cancellationToken);
            return Ok(new { message = "Imagen principal actualizada." });
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (ConflictException ex)
        {
            return Conflict(new { message = ex.Message, details = ex.Details });
        }
    }

    [HttpPatch("{id:guid}/media/sort")]
    public async Task<IActionResult> SortMedia(Guid id, [FromBody] SortActivityMediaRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await _activityService.SortMediaAsync(id, request, cancellationToken);
            return Ok(new { message = "Orden de medios actualizado." });
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
