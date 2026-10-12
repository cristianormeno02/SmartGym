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
[Route("api/admin/notifications")]
public class AdminNotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public AdminNotificationsController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpGet]
    [Authorize(Policy = Policies.NotificationsReadAll)]
    public Task<IActionResult> GetAll(
        [FromQuery] Guid? recipientPersonId = null,
        [FromQuery] NotificationCategory? category = null,
        [FromQuery] NotificationType? type = null,
        [FromQuery] bool? isRead = null,
        [FromQuery] NotificationDeliveryStatus? deliveryStatus = null,
        [FromQuery] NotificationChannel? channel = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () =>
        {
            var filter = new AdminNotificationQueryFilter(
                RecipientPersonId: recipientPersonId,
                Category: category,
                Type: type,
                IsRead: isRead,
                DeliveryStatus: deliveryStatus,
                Channel: channel,
                FromUtc: fromUtc,
                ToUtc: toUtc,
                Page: page,
                PageSize: pageSize);
            var paged = await _notificationService.GetAdminPagedAsync(filter, cancellationToken);
            return Ok(paged);
        });

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.NotificationsReadAll)]
    public Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () =>
        {
            var dto = await _notificationService.GetAdminByIdAsync(id, cancellationToken);
            return Ok(dto);
        });

    [HttpGet("{id:guid}/deliveries")]
    [Authorize(Policy = Policies.NotificationsDeliveryRead)]
    public Task<IActionResult> GetDeliveries(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () =>
        {
            var deliveries = await _notificationService.GetDeliveriesByNotificationIdAsync(id, cancellationToken);
            return Ok(deliveries);
        });

    [HttpGet("deliveries/failed")]
    [Authorize(Policy = Policies.NotificationsDeliveryRead)]
    public Task<IActionResult> GetFailedDeliveries(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] NotificationChannel? channel = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () =>
        {
            var paged = await _notificationService.GetFailedDeliveriesPagedAsync(page, pageSize, channel, cancellationToken);
            return Ok(paged);
        });

    [HttpPost("deliveries/{id:guid}/retry")]
    [Authorize(Policy = Policies.NotificationsRetryFailed)]
    public Task<IActionResult> RetryDelivery(
        Guid id,
        [FromBody] RetryNotificationDeliveryRequest? request = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () =>
        {
            var retried = await _notificationService.RetryDeliveryAsync(
                id,
                request ?? new RetryNotificationDeliveryRequest(),
                cancellationToken);
            return Ok(retried);
        });

    [HttpPost("send")]
    [Authorize(Policy = Policies.NotificationsManage)]
    public Task<IActionResult> Send(
        [FromBody] SendNotificationRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () =>
        {
            var created = await _notificationService.SendNotificationAsync(request, cancellationToken);
            return Ok(created);
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
            return NotFound(new NotificationApiError(NotificationErrorCodes.DeliveryNotFound, ex.Message));
        }
        catch (ConflictException ex)
        {
            return Conflict(new NotificationApiError(ex.Code ?? "CONFLICT", ex.Message, ex.Details));
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
}
