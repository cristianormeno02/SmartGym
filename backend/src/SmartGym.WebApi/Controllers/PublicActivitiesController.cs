using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartGym.Application.Modules.Activities.Services;

namespace SmartGym.WebApi.Controllers;

[ApiController]
[Route("api/public/activities")]
[AllowAnonymous]
public class PublicActivitiesController : ControllerBase
{
    private readonly IActivityService _activityService;

    public PublicActivitiesController(IActivityService activityService)
    {
        _activityService = activityService;
    }

    [HttpGet]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetCatalog([FromQuery] int? age, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "public, max-age=300";
        var activities = await _activityService.GetPublicCatalogAsync(age, cancellationToken);
        return Ok(activities);
    }

    [HttpGet("{code}")]
    public async Task<IActionResult> GetByCode(string code, CancellationToken cancellationToken)
    {
        var activity = await _activityService.GetPublicByCodeAsync(code, cancellationToken);
        if (activity == null)
        {
            return NotFound(new { message = $"Actividad con código '{code}' no encontrada o no disponible públicamente." });
        }

        return Ok(activity);
    }
}
