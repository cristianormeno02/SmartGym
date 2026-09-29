using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Application.Modules.Activities.Services;

namespace SmartGym.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ActivitiesController : ControllerBase
{
    private readonly IActivityService _activityService;
    private readonly IFileStorageService _fileStorageService;

    public ActivitiesController(
        IActivityService activityService,
        IFileStorageService fileStorageService)
    {
        _activityService = activityService;
        _fileStorageService = fileStorageService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool onlyEnabled = false, CancellationToken cancellationToken = default)
    {
        var activities = await _activityService.GetAllAsync(onlyEnabled, cancellationToken);
        return Ok(activities);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var activity = await _activityService.GetByIdAsync(id, cancellationToken);
        if (activity == null)
        {
            return NotFound(new { message = "Actividad no encontrada." });
        }

        return Ok(activity);
    }

    [HttpPost]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> Create([FromBody] CreateActivityRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var created = await _activityService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateActivityRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _activityService.UpdateAsync(id, request, cancellationToken);
            if (updated == null)
            {
                return NotFound(new { message = "Actividad no encontrada." });
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
        var deleted = await _activityService.DeleteAsync(id, cancellationToken);
        if (!deleted)
        {
            return NotFound(new { message = "Actividad no encontrada." });
        }

        return NoContent();
    }

    [HttpPost("{id:guid}/logo")]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> UploadLogo(Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "Archivo no proporcionado o vacío." });
        }

        var activity = await _activityService.GetByIdAsync(id, cancellationToken);
        if (activity == null)
        {
            return NotFound(new { message = "Actividad no encontrada." });
        }

        await using var stream = file.OpenReadStream();
        var logoUrl = await _fileStorageService.UploadFileAsync(
            stream,
            file.FileName,
            file.ContentType,
            "activities/logos",
            cancellationToken);

        var updateRequest = new UpdateActivityRequest(
            activity.Name,
            activity.Summary,
            activity.Description,
            activity.MinCapacity,
            activity.MaxCapacity,
            logoUrl,
            activity.ImageUrls,
            activity.Status,
            activity.MinAge,
            activity.MaxAge,
            activity.DefaultRoomId,
            activity.IsActive
        );

        var updated = await _activityService.UpdateAsync(id, updateRequest, cancellationToken);
        return Ok(updated);
    }

    [HttpPost("{id:guid}/images")]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> UploadImage(Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "Archivo no proporcionado o vacío." });
        }

        var activity = await _activityService.GetByIdAsync(id, cancellationToken);
        if (activity == null)
        {
            return NotFound(new { message = "Actividad no encontrada." });
        }

        await using var stream = file.OpenReadStream();
        var imageUrl = await _fileStorageService.UploadFileAsync(
            stream,
            file.FileName,
            file.ContentType,
            "activities/gallery",
            cancellationToken);

        var updatedImages = new List<string>(activity.ImageUrls) { imageUrl };

        var updateRequest = new UpdateActivityRequest(
            activity.Name,
            activity.Summary,
            activity.Description,
            activity.MinCapacity,
            activity.MaxCapacity,
            activity.LogoUrl,
            updatedImages,
            activity.Status,
            activity.MinAge,
            activity.MaxAge,
            activity.DefaultRoomId,
            activity.IsActive
        );

        var updated = await _activityService.UpdateAsync(id, updateRequest, cancellationToken);
        return Ok(updated);
    }
}
