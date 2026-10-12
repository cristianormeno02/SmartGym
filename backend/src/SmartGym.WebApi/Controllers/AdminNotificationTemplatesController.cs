using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Operations;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Enums;

namespace SmartGym.WebApi.Controllers;

[ApiController]
[Route("api/admin/notification-templates")]
public class AdminNotificationTemplatesController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public AdminNotificationTemplatesController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpGet]
    [Authorize(Policy = Policies.NotificationsTemplatesRead)]
    public Task<IActionResult> GetAll(
        [FromQuery] NotificationType? type = null,
        [FromQuery] NotificationChannel? channel = null,
        [FromQuery] string? locale = null,
        [FromQuery] NotificationTemplateStatus? status = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () =>
        {
            var filter = new NotificationTemplateQueryFilter(
                Type: type,
                Channel: channel,
                Locale: locale,
                Status: status);
            var templates = await _notificationService.GetTemplatesAsync(filter, cancellationToken);
            return Ok(templates);
        });

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.NotificationsTemplatesRead)]
    public Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () =>
        {
            var dto = await _notificationService.GetTemplateByIdAsync(id, cancellationToken);
            return Ok(dto);
        });

    [HttpPost]
    [Authorize(Policy = Policies.NotificationsTemplatesManage)]
    public Task<IActionResult> Create(
        [FromBody] CreateNotificationTemplateRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () =>
        {
            var created = await _notificationService.CreateTemplateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        });

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.NotificationsTemplatesManage)]
    public Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateNotificationTemplateRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () =>
        {
            var updated = await _notificationService.UpdateTemplateAsync(id, request, cancellationToken);
            return Ok(updated);
        });

    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = Policies.NotificationsTemplatesManage)]
    public Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () =>
        {
            var activated = await _notificationService.ActivateTemplateAsync(id, cancellationToken);
            return Ok(activated);
        });

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = Policies.NotificationsTemplatesManage)]
    public Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () =>
        {
            var deactivated = await _notificationService.DeactivateTemplateAsync(id, cancellationToken);
            return Ok(deactivated);
        });

    private async Task<IActionResult> ExecuteAsync(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ForbiddenException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new NotificationApiError("FORBIDDEN", ex.Message));
        }
        catch (NotFoundException ex)
        {
            return NotFound(new NotificationApiError(NotificationErrorCodes.TemplateNotFound, ex.Message));
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
