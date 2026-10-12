using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Operations;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Enums;

namespace SmartGym.WebApi.Controllers;

public record NotificationApiError(string Code, string Message, object? Details = null);

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

    [HttpGet]
    [Authorize(Policy = Policies.NotificationsReadOwn)]
    public Task<IActionResult> GetNotifications(
        [FromQuery] bool? isRead = null,
        [FromQuery] NotificationCategory? category = null,
        [FromQuery] NotificationType? type = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        ExecuteForPersonAsync(async personId =>
        {
            var filter = new NotificationQueryFilter(
                IsRead: isRead,
                Category: category,
                Type: type,
                FromUtc: fromUtc,
                ToUtc: toUtc,
                Page: page,
                PageSize: pageSize);
            var paged = await _notificationService.GetMyPagedAsync(personId, filter, cancellationToken);
            return Ok(paged);
        });

    [HttpGet("my")]
    [Authorize(Policy = Policies.NotificationsReadOwn)]
    public Task<IActionResult> GetMyNotifications(
        [FromQuery] bool onlyUnread = false,
        CancellationToken cancellationToken = default) =>
        ExecuteForPersonAsync(async personId =>
        {
            var list = await _notificationService.GetNotificationsForPersonAsync(personId, onlyUnread, cancellationToken);
            return Ok(list);
        });

    [HttpGet("unread-count")]
    [Authorize(Policy = Policies.NotificationsReadOwn)]
    public Task<IActionResult> GetUnreadCount(CancellationToken cancellationToken = default) =>
        ExecuteForPersonAsync(async personId =>
        {
            var count = await _notificationService.GetUnreadCountAsync(personId, cancellationToken);
            return Ok(count);
        });

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.NotificationsReadOwn)]
    public Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteForPersonAsync(async personId =>
        {
            var dto = await _notificationService.GetByIdForPersonAsync(id, personId, cancellationToken);
            return Ok(dto);
        });

    [HttpPatch("{id:guid}/read")]
    [Authorize(Policy = Policies.NotificationsMarkReadOwn)]
    public Task<IActionResult> MarkAsReadPatch(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteForPersonAsync(async personId =>
        {
            var dto = await _notificationService.MarkAsReadForPersonAsync(id, personId, cancellationToken);
            return Ok(dto);
        });

    [HttpPost("{id:guid}/read")]
    [Authorize(Policy = Policies.NotificationsMarkReadOwn)]
    public Task<IActionResult> MarkAsRead(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteForPersonAsync(async personId =>
        {
            var dto = await _notificationService.MarkAsReadForPersonAsync(id, personId, cancellationToken);
            return Ok(dto);
        });

    [HttpPatch("{id:guid}/unread")]
    [Authorize(Policy = Policies.NotificationsMarkReadOwn)]
    public Task<IActionResult> MarkAsUnread(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteForPersonAsync(async personId =>
        {
            var dto = await _notificationService.MarkAsUnreadForPersonAsync(id, personId, cancellationToken);
            return Ok(dto);
        });

    [HttpPost("mark-all-read")]
    [Authorize(Policy = Policies.NotificationsMarkReadOwn)]
    public Task<IActionResult> MarkAllAsRead(CancellationToken cancellationToken = default) =>
        ExecuteForPersonAsync(async personId =>
        {
            var result = await _notificationService.MarkAllAsReadForPersonAsync(personId, cancellationToken);
            return Ok(result);
        });

    [HttpGet("preferences")]
    [Authorize(Policy = Policies.NotificationsPreferencesManage)]
    public Task<IActionResult> GetPreferences(CancellationToken cancellationToken = default) =>
        ExecuteForPersonAsync(async personId =>
        {
            var preferences = await _notificationService.GetPreferencesAsync(personId, cancellationToken);
            return Ok(preferences);
        });

    [HttpPut("preferences")]
    [Authorize(Policy = Policies.NotificationsPreferencesManage)]
    public Task<IActionResult> UpdatePreferences(
        [FromBody] UpdateNotificationPreferencesRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteForPersonAsync(async personId =>
        {
            var preferences = await _notificationService.UpdatePreferencesAsync(personId, request, cancellationToken);
            return Ok(preferences);
        });

    [HttpPost]
    [Authorize(Policy = Policies.NotificationsManage)]
    public async Task<IActionResult> SendNotification(
        [FromBody] SendNotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var notification = await _notificationService.SendNotificationAsync(request, cancellationToken);
            return Ok(notification);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new NotificationApiError("VALIDATION_ERROR", ex.Message, ex.Errors));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new NotificationApiError(NotificationErrorCodes.RecipientNotFound, ex.Message));
        }
    }

    private async Task<IActionResult> ExecuteForPersonAsync(Func<Guid, Task<IActionResult>> action)
    {
        if (!_currentUserService.PersonId.HasValue)
        {
            return Unauthorized(new NotificationApiError("UNAUTHORIZED", "No se pudo resolver la identidad del usuario autenticado."));
        }

        try
        {
            return await action(_currentUserService.PersonId.Value);
        }
        catch (ForbiddenException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new NotificationApiError("FORBIDDEN", ex.Message));
        }
        catch (NotFoundException ex)
        {
            return NotFound(new NotificationApiError(NotificationErrorCodes.NotificationNotFound, ex.Message));
        }
        catch (ConflictException ex)
        {
            return Conflict(new NotificationApiError(ex.Code ?? "CONFLICT", ex.Message, ex.Details));
        }
        catch (ValidationException ex)
        {
            return BadRequest(new NotificationApiError("VALIDATION_ERROR", ex.Message, ex.Errors));
        }
    }
}
