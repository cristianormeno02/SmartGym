using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Application.Modules.Operations.Services;

namespace SmartGym.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;
    private readonly ICurrentUserService _currentUserService;

    public NotificationsController(
        INotificationService notificationService,
        ICurrentUserService currentUserService)
    {
        _notificationService = notificationService;
        _currentUserService = currentUserService;
    }

    [HttpGet("my")]
    [Authorize]
    public async Task<IActionResult> GetMyNotifications([FromQuery] bool onlyUnread = false, CancellationToken cancellationToken = default)
    {
        if (!_currentUserService.PersonId.HasValue)
        {
            return Unauthorized();
        }

        var list = await _notificationService.GetNotificationsForPersonAsync(_currentUserService.PersonId.Value, onlyUnread, cancellationToken);
        return Ok(list);
    }

    [HttpPost]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> SendNotification([FromBody] SendNotificationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var notification = await _notificationService.SendNotificationAsync(request, cancellationToken);
            return Ok(notification);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/read")]
    [Authorize]
    public async Task<IActionResult> MarkAsRead(Guid id, CancellationToken cancellationToken)
    {
        var success = await _notificationService.MarkAsReadAsync(id, cancellationToken);
        if (!success)
        {
            return NotFound(new { message = "Notificación no encontrada." });
        }

        return NoContent();
    }
}
